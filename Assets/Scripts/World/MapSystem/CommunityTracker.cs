using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    public enum CommunityTier
    {
        RoamingCamp = 0,
        Settlement = 1,
        EstablishedCity = 2,
        AbandonedCity = 3
    }

    public enum BuildingState
    {
        UnderConstruction = 0,
        Complete = 1,
        Ruined = 2
    }

    [Serializable]
    public class BuildingSaveData
    {
        public string BuildingId;        // GUID - unique per instance
        public string PrefabId;          // Registry key, NOT a path
        public Vector3 Position;
        public Quaternion Rotation;
        public string OwnerNpcId;
        public BuildingState State;      // UnderConstruction, Complete, Ruined
        public float ConstructionProgress; // 0-1, only relevant if UnderConstruction
    }

    [Serializable]
    public class ResourcePoolEntry
    {
        public string ResourceId;
        public float CurrentAmount;
        public float MaxAmount;             // Derived from HarvestableDensity * map area
        public double LastHarvestedDay;     // For regeneration math
    }

    [Serializable]
    public class CommunityData
    {
        public string MapId;
        public int SlotIndex = -1; // -1 means no slot allocated yet
        public CommunityTier Tier;
        
        public Vector2Int OriginChunk;
        
        // Progression
        public int DayStartedSustaining;
        public int DayDroppedBelowThreshold = -1;

        // Dynamic City Growth
        public List<BuildingSaveData> ConstructedBuildings = new List<BuildingSaveData>();
        public List<ResourcePoolEntry> ResourcePools = new List<ResourcePoolEntry>();

        public bool IsPredefinedMap;
        [NonSerialized] public int CurrentDailyPopulation;
        [NonSerialized] public bool IsHibernating; 
    }

    [Serializable]
    public class CommunityTrackerSaveData
    {
        public List<CommunityData> Communities = new List<CommunityData>();
        public List<RoamingClusterData> PendingClusters = new List<RoamingClusterData>();
    }

    [Serializable]
    public class RoamingClusterData
    {
        public Vector2Int Chunk;
        public int DayDiscovered;
    }

    /// <summary>
    /// Server-side heartbeat system that watches NPC clustering to promote wilderness settlements into proper Maps.
    /// Manages the full state machine: Roaming Camp -> Settlement -> Established City -> Abandoned City -> Reclaimed.
    /// </summary>
    public class CommunityTracker : MonoBehaviour, ISaveable
    {
        public static CommunityTracker Instance { get; private set; }

        [SerializeField] private WorldSettingsData _settings;
        [SerializeField] private GameObject _mapControllerPrefab;

        [SerializeField] private List<CommunityData> _communities = new List<CommunityData>();
        [SerializeField] private List<RoamingClusterData> _pendingClusters = new List<RoamingClusterData>();

        public string SaveKey => "CommunityTracker_Data";

        public CommunityData GetCommunity(string mapId)
        {
            return _communities.Find(c => c.MapId == mapId);
        }

        public void AddCommunity(CommunityData community)
        {
            if (GetCommunity(community.MapId) != null) return;
            _communities.Add(community);
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (_settings == null) _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            if (_mapControllerPrefab == null) _mapControllerPrefab = Resources.Load<GameObject>("Prefabs/World/MapController");

            // Defer ISaveable registration
            Invoke(nameof(RegisterWithSaveManager), 0.5f);

            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnNewDay += HandleNewDay;
            }
        }

        private void RegisterWithSaveManager()
        {
            if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                SaveManager.Instance.RegisterWorldSaveable(this);
            }
        }

        private void OnDestroy()
        {
            if (SaveManager.Instance != null) SaveManager.Instance.UnregisterWorldSaveable(this);
            if (TimeManager.Instance != null) TimeManager.Instance.OnNewDay -= HandleNewDay;
        }

        private void HandleNewDay()
        {
            if (!NetworkManager.Singleton.IsServer) return;

            EvaluatePopulations();
        }

        private void EvaluatePopulations()
        {
            int currentDay = TimeManager.Instance.CurrentDay;
            float chunkSize = _settings != null ? _settings.ProximityChunkSize : 75f;

            // 1. Reset metrics and assume all maps are offline (hibernating)
            foreach (var comm in _communities) 
            {
                comm.CurrentDailyPopulation = 0;
                comm.IsHibernating = true; 
            }

            // 2. Mark active maps based on loaded runtime MapControllers
            MapController[] activeMaps = FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var map in activeMaps)
            {
                if (!map.IsHibernating)
                {
                    CommunityData comm = _communities.Find(c => c.MapId == map.MapId);
                    if (comm != null) comm.IsHibernating = false; // Player is here, map is live
                }
            }

            Dictionary<Vector2Int, int> unstructuredChunks = new Dictionary<Vector2Int, int>();

            // 3. Count NPCs
            Character[] allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
            foreach (var c in allCharacters)
            {
                if (c.IsPlayer() || !c.IsAlive()) continue;

                Vector2Int chunk = new Vector2Int(
                    Mathf.FloorToInt(c.transform.position.x / chunkSize),
                    Mathf.FloorToInt(c.transform.position.z / chunkSize)
                );

                // Find if NPC belongs to a tracked community chunk proximity
                CommunityData ownerComm = _communities.Find(comm => 
                    Mathf.Abs(comm.OriginChunk.x - chunk.x) <= 1 && 
                    Mathf.Abs(comm.OriginChunk.y - chunk.y) <= 1
                );

                if (ownerComm != null)
                {
                    ownerComm.CurrentDailyPopulation++;
                    ownerComm.IsHibernating = false; // Active NPCs override hibernation check
                }
                else
                {
                    if (!unstructuredChunks.ContainsKey(chunk)) unstructuredChunks[chunk] = 0;
                    unstructuredChunks[chunk]++;
                }
            }

            ProcessExistingCommunities(currentDay);
            ProcessPendingClusters(unstructuredChunks, currentDay);
        }

        private void ProcessExistingCommunities(int currentDay)
        {
            if (_settings == null) return;

            for (int i = _communities.Count - 1; i >= 0; i--)
            {
                var comm = _communities[i];

                if (comm.IsHibernating) 
                {
                    // Map is offline. It cannot dissolve or promote while offline.
                    continue; 
                }

                // A. Check for Dissolution/Abandonment first
                if (comm.CurrentDailyPopulation == 0)
                {
                    if (comm.DayDroppedBelowThreshold == -1) comm.DayDroppedBelowThreshold = currentDay;
                    
                    if (currentDay - comm.DayDroppedBelowThreshold >= _settings.DissolutionGracePeriodDays)
                    {
                        if (comm.Tier == CommunityTier.RoamingCamp)
                        {
                            // Dissolve completely.
                            Debug.Log($"<color=orange>[CommunityTracker]</color> Roaming Camp at {comm.OriginChunk} disbanded.");
                            _communities.RemoveAt(i);
                            continue;
                        }
                        else if (comm.Tier == CommunityTier.Settlement || comm.Tier == CommunityTier.EstablishedCity)
                        {
                            // Turn into Abandoned City. Permanent slot retention.
                            comm.Tier = CommunityTier.AbandonedCity;
                            Debug.Log($"<color=red>[CommunityTracker]</color> Map {comm.MapId} has been ABANDONED.");
                        }
                    }
                }
                else
                {
                    comm.DayDroppedBelowThreshold = -1; // Reset dissolution timer
                }

                // B. Handle Upgrades and Reclamation
                if (comm.Tier == CommunityTier.RoamingCamp)
                {
                    if (comm.CurrentDailyPopulation >= _settings.SettlementMinPopulation)
                    {
                        if (currentDay - comm.DayStartedSustaining >= _settings.SettlementSustainedDays)
                        {
                            PromoteToSettlement(comm, currentDay);
                        }
                    }
                    else
                    {
                        comm.DayStartedSustaining = currentDay; // Reset sustaining timeline
                    }
                }
                else if (comm.Tier == CommunityTier.Settlement)
                {
                    if (comm.CurrentDailyPopulation >= _settings.CityMinPopulation)
                    {
                        if (currentDay - comm.DayStartedSustaining >= _settings.CitySustainedDays)
                        {
                            comm.Tier = CommunityTier.EstablishedCity;
                            comm.DayStartedSustaining = currentDay;
                            Debug.Log($"<color=green>[CommunityTracker]</color> Settlement {comm.MapId} promoted to Established City!");
                        }
                    }
                    else
                    {
                        comm.DayStartedSustaining = currentDay;
                    }
                }
                else if (comm.Tier == CommunityTier.AbandonedCity)
                {
                    if (comm.CurrentDailyPopulation >= _settings.ReclamationMinPopulation)
                    {
                        if (currentDay - comm.DayStartedSustaining >= _settings.ReclamationSustainedDays)
                        {
                            comm.Tier = CommunityTier.Settlement; // Reclaimed! Back to Settlement status.
                            comm.DayStartedSustaining = currentDay;
                            Debug.Log($"<color=cyan>[CommunityTracker]</color> Abandoned City {comm.MapId} has been RECLAIMED!");
                        }
                    }
                    else
                    {
                        comm.DayStartedSustaining = currentDay;
                    }
                }
            }
        }

        private void ProcessPendingClusters(Dictionary<Vector2Int, int> unstructuredChunks, int currentDay)
        {
            if (_settings == null) return;

            // 1. Update existing pending clusters
            for (int i = _pendingClusters.Count - 1; i >= 0; i--)
            {
                var cluster = _pendingClusters[i];
                if (unstructuredChunks.TryGetValue(cluster.Chunk, out int pop))
                {
                    if (pop >= 3) // Hardcoding "3" as roaming camp minimum
                    {
                        if (currentDay - cluster.DayDiscovered >= 3) // Hardcoding 3 days
                        {
                            // Create new Roaming Camp!
                            CommunityData newCamp = new CommunityData
                            {
                                MapId = Guid.NewGuid().ToString(),
                                OriginChunk = cluster.Chunk,
                                Tier = CommunityTier.RoamingCamp,
                                DayStartedSustaining = currentDay
                            };
                            _communities.Add(newCamp);
                            _pendingClusters.RemoveAt(i);
                            Debug.Log($"<color=yellow>[CommunityTracker]</color> New Roaming Camp formed at {cluster.Chunk}!");
                        }
                    }
                    else
                    {
                        // Died out
                        _pendingClusters.RemoveAt(i); 
                    }
                    unstructuredChunks.Remove(cluster.Chunk);
                }
                else
                {
                    // Nobody here anymore
                    _pendingClusters.RemoveAt(i); 
                }
            }

            // 2. Register any brand new chunks that popped up today
            foreach (var kvp in unstructuredChunks)
            {
                if (kvp.Value >= 3)
                {
                    _pendingClusters.Add(new RoamingClusterData
                    {
                        Chunk = kvp.Key,
                        DayDiscovered = currentDay
                    });
                }
            }
        }

        private void PromoteToSettlement(CommunityData comm, int currentDay)
        {
            comm.Tier = CommunityTier.Settlement;
            comm.DayStartedSustaining = currentDay;

            if (WorldOffsetAllocator.Instance == null)
            {
                Debug.LogError("[CommunityTracker] WorldOffsetAllocator is missing! Cannot promote Settlement to a physical slot.");
                return;
            }

            // 1. Allocate Slot
            comm.SlotIndex = WorldOffsetAllocator.Instance.AllocateSlotIndex();
            Vector3 worldPos = WorldOffsetAllocator.Instance.GetOffsetVector(comm.SlotIndex);

            // 2. Instantiate MapController
            if (_mapControllerPrefab != null)
            {
                GameObject mapObj = Instantiate(_mapControllerPrefab, worldPos, Quaternion.identity);
                MapController mapController = mapObj.GetComponent<MapController>();
                if (mapController != null)
                {
                    mapController.MapId = comm.MapId;
                    mapObj.GetComponent<NetworkObject>().Spawn();
                }

                // 2.5 Instantiate physical terrain prefab for the Settlement
                GameObject terrainPrefab = _settings.GetPrefabForTier(comm.Tier);
                if (terrainPrefab != null)
                {
                    GameObject terrain = Instantiate(terrainPrefab, worldPos, Quaternion.identity);
                    terrain.transform.SetParent(mapObj.transform); // Attach to MapController
                    if (terrain.TryGetComponent(out NetworkObject terrainNet))
                    {
                        terrainNet.Spawn();
                    }
                }

                // 3. Migrate founding NPCs
                float chunkSize = _settings != null ? _settings.ProximityChunkSize : 75f;
                Character[] allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
                foreach (var c in allCharacters)
                {
                    if (c.IsPlayer() || !c.IsAlive()) continue;

                    Vector2Int chunk = new Vector2Int(
                        Mathf.FloorToInt(c.transform.position.x / chunkSize),
                        Mathf.FloorToInt(c.transform.position.z / chunkSize)
                    );

                    if (Mathf.Abs(comm.OriginChunk.x - chunk.x) <= 1 && Mathf.Abs(comm.OriginChunk.y - chunk.y) <= 1)
                    {
                        Debug.Log($"<color=yellow>[CommunityTracker]</color> Migrating NPC {c.CharacterName} to newly founded Settlement Map {comm.MapId}");
                        
                        // Set Map Tracking & Anchors
                        if (c.TryGetComponent(out CharacterMapTracker tracker))
                        {
                            tracker.SetCurrentMap(comm.MapId);
                            tracker.HomeMapId.Value = comm.MapId;
                            tracker.HomePosition.Value = worldPos;
                        }
                        
                        // Physically Warp
                        if (c.TryGetComponent(out CharacterMovement movement))
                        {
                            Vector3 randomOffset = new Vector3(UnityEngine.Random.Range(-3f, 3f), 0, UnityEngine.Random.Range(-3f, 3f));
                            movement.Warp(worldPos + randomOffset);
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("[CommunityTracker] MapController prefab is not set. Map instantiated logically but not physically.");
            }

            Debug.Log($"<color=green>[CommunityTracker]</color> Roaming Camp promoted to SETTLEMENT! MapID: {comm.MapId} at Slot {comm.SlotIndex}");
        }

        #region ISaveable Implementation

        public object CaptureState()
        {
            return new CommunityTrackerSaveData
            {
                Communities = new List<CommunityData>(_communities),
                PendingClusters = new List<RoamingClusterData>(_pendingClusters)
            };
        }

        public void RestoreState(object state)
        {
            if (state is CommunityTrackerSaveData data)
            {
                _communities = data.Communities ?? new List<CommunityData>();
                _pendingClusters = data.PendingClusters ?? new List<RoamingClusterData>();
                Debug.Log($"<color=green>[CommunityTracker]</color> Restored {_communities.Count} communities.");
            }
        }

        #endregion
    }
}
