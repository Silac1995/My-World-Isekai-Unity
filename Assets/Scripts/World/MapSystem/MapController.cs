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

        [Header("Runtime State")]
        public int ConnectedPlayersCount = 0;
        public bool IsHibernating = false;
        [SerializeField] private int _activePlayerCount = 0;

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
            }

            if (!IsServer) return;

            // Subscribe to NetworkManager disconnects to ensure _activePlayers doesn't drift
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;

            // Optional: Delay the initial check slightly so players have time to spawn
            Invoke(nameof(CheckHibernationState), 1f);
        }

        private void Start()
        {
            if (IsServer)
            {
                if (_timeManager != null)
                {
                    _timeManager.OnNewDay += OnNewDay;
                }

                if (!IsHibernating)
                {
                    SpawnVirtualBuildings();
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

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
            if (_activePlayers.Count == 0 && !IsHibernating)
            {
                Hibernate();
            }
        }

        private void CheckWakeUp()
        {
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
            Debug.Log($"<color=orange>[MapController]</color> Map '{MapId}' entering Hibernation (0 players).");

            _hibernationData = new MapSaveData()
            {
                MapId = this.MapId,
                LastHibernationTime = GetAbsoluteTimeInDays()
            };

            // 1. Find all NPCs inside this Map
            // Using OverlapBox is safer than tracking every single NPC manually, 
            // ensuring we grab any NPC physically standing in the map boundaries right now.
            Collider[] colliders = Physics.OverlapBox(_mapTrigger.bounds.center, _mapTrigger.bounds.extents, Quaternion.identity);
            
            foreach (var col in colliders)
            {
                if (col.CompareTag("Character") && col.TryGetComponent(out Character npc))
                {
                    // Ignore Players
                    if (npc.NetworkObject != null && npc.NetworkObject.IsPlayerObject) continue;

                    // Serialize to V1 Dumb Data
                    HibernatedNPCData npcData = new HibernatedNPCData()
                    {
                        CharacterId = npc.CharacterName, 
                        PrefabName = npc.gameObject.name.Replace("(Clone)", "").Trim(),
                        PrefabHash = npc.NetworkObject != null ? npc.NetworkObject.PrefabIdHash : 0,
                        Position = npc.transform.position,
                        Rotation = npc.transform.rotation
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

            // 3. Destroy Virtual Harvesting Buildings
            foreach (Transform child in transform)
            {
                if (child.GetComponent<VirtualResourceSupplier>() != null)
                {
                    Destroy(child.gameObject);
                }
            }

            Debug.Log($"<color=orange>[MapController]</color> Map '{MapId}' Hibernated. {_hibernationData.HibernatedNPCs.Count} NPCs serialized and despawned.");
        }

        private void WakeUp()
        {
            if (!IsHibernating) return;

            IsHibernating = false;
            
            double currentTime = GetAbsoluteTimeInDays();
            double deltaDays = currentTime - (_hibernationData?.LastHibernationTime ?? currentTime);

            Debug.Log($"<color=green>[MapController]</color> Map '{MapId}' Waking Up! Simulating {deltaDays:F2} offline days.");

            // Get Community Data for Map Tier and Buildings
            CommunityData community = null;
            if (CommunityTracker.Instance != null)
            {
                community = CommunityTracker.Instance.GetCommunity(MapId);
            }

            WorldSettingsData settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
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
                    foreach (var bSave in community.ConstructedBuildings)
                    {
                        GameObject bPrefab = settings.GetBuildingPrefab(bSave.PrefabId);
                        
                        if (bSave.State == BuildingState.UnderConstruction && settings.GenericScaffoldPrefab != null)
                        {
                            bPrefab = settings.GenericScaffoldPrefab;
                        }

                        if (bPrefab != null)
                        {
                            Vector3 worldPos = transform.position + bSave.Position;
                            GameObject bObj = Instantiate(bPrefab, worldPos, bSave.Rotation);
                            bObj.transform.SetParent(this.transform);
                            if (bObj.TryGetComponent(out NetworkObject bNet))
                            {
                                bNet.Spawn();
                            }
                        }
                    }
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

                    // Inject caught-up needs back
                    if (inst.TryGetComponent(out Character spawnedChar) && spawnedChar.CharacterNeeds != null)
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
