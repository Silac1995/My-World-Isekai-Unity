using System;
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
        // ── Cached resources ──
        // Transition rules are SOs that never change at runtime. SimulateOneHour can be called
        // up to 168x for a week-long skip — caching avoids redundant Resources.LoadAll allocations.
        // Canonical pattern from CLAUDE.md rule #34.
        private static TerrainTransitionRule[] _cachedTransitionRules;
        private static List<TerrainTransitionRule> _transitionRuleList;

        private static List<TerrainTransitionRule> GetTransitionRulesCached()
        {
            if (_cachedTransitionRules == null)
            {
                _cachedTransitionRules = Resources.LoadAll<TerrainTransitionRule>("Data/Terrain/TransitionRules");
                _transitionRuleList = new List<TerrainTransitionRule>(_cachedTransitionRules);
            }
            return _transitionRuleList;
        }

        /// <summary>
        /// Step 5 of the catch-up loop (Phase 1). Iterates all WildernessZones and applies
        /// accumulated daily deltas from their IZoneMotionStrategy lists. Clamps each
        /// proposed position by WorldSettingsData.MapMinSeparation to prevent zone-on-zone
        /// overlap.
        ///
        /// Phase 1 note: all zones default to StaticMotionStrategy which returns
        /// Vector3.zero, so this is effectively a no-op until reactive strategies land in
        /// later phases. Active-play daily ticking is deferred — SimulateCatchUp (called
        /// on map wake-up) applies the accumulated drift at that moment.
        /// </summary>
        public static void TickZoneMotion(int daysSinceLastTick)
        {
            if (daysSinceLastTick <= 0) return;

            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            float minSep = settings != null ? settings.MapMinSeparation : 150f;
            float minSqr = minSep * minSep;

            var zones = UnityEngine.Object.FindObjectsByType<WildernessZone>(FindObjectsSortMode.None);
            foreach (var zone in zones)
            {
                if (zone == null || zone.MotionStrategies == null || zone.MotionStrategies.Count == 0) continue;

                Vector3 totalDelta = Vector3.zero;
                foreach (var strategy in zone.MotionStrategies)
                {
                    if (strategy == null) continue;
                    try
                    {
                        totalDelta += strategy.ComputeDailyDelta(zone, currentDay);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                if (totalDelta == Vector3.zero) continue;

                Vector3 proposed = zone.transform.position + totalDelta * daysSinceLastTick;

                // Clamp: skip the update if proposed position is within MapMinSeparation of another zone.
                bool blocked = false;
                foreach (var other in zones)
                {
                    if (other == null || other == zone) continue;
                    if ((other.transform.position - proposed).sqrMagnitude < minSqr)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                {
                    zone.transform.position = proposed;
                }
            }
        }

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
            MapController[] activeMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
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
                    SimulateTerrainCatchUp(savedData.TerrainCells, climateProfile, hoursPassed,
                        GetTransitionRulesCached());
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

                                // TODO Task 26: Accrue WorkLog units here once HibernatedNPCData carries
                                // per-NPC CharacterWorkLog data (likely scenario b: load profile, mutate, save).
                                // For v1 the WorkLog career counter is wake-up-time-only; offline yields
                                // flow into community.ResourcePools but are NOT credited to the NPC's WorkLog.
                                // Player-visible impact: NPCs that hibernated through productive shifts will
                                // not have those shifts in their work history when they wake up.
                                // Resolution path: extend HibernatedNPCData with a WorkLogSaveData field,
                                // then call workLog.LogShiftUnit(npc.SavedJobType, workplaceBuildingId, yieldAmount)
                                // on the deserialized log before re-serializing.
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

            // 6. Zone Motion (Phase 1: no-op under StaticMotionStrategy)
            int dayDelta = Mathf.Max(1, fullDays);
            TickZoneMotion(dayDelta);
        }

        /// <summary>
        /// Per-hour catch-up entry point used by TimeSkipController.
        /// Runs hour-grained steps every call and day-grained steps only on hour-23→hour-0
        /// rollover. Updates <c>data.LastHibernationTime</c> so a subsequent <c>WakeUp()</c>
        /// does not double-process. Existing <see cref="SimulateCatchUp"/> remains the
        /// single-pass wake-up path for hibernated maps.
        /// </summary>
        /// <param name="data">The active map's hibernation snapshot — typically <c>MapController.HibernationData</c>.</param>
        /// <param name="currentDay">Post-advance day (after <c>TimeManager.AdvanceOneHour</c>).</param>
        /// <param name="currentTime01">Post-advance time01.</param>
        /// <param name="jobYields">Job yield registry passed through to inventory-yield helpers.</param>
        /// <param name="previousHour">The hour value BEFORE this hour-advance (used for day-rollover detection).</param>
        public static void SimulateOneHour(MapSaveData data, int currentDay, float currentTime01, JobYieldRegistry jobYields, int previousHour)
        {
            if (data == null || data.HibernatedNPCs == null) return;

            int currentHour = Mathf.FloorToInt(currentTime01 * 24f);
            bool crossedDayBoundary = (previousHour == 23 && currentHour == 0);

            MapController map = null;
            var activeMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in activeMaps)
            {
                if (m.MapId == data.MapId) { map = m; break; }
            }

            CommunityData community = null;
            if (MapRegistry.Instance != null) community = MapRegistry.Instance.GetCommunity(data.MapId);

            // ── Hour-grained: always run ──

            // Needs decay + sleep restoration + schedule snap (per-NPC)
            foreach (var npc in data.HibernatedNPCs)
            {
                ApplyNeedsDecayHours(npc, hoursPassed: 1f);
                ApplySleepRestoreHours(npc, hoursPassed: 1f);
                SnapPositionFromSchedule(npc, data.MapId, currentHour);
            }

            // Terrain + vegetation
            if (data.TerrainCells != null)
            {
                var climateProfile = map?.Biome?.ClimateProfile;
                if (climateProfile != null)
                {
                    SimulateTerrainCatchUp(data.TerrainCells, climateProfile, 1f, GetTransitionRulesCached());
                    SimulateVegetationCatchUp(data.TerrainCells, climateProfile, 1f);
                }
            }

            // ── Day-grained: only on day rollover ──
            if (crossedDayBoundary)
            {
                // 1. Resource pool regen — port from existing SimulateCatchUp's "1. Resource Regeneration" block, with fullDays=1
                if (map != null && map.Biome != null && community != null)
                {
                    foreach (var pool in community.ResourcePools)
                    {
                        var entry = map.Biome.Harvestables.Find(h => h.ResourceId == pool.ResourceId);
                        if (entry == null) continue;
                        pool.CurrentAmount = Mathf.Min(pool.CurrentAmount + Mathf.CeilToInt(entry.BaseYieldQuantity), pool.MaxAmount);
                    }
                }

                // 2. Inventory yields per NPC — port from existing block with daysPassed=1
                if (jobYields != null && community != null)
                {
                    foreach (var npc in data.HibernatedNPCs)
                    {
                        if (npc.SavedJobType == JobType.None) continue;
                        var recipe = jobYields.GetYieldFor(npc.SavedJobType);
                        if (recipe == null) continue;

                        float fraction = ((npc.FreeTimeStarts - npc.WorkHourStarts + 24) % 24) / 24f;
                        float workFraction = Mathf.Clamp(fraction == 0f ? 1f : fraction, 0.1f, 1f);

                        foreach (var output in recipe.Outputs)
                        {
                            int yieldAmount = Mathf.FloorToInt(output.BaseAmountPerDay * workFraction);
                            if (yieldAmount <= 0) continue;
                            var pool = community.ResourcePools.Find(p => p.ResourceId == output.ResourceId);
                            if (pool == null)
                            {
                                pool = new ResourcePoolEntry { ResourceId = output.ResourceId, CurrentAmount = 0, MaxAmount = 9999f, LastHarvestedDay = currentDay };
                                community.ResourcePools.Add(pool);
                            }
                            pool.CurrentAmount += yieldAmount;
                        }
                    }
                }

                // 3. City growth — call existing SimulateCityGrowth helper with daysPassed=1.0
                if (community != null) SimulateCityGrowth(community, daysPassed: 1.0, data);

                // 4. Zone motion — daysSinceLastTick=1
                TickZoneMotion(daysSinceLastTick: 1);
            }

            // Stamp the new hibernation time so a future WakeUp single-pass does not re-process this delta.
            data.LastHibernationTime = (double)currentDay + currentTime01;
        }

        /// <summary>
        /// Extracted from <see cref="SimulateCatchUp"/> step 3 (Needs Decay) so both per-hour and
        /// per-day paths share one implementation.
        /// </summary>
        private static void ApplyNeedsDecayHours(HibernatedNPCData npcData, float hoursPassed)
        {
            foreach (var need in npcData.SavedNeeds)
            {
                if (need.NeedType == "NeedSocial")
                {
                    float drainRate = 45f / 24f;
                    need.Value -= (hoursPassed * drainRate);
                    if (need.Value < 0) need.Value = 0;
                }
                else if (need.NeedType == "NeedHunger")
                {
                    // 100 hunger per day = 100/24 per hour. Matches NeedHunger._decayPerPhase=25 x 4 phases.
                    const float drainRatePerHour = 100f / 24f;
                    need.Value = MWI.Needs.HungerCatchUpMath.ApplyDecay(need.Value, drainRatePerHour, hoursPassed);
                }
                // NeedSleep: no offline decay. Awake characters simply stay at whatever value they had when
                // they hibernated. Sleeping characters are handled by ApplySleepRestoreHours below.
            }
        }

        /// <summary>
        /// Per-hour sleep restoration for <see cref="HibernatedNPCData.IsSleeping"/> characters
        /// during a TimeSkip. Sibling to <see cref="ApplyNeedsDecayHours"/> — while the character
        /// is in a sleep pose, NeedSleep restores at the offline bed-rate from
        /// <see cref="MWI.Needs.NeedSleepMath"/>.
        ///
        /// Stamina restoration is intentionally omitted in v1: stamina lives in
        /// ProfileData (CharacterProfileSaveData.componentStates) as serialized JSON,
        /// and mutating it without deserializing the full stat component graph is not
        /// safe here. Track as: "Task 10 open — offline stamina restore during sleep
        /// requires ProfileData round-trip or a dedicated flat field on HibernatedNPCData."
        ///
        /// v1 defaults to bed-rate for all sleeping NPCs. NPCs can only set IsSleeping = true
        /// via BedFurniture.UseSlot or EnterSleep, but we don't currently persist which path
        /// was taken. Future: add SleepingOnBedFurniture bool to HibernatedNPCData for
        /// ground-vs-bed rate selection.
        /// </summary>
        private static void ApplySleepRestoreHours(HibernatedNPCData npcData, float hoursPassed)
        {
            if (npcData == null || hoursPassed <= 0f) return;
            if (!npcData.IsSleeping) return;

            float restoreAmount = MWI.Needs.NeedSleepMath.OFFLINE_BED_RESTORE_PER_HOUR * hoursPassed;

            for (int i = 0; i < npcData.SavedNeeds.Count; i++)
            {
                if (npcData.SavedNeeds[i].NeedType == "NeedSleep")
                {
                    npcData.SavedNeeds[i].Value = Mathf.Clamp(
                        npcData.SavedNeeds[i].Value + restoreAmount,
                        0f,
                        MWI.Needs.NeedSleepMath.DEFAULT_MAX);
                    return;
                }
            }
            // NeedSleep entry not found in SavedNeeds — NPC was hibernated before sleep-need was
            // added, or CharacterNeeds had no NeedSleep component. No-op; do not inject a new entry
            // (we don't know the correct starting value).
        }

        /// <summary>
        /// Extracted from <see cref="SimulateCatchUp"/> step 4 (Schedule Snap).
        /// </summary>
        private static void SnapPositionFromSchedule(HibernatedNPCData npcData, string currentMapId, int currentHour)
        {
            if (!npcData.HasSchedule) return;

            string targetMapId;
            Vector3 targetPosition = npcData.Position;

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
            else
            {
                targetMapId = npcData.FreeTimeMapId;
                targetPosition = npcData.FreeTimePosition;
            }

            if (string.IsNullOrEmpty(targetMapId) || targetMapId == currentMapId)
            {
                if (targetPosition != Vector3.zero) npcData.Position = targetPosition;
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
            ApplyNeedsDecayHours(npcData, hoursPassed);
            SnapPositionFromSchedule(npcData, currentMapId, currentHour);
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
