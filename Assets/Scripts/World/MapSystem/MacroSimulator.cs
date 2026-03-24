using UnityEngine;
using MWI.WorldSystem;
using System.Linq;

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
            if (CommunityTracker.Instance != null)
            {
                community = CommunityTracker.Instance.GetCommunity(savedData.MapId);
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
            bool hasLeader = false;
            HibernatedNPCData leaderData = null;
            if (community != null && !string.IsNullOrEmpty(community.LeaderNpcId))
            {
                leaderData = savedData.HibernatedNPCs.Find(n => n.CharacterId == community.LeaderNpcId);
                hasLeader = leaderData != null;
            }

            if (community != null && daysPassed >= 1.0 && hasLeader)
            {
                SimulateCityGrowth(community, daysPassed, leaderData.UnlockedBuildingIds);
            }
        }

        private static void SimulateCityGrowth(CommunityData community, double daysPassed, System.Collections.Generic.List<string> unlockedBuildingIds)
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

            if (unlockedBuildingIds == null || unlockedBuildingIds.Count == 0) return; // Leader knows nothing

            // Filter registry by what the leader knows
            var knownBuildings = settings.BuildingRegistry
                .Where(b => unlockedBuildingIds.Contains(b.PrefabId))
                .ToList();

            if (knownBuildings.Count == 0) return;

            // Sort by priority (highest first)
            knownBuildings.Sort((a, b) => b.CommunityPriority.CompareTo(a.CommunityPriority));

            int addedCount = 0;
            while (addedCount < maxNewBuildings)
            {
                var targetBuildingEntry = knownBuildings[0];

                Vector3 randomOffset = new Vector3(
                    UnityEngine.Random.Range(-30f, 30f), 
                    0f, 
                    UnityEngine.Random.Range(-30f, 30f)
                );
                
                BuildingSaveData newBuilding = new BuildingSaveData
                {
                    BuildingId = System.Guid.NewGuid().ToString(),
                    PrefabId = targetBuildingEntry.PrefabId,
                    Position = randomOffset,
                    Rotation = Quaternion.identity, // TODO: Randomize 90 degree increments
                    State = BuildingState.UnderConstruction,
                    ConstructionProgress = 0f
                };

                community.ConstructedBuildings.Add(newBuilding);
                addedCount++;
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
    }
}
