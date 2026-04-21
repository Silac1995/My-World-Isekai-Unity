using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Terrain;
using MWI.Weather;
using MWI.WorldSystem;

namespace MWI.Time
{
    public static class MacroSimulator
    {
        /// <summary>
        /// Fast-forwards the hibernated data based on the elapsed time.
        /// </summary>
        public static void SimulateCatchUp(MapSaveData savedData, int currentDay, float currentTime01, JobYieldRegistry jobYields)
        {
            if (savedData == null || savedData.HibernatedNPCs == null) return;

            // Absolute time formula: (Full Days) + (Fraction of Current Day)
            double currentAbsoluteTime = currentDay + currentTime01;
            double daysPassed = currentAbsoluteTime - savedData.LastHibernationTime;

            if (daysPassed <= 0) return;

            float hoursPassed = (float)(daysPassed * 24.0);
            int fullDays = Mathf.FloorToInt((float)daysPassed);

            Debug.Log($"<color=orange>[MacroSim]</color> Fast-forwarding Map '{savedData.MapId}' by {hoursPassed:F2} in-game hours.");

            MapController map = null;
            MapController[] activeMaps = Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in activeMaps)
            {
                if (m.MapId == savedData.MapId)
                {
                    map = m;
                    break;
                }
            }

            CommunityData community = null;
            if (MapRegistry.Instance != null)
            {
                community = MapRegistry.Instance.GetCommunity(savedData.MapId);
            }

            // 1. Resource Regeneration
            if (map != null && map.Biome != null && community != null && fullDays > 0)
            {
                foreach (var pool in community.ResourcePools)
                {
                    var entry = map.Biome.Harvestables.Find(h => h.ResourceId == pool.ResourceId);
                    if (entry == null) continue;

                    double daysSinceHarvest = currentDay - (pool.LastHarvestedDay > 0 ? pool.LastHarvestedDay : currentDay);
                    // Standard offline regeneration
                    pool.CurrentAmount = Mathf.Min(pool.CurrentAmount + Mathf.CeilToInt(entry.BaseYieldQuantity) * fullDays, pool.MaxAmount);
                }
            }

            // Terrain catch-up
            if (savedData.TerrainCells != null)
            {
                var climateProfile = map?.Biome?.ClimateProfile;
                if (climateProfile != null)
                {
                    var transitionRules = Resources.LoadAll<TerrainTransitionRule>("Data/Terrain/TransitionRules");
                    SimulateTerrainCatchUp(savedData.TerrainCells, climateProfile, hoursPassed,
                        new List<TerrainTransitionRule>(transitionRules));
                    SimulateVegetationCatchUp(savedData.TerrainCells, climateProfile, hoursPassed);
                }
            }

            int currentHour = Mathf.FloorToInt(currentTime01 * 24f);

            // Per-NPC Simulation steps
            foreach (var npc in savedData.HibernatedNPCs)
            {
                // 2. Inventory Yields
                if (jobYields != null && npc.SavedJobType != JobType.None && community != null)
                {
                    var recipe = jobYields.GetYieldFor(npc.SavedJobType);
                    if (recipe != null)
                    {
                        float fraction = ((npc.FreeTimeStarts - npc.WorkHourStarts + 24) % 24) / 24f;
                        float workFraction = Mathf.Clamp(fraction == 0f ? 1f : fraction, 0.1f, 1f);

                        foreach (var output in recipe.Outputs)
                        {
                            int yieldAmount = Mathf.FloorToInt(output.BaseAmountPerDay * workFraction * (float)daysPassed);
                            if (yieldAmount > 0)
                            {
                                var pool = community.ResourcePools.Find(p => p.ResourceId == output.ResourceId);
                                if (pool == null)
                                {
                                    pool = new ResourcePoolEntry { ResourceId = output.ResourceId, CurrentAmount = 0, MaxAmount = 9999f, LastHarvestedDay = currentDay };
                                    community.ResourcePools.Add(pool);
                                }
                                pool.CurrentAmount += yieldAmount;
                                Debug.Log($"<color=orange>[MacroSim]</color> Offline Yields: {npc.CharacterId} produced {yieldAmount} {output.ResourceId}.");
                            }
                        }
                    }
                }

                // 3. Needs Decay & 4. Snap Position
                SimulateNPCCatchUp(npc, savedData.MapId, currentHour, hoursPassed);
            }

