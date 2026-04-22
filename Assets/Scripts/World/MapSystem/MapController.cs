using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Unity.AI.Navigation;
using MWI.Terrain;
using MWI.Time;
using System.Linq;

namespace MWI.WorldSystem
{
    [RequireComponent(typeof(BoxCollider))]
    public class MapController : NetworkBehaviour
    {
        [Header("Map Settings")]
        [Tooltip("Unique ID matching the Decoupled Character Save (e.g. World_Aethelgard_Region_North)")]
        public string MapId;

        [SerializeField] private MapType _mapType = MapType.Region;
        public MapType Type => _mapType;

        [System.Obsolete("Use Type == MapType.Interior instead")]
        public bool IsInteriorOffset => _mapType == MapType.Interior;

        /// <summary>
        /// Sets the map type at runtime. Used by BuildingInteriorSpawner to mark
        /// dynamically spawned interiors before the NetworkObject is spawned.
        /// </summary>
        public void SetMapType(MapType mapType) => _mapType = mapType;

        [Header("Interior Exit Info (Replicated)")]
        [Tooltip("For interiors: the exterior map ID to return to. Set by BuildingInteriorSpawner.")]
        public NetworkVariable<FixedString128Bytes> ExteriorMapId = new NetworkVariable<FixedString128Bytes>(
            "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
        );

        [Tooltip("For interiors: the world position to warp to when exiting. Set by BuildingInteriorSpawner.")]
        public NetworkVariable<Vector3> ExteriorReturnPosition = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
        );

