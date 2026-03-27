using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
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

        [Tooltip("Use this to determine if the map is visually loaded or offset")]
        public bool IsInteriorOffset = false;

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
        /// Ensures a CommunityData entry exists for this map in CommunityTracker.
        /// Predefined maps and dynamically spawned maps both need this for building persistence.
        /// </summary>
        private void EnsureCommunityData()
        {
            if (string.IsNullOrEmpty(MapId)) return;
            if (CommunityTracker.Instance == null) return;

            CommunityData existing = CommunityTracker.Instance.GetCommunity(MapId);
            if (existing != null) return;

            // Auto-create a CommunityData entry for this map
            // Set OriginChunk from actual position so NPC proximity matching works correctly
            float chunkSize = 75f; // Match CommunityTracker default
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

            CommunityTracker.Instance.AddCommunity(newCommunity);
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
                CheckWakeUp();
            }
        }

        private void RemovePlayer(ulong clientId)
        {
            if (_activePlayers.Remove(clientId))
            {
                _activePlayerCount = _activePlayers.Count;
                Debug.Log($"<color=cyan>[MapController]</color> Player {clientId} left '{MapId}'. Total: {_activePlayerCount}");
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
            if (CommunityTracker.Instance == null) return null;
            var community = CommunityTracker.Instance.GetCommunity(MapId);
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
            if (CommunityTracker.Instance != null)
            {
                community = CommunityTracker.Instance.GetCommunity(MapId);
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

        private void Hibernate()
        {
            if (IsHibernating) return;

            IsHibernating = true;
            Debug.Log($"<color=orange>[MapController:Hibernate]</color> Map '{MapId}' entering Hibernation.");

            // Ensure CommunityData exists before saving buildings
            EnsureCommunityData();

            _hibernationData = new MapSaveData()
            {
                MapId = this.MapId,
                LastHibernationTime = GetAbsoluteTimeInDays()
            };

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
                        VisualSeed = npc.NetworkVisualSeed.Value
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
            if (CommunityTracker.Instance != null)
            {
                community = CommunityTracker.Instance.GetCommunity(MapId);
                Debug.Log($"<color=orange>[MapController:Hibernate]</color> CommunityTracker lookup for MapId='{MapId}': {(community != null ? "FOUND" : "NULL — buildings WILL NOT be saved!")}");
            }
            else
            {
                Debug.LogError($"<color=red>[MapController:Hibernate]</color> CommunityTracker.Instance is NULL! Cannot save buildings for '{MapId}'.");
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
                        saveEntry.State = building.CurrentState;
                        Debug.Log($"<color=orange>[MapController:Hibernate]</color> Updated existing save entry for '{building.BuildingName}'. State={saveEntry.State}");
                    }
                    else
                    {
                        var newEntry = BuildingSaveData.FromBuilding(building, transform.position);
                        community.ConstructedBuildings.Add(newEntry);
                        Debug.Log($"<color=cyan>[MapController:Hibernate]</color> Auto-registered untracked building '{building.BuildingName}' into '{MapId}'. PrefabId='{newEntry.PrefabId}', RelPos={newEntry.Position}");
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
            if (CommunityTracker.Instance != null)
            {
                community = CommunityTracker.Instance.GetCommunity(MapId);
                Debug.Log($"<color=green>[MapController:WakeUp]</color> CommunityTracker lookup for MapId='{MapId}': {(community != null ? $"FOUND (Tier={community.Tier}, Buildings={community.ConstructedBuildings?.Count ?? 0})" : "NULL — no buildings to respawn!")}");
            }
            else
            {
                Debug.LogError($"<color=red>[MapController:WakeUp]</color> CommunityTracker.Instance is NULL!");
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

                // 5. Spawn NPCs at their simulated positions
                foreach (var npcData in _hibernationData.HibernatedNPCs)
                {
                    GameObject prefab = null;
                    
                    // Optimization: Use NGO's pre-loaded prefab registry instead of disk I/O (Resources.Load)
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
                    {
                        foreach (var networkPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                        {
                            if (networkPrefab.Prefab != null)
                            {
                                // Priority 1: Match by NGO hash (Guarantees exact prefab even if GameObject was renamed in scene)
                                if (npcData.PrefabHash != 0 && networkPrefab.Prefab.TryGetComponent(out NetworkObject prefabNetObj) && prefabNetObj.PrefabIdHash == npcData.PrefabHash)
                                {
                                    prefab = networkPrefab.Prefab;
                                    break;
                                }

                                // Priority 2: Fallback to exact name match
                                if (prefab == null && networkPrefab.Prefab.name == npcData.PrefabName)
                                {
                                    prefab = networkPrefab.Prefab;
                                    // Don't break; keep looking in case strict Hash match is found later in the list
                                }
                            }
                        }
                    }

                    if (prefab == null)
                    {
                        Debug.LogError($"[MapController] Could not find prefab {npcData.PrefabName} in NetworkManager to wake up {npcData.CharacterId}!");
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
                        if (!string.IsNullOrEmpty(npcData.RaceId))
                            spawnedChar.NetworkRaceId.Value = new Unity.Collections.FixedString64Bytes(npcData.RaceId);
                        if (!string.IsNullOrEmpty(npcData.CharacterName))
                            spawnedChar.NetworkCharacterName.Value = new Unity.Collections.FixedString64Bytes(npcData.CharacterName);
                        if (npcData.VisualSeed != 0)
                            spawnedChar.NetworkVisualSeed.Value = npcData.VisualSeed;

                        // Inject caught-up needs back
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
            }

            // Safety: Only clear hibernation data AFTER a successful full spawn loop.
            // If the server crashes mid-wake, data isn't lost.
            _hibernationData = null; 
        }

        // (RunMacroSimulation_V1 removed, see MacroSimulator.cs)

        #endregion
        
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