            // 5. City Growth
            if (community != null && daysPassed >= 1.0)
            {
                SimulateCityGrowth(community, daysPassed, savedData);
            }
        }

        private static void SimulateCityGrowth(CommunityData community, double daysPassed, MapSaveData mapData)
        {
            // Max 1 new building per 7 offline days to prevent massive lag spikes/runaway growth
            int maxNewBuildings = Mathf.FloorToInt((float)daysPassed / 7f);
            
            // Advance any under-construction buildings first
            foreach (var b in community.ConstructedBuildings)
            {
                if (b.State == BuildingState.UnderConstruction)
                {
                    b.ConstructionProgress += (float)daysPassed * 0.2f; // Arbitrary 5 days to build
                    if (b.ConstructionProgress >= 1f)
                    {
                        b.ConstructionProgress = 1f;
                        b.State = BuildingState.Complete;
                        Debug.Log($"<color=green>[MacroSim]</color> Offline construction finished for {b.PrefabId} in {community.MapId}");
                    }
                }
            }

            if (maxNewBuildings <= 0) return;

            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            if (settings == null || settings.BuildingRegistry.Count == 0) return;

            // Find the leader's offline data
            var leaderData = mapData.HibernatedNPCs.FirstOrDefault(n => n.CharacterId == community.LeaderNpcId);
            if (leaderData == null) 
            {
                Debug.LogWarning($"<color=yellow>[MacroSim]</color> Community {community.MapId} has no Leader offline data. Skipping growth.");
                return;
            }

            // Filter registry by what the leader knows, except buildings already constructed
            var knownBuildings = leaderData.UnlockedBuildingIds;
            
            var availableToBuild = settings.BuildingRegistry
                .Where(entry => knownBuildings.Contains(entry.PrefabId))
                .Where(entry => !community.ConstructedBuildings.Any(cb => cb.PrefabId == entry.PrefabId))
                .OrderByDescending(entry => entry.CommunityPriority)
                .ToList();

            if (availableToBuild.Count == 0)
            {
                Debug.Log($"<color=yellow>[MacroSim]</color> Leader '{community.LeaderNpcId}' knows no new missing buildings. Growth paused.");
                return;
            }

            int addedCount = 0;
            while (addedCount < maxNewBuildings && availableToBuild.Count > 0)
            {
                var bestEntry = availableToBuild[0];

                Vector3 randomOffset = new Vector3(
                    UnityEngine.Random.Range(-30f, 30f), 
                    0f, 
                    UnityEngine.Random.Range(-30f, 30f)
                );
                
                BuildingSaveData newBuilding = new BuildingSaveData
                {
                    BuildingId = System.Guid.NewGuid().ToString(),
                    PrefabId = bestEntry.PrefabId,
                    Position = randomOffset,
                    Rotation = Quaternion.identity, // TODO: Randomize 90 degree increments
                    State = BuildingState.UnderConstruction,
                    ConstructionProgress = 0f
                };

                community.ConstructedBuildings.Add(newBuilding);
                addedCount++;
                
                availableToBuild.RemoveAt(0);

                Debug.Log($"<color=cyan>[MacroSim]</color> Offline growth started scaffold for {newBuilding.PrefabId} in {community.MapId}");
            }
        }