        [Tooltip("For interiors: the world position where players appear when entering (near exit door). Set by BuildingInteriorSpawner.")]
        public NetworkVariable<Vector3> InteriorEntryPosition = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
        );

        [Header("Runtime State")]
        public int ConnectedPlayersCount = 0;
        public bool IsHibernating = false;
        [SerializeField] private int _activePlayerCount = 0;

        [Header("Hibernation")]
        [Tooltip("Master switch for NPC/Building hibernation. When disabled, NPCs stay alive regardless of player presence.")]
        [SerializeField] private bool _hibernationEnabled = false;

        private BoxCollider _mapTrigger;
        public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(false);

        [Header("Biome & Map Type")]
        [Tooltip("The biome defining resource density and yields for Offline Growth.")]
        public BiomeDefinition Biome;

        [Header("Simulation")]
        [Tooltip("The registry for offline job yields.")]
        public JobYieldRegistry JobYields;

        [Tooltip("If true, the terrain is hand-crafted and not procedurally spawned by the MapPrefabRegistry.")]
        public bool IsPredefinedMap;

        private HashSet<ulong> _activePlayers = new HashSet<ulong>();
        private MapSaveData _hibernationData;

        // Exposed for Debug UI
        public System.Collections.Generic.IEnumerable<ulong> ActivePlayers => _activePlayers;
        public MapSaveData HibernationData => _hibernationData;
        // Dependencies
        private TimeManager _timeManager => TimeManager.Instance;

        // --- Static MapId -> MapController registry for fast lookup ---
        private static readonly Dictionary<string, MapController> _mapRegistry = new Dictionary<string, MapController>();

        /// <summary>
        /// All MapControllers that currently have at least one active player (i.e., are NOT hibernating).
        /// Used by SaveManager to snapshot live NPC state on world save without despawning.
        /// </summary>
        public static HashSet<MapController> ActiveControllers = new HashSet<MapController>();

        /// <summary>
        /// Pending NPC snapshots loaded from a save file, keyed by MapId.
        /// When a MapController initializes and finds a matching entry, it spawns those NPCs
        /// and removes the entry.
        /// </summary>
        public static Dictionary<string, MapSaveData> PendingSnapshots = new Dictionary<string, MapSaveData>();

        /// <summary>
        /// Looks up a MapController by its MapId. Returns null if not found.
        /// </summary>
        public static MapController GetByMapId(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            _mapRegistry.TryGetValue(mapId, out var controller);
            return controller;
        }

        /// <summary>
        /// Finds the exterior MapController whose trigger bounds contain the given world position.
        /// Returns null if no map contains the position (open world).
        /// </summary>
        public static MapController GetMapAtPosition(Vector3 worldPosition)
        {
            foreach (var kvp in _mapRegistry)
            {
                MapController map = kvp.Value;
                if (map == null || map.IsInteriorOffset) continue;
                if (map._mapTrigger != null && map._mapTrigger.bounds.Contains(worldPosition))
                {
                    return map;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the nearest non-interior MapController whose trigger bounds lie within
        /// <paramref name="maxDistance"/> world units of the given position, or null if none qualify.
        /// Distance is measured from the trigger's closest surface point, so a position just outside
        /// a large map is correctly picked up.
        /// </summary>
        public static MapController GetNearestExteriorMap(Vector3 worldPosition, float maxDistance)
        {
            MapController best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (var kvp in _mapRegistry)
            {
                MapController map = kvp.Value;
                if (map == null) continue;
                if (map.Type == MapType.Interior) continue;

                Vector3 refPoint = map._mapTrigger != null
                    ? map._mapTrigger.ClosestPoint(worldPosition)
                    : map.transform.position;
                float sqr = (refPoint - worldPosition).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = map;
                }
            }
            return best;
        }

        /// <summary>
        /// Notifies source and destination MapControllers about a player transition.
        /// Ensures hibernation/wake-up triggers before physics colliders update.
        /// </summary>
        public static void NotifyPlayerTransition(ulong clientId, string fromMapId, string toMapId)
        {
            var sourceMap = GetByMapId(fromMapId);
            if (sourceMap != null)
            {
                sourceMap.ForcePlayerTransition(clientId, entering: false);
            }

            var destMap = GetByMapId(toMapId);
            if (destMap != null)
            {
                destMap.ForcePlayerTransition(clientId, entering: true);
            }
        }

        private void Awake()
        {
            _mapTrigger = GetComponent<BoxCollider>();
            _mapTrigger.isTrigger = true;
            ScopeNavMeshToMapBounds();
        }

        /// <summary>
        /// Syncs the NavMeshSurface's volume bounds to this MapController's BoxCollider
        /// so runtime bakes (triggered by Building.RebuildNavMesh) only cover this map's
        /// area instead of the entire scene. Prevents multiple MapControllers from each
        /// baking overlapping world-wide navmesh layers.
        /// </summary>
        private void ScopeNavMeshToMapBounds()
        {
            if (_mapTrigger == null) return;
            var surface = GetComponent<NavMeshSurface>();
            if (surface == null) return;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = _mapTrigger.center;
            surface.size = _mapTrigger.size;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Register in the static lookup so any code can find this map by ID
            if (!string.IsNullOrEmpty(MapId))
            {
                _mapRegistry[MapId] = this;
                Debug.Log($"<color=cyan>[MapController:OnNetworkSpawn]</color> Registered '{MapId}' in _mapRegistry. Total maps: {_mapRegistry.Count}");
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[MapController:OnNetworkSpawn]</color> MapId is EMPTY! This map won't be findable by GetMapAtPosition or GetByMapId. GameObject='{gameObject.name}'");
            }

            if (!IsServer) return;

            // Subscribe to NetworkManager disconnects to ensure _activePlayers doesn't drift
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;

            // Detect players already overlapping this trigger (e.g. map spawned on top of them)
            DetectOverlappingPlayers();

            // Delay the initial hibernation check to give OnTriggerEnter time to fire
            Debug.Log($"<color=cyan>[MapController:OnNetworkSpawn]</color> Scheduling CheckHibernationState in 3s for '{MapId}'. ActivePlayers={_activePlayers.Count}");
            Invoke(nameof(CheckHibernationState), 3f);

            // Consume any pending NPC snapshot from a previous save/load cycle
            if (PendingSnapshots.TryGetValue(MapId, out var pendingSnapshot))
            {
                Debug.Log($"<color=green>[MapController:OnNetworkSpawn]</color> Found pending NPC snapshot for '{MapId}' with {pendingSnapshot.HibernatedNPCs.Count} NPCs. Spawning...");
                SpawnNPCsFromSnapshot(pendingSnapshot);
                PendingSnapshots.Remove(MapId);
            }
        }

        private void Start()
        {
            if (IsServer)
            {
                if (_timeManager != null)
                {
                    _timeManager.OnNewDay += OnNewDay;
                }

                // Ensure this map has a CommunityData entry so buildings can be saved
                EnsureCommunityData();

                if (!IsHibernating)
                {
                    SpawnVirtualBuildings();
                }
            }
        }

        /// <summary>
        /// Ensures a CommunityData entry exists for this map in MapRegistry.
        /// Predefined maps and dynamically spawned maps both need this for building persistence.
        /// </summary>
        private void EnsureCommunityData()
        {
            if (string.IsNullOrEmpty(MapId)) return;
            if (MapRegistry.Instance == null) return;

            CommunityData existing = MapRegistry.Instance.GetCommunity(MapId);
            if (existing != null) return;

            // Auto-create a CommunityData entry for this map
            // Set OriginChunk from actual position so NPC proximity matching works correctly
            float chunkSize = 75f; // Match MapRegistry default
            Vector2Int originChunk = new Vector2Int(
                Mathf.FloorToInt(transform.position.x / chunkSize),
                Mathf.FloorToInt(transform.position.z / chunkSize)
            );
            CommunityData newCommunity = new CommunityData
            {
                MapId = this.MapId,
                Tier = IsPredefinedMap ? CommunityTier.EstablishedCity : CommunityTier.RoamingCamp,
                IsPredefinedMap = this.IsPredefinedMap,
                OriginChunk = originChunk
            };

            MapRegistry.Instance.AddCommunity(newCommunity);
            Debug.Log($"<color=green>[MapController]</color> Auto-created CommunityData for map '{MapId}' (IsPredefined={IsPredefinedMap}, Tier={newCommunity.Tier}).");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            Debug.LogWarning($"<color=red>[MapController:OnNetworkDespawn]</color> Map '{MapId}' is being DESPAWNED! Stack trace will follow.");
            Debug.LogWarning($"<color=red>[MapController:OnNetworkDespawn]</color> StackTrace: {System.Environment.StackTrace}");

            // Unregister from static lookup
            if (!string.IsNullOrEmpty(MapId) && _mapRegistry.TryGetValue(MapId, out var registered) && registered == this)
            {
                _mapRegistry.Remove(MapId);
            }
            ActiveControllers.Remove(this);

            if (!IsServer) return;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
            }

            if (_timeManager != null)
            {
                _timeManager.OnNewDay -= OnNewDay;
            }
        }

        #region Player Tracking

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            // Only track actual PlayerObjects, not NPCs or random physics objects
            if (other.CompareTag("Character") && other.TryGetComponent(out Character character))
            {
                if (character.NetworkObject != null && character.NetworkObject.IsPlayerObject)
                {
                    AddPlayer(character.OwnerClientId);

                    // Update the character's tracker so CurrentMapID reflects "I'm in this map now".
                    // Door transitions also set this explicitly; this path covers plain walking
                    // between maps on the same plane (e.g., entering a wild map in a Region).
                    var tracker = character.GetComponent<CharacterMapTracker>();
                    if (tracker != null && tracker.CurrentMapID.Value.ToString() != MapId)
                    {
                        tracker.SetCurrentMap(MapId);
                    }
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;

            if (other.CompareTag("Character") && other.TryGetComponent(out Character character))
            {
                if (character.NetworkObject != null && character.NetworkObject.IsPlayerObject)
                {
                    RemovePlayer(character.OwnerClientId);

                    // Clear the tracker only if it still references THIS map. If the player
                    // entered an adjacent map in the same frame, that map's OnTriggerEnter
                    // may already have set CurrentMapID; don't clobber it.
                    var tracker = character.GetComponent<CharacterMapTracker>();
                    if (tracker != null && tracker.CurrentMapID.Value.ToString() == MapId)
                    {
                        tracker.SetCurrentMap("");
                    }
                }
            }
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            // If a client disconnects unexpectedly, remove them from this map 
            // even if OnTriggerExit never fired.
            RemovePlayer(clientId);
        }

        private void AddPlayer(ulong clientId)
        {
            if (_activePlayers.Add(clientId))
            {
                _activePlayerCount = _activePlayers.Count;
                Debug.Log($"<color=cyan>[MapController]</color> Player {clientId} entered '{MapId}'. Total: {_activePlayerCount}");
                ActiveControllers.Add(this);
                CheckWakeUp();
            }
        }

        private void RemovePlayer(ulong clientId)
        {
            if (_activePlayers.Remove(clientId))
            {
                _activePlayerCount = _activePlayers.Count;
                Debug.Log($"<color=cyan>[MapController]</color> Player {clientId} left '{MapId}'. Total: {_activePlayerCount}");
                if (_activePlayers.Count == 0)
                {
                    ActiveControllers.Remove(this);
                }
                CheckHibernationState();
            }
        }

        /// <summary>
        /// Scans for player characters already inside this map's trigger bounds.
        /// Required because Unity's OnTriggerEnter doesn't fire for objects that
        /// were already overlapping when the trigger was created (e.g. settlement promotion).
        /// </summary>
        public void DetectOverlappingPlayers()
        {
            if (_mapTrigger == null)
            {
                Debug.LogError($"<color=red>[MapController:DetectOverlapping]</color> _mapTrigger is NULL for '{MapId}'!");
                return;
            }

            Debug.Log($"<color=cyan>[MapController:DetectOverlapping]</color> Scanning for players in '{MapId}'. TriggerCenter={_mapTrigger.bounds.center}, TriggerExtents={_mapTrigger.bounds.extents}");

            Collider[] overlapping = Physics.OverlapBox(
                _mapTrigger.bounds.center,
                _mapTrigger.bounds.extents,
                Quaternion.identity
            );

            Debug.Log($"<color=cyan>[MapController:DetectOverlapping]</color> OverlapBox found {overlapping.Length} colliders.");

            int playersFound = 0;
            foreach (var col in overlapping)
            {
                if (col.CompareTag("Character") && col.TryGetComponent(out Character character))
                {
                    bool isPlayer = character.NetworkObject != null && character.NetworkObject.IsPlayerObject;
                    Debug.Log($"<color=cyan>[MapController:DetectOverlapping]</color> Character '{character.CharacterName}' at {character.transform.position} — IsPlayer={isPlayer}");

                    if (isPlayer)
                    {
                        playersFound++;
                        if (_activePlayers.Add(character.OwnerClientId))
                        {
                            _activePlayerCount = _activePlayers.Count;
                            Debug.Log($"<color=cyan>[MapController:DetectOverlapping]</color> Added player {character.OwnerClientId} to '{MapId}'. Total={_activePlayerCount}");
                        }
                    }
                }
            }

            Debug.Log($"<color=cyan>[MapController:DetectOverlapping]</color> Result for '{MapId}': {playersFound} players found, {_activePlayers.Count} active.");

            if (_activePlayers.Count > 0)
            {
                CheckWakeUp();
            }
        }

        #endregion

        #region Resource Harvesting & Virtual Buildings

        public ResourcePoolEntry GetResourcePool(string resourceId)
        {
            if (MapRegistry.Instance == null) return null;
            var community = MapRegistry.Instance.GetCommunity(MapId);
            if (community == null || community.ResourcePools == null) return null;

            return community.ResourcePools.Find(p => p.ResourceId == resourceId);
        }

        private void SpawnVirtualBuildings()
        {
            if (Biome == null || Biome.Harvestables == null) return;
            
            // Clean up any existing ones first
            foreach (Transform child in transform)
            {
                if (child.GetComponent<VirtualResourceSupplier>() != null)
                {
                    Destroy(child.gameObject);
                }
            }

            foreach (var resDef in Biome.Harvestables)
            {
                GameObject supplierGO = new GameObject($"VirtualResourceSupplier_{resDef.ResourceId}");
                supplierGO.transform.SetParent(this.transform);
                var supplier = supplierGO.AddComponent<VirtualResourceSupplier>();
                supplier.Initialize(resDef.ResourceId, this);
            }
        }

        private void OnNewDay()
        {
            if (Biome == null || Biome.Harvestables == null) return;
            
            CommunityData community = null;
            if (MapRegistry.Instance != null)
            {
                community = MapRegistry.Instance.GetCommunity(MapId);
            }

            if (community?.ResourcePools == null) return;

            foreach (var pool in community.ResourcePools)
            {
                var entry = Biome.Harvestables.FirstOrDefault(h => h.ResourceId == pool.ResourceId);
                if (entry == null) continue;

                double daysSinceHarvest = _timeManager.CurrentDay - (pool.LastHarvestedDay > 0 ? pool.LastHarvestedDay : _timeManager.CurrentDay);
                if (daysSinceHarvest >= entry.RegenerationDays || entry.RegenerationDays == 0)
                {
                    // Regenerate by BaseYieldQuantity
                    pool.CurrentAmount = Mathf.Min(pool.CurrentAmount + Mathf.CeilToInt(entry.BaseYieldQuantity), pool.MaxAmount);
                    pool.LastHarvestedDay = _timeManager.CurrentDay;
                }
            }
        }

        #endregion

        #region Hibernation Logic

        private void CheckHibernationState()
        {
            if (!_hibernationEnabled) return;

            Debug.Log($"<color=cyan>[MapController:CheckHibernation]</color> Map '{MapId}': ActivePlayers={_activePlayers.Count}, IsHibernating={IsHibernating}");
            if (_activePlayers.Count == 0 && !IsHibernating)
            {
                Hibernate();
            }
        }

        private void CheckWakeUp()
        {
            if (!_hibernationEnabled) return;

            if (_activePlayers.Count > 0 && IsHibernating)
            {
                WakeUp();
            }
        }

        private double GetAbsoluteTimeInDays()
        {
            if (_timeManager == null) return 0;
            // Absolute time formula: (Full Days Passed) + (Fraction of Current Day)
            return _timeManager.CurrentDay + _timeManager.CurrentTime01;
        }

        /// <summary>
        /// Serializes all live NPCs on this map into a MapSaveData snapshot WITHOUT despawning them.
        /// Used by SaveManager during world save to capture NPC state on active (non-hibernating) maps
        /// so NPCs are not lost if the player quits and reloads.
        /// </summary>
        /// <summary>
        /// Spawns NPCs from PendingSnapshots for this map, if any exist.
        /// Called by GameLauncher after LoadWorldAsync populates PendingSnapshots.
        /// </summary>
        public void SpawnNPCsFromPendingSnapshot()
        {
            if (!IsServer) return;
            if (PendingSnapshots.TryGetValue(MapId, out var snapshot))
            {
                Debug.Log($"<color=green>[MapController]</color> Spawning {snapshot.HibernatedNPCs.Count} NPC(s) from pending snapshot for '{MapId}'.");
                SpawnNPCsFromSnapshot(snapshot);
                PendingSnapshots.Remove(MapId);
            }
        }

        public MapSaveData SnapshotActiveNPCs()
        {
            var snapshot = new MapSaveData()
            {
                MapId = this.MapId,
                LastHibernationTime = GetAbsoluteTimeInDays()
            };

            if (_mapTrigger == null)
            {
                Debug.LogError($"<color=red>[MapController:SnapshotActiveNPCs]</color> _mapTrigger is NULL for '{MapId}'! Cannot snapshot.");
                return snapshot;
            }

            Collider[] colliders = Physics.OverlapBox(_mapTrigger.bounds.center, _mapTrigger.bounds.extents, Quaternion.identity);
            Debug.Log($"<color=cyan>[MapController:SnapshotActiveNPCs]</color> OverlapBox found {colliders.Length} colliders in '{MapId}'. Bounds center={_mapTrigger.bounds.center}, extents={_mapTrigger.bounds.extents}");

            int characterTagCount = 0;
            int playerSkipCount = 0;
            foreach (var col in colliders)
            {
                if (col.CompareTag("Character"))
                {
                    characterTagCount++;
                    if (!col.TryGetComponent(out Character npc))
                    {
                        Debug.LogWarning($"<color=orange>[MapController:SnapshotActiveNPCs]</color> Collider '{col.gameObject.name}' has Character tag but no Character component!");
                        continue;
                    }

                    // Ignore Players — only snapshot NPCs
                    if (npc.NetworkObject != null && npc.NetworkObject.IsPlayerObject)
                    {
                        playerSkipCount++;
                        continue;
                    }

                    HibernatedNPCData npcData = new HibernatedNPCData()
                    {
                        CharacterId = npc.CharacterId,
                        PrefabName = npc.gameObject.name.Replace("(Clone)", "").Trim(),
                        PrefabHash = npc.NetworkObject != null ? npc.NetworkObject.PrefabIdHash : 0,
                        Position = npc.transform.position,
                        Rotation = npc.transform.rotation,
                        // Identity & Visuals
                        RaceId = npc.NetworkRaceId.Value.ToString(),
                        CharacterName = npc.NetworkCharacterName.Value.ToString(),
                        VisualSeed = npc.NetworkVisualSeed.Value,
                        // Abandoned NPC tracking
                        IsAbandoned = npc.IsAbandoned,
                        FormerPartyLeaderId = npc.FormerPartyLeaderId,
                        FormerPartyLeaderWorldGuid = npc.FormerPartyLeaderWorldGuid
                    };

                    // Extract Tier 1 V2 GOAP Anchors
                    if (npc.TryGetComponent(out CharacterMapTracker tracker))
                    {
                        npcData.HomeMapId = tracker.HomeMapId.Value.ToString();
                        npcData.HomePosition = tracker.HomePosition.Value;
                    }

                    // Extract Build/Harvest Flags and JobType
                    if (npc.TryGetComponent(out CharacterJob charJob))
                    {
                        npcData.HasHarvesterJob = charJob.HasJobOfType<JobHarvester>();

                        var currentJob = charJob.CurrentJob;
                        npcData.SavedJobType = currentJob != null ? currentJob.Type : JobType.None;
                    }

                    // Extract Blueprint Knowledge
                    if (npc.TryGetComponent(out CharacterBlueprints blueprints))
                    {
                        npcData.UnlockedBuildingIds.AddRange(blueprints.UnlockedBuildingIds);
                    }

                    // Extract Needs
                    if (npc.CharacterNeeds != null)
                    {
                        foreach (var need in npc.CharacterNeeds.AllNeeds)
                        {
                            npcData.SavedNeeds.Add(new HibernatedNeedData
                            {
                                NeedType = need.GetType().Name,
                                Value = need.CurrentValue
                            });
                        }
                    }

                    // Extract Party membership
                    if (npc.CharacterParty != null && npc.CharacterParty.IsInParty)
                        npcData.PartyId = npc.CharacterParty.PartyData.PartyId;

                    snapshot.HibernatedNPCs.Add(npcData);
                }
            }

            Debug.Log($"<color=cyan>[MapController:SnapshotActiveNPCs]</color> Map '{MapId}' snapshot complete. " +
                      $"{snapshot.HibernatedNPCs.Count} NPCs serialized, {characterTagCount} Character-tagged colliders found, {playerSkipCount} players skipped.");
            return snapshot;
        }

        /// <summary>
        /// Syncs all live buildings on this active map into CommunityData.ConstructedBuildings
        /// WITHOUT despawning them. Called during world save so buildings persist through save/load.
        /// Mirrors the building serialization in Hibernate() but skips the despawn step.
        /// </summary>
        public void SnapshotActiveBuildings()
        {
            if (MapRegistry.Instance == null)
            {
                Debug.LogWarning($"<color=orange>[MapController:SnapshotActiveBuildings]</color> MapRegistry.Instance is null for '{MapId}' — cannot snapshot buildings.");
                return;
            }

            var community = MapRegistry.Instance.GetCommunity(MapId);
            if (community == null)
            {
                Debug.LogWarning($"<color=orange>[MapController:SnapshotActiveBuildings]</color> No CommunityData for MapId='{MapId}' — cannot snapshot buildings.");
                return;
            }

            if (_mapTrigger == null)
            {
                Debug.LogWarning($"<color=orange>[MapController:SnapshotActiveBuildings]</color> _mapTrigger is NULL for '{MapId}'!");
                return;
            }

            Collider[] colliders = Physics.OverlapBox(_mapTrigger.bounds.center, _mapTrigger.bounds.extents, Quaternion.identity);
            HashSet<Building> processedBuildings = new HashSet<Building>();
            int syncedCount = 0;

            foreach (var col in colliders)
            {
                Building building = col.GetComponent<Building>() ?? col.GetComponentInParent<Building>();
                if (building == null || !processedBuildings.Add(building)) continue;

                // Skip preplaced buildings — they exist in the scene and don't need saving.
                // Only player-placed buildings (via BuildingPlacementManager) have PlacedByCharacterId set.
                if (building.PlacedByCharacterId.Value.IsEmpty) continue;

                var saveEntry = community.ConstructedBuildings.Find(b => b.BuildingId == building.BuildingId);
                if (saveEntry != null)
                {
                    // Update existing entry with current state. Replace dynamic fields
                    // (owners, employees) wholesale so changes since last snapshot stick.
                    var refreshed = BuildingSaveData.FromBuilding(building, transform.position);
                    saveEntry.State = refreshed.State;
                    saveEntry.Position = refreshed.Position;
                    saveEntry.Rotation = refreshed.Rotation;
                    saveEntry.OwnerCharacterIds = refreshed.OwnerCharacterIds;
                    saveEntry.Employees = refreshed.Employees;
                }
                else
                {
                    // Auto-register untracked building
                    community.ConstructedBuildings.Add(BuildingSaveData.FromBuilding(building, transform.position));
                }
                syncedCount++;
            }

            Debug.Log($"<color=cyan>[MapController:SnapshotActiveBuildings]</color> Map '{MapId}': synced {syncedCount} building(s) into CommunityData (NOT despawned).");
        }

        /// <summary>
        /// Spawns buildings from CommunityData.ConstructedBuildings for this map.
        /// Used by predefined maps on world load (they never WakeUp) and by WakeUp itself.
        /// Skips buildings that already exist in the scene (checks by BuildingId).
        /// </summary>
        public void SpawnSavedBuildings()
        {
            if (MapRegistry.Instance == null) return;

            var community = MapRegistry.Instance.GetCommunity(MapId);
            if (community == null || community.ConstructedBuildings == null || community.ConstructedBuildings.Count == 0)
            {
                Debug.Log($"<color=cyan>[MapController:SpawnSavedBuildings]</color> No buildings to spawn for '{MapId}'.");
                return;
            }

            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            if (settings == null)
            {
                Debug.LogError($"<color=red>[MapController:SpawnSavedBuildings]</color> WorldSettingsData not found!");
                return;
            }

            // Collect ALL existing building PrefabIds in the scene to avoid duplicating preplaced buildings.
            // Preplaced buildings generate new BuildingIds each session, so we can't match by ID.
            // Instead, match by PrefabId + approximate position to detect preplaced buildings.
            var existingBuildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
            var existingBuildingIds = new HashSet<string>();
            foreach (var building in existingBuildings)
            {
                if (!string.IsNullOrEmpty(building.BuildingId))
                    existingBuildingIds.Add(building.BuildingId);
            }

            int spawnedCount = 0;
            foreach (var bSave in community.ConstructedBuildings)
            {
                // Skip if this building already exists in the scene (e.g., preplaced building)
                if (existingBuildingIds.Contains(bSave.BuildingId))
                {
                    Debug.Log($"<color=cyan>[MapController:SpawnSavedBuildings]</color> Skipping '{bSave.PrefabId}' (ID={bSave.BuildingId}) — already in scene.");
                    continue;
                }

                GameObject bPrefab = settings.GetBuildingPrefab(bSave.PrefabId);
                if (bSave.State == BuildingState.UnderConstruction && settings.GenericScaffoldPrefab != null)
                    bPrefab = settings.GenericScaffoldPrefab;

                if (bPrefab != null)
                {
                    Vector3 worldPos = transform.position + bSave.Position;
                    GameObject bObj = Instantiate(bPrefab, worldPos, bSave.Rotation);
                    if (bObj.TryGetComponent(out NetworkObject bNet))
                    {
                        bNet.Spawn();
                        bObj.transform.SetParent(this.transform);

                        Building restoredBuilding = bObj.GetComponent<Building>();
                        if (restoredBuilding != null)
                        {
                            restoredBuilding.NetworkBuildingId.Value = bSave.BuildingId;

                            // Restore who originally placed the building (saved but previously dropped on load).
                            if (!string.IsNullOrEmpty(bSave.PlacedByCharacterId))
                                restoredBuilding.PlacedByCharacterId.Value = new Unity.Collections.FixedString64Bytes(bSave.PlacedByCharacterId);

                            // Restore boss + crew. Owner field also covers ResidentialBuilding via the Building.OwnerIds path.
                            if (restoredBuilding is CommercialBuilding commercial)
                                commercial.RestoreFromSaveData(bSave.OwnerCharacterIds, bSave.Employees);
                        }
                    }
                    spawnedCount++;
                }
                else
                {
                    Debug.LogError($"<color=red>[MapController:SpawnSavedBuildings]</color> Could not find prefab for PrefabId='{bSave.PrefabId}'!");
                }
            }

            Debug.Log($"<color=cyan>[MapController:SpawnSavedBuildings]</color> Map '{MapId}': spawned {spawnedCount} building(s) from save data.");
        }

        /// <summary>
        /// Spawns NPCs from a MapSaveData snapshot. Used both by WakeUp (from hibernation) and
        /// by OnNetworkSpawn (from a PendingSnapshot loaded from save file).
        /// Does NOT run MacroSimulator catch-up — call that separately if needed.
        /// </summary>
        private void SpawnNPCsFromSnapshot(MapSaveData snapshotData)
        {
            if (snapshotData == null || snapshotData.HibernatedNPCs.Count == 0) return;

            foreach (var npcData in snapshotData.HibernatedNPCs)
            {
                GameObject prefab = null;

                // Use NGO's pre-loaded prefab registry instead of disk I/O
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
                {
                    foreach (var networkPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                    {
                        if (networkPrefab.Prefab != null)
                        {
                            // Priority 1: Match by NGO hash
                            if (npcData.PrefabHash != 0 && networkPrefab.Prefab.TryGetComponent(out NetworkObject prefabNetObj) && prefabNetObj.PrefabIdHash == npcData.PrefabHash)
                            {
                                prefab = networkPrefab.Prefab;
                                break;
                            }

                            // Priority 2: Fallback to exact name match
                            if (prefab == null && networkPrefab.Prefab.name == npcData.PrefabName)
                            {
                                prefab = networkPrefab.Prefab;
                            }
                        }
                    }
                }

                if (prefab == null)
                {
                    Debug.LogError($"<color=red>[MapController:SpawnNPCsFromSnapshot]</color> Could not find prefab '{npcData.PrefabName}' (hash={npcData.PrefabHash}) for NPC '{npcData.CharacterId}' on map '{MapId}'!");
                    continue;
                }

                GameObject inst = Instantiate(prefab, npcData.Position, npcData.Rotation);

                // Inject blueprint knowledge back
                if (inst.TryGetComponent(out CharacterBlueprints blueprints))
                {
                    blueprints.SetUnlockedBuildings(npcData.UnlockedBuildingIds);
                }

                // Restore identity & visual data BEFORE spawn so OnNetworkSpawn reads correct values
                if (inst.TryGetComponent(out Character spawnedChar))
                {
                    if (!string.IsNullOrEmpty(npcData.CharacterId))
                        spawnedChar.NetworkCharacterId.Value = new Unity.Collections.FixedString64Bytes(npcData.CharacterId);
                    if (!string.IsNullOrEmpty(npcData.RaceId))
                        spawnedChar.NetworkRaceId.Value = new Unity.Collections.FixedString64Bytes(npcData.RaceId);
                    if (!string.IsNullOrEmpty(npcData.CharacterName))
                        spawnedChar.NetworkCharacterName.Value = new Unity.Collections.FixedString64Bytes(npcData.CharacterName);
                    if (npcData.VisualSeed != 0)
                        spawnedChar.NetworkVisualSeed.Value = npcData.VisualSeed;

                    // Restore abandoned NPC state
                    if (npcData.IsAbandoned)
                    {
                        spawnedChar.IsAbandoned = true;
                        spawnedChar.FormerPartyLeaderId = npcData.FormerPartyLeaderId;
                        spawnedChar.FormerPartyLeaderWorldGuid = npcData.FormerPartyLeaderWorldGuid;
                    }

                    // Inject saved needs back
                    if (spawnedChar.CharacterNeeds != null)
                    {
                        foreach (var savedNeed in npcData.SavedNeeds)
                        {
                            var liveNeed = spawnedChar.CharacterNeeds.AllNeeds.FirstOrDefault(n => n.GetType().Name == savedNeed.NeedType);
                            if (liveNeed != null)
                            {
                                liveNeed.CurrentValue = savedNeed.Value;
                            }
                        }
                    }
                }

                if (inst.TryGetComponent(out NetworkObject netObj))
                {
                    netObj.Spawn(true);
                }
            }

            Debug.Log($"<color=green>[MapController:SpawnNPCsFromSnapshot]</color> Spawned {snapshotData.HibernatedNPCs.Count} NPCs from snapshot on map '{MapId}'.");
        }

        private void Hibernate()
        {
            if (IsHibernating) return;

            IsHibernating = true;
            ActiveControllers.Remove(this);
            Debug.Log($"<color=orange>[MapController:Hibernate]</color> Map '{MapId}' entering Hibernation.");

            // Ensure CommunityData exists before saving buildings
            EnsureCommunityData();

            _hibernationData = new MapSaveData()
            {
                MapId = this.MapId,
                LastHibernationTime = GetAbsoluteTimeInDays()
            };

            // 0. Serialize terrain cell data before NPC serialization
            var terrainGrid = GetComponent<TerrainCellGrid>();
            if (terrainGrid != null)
                _hibernationData.TerrainCells = terrainGrid.SerializeCells();

            // 1. Find all NPCs inside this Map
            Collider[] colliders = Physics.OverlapBox(_mapTrigger.bounds.center, _mapTrigger.bounds.extents, Quaternion.identity);
            Debug.Log($"<color=orange>[MapController:Hibernate]</color> OverlapBox found {colliders.Length} colliders in '{MapId}'.");
            
            foreach (var col in colliders)
            {
                if (col.CompareTag("Character") && col.TryGetComponent(out Character npc))
                {
                    // Ignore Players
                    if (npc.NetworkObject != null && npc.NetworkObject.IsPlayerObject) continue;

                    // Serialize to V1 Dumb Data
                    HibernatedNPCData npcData = new HibernatedNPCData()
                    {
                        CharacterId = npc.CharacterId,
                        PrefabName = npc.gameObject.name.Replace("(Clone)", "").Trim(),
                        PrefabHash = npc.NetworkObject != null ? npc.NetworkObject.PrefabIdHash : 0,
                        Position = npc.transform.position,
                        Rotation = npc.transform.rotation,
                        // Identity & Visuals — critical for proper respawn
                        RaceId = npc.NetworkRaceId.Value.ToString(),
                        CharacterName = npc.NetworkCharacterName.Value.ToString(),
                        VisualSeed = npc.NetworkVisualSeed.Value,
                        // Abandoned NPC tracking
                        IsAbandoned = npc.IsAbandoned,
                        FormerPartyLeaderId = npc.FormerPartyLeaderId,
                        FormerPartyLeaderWorldGuid = npc.FormerPartyLeaderWorldGuid
                    };

                    // Extract Tier 1 V2 GOAP Anchors
                    if (npc.TryGetComponent(out CharacterMapTracker tracker))
                    {
                        npcData.HomeMapId = tracker.HomeMapId.Value.ToString();
                        npcData.HomePosition = tracker.HomePosition.Value;
                    }

                    // Extract Build/Harvest Flags and JobType
                    if (npc.TryGetComponent(out CharacterJob charJob))
                    {
                        npcData.HasHarvesterJob = charJob.HasJobOfType<JobHarvester>();
                        
                        var currentJob = charJob.CurrentJob;
                        npcData.SavedJobType = currentJob != null ? currentJob.Type : JobType.None;
                    }

                    // Extract Blueprint Knowledge
                    if (npc.TryGetComponent(out CharacterBlueprints blueprints))
                    {
                        npcData.UnlockedBuildingIds.AddRange(blueprints.UnlockedBuildingIds);
                    }

                    // Extract Needs
                    if (npc.CharacterNeeds != null)
                    {
                        foreach (var need in npc.CharacterNeeds.AllNeeds)
                        {
                            npcData.SavedNeeds.Add(new HibernatedNeedData 
                            { 
                                NeedType = need.GetType().Name,
                                Value = need.CurrentValue 
                            });
                        }
                    }

                    // Extract Party membership
                    if (npc.CharacterParty != null && npc.CharacterParty.IsInParty)
                        npcData.PartyId = npc.CharacterParty.PartyData.PartyId;

                    _hibernationData.HibernatedNPCs.Add(npcData);

                    // 2. Despawn & Destroy the physical instance
                    if (npc.NetworkObject != null && npc.NetworkObject.IsSpawned)
                    {
                        npc.NetworkObject.Despawn(true); // true = destroy the GameObject
                    }
                    else
                    {
                        Destroy(npc.gameObject);
                    }
                }
            }

            // 2.5 Sync live building state to ConstructedBuildings, then despawn
            CommunityData community = null;
            if (MapRegistry.Instance != null)
            {
                community = MapRegistry.Instance.GetCommunity(MapId);
                Debug.Log($"<color=orange>[MapController:Hibernate]</color> MapRegistry lookup for MapId='{MapId}': {(community != null ? "FOUND" : "NULL — buildings WILL NOT be saved!")}");
            }
            else
            {
                Debug.LogError($"<color=red>[MapController:Hibernate]</color> MapRegistry.Instance is NULL! Cannot save buildings for '{MapId}'.");
            }

            HashSet<Building> processedBuildings = new HashSet<Building>();
            foreach (var col in colliders)
            {
                Building building = col.GetComponent<Building>() ?? col.GetComponentInParent<Building>();
                if (building == null || !processedBuildings.Add(building)) continue;

                Debug.Log($"<color=orange>[MapController:Hibernate]</color> Found building '{building.BuildingName}' (ID={building.BuildingId}, PrefabId={building.PrefabId}) at {building.transform.position}");

                // Sync state to save data (auto-register if not yet tracked)
                if (community != null)
                {
                    var saveEntry = community.ConstructedBuildings.Find(b => b.BuildingId == building.BuildingId);
                    if (saveEntry != null)
                    {
                        // Refresh dynamic fields from the live building before despawn.
                        var refreshed = BuildingSaveData.FromBuilding(building, transform.position);
                        saveEntry.State = refreshed.State;
                        saveEntry.OwnerCharacterIds = refreshed.OwnerCharacterIds;
                        saveEntry.Employees = refreshed.Employees;
                        Debug.Log($"<color=orange>[MapController:Hibernate]</color> Updated existing save entry for '{building.BuildingName}'. State={saveEntry.State}, owners={saveEntry.OwnerCharacterIds.Count}, employees={saveEntry.Employees.Count}");
                    }
                    else
                    {
                        var newEntry = BuildingSaveData.FromBuilding(building, transform.position);
                        community.ConstructedBuildings.Add(newEntry);
                        Debug.Log($"<color=cyan>[MapController:Hibernate]</color> Auto-registered untracked building '{building.BuildingName}' into '{MapId}'. PrefabId='{newEntry.PrefabId}', RelPos={newEntry.Position}, owners={newEntry.OwnerCharacterIds.Count}, employees={newEntry.Employees.Count}");
                    }
                }
                else
                {
                    Debug.LogError($"<color=red>[MapController:Hibernate]</color> CANNOT save building '{building.BuildingName}' — no CommunityData for MapId='{MapId}'! This building will be LOST on wake-up.");
                }

                // Despawn the live building (WakeUp re-instantiates from ConstructedBuildings)
                if (building.NetworkObject != null && building.NetworkObject.IsSpawned)
                {
                    building.NetworkObject.Despawn(true);
                    Debug.Log($"<color=orange>[MapController:Hibernate]</color> Despawned building '{building.BuildingName}'.");
                }
                else
                {
                    Destroy(building.gameObject);
                    Debug.Log($"<color=orange>[MapController:Hibernate]</color> Destroyed (non-networked) building '{building.BuildingName}'.");
                }
            }

            // 3. Destroy Virtual Harvesting Buildings
            foreach (Transform child in transform)
            {
                if (child.GetComponent<VirtualResourceSupplier>() != null)
                {
                    Destroy(child.gameObject);
                }
            }

            Debug.Log($"<color=orange>[MapController]</color> Map '{MapId}' Hibernated. {_hibernationData.HibernatedNPCs.Count} NPCs and {processedBuildings.Count} buildings serialized and despawned.");
        }

        private void WakeUp()
        {
            if (!IsHibernating) return;

            IsHibernating = false;
            
            double currentTime = GetAbsoluteTimeInDays();
            double deltaDays = currentTime - (_hibernationData?.LastHibernationTime ?? currentTime);

            Debug.Log($"<color=green>[MapController:WakeUp]</color> Map '{MapId}' Waking Up! Simulating {deltaDays:F2} offline days.");

            // Ensure CommunityData exists before restoring buildings
            EnsureCommunityData();

            // Get Community Data for Map Tier and Buildings
            CommunityData community = null;
            if (MapRegistry.Instance != null)
            {
                community = MapRegistry.Instance.GetCommunity(MapId);
                Debug.Log($"<color=green>[MapController:WakeUp]</color> MapRegistry lookup for MapId='{MapId}': {(community != null ? $"FOUND (Tier={community.Tier}, Buildings={community.ConstructedBuildings?.Count ?? 0})" : "NULL — no buildings to respawn!")}");
            }
            else
            {
                Debug.LogError($"<color=red>[MapController:WakeUp]</color> MapRegistry.Instance is NULL!");
            }

            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            Debug.Log($"<color=green>[MapController:WakeUp]</color> WorldSettingsData loaded: {(settings != null ? "OK" : "NULL")}");

            if (community != null && settings != null)
            {
                // 1. Spawn base terrain prefab (tier)
                // Note: Only spawn if it doesn't already exist as a child (e.g. from immediate promotion)
                bool hasTerrain = false;
                foreach (Transform child in transform)
                {
                    if (child.gameObject.name.Contains("Prefab")) hasTerrain = true;
                }

                if (!IsPredefinedMap && !hasTerrain)
                {
                    GameObject terrainPrefab = settings.GetPrefabForTier(community.Tier);
                    if (terrainPrefab != null)
                    {
                        GameObject terrain = Instantiate(terrainPrefab, transform.position, Quaternion.identity);
                        terrain.transform.SetParent(this.transform);
                        terrain.name = terrainPrefab.name + "_TerrainPrefab";
                        if (terrain.TryGetComponent(out NetworkObject terrainNet))
                        {
                            terrainNet.Spawn();
                        }
                    }
                }

                // 2 & 3. Spawn completed and under-construction buildings
                if (community.ConstructedBuildings != null)
                {
                    Debug.Log($"<color=green>[MapController:WakeUp]</color> Respawning {community.ConstructedBuildings.Count} buildings for '{MapId}'...");
                    foreach (var bSave in community.ConstructedBuildings)
                    {
                        GameObject bPrefab = settings.GetBuildingPrefab(bSave.PrefabId);
                        Debug.Log($"<color=green>[MapController:WakeUp]</color> Building '{bSave.BuildingId}': PrefabId='{bSave.PrefabId}', State={bSave.State}, RelPos={bSave.Position}, PrefabFound={bPrefab != null}");

                        if (bSave.State == BuildingState.UnderConstruction && settings.GenericScaffoldPrefab != null)
                        {
                            bPrefab = settings.GenericScaffoldPrefab;
                            Debug.Log($"<color=green>[MapController:WakeUp]</color> Using scaffold prefab for under-construction building.");
                        }

                        if (bPrefab != null)
                        {
                            Vector3 worldPos = transform.position + bSave.Position;
                            Debug.Log($"<color=green>[MapController:WakeUp]</color> Instantiating at worldPos={worldPos} (mapPos={transform.position} + relPos={bSave.Position})");
                            GameObject bObj = Instantiate(bPrefab, worldPos, bSave.Rotation);
                            if (bObj.TryGetComponent(out NetworkObject bNet))
                            {
                                // Spawn first (NetworkVariables require spawned state)
                                bNet.Spawn();
                                // Parent after spawn to avoid SpawnStateException
                                bObj.transform.SetParent(this.transform);

                                // Restore the original BuildingId from save data
                                // (OnNetworkSpawn generates a new GUID — we must overwrite it)
                                Building restoredBuilding = bObj.GetComponent<Building>();
                                if (restoredBuilding != null)
                                {
                                    restoredBuilding.NetworkBuildingId.Value = bSave.BuildingId;

                                    if (!string.IsNullOrEmpty(bSave.PlacedByCharacterId))
                                        restoredBuilding.PlacedByCharacterId.Value = new Unity.Collections.FixedString64Bytes(bSave.PlacedByCharacterId);

                                    // Restore boss + crew. Characters from THIS map haven't spawned yet
                                    // (SpawnNPCsFromSnapshot runs after this loop), so the resolver will
                                    // queue them and bind on Character.OnCharacterSpawned.
                                    if (restoredBuilding is CommercialBuilding commercial)
                                        commercial.RestoreFromSaveData(bSave.OwnerCharacterIds, bSave.Employees);

                                    Debug.Log($"<color=green>[MapController:WakeUp]</color> Building '{bSave.PrefabId}' restored with ID={bSave.BuildingId} at {worldPos}.");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"<color=yellow>[MapController:WakeUp]</color> Building prefab '{bSave.PrefabId}' has no NetworkObject!");
                            }
                        }
                        else
                        {
                            Debug.LogError($"<color=red>[MapController:WakeUp]</color> Could not find prefab for PrefabId='{bSave.PrefabId}'! Building LOST. Check WorldSettingsData.BuildingRegistry.");
                        }
                    }
                }
                else
                {
                    Debug.Log($"<color=green>[MapController:WakeUp]</color> No ConstructedBuildings list for '{MapId}'.");
                }

                // 4. Spawn Virtual Harvesting Buildings for Logistics
                SpawnVirtualBuildings();
            }

            if (_hibernationData != null && _hibernationData.HibernatedNPCs.Count > 0)
            {
                // 4. Run MacroSimulator catch-up on NPCs
                MacroSimulator.SimulateCatchUp(_hibernationData, _timeManager.CurrentDay, _timeManager.CurrentTime01, JobYields);

                // 4b. Restore terrain cells after macro-simulation has updated them
                var terrainGrid = GetComponent<TerrainCellGrid>();
                if (terrainGrid != null && _hibernationData?.TerrainCells != null)
                    terrainGrid.RestoreFromSaveData(_hibernationData.TerrainCells);

                // 5. Spawn NPCs at their simulated positions (shared with snapshot restore)
                SpawnNPCsFromSnapshot(_hibernationData);
            }

            // Safety: Only clear hibernation data AFTER a successful full spawn loop.
            // If the server crashes mid-wake, data isn't lost.
            _hibernationData = null;
        }

        // (RunMacroSimulation_V1 removed, see MacroSimulator.cs)

        #endregion
        
        /// <summary>
        /// Sends the full terrain grid state to a specific client (e.g. late-joining player).
        /// Server-only: call this when a new player enters the map.
        /// </summary>
        [ClientRpc]
        private void SendTerrainGridClientRpc(TerrainCellSaveData[] cells, ClientRpcParams rpcParams = default)
        {
            var grid = GetComponent<TerrainCellGrid>();
            if (grid != null)
                grid.RestoreFromSaveData(cells);
        }

        /// <summary>
        /// Call this directly from Map Transition doors/portals to force a fast Add/Remove
        /// before physics triggers have a chance to update.
        /// </summary>
        public void ForcePlayerTransition(ulong clientId, bool entering)
        {
            if (!IsServer) return;
            
            if (entering) AddPlayer(clientId);
            else RemovePlayer(clientId);
        }
    }
}
