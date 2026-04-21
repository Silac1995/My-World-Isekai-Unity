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

    /// <summary>
    /// One employee assignment inside a CommercialBuilding. Persisted alongside
    /// the building's owner so that crews survive hibernation and world save/load.
    /// </summary>
    [Serializable]
    public class EmployeeSaveEntry
    {
        public string CharacterId;   // Worker's persistent UUID
        public string JobType;       // Reflection-friendly type name (e.g. "JobBarman")
    }

    [Serializable]
    public class BuildingSaveData
    {
        public string BuildingId;        // GUID - unique per instance
        public string PrefabId;          // Registry key, NOT a path
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>All owner UUIDs (Room/Building.OwnerIds). Replaces the old single-owner
        /// OwnerNpcId field. For CommercialBuilding the boss is OwnerCharacterIds[0] by convention.</summary>
        public List<string> OwnerCharacterIds = new List<string>();
        public BuildingState State;      // UnderConstruction, Complete, Ruined
        public float ConstructionProgress; // 0-1, only relevant if UnderConstruction

        // Interior map data (null/-1 if no interior has been spawned yet)
        public string InteriorMapId;
        public int InteriorSlotIndex = -1;

        // Who originally placed this building (distinct from CommercialBuilding.Owner who runs the business)
        public string PlacedByCharacterId;

        /// <summary>Saved employee → job assignments. Populated only for CommercialBuilding.</summary>
        public List<EmployeeSaveEntry> Employees = new List<EmployeeSaveEntry>();

        /// <summary>
        /// Creates a BuildingSaveData entry from a live Building, storing position
        /// relative to the given map center.
        /// </summary>
        public static BuildingSaveData FromBuilding(Building building, Vector3 mapCenter)
        {
            var data = new BuildingSaveData
            {
                BuildingId = building.BuildingId,
                PrefabId = building.PrefabId,
                Position = building.transform.position - mapCenter,
                Rotation = building.transform.rotation,
                State = building.CurrentState,
                ConstructionProgress = building.CurrentState == BuildingState.Complete ? 1f : 0f,
                PlacedByCharacterId = building.PlacedByCharacterId.Value.ToString()
            };

            // Owner UUIDs (works for both Residential and Commercial).
            // We read raw IDs (NOT Owners getter) so hibernated owners are preserved.
            try
            {
                foreach (string id in building.OwnerIds)
                {
                    if (!string.IsNullOrEmpty(id))
                        data.OwnerCharacterIds.Add(id);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // Employees: only commercial buildings have a Jobs roster.
            if (building is CommercialBuilding commercial)
            {
                foreach (var job in commercial.Jobs)
                {
                    if (job == null || !job.IsAssigned || job.Worker == null) continue;
                    string workerId = job.Worker.CharacterId;
                    if (string.IsNullOrEmpty(workerId)) continue;

                    data.Employees.Add(new EmployeeSaveEntry
                    {
                        CharacterId = workerId,
                        JobType = job.GetType().Name
                    });
                }
            }

            return data;
        }
    }

    [Serializable]
    public class BuildPermit
    {
        public string CharacterId;        // Who received the permit
        public string GrantedByLeaderId;  // Which leader approved it
        public int RemainingPlacements;   // How many buildings they can still place
        public string MapId;              // Which zone this permit applies to
    }

    [Serializable]
    public class PendingBuildingClaim
    {
        public string BuildingId;
        public string OwnerCharacterId;
        public int DayClaimed;            // Game day when claim was initiated
        public int TimeoutDays = 7;       // Auto-claim after this many days
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
        public string LeaderNpcId; // Primary leader UUID
        public List<string> LeaderIds = new List<string>(); // All leaders (primary is first)

        /// <summary>
        /// Returns true if the given character ID is a recognized leader of this community.
        /// </summary>
        public bool IsLeader(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            return LeaderIds.Contains(characterId);
        }

        /// <summary>
        /// Adds a leader to the community. If this is the first leader, also sets LeaderNpcId (primary).
        /// </summary>
        public void AddLeader(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || LeaderIds.Contains(characterId)) return;
            LeaderIds.Add(characterId);
            if (string.IsNullOrEmpty(LeaderNpcId))
            {
                LeaderNpcId = characterId;
            }
        }
        
        // Progression
        public int DayStartedSustaining;
        public int DayDroppedBelowThreshold = -1;

        // Dynamic City Growth
        public List<BuildingSaveData> ConstructedBuildings = new List<BuildingSaveData>();
        public List<ResourcePoolEntry> ResourcePools = new List<ResourcePoolEntry>();

        // Build Permits (granted by leaders to non-leaders)
        public List<BuildPermit> BuildPermits = new List<BuildPermit>();

        // Pending building claims (for buildings whose owner was absent at expansion time)
        public List<PendingBuildingClaim> PendingBuildingClaims = new List<PendingBuildingClaim>();

        public bool IsPredefinedMap;
        [NonSerialized] public int CurrentDailyPopulation;
        [NonSerialized] public bool IsHibernating;

        /// <summary>
        /// Grants a build permit allowing a character to place buildings in this zone.
        /// </summary>
        public void GrantPermit(string characterId, string leaderId, int count)
        {
            // Stack onto existing permit if one exists
            var existing = BuildPermits.Find(p => p.CharacterId == characterId && p.MapId == MapId);
            if (existing != null)
            {
                existing.RemainingPlacements += count;
                return;
            }

            BuildPermits.Add(new BuildPermit
            {
                CharacterId = characterId,
                GrantedByLeaderId = leaderId,
                RemainingPlacements = count,
                MapId = MapId
            });
        }

        /// <summary>
        /// Returns true if the character has an active build permit for this zone.
        /// </summary>
        public bool HasPermit(string characterId)
        {
            return BuildPermits.Exists(p => p.CharacterId == characterId && p.MapId == MapId && p.RemainingPlacements > 0);
        }

        /// <summary>
        /// Consumes one placement from the character's permit. Returns true if successful.
        /// </summary>
        public bool ConsumePermit(string characterId)
        {
            var permit = BuildPermits.Find(p => p.CharacterId == characterId && p.MapId == MapId && p.RemainingPlacements > 0);
            if (permit == null) return false;

            permit.RemainingPlacements--;
            if (permit.RemainingPlacements <= 0)
            {
                BuildPermits.Remove(permit);
            }
            return true;
        }
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

        public IReadOnlyList<CommunityData> GetAllCommunities() => _communities;

        /// <summary>
        /// Allows the recognized leader of a community to forcefully assign a job to a citizen.
        /// </summary>
        public bool ImposeJobOnCitizen(string mapId, string leaderId, Character citizen, Job job, CommercialBuilding building)
        {
            if (citizen == null || job == null || building == null) return false;

            CommunityData comm = GetCommunity(mapId);
            if (comm == null)
            {
                Debug.LogWarning($"<color=orange>[CommunityTracker]</color> Cannot impose job. Map {mapId} not found.");
                return false;
            }

            if (!comm.IsLeader(leaderId))
            {
                Debug.LogWarning($"<color=red>[CommunityTracker]</color> Character {leaderId} is not a recognized leader of {mapId}. Cannot impose job.");
                return false;
            }

            if (citizen.CharacterJob != null)
            {
                return citizen.CharacterJob.ForceAssignJob(job, building);
            }
            return false;
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
            ProcessPendingBuildingClaims();
        }

        private void EvaluatePopulations()
        {
            int currentDay = TimeManager.Instance.CurrentDay;
            float chunkSize = _settings != null ? _settings.ProximityChunkSize : 75f;

            Debug.Log($"<color=yellow>[CommunityTracker:Evaluate]</color> Day={currentDay}, ChunkSize={chunkSize}, Communities={_communities.Count}, PendingClusters={_pendingClusters.Count}");

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

            // Debug: Log unstructured chunk populations
            foreach (var kvp in unstructuredChunks)
            {
                Debug.Log($"<color=yellow>[CommunityTracker:Evaluate]</color> Unstructured chunk {kvp.Key}: {kvp.Value} NPCs");
            }
            foreach (var comm in _communities)
            {
                Debug.Log($"<color=yellow>[CommunityTracker:Evaluate]</color> Community '{comm.MapId}' (Tier={comm.Tier}, OriginChunk={comm.OriginChunk}): Pop={comm.CurrentDailyPopulation}");
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

            Debug.Log($"<color=yellow>[CommunityTracker:PendingClusters]</color> Processing {_pendingClusters.Count} pending clusters, {unstructuredChunks.Count} unstructured chunks with 3+ NPCs");

            // 1. Update existing pending clusters
            for (int i = _pendingClusters.Count - 1; i >= 0; i--)
            {
                var cluster = _pendingClusters[i];
                if (unstructuredChunks.TryGetValue(cluster.Chunk, out int pop))
                {
                    Debug.Log($"<color=yellow>[CommunityTracker:PendingClusters]</color> Cluster at {cluster.Chunk}: pop={pop}, age={currentDay - cluster.DayDiscovered} days (need 3+ pop and 3+ days)");
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

            Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> Promoting '{comm.MapId}' to Settlement. OriginChunk={comm.OriginChunk}, Day={currentDay}");

            if (WorldOffsetAllocator.Instance == null)
            {
                Debug.LogError("[CommunityTracker:Promote] WorldOffsetAllocator is missing! Cannot promote Settlement to a physical slot.");
                return;
            }

            // 1. Allocate Logical Slot
            comm.SlotIndex = WorldOffsetAllocator.Instance.AllocateSlotIndex();
            Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> Allocated SlotIndex={comm.SlotIndex}");

            float chunkSize = _settings != null ? _settings.ProximityChunkSize : 75f;
            Vector3 worldPos = new Vector3(
                comm.OriginChunk.x * chunkSize + (chunkSize / 2f),
                0,
                comm.OriginChunk.y * chunkSize + (chunkSize / 2f)
            );
            Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> WorldPos={worldPos}, ChunkSize={chunkSize}");

            // 2. Instantiate MapController
            if (_mapControllerPrefab != null)
            {
                Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> Instantiating MapController prefab '{_mapControllerPrefab.name}' at {worldPos}");
                GameObject mapObj = Instantiate(_mapControllerPrefab, worldPos, Quaternion.identity);
                MapController mapController = mapObj.GetComponent<MapController>();
                if (mapController != null)
                {
                    mapController.MapId = comm.MapId;
                    var netObj = mapObj.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Spawn();
                        Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> MapController spawned. MapId='{comm.MapId}', IsSpawned={netObj.IsSpawned}, GO active={mapObj.activeSelf}");
                    }
                    else
                    {
                        Debug.LogError($"<color=red>[CommunityTracker:Promote]</color> MapController prefab has NO NetworkObject!");
                    }

                    // Log trigger bounds
                    var trigger = mapObj.GetComponent<BoxCollider>();
                    if (trigger != null)
                    {
                        Debug.Log($"<color=magenta>[CommunityTracker:Promote]</color> MapController trigger: center={trigger.bounds.center}, extents={trigger.bounds.extents}, isTrigger={trigger.isTrigger}");
                    }
                }
                else
                {
                    Debug.LogError($"<color=red>[CommunityTracker:Promote]</color> MapController prefab has NO MapController component!");
                }

                // 2.5 Instantiate physical terrain prefab for the Settlement
                // This will stamp the village layout (tents, fences, fires) right onto the open world terrain
                GameObject terrainPrefab = _settings.GetPrefabForTier(comm.Tier);
                if (terrainPrefab != null)
                {
                    GameObject terrain = Instantiate(terrainPrefab, worldPos, Quaternion.identity);
                    terrain.transform.SetParent(mapObj.transform); // Attach to MapController
                    terrain.name = terrainPrefab.name + "_TerrainPrefab"; // Prevents MapController WakeUp duplication
                    if (terrain.TryGetComponent(out NetworkObject terrainNet))
                    {
                        terrainNet.Spawn();
                    }
                }

                // 3. Migrate founding NPCs
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
                        
                        // Set Map Tracking & Anchors to the centroid of the new settlement
                        if (c.TryGetComponent(out CharacterMapTracker tracker))
                        {
                            tracker.SetCurrentMap(comm.MapId);
                            tracker.HomeMapId.Value = comm.MapId;
                            tracker.HomePosition.Value = worldPos;
                        }
                        
                        // We DO NOT warp the NPCs. They are already standing here on the open world plane!
                        
                        // Assign the first migrated NPC as the Community Leader (primary)
                        if (string.IsNullOrEmpty(comm.LeaderNpcId))
                        {
                            comm.AddLeader(c.CharacterName);
                            Debug.Log($"<color=magenta>[CommunityTracker]</color> Character '{c.CharacterName}' is now the PRIMARY LEADER of '{comm.MapId}'!");
                        }
                    }
                }

                // 4. Adopt existing buildings within the new map bounds
                AdoptExistingBuildings(mapController, comm);
            }
            else
            {
                Debug.LogWarning("[CommunityTracker] MapController prefab is not set. Map instantiated logically but not physically.");
            }

            Debug.Log($"<color=green>[CommunityTracker]</color> Roaming Camp promoted to SETTLEMENT! MapID: {comm.MapId} at Slot {comm.SlotIndex}");
        }

        // ────────────────────── Building Adoption ──────────────────────

        /// <summary>
        /// Discovers existing buildings within the MapController's bounds and adopts them
        /// into the community. Unowned buildings are auto-claimed. Owned buildings trigger
        /// negotiation or are queued as pending claims if the owner is absent.
        /// </summary>
        private void AdoptExistingBuildings(MapController mapController, CommunityData comm)
        {
            if (mapController == null) return;

            BoxCollider trigger = mapController.GetComponent<BoxCollider>();
            if (trigger == null) return;

            Collider[] colliders = Physics.OverlapBox(
                trigger.bounds.center,
                trigger.bounds.extents,
                Quaternion.identity
            );

            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
            HashSet<Building> processed = new HashSet<Building>();
            int adoptedCount = 0;

            foreach (var col in colliders)
            {
                Building building = col.GetComponent<Building>() ?? col.GetComponentInParent<Building>();
                if (building == null || !processed.Add(building)) continue;

                // Skip if already registered in any community
                bool alreadyOwned = false;
                foreach (var c in _communities)
                {
                    if (c.ConstructedBuildings.Exists(b => b.BuildingId == building.BuildingId))
                    {
                        alreadyOwned = true;
                        break;
                    }
                }
                if (alreadyOwned) continue;

                string ownerId = building.PlacedByCharacterId.Value.ToString();

                if (string.IsNullOrEmpty(ownerId))
                {
                    // No owner — auto-claim
                    building.transform.SetParent(mapController.transform);
                    comm.ConstructedBuildings.Add(BuildingSaveData.FromBuilding(building, mapController.transform.position));
                    adoptedCount++;
                }
                else
                {
                    // Has an owner — try to negotiate or queue pending claim
                    Character owner = Character.FindByUUID(ownerId);

                    if (owner != null && owner.IsAlive())
                    {
                        // Owner is present — a leader will negotiate via invitation
                        Character leader = FindLeaderCharacter(comm);
                        if (leader != null)
                        {
                            var negotiation = new InteractionNegotiateBuildingClaim(building, comm, mapController);
                            if (negotiation.CanExecute(leader, owner))
                            {
                                negotiation.Execute(leader, owner);
                            }
                        }
                    }
                    else
                    {
                        // Owner is absent — queue pending claim with timeout
                        comm.PendingBuildingClaims.Add(new PendingBuildingClaim
                        {
                            BuildingId = building.BuildingId,
                            OwnerCharacterId = ownerId,
                            DayClaimed = currentDay,
                            TimeoutDays = 7
                        });
                        Debug.Log($"<color=yellow>[CommunityTracker]</color> Queued pending claim for building '{building.BuildingName}' (owner absent). Auto-claim in 7 days.");
                    }
                }
            }

            if (adoptedCount > 0)
            {
                Debug.Log($"<color=green>[CommunityTracker]</color> Adopted {adoptedCount} unowned building(s) into Settlement '{comm.MapId}'.");
            }
        }

        /// <summary>
        /// Finds the live Character object for the primary leader of a community.
        /// </summary>
        private Character FindLeaderCharacter(CommunityData comm)
        {
            if (string.IsNullOrEmpty(comm.LeaderNpcId)) return null;
            return Character.FindByUUID(comm.LeaderNpcId);
        }

        /// <summary>
        /// Processes pending building claims across all communities.
        /// Auto-claims buildings whose timeout has expired.
        /// Attempts negotiation if the owner has returned.
        /// </summary>
        private void ProcessPendingBuildingClaims()
        {
            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;

            foreach (var comm in _communities)
            {
                if (comm.PendingBuildingClaims == null || comm.PendingBuildingClaims.Count == 0) continue;

                MapController map = MapController.GetByMapId(comm.MapId);

                for (int i = comm.PendingBuildingClaims.Count - 1; i >= 0; i--)
                {
                    var claim = comm.PendingBuildingClaims[i];

                    // Check if the building was already claimed by other means
                    if (comm.ConstructedBuildings.Exists(b => b.BuildingId == claim.BuildingId))
                    {
                        comm.PendingBuildingClaims.RemoveAt(i);
                        continue;
                    }

                    // Auto-claim after timeout
                    if (currentDay - claim.DayClaimed >= claim.TimeoutDays)
                    {
                        AutoClaimPendingBuilding(claim, comm, map);
                        comm.PendingBuildingClaims.RemoveAt(i);
                        continue;
                    }

                    // If the owner has returned, attempt negotiation
                    Character owner = Character.FindByUUID(claim.OwnerCharacterId);
                    if (owner != null && owner.IsAlive() && map != null)
                    {
                        Character leader = FindLeaderCharacter(comm);
                        Building building = FindLiveBuildingById(claim.BuildingId);
                        if (leader != null && building != null)
                        {
                            var negotiation = new InteractionNegotiateBuildingClaim(building, comm, map);
                            if (negotiation.CanExecute(leader, owner))
                            {
                                negotiation.Execute(leader, owner);
                                comm.PendingBuildingClaims.RemoveAt(i);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Auto-claims a building after the pending claim timeout expires.
        /// </summary>
        private void AutoClaimPendingBuilding(PendingBuildingClaim claim, CommunityData comm, MapController map)
        {
            Building building = FindLiveBuildingById(claim.BuildingId);
            if (building == null)
            {
                Debug.LogWarning($"<color=orange>[CommunityTracker]</color> Pending claim auto-expired but building '{claim.BuildingId}' no longer exists.");
                return;
            }

            if (map != null)
            {
                building.transform.SetParent(map.transform);
            }

            comm.ConstructedBuildings.Add(BuildingSaveData.FromBuilding(building, map != null ? map.transform.position : Vector3.zero));
            Debug.Log($"<color=green>[CommunityTracker]</color> Auto-claimed building '{building.BuildingName}' into community '{comm.MapId}' (owner timeout).");
        }

        /// <summary>
        /// Finds a live Building instance by its BuildingId across all spawned buildings.
        /// </summary>
        private Building FindLiveBuildingById(string buildingId)
        {
            if (BuildingManager.Instance == null) return null;
            return BuildingManager.Instance.FindBuildingById(buildingId);
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