        private static void SimulateNPCCatchUp(HibernatedNPCData npcData, string currentMapId, int currentHour, float hoursPassed)
        {
            // Tier 2: Offline Needs Decay (Step 3)
            foreach (var need in npcData.SavedNeeds)
            {
                if (need.NeedType == "NeedSocial")
                {
                    float drainRate = 45f / 24f;
                    need.Value -= (hoursPassed * drainRate);
                    if (need.Value < 0) need.Value = 0;
                }
            }

            // Tier 1: Snap Position based on Anchors (Step 4)
            if (!npcData.HasSchedule) return;

            string targetMapId = null;
            Vector3 targetPosition = npcData.Position; // Default to wherever they were

            if (IsHourInRange(currentHour, npcData.SleepHourStarts, npcData.WorkHourStarts))
            {
                targetMapId = npcData.HomeMapId;
                targetPosition = npcData.HomePosition;
            }
            else if (IsHourInRange(currentHour, npcData.WorkHourStarts, npcData.FreeTimeStarts))
            {
                targetMapId = npcData.WorkMapId;
                targetPosition = npcData.WorkPosition;
            }
            else // Free Time zone
            {
                targetMapId = npcData.FreeTimeMapId;
                targetPosition = npcData.FreeTimePosition;
            }

            if (string.IsNullOrEmpty(targetMapId) || targetMapId == currentMapId)
            {
                if (targetPosition != Vector3.zero) 
                {
                    npcData.Position = targetPosition;
                }
            }
        }

        private static bool IsHourInRange(int currentHour, int startHour, int endHour)
        {
            if (startHour == endHour) return false;

            if (startHour < endHour)
            {
                // Normal daytime shift (e.g. 8 to 18)
                return currentHour >= startHour && currentHour < endHour;
            }
            else
            {
                // Overnight shift (e.g. 22 to 8)
                return currentHour >= startHour || currentHour < endHour;
            }
        }

        /// <summary>
        /// Offline catch-up for terrain cell moisture, temperature, and type transitions.
        /// Runs pure math on serialized cell data — no live Unity systems required.
        /// </summary>
        public static void SimulateTerrainCatchUp(
            TerrainCellSaveData[] cells,
            BiomeClimateProfile climate,
            float hoursPassed,
            List<TerrainTransitionRule> rules)
        {
            if (cells == null || climate == null) return;

            float estimatedRainHours = hoursPassed * climate.RainProbability;
            float estimatedDryHours = hoursPassed * (1f - climate.RainProbability - climate.SnowProbability - climate.CloudyProbability);
            float ambientTempAvg = (climate.AmbientTemperatureMin + climate.AmbientTemperatureMax) / 2f;

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Moisture += estimatedRainHours * 0.1f;
                cells[i].Moisture -= estimatedDryHours * climate.EvaporationRate;
                cells[i].Moisture = Mathf.Clamp01(cells[i].Moisture);
                cells[i].Temperature = ambientTempAvg;

                if (estimatedRainHours > 0)
                    cells[i].TimeSinceLastWatered = 0f;
                else
                    cells[i].TimeSinceLastWatered += hoursPassed;

                if (rules != null)
                {
                    foreach (var rule in rules)
                    {
                        if (rule.SourceType.TypeId != cells[i].CurrentTypeId
                            && rule.SourceType.TypeId != cells[i].BaseTypeId) continue;
                        if (rule.Evaluate(cells[i].Moisture, cells[i].Temperature, cells[i].SnowDepth))
                        {
                            cells[i].CurrentTypeId = rule.ResultType.TypeId;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Offline catch-up for vegetation growth and drought death on terrain cells.
        /// Runs pure math on serialized cell data — no live Unity systems required.
        /// </summary>
        public static void SimulateVegetationCatchUp(
            TerrainCellSaveData[] cells,
            BiomeClimateProfile climate,
            float hoursPassed,
            float minimumMoistureForGrowth = 0.2f,
            float droughtDeathHours = 48f)
        {
            if (cells == null || climate == null) return;

            float avgMoisture = climate.BaselineMoisture + (climate.RainProbability * 0.3f);

            for (int i = 0; i < cells.Length; i++)
            {
                var type = TerrainTypeRegistry.Get(cells[i].CurrentTypeId);
                if (type == null || !type.CanGrowVegetation) continue;
                if (cells[i].IsPlowed) continue;

                if (avgMoisture >= minimumMoistureForGrowth)
                {
                    cells[i].GrowthTimer += hoursPassed;
                    cells[i].TimeSinceLastWatered = 0f;
                }
                else
                {
                    cells[i].TimeSinceLastWatered += hoursPassed;
                    if (cells[i].TimeSinceLastWatered > droughtDeathHours)
                    {
                        cells[i].GrowthTimer = 0f;
                        cells[i].PlantedCropId = null;
                    }
                }
            }
        }
    }
}
