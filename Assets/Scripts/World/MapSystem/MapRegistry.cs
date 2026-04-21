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
    public class MapRegistrySaveData
    {
        public List<CommunityData> Communities = new List<CommunityData>();
    }

    /// <summary>
    /// Server-side registry that tracks all CommunityData entries (one per map) and coordinates
    /// building adoption + pending claim resolution. NPC-cluster auto-promotion has been removed;
    /// map creation now happens explicitly via CreateMapAtPosition (building placement) or via
    /// predefined map registration.
    /// </summary>
    public class MapRegistry : MonoBehaviour, ISaveable
    {
        public static MapRegistry Instance { get; private set; }

        [SerializeField] private WorldSettingsData _settings;
        [SerializeField] private GameObject _mapControllerPrefab;

        [SerializeField] private List<CommunityData> _communities = new List<CommunityData>();

        // IMPORTANT: Do not rename this literal. Save files on disk key on this string.
        // See ADR-0001 (wiki/decisions/adr-0001-living-world-hierarchy-refactor.md).
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
        /// Server-only. Spawns a fresh exterior MapController centered on the given world position
        /// and registers a matching CommunityData entry. Used by BuildingPlacementManager when a
        /// building is placed outside any existing map and no nearby map can be joined.
        /// </summary>
        /// <returns>The newly spawned MapController, or null on failure.</returns>
        public MapController CreateMapAtPosition(Vector3 worldPosition)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("<color=red>[MapRegistry:CreateMapAtPosition]</color> Must run on the server.");
                return null;
            }

            if (_mapControllerPrefab == null)
            {
                _mapControllerPrefab = Resources.Load<GameObject>("Prefabs/World/MapController");
                if (_mapControllerPrefab == null)
                {
                    Debug.LogError("<color=red>[MapRegistry:CreateMapAtPosition]</color> MapController prefab is not assigned and could not be loaded from Resources.");
                    return null;
                }
            }

            float chunkSize = _settings != null ? _settings.ProximityChunkSize : 75f;
            Vector2Int originChunk = new Vector2Int(
                Mathf.FloorToInt(worldPosition.x / chunkSize),
                Mathf.FloorToInt(worldPosition.z / chunkSize)
            );

            string mapId = $"Wild_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
            int slotIndex = WorldOffsetAllocator.Instance != null
                ? WorldOffsetAllocator.Instance.AllocateSlotIndex()
                : -1;
            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;

            // Pre-register the CommunityData so MapController.Start() does not auto-create
            // a RoamingCamp stub for this map. Tier is Settlement because a player (or NPC)
            // actively founded it by placing a building.
            CommunityData newCommunity = new CommunityData
            {
                MapId = mapId,
                SlotIndex = slotIndex,
                Tier = CommunityTier.Settlement,
                OriginChunk = originChunk,
                DayStartedSustaining = currentDay,
                IsPredefinedMap = false
            };
            AddCommunity(newCommunity);

            GameObject mapObj;
            try
            {
                mapObj = Instantiate(_mapControllerPrefab, worldPosition, Quaternion.identity);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _communities.Remove(newCommunity);
                return null;
            }

            MapController mapController = mapObj.GetComponent<MapController>();
            if (mapController == null)
            {
                Debug.LogError("<color=red>[MapRegistry:CreateMapAtPosition]</color> Prefab is missing a MapController component.");
                Destroy(mapObj);
                _communities.Remove(newCommunity);
                return null;
            }

            mapController.MapId = mapId;

            NetworkObject netObj = mapObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("<color=red>[MapRegistry:CreateMapAtPosition]</color> Prefab is missing a NetworkObject component.");
                Destroy(mapObj);
                _communities.Remove(newCommunity);
                return null;
            }

            try
            {
                netObj.Spawn();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Destroy(mapObj);
                _communities.Remove(newCommunity);
                return null;
            }

            Debug.Log($"<color=magenta>[MapRegistry:CreateMapAtPosition]</color> Wild map '{mapId}' spawned at {worldPosition} (slot={slotIndex}, chunk={originChunk}).");
            return mapController;
        }

        /// <summary>
        /// Allows the recognized leader of a community to forcefully assign a job to a citizen.
        /// </summary>
        public bool ImposeJobOnCitizen(string mapId, string leaderId, Character citizen, Job job, CommercialBuilding building)
        {
            if (citizen == null || job == null || building == null) return false;

            CommunityData comm = GetCommunity(mapId);
            if (comm == null)
            {
                Debug.LogWarning($"<color=orange>[MapRegistry]</color> Cannot impose job. Map {mapId} not found.");
                return false;
            }

            if (!comm.IsLeader(leaderId))
            {
                Debug.LogWarning($"<color=red>[MapRegistry]</color> Character {leaderId} is not a recognized leader of {mapId}. Cannot impose job.");
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
            ProcessPendingBuildingClaims();
        }

        // ────────────────────── Building Adoption ──────────────────────

        /// <summary>
        /// Discovers existing buildings within the MapController's bounds and adopts them
        /// into the community. Unowned buildings are auto-claimed. Owned buildings trigger
        /// negotiation or are queued as pending claims if the owner is absent.
        ///
        /// PRESERVED API — currently has no caller after the Phase 1 cluster-promotion rip
        /// (ADR-0001). It remains here because <see cref="CreateMapAtPosition"/> will invoke
        /// it when a player-placed building triggers wild-map creation near existing
        /// buildings. Do not delete without updating ADR-0001.
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
                        Debug.Log($"<color=yellow>[MapRegistry]</color> Queued pending claim for building '{building.BuildingName}' (owner absent). Auto-claim in 7 days.");
                    }
                }
            }

            if (adoptedCount > 0)
            {
                Debug.Log($"<color=green>[MapRegistry]</color> Adopted {adoptedCount} unowned building(s) into Settlement '{comm.MapId}'.");
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
                Debug.LogWarning($"<color=orange>[MapRegistry]</color> Pending claim auto-expired but building '{claim.BuildingId}' no longer exists.");
                return;
            }

            if (map != null)
            {
                building.transform.SetParent(map.transform);
            }

            comm.ConstructedBuildings.Add(BuildingSaveData.FromBuilding(building, map != null ? map.transform.position : Vector3.zero));
            Debug.Log($"<color=green>[MapRegistry]</color> Auto-claimed building '{building.BuildingName}' into community '{comm.MapId}' (owner timeout).");
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
            return new MapRegistrySaveData
            {
                Communities = new List<CommunityData>(_communities)
            };
        }

        public void RestoreState(object state)
        {
            if (state is MapRegistrySaveData data)
            {
                _communities = data.Communities ?? new List<CommunityData>();
                // Legacy CommunityTrackerSaveData may have contained PendingClusters;
                // those are silently discarded by JSON deserialization. Log so this is traceable.
                Debug.Log($"<color=cyan>[MapRegistry:RestoreState]</color> Restored {_communities.Count} communities. Legacy cluster data (if any) discarded.");
            }
        }

        #endregion
    }
}
