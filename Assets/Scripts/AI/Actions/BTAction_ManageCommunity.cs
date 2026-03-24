using UnityEngine;
using Unity.Netcode;
using MWI.WorldSystem;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Active simulation node for the Community Leader to manage the settlement.
    /// Evaluates if new buildings are needed and instantiates scaffolds within the MapController bounds.
    /// </summary>
    public class BTAction_ManageCommunity : BTNode
    {
        private float _lastCheckTime;
        private const float CHECK_COOLDOWN = 60f; // Only evaluate city growth once per minute to save performance

        protected override void OnEnter(Blackboard bb)
        {
            _lastCheckTime = 0f; // Force a check immediately when entering
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            if (UnityEngine.Time.time - _lastCheckTime < CHECK_COOLDOWN)
            {
                return BTNodeStatus.Success; // Fast pass if on cooldown
            }

            _lastCheckTime = UnityEngine.Time.time;

            Character self = bb.Self;
            if (self == null || !NetworkManager.Singleton.IsServer) 
                return BTNodeStatus.Failure;

            if (!self.TryGetComponent(out CharacterMapTracker tracker) || string.IsNullOrEmpty(tracker.CurrentMapID.Value.ToString())) 
                return BTNodeStatus.Failure;

            string mapId = tracker.CurrentMapID.Value.ToString();
            CommunityData comm = CommunityTracker.Instance?.GetCommunity(mapId);
            
            if (comm == null) 
                return BTNodeStatus.Failure;

            // 1. Am I the Leader?
            if (comm.LeaderNpcId != self.CharacterName)
                return BTNodeStatus.Failure;

            // 2. Are we already building something? (One at a time)
            foreach (var b in comm.ConstructedBuildings)
            {
                if (b.State == BuildingState.UnderConstruction)
                    return BTNodeStatus.Success; // Already busy building
            }

            // 3. Do we need a new building? (Simple logic: 1 building per 3 population)
            int allowedBuildings = Mathf.Max(1, comm.CurrentDailyPopulation / 3);
            if (comm.ConstructedBuildings.Count >= allowedBuildings)
                return BTNodeStatus.Success; // City is big enough for current population

            // 4. Place a new scaffold
            PlaceNewBuildingScaffold(self, comm);

            return BTNodeStatus.Success;
        }

        private void PlaceNewBuildingScaffold(Character self, CommunityData comm)
        {
            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            if (settings == null || settings.BuildingRegistry == null || settings.BuildingRegistry.Count == 0) return;

            // 1. Get Leader's blueprints
            if (!self.TryGetComponent(out CharacterSystem.CharacterBlueprints blueprints)) return;
            var unlockedIds = blueprints.GetUnlockedBuildingIds();
            if (unlockedIds == null || unlockedIds.Count == 0) return; // Leader knows nothing

            // 2. Filter the registry to only buildings the leader knows how to build
            var knownBuildings = settings.BuildingRegistry
                .Where(b => unlockedIds.Contains(b.PrefabId))
                .ToList();

            if (knownBuildings.Count == 0) return;

            // 3. Sort by priority (highest first)
            knownBuildings.Sort((a, b) => b.CommunityPriority.CompareTo(a.CommunityPriority));

            // Select the highest priority building the leader knows
            var targetBuildingEntry = knownBuildings[0];

            // Find MapController to get bounds and position
            Bounds? mapBounds = null;
            Vector3 mapCenter = Vector3.zero;
            Transform mapTransform = null;
            
            var maps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in maps)
            {
                if (m.MapId == comm.MapId)
                {
                    mapTransform = m.transform;
                    mapCenter = m.transform.position;
                    if (m.TryGetComponent(out Collider mapCollider))
                    {
                        mapBounds = mapCollider.bounds;
                    }
                    break;
                }
            }

            if (mapTransform == null) return;

            // Choose an empty spot inside MapController bounds
            Vector3 randomPos = mapCenter;
            if (mapBounds.HasValue)
            {
                randomPos = new Vector3(
                    UnityEngine.Random.Range(mapBounds.Value.min.x, mapBounds.Value.max.x),
                    mapCenter.y,
                    UnityEngine.Random.Range(mapBounds.Value.min.z, mapBounds.Value.max.z)
                );
            }
            else
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * 30f;
                randomPos = new Vector3(randomCircle.x, 0, randomCircle.y) + mapCenter;
            }

            // Register scaffold to CommunityData
            BuildingSaveData newBuilding = new BuildingSaveData
            {
                BuildingId = System.Guid.NewGuid().ToString(),
                PrefabId = targetBuildingEntry.PrefabId,
                Position = randomPos - mapCenter, // Local position relative to MapCenter
                Rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 4) * 90f, 0), // 90 degree increments
                State = BuildingState.UnderConstruction,
                ConstructionProgress = 0f
            };

            comm.ConstructedBuildings.Add(newBuilding);

            Debug.Log($"<color=magenta>[Leader AI]</color> {self.CharacterName} decided to build a {newBuilding.PrefabId} at {randomPos} in '{comm.MapId}'!");

            // Physically instantiate BuildingState.UnderConstruction scaffold
            GameObject bPrefab = settings.GenericScaffoldPrefab;
            if (bPrefab == null) bPrefab = settings.GetBuildingPrefab(targetBuildingEntry.PrefabId);

            if (bPrefab != null)
            {
                GameObject bObj = UnityEngine.Object.Instantiate(bPrefab, randomPos, newBuilding.Rotation);
                bObj.transform.SetParent(mapTransform);
                if (bObj.TryGetComponent(out NetworkObject bNet))
                {
                    bNet.Spawn();
                }
            }
        }
    }
}
