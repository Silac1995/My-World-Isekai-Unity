using UnityEngine;
using Unity.Netcode;
using MWI.WorldSystem;
using MWI.UI.Notifications;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Handles the building placement lifecycle for any Character (Player or NPC).
    /// For Player: drives the ghost visual, mouse-based positioning, and click-to-place.
    /// Validation (range + obstacle) is shared so NPCs can reuse the same rules.
    /// Server-authoritative: the actual building is spawned via ServerRpc.
    /// </summary>
    public class BuildingPlacementManager : CharacterSystem
    {
        [Header("Settings")]
        [SerializeField] private LayerMask _groundLayer;
        [SerializeField] private LayerMask _obstacleLayer;
        [SerializeField] private Material _ghostMaterialValid;
        [SerializeField] private Material _ghostMaterialInvalid;

        [Header("Notifications")]
        [SerializeField] private ToastNotificationChannel _toastChannel;

        [SerializeField] private WorldSettingsData _settings;
        private GameObject _ghostInstance;
        private string _activePrefabId;
        private Building _ghostBuildingComponent;
        private bool _isPlacementActive;
        private bool _isInstantMode;
        private bool _permissionToastShown; // Prevents spamming toast every frame

        public bool IsPlacementActive => _isPlacementActive;

        // ────────────────────── Initialization ──────────────────────

        public void Initialize(Character character)
        {
            _character = character;

            EnsureSettings();
        }

        public void SetSettings(WorldSettingsData settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Toggles instant build mode. When enabled, placed buildings skip construction requirements.
        /// </summary>
        public void SetInstantMode(bool instant)
        {
            _isInstantMode = instant;
        }

        // ────────────────────── Placement Lifecycle ──────────────────────

        public void StartPlacement(string prefabId)
        {
            // We only clear the ghost/selection to stay in building mode (keep UI open)
            ClearGhost();
            EnsureSettings();

            if (_settings == null)
            {
                Debug.LogError("[BuildingPlacementManager] WorldSettingsData could not be loaded.");
                return;
            }

            var entry = _settings.BuildingRegistry.Find(e => e.PrefabId == prefabId);
            if (entry.BuildingPrefab == null)
            {
                Debug.LogWarning($"[BuildingPlacementManager] No prefab found for PrefabId '{prefabId}'.");
                return;
            }

            _activePrefabId = prefabId;
            _ghostInstance = Instantiate(entry.BuildingPrefab);
            _ghostBuildingComponent = _ghostInstance.GetComponent<Building>();

            // Disable physics on ghost — it's purely visual
            // Disable colliders entirely instead of setting isTrigger (concave MeshColliders don't support triggers)
            if (_ghostInstance.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
            foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>()) col.enabled = false;

            // Disable any NetworkObject on the ghost to prevent network errors
            if (_ghostInstance.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;

            // Move ghost to Ignore Raycast layer so its colliders don't interfere
            // with the OverlapBox obstacle check (which includes the Building layer)
            SetLayerRecursive(_ghostInstance, LayerMask.NameToLayer("Ignore Raycast"));

            _ghostInstance.name = "PlacementGhost_" + prefabId;
            _isPlacementActive = true;
        
            // Ensure character state is set (in case it was started without the UI, though unlikely now)
            if (_character != null && !_character.IsBuilding)
                _character.SetBuildingState(true);
            
            ApplyGhostMaterials(_ghostMaterialValid);
        }

        public void CancelPlacement()
        {
            ClearGhost();
            
            if (_character != null)
                _character.SetBuildingState(false);
        }

        private void ClearGhost()
        {
            if (_ghostInstance != null)
            {
                Destroy(_ghostInstance);
                _ghostInstance = null;
            }
            _isPlacementActive = false;
            _activePrefabId = string.Empty;
            _ghostBuildingComponent = null;
            _permissionToastShown = false;
        }

        protected override void HandleIncapacitated(Character character)
        {
            base.HandleIncapacitated(character);
            CancelPlacement();
        }

        protected override void HandleCombatStateChanged(bool inCombat)
        {
            base.HandleCombatStateChanged(inCombat);
            if (inCombat)
            {
                CancelPlacement();
            }
        }

        // ────────────────────── Frame Update (Player only) ──────────────────────

        private void Update()
        {
            if (!_isPlacementActive || !IsOwner) return;

            UpdateGhostPosition();
            HandleInput();
        }

        private void UpdateGhostPosition()
        {
            if (_ghostInstance == null || Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _groundLayer))
            {
                _ghostInstance.transform.position = hit.point;

                bool hasPermission = HasCommunityPlacementPermission(hit.point);
                bool isValid = ValidatePlacement(hit.point);
                ApplyGhostMaterials(isValid ? _ghostMaterialValid : _ghostMaterialInvalid);

                // Show toast once when entering a denied zone
                if (!hasPermission && !_permissionToastShown)
                {
                    _permissionToastShown = true;
                    if (_toastChannel != null)
                    {
                        _toastChannel.Raise(new ToastNotificationPayload(
                            message: "You don't have permission to build here. Ask a community leader for a Build Permit.",
                            type: ToastType.Warning,
                            duration: 4f
                        ));
                    }
                }
                else if (hasPermission)
                {
                    _permissionToastShown = false;
                }
            }
        }

        private void HandleInput()
        {
            // Left-Click: Confirm placement
            if (Input.GetMouseButtonDown(0))
            {
                if (_ghostInstance != null && ValidatePlacement(_ghostInstance.transform.position))
                {
                    RequestPlacementServerRpc(
                        _activePrefabId,
                        _ghostInstance.transform.position,
                        _ghostInstance.transform.rotation,
                        _isInstantMode
                    );
                    // We call ClearGhost instead of CancelPlacement to keep the UI open
                    ClearGhost();
                }
            }

            // Right-Click: Cancel current selection but keep building mode active
            if (Input.GetMouseButtonDown(1))
            {
                ClearGhost();
            }

            // Escape: Exit building mode completely
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
            }
        }

        // ────────────────────── Validation (shared with NPC systems) ──────────────────────

        /// <summary>
        /// Validates whether a building can be placed at the given position.
        /// Checks range from the owning character and obstacle overlap.
        /// This is intentionally public so NPC AI can call it directly.
        /// </summary>
        public bool ValidatePlacement(Vector3 position)
        {
            if (_character == null || _character.CharacterBlueprints == null) return false;

            // 1. Range check — uses MaxPlacementRange from CharacterBlueprints
            float dist = Vector3.Distance(_character.transform.position, position);
            if (dist > _character.CharacterBlueprints.MaxPlacementRange) return false;

            // 2. Obstacle overlap check using the building's zone collider
            if (_ghostBuildingComponent != null && _ghostBuildingComponent.BuildingZone is BoxCollider box)
            {
                Vector3 center = _ghostInstance.transform.TransformPoint(box.center);
                // Slightly smaller than real size to avoid false positives from grazing edges
                Vector3 halfExtents = Vector3.Scale(box.size, _ghostInstance.transform.lossyScale) * 0.45f;

                Collider[] overlaps = Physics.OverlapBox(center, halfExtents, _ghostInstance.transform.rotation, _obstacleLayer);
                if (overlaps.Length > 0) return false;
            }

            // 3. Community zone permission check
            if (!HasCommunityPlacementPermission(position)) return false;

            return true;
        }

        // ────────────────────── Visual Helpers ──────────────────────

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void ApplyGhostMaterials(Material mat)
        {
            if (mat == null || _ghostInstance == null) return;
            foreach (var renderer in _ghostInstance.GetComponentsInChildren<Renderer>())
            {
                renderer.material = mat;
            }
        }

        /// <summary>
        /// Checks whether the placing character has permission to build inside a community zone.
        /// Open world (no map) always returns true. Leaders and permit-holders are allowed.
        /// </summary>
        private bool HasCommunityPlacementPermission(Vector3 position)
        {
            MapController map = MapController.GetMapAtPosition(position);
            if (map == null) return true; // Open world — no restriction

            if (MapRegistry.Instance == null) return true;
            CommunityData community = MapRegistry.Instance.GetCommunity(map.MapId);
            if (community == null) return true; // Map with no community data — allow

            // If the community has no leaders at all, there's no authority to deny placement
            if (community.LeaderIds.Count == 0) return true;

            string characterId = _character != null ? _character.CharacterId : "";

            // Leaders can always place
            if (community.IsLeader(characterId)) return true;

            // Non-leaders need a build permit
            if (community.HasPermit(characterId)) return true;

            return false;
        }

        // ────────────────────── Server-Authoritative Spawn ──────────────────────

        [ServerRpc]
        private void RequestPlacementServerRpc(string prefabId, Vector3 position, Quaternion rotation, bool instant)
        {
            EnsureSettings();
            if (_settings == null) return;

            // Server-side permission re-validation
            if (!HasCommunityPlacementPermission(position))
            {
                Debug.LogWarning($"<color=red>[BuildingPlacementManager]</color> Server rejected placement: no permission for community zone.");
                return;
            }

            // Consume a build permit if applicable (leaders don't need permits)
            MapController map = MapController.GetMapAtPosition(position);
            if (map != null && MapRegistry.Instance != null && _character != null)
            {
                CommunityData community = MapRegistry.Instance.GetCommunity(map.MapId);
                if (community != null && !community.IsLeader(_character.CharacterId))
                {
                    community.ConsumePermit(_character.CharacterId);
                }
            }

            var entry = _settings.BuildingRegistry.Find(e => e.PrefabId == prefabId);
            if (entry.BuildingPrefab == null) return;

            GameObject buildingObj = Instantiate(entry.BuildingPrefab, position, rotation);

            // Spawn on the network
            var netObj = buildingObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }
            else
            {
                Debug.LogError($"[BuildingPlacementManager] Prefab for '{prefabId}' is missing a NetworkObject component! It will only exist on the Server/Host.");
                // Destroy to prevent desync where host has a building that clients don't see
                Destroy(buildingObj);
                return;
            }

            // Set PrefabId so the building can be saved and restored from WorldSettingsData
            var placedBuilding = buildingObj.GetComponent<Building>();
            if (placedBuilding != null)
            {
                placedBuilding.PrefabId = prefabId;
            }

            // If instant mode, skip construction requirements
            if (instant)
            {
                if (placedBuilding != null)
                {
                    placedBuilding.BuildInstantly();
                }
            }

            // Register with MapController for hibernation persistence
            RegisterBuildingWithMap(buildingObj, position);
        }

        /// <summary>
        /// Finds the MapController containing the position, parents the building to it,
        /// and adds it to the community's ConstructedBuildings for hibernation persistence.
        /// </summary>
        private void RegisterBuildingWithMap(GameObject buildingObj, Vector3 worldPosition)
        {
            Building building = buildingObj.GetComponent<Building>();
            if (building == null) return;

            // Tag who placed this building
            if (_character != null)
            {
                building.PlacedByCharacterId.Value = _character.CharacterId;
            }

            Debug.Log($"<color=yellow>[BuildingPlacementManager:Register]</color> Trying to find MapController for building '{building.BuildingName}' at {worldPosition}.");

            // 1. Is the placement inside the trigger bounds of an existing exterior map?
            MapController map = MapController.GetMapAtPosition(worldPosition);

            // 2. Bounds fallback — catches maps that GetMapAtPosition skips (e.g. registry lag).
            if (map == null)
            {
                var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
                foreach (var m in allMaps)
                {
                    if (m == null || m.Type == MapType.Interior) continue;
                    var col = m.GetComponent<BoxCollider>();
                    if (col != null && col.bounds.Contains(worldPosition))
                    {
                        map = m;
                        Debug.Log($"<color=yellow>[BuildingPlacementManager:Register]</color> Found map '{m.MapId}' via bounds fallback (Type={m.Type}).");
                        break;
                    }
                }
            }

            // 3. Region-aware branching.
            //    If the placement is inside an authored Region that has no enclosing map,
            //    create a new wild map *in that region*. Don't poach a map from a different
            //    region via the nearest-map-within-MinSep fallback.
            float minSep = _settings != null ? _settings.MapMinSeparation : 150f;
            if (map == null)
            {
                Region parentRegion = Region.GetRegionAtPosition(worldPosition);

                if (parentRegion != null && MapRegistry.Instance != null)
                {
                    Debug.Log($"<color=yellow>[BuildingPlacementManager:Register]</color> Inside Region '{parentRegion.ZoneId}' with no enclosing map. Creating a new wild map at {worldPosition}.");
                    try
                    {
                        map = MapRegistry.Instance.CreateMapAtPosition(worldPosition);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                else
                {
                    // Open world (outside any Region): use the legacy join-nearest-else-create flow.
                    MapController nearest = MapController.GetNearestExteriorMap(worldPosition, minSep);
                    if (nearest != null)
                    {
                        map = nearest;
                        Debug.Log($"<color=yellow>[BuildingPlacementManager:Register]</color> Open world. Joining nearest exterior map '{map.MapId}' within {minSep} units.");
                    }
                    else if (MapRegistry.Instance != null)
                    {
                        Debug.Log($"<color=yellow>[BuildingPlacementManager:Register]</color> Open world. Creating a new wild map at {worldPosition}.");
                        try
                        {
                            map = MapRegistry.Instance.CreateMapAtPosition(worldPosition);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }

            if (map == null)
            {
                Debug.LogWarning($"<color=yellow>[BuildingPlacementManager:Register]</color> Failed to find or create a MapController for building '{building.BuildingName}' at {worldPosition}. Building will not survive hibernation.");
                return;
            }

            Debug.Log($"<color=green>[BuildingPlacementManager:Register]</color> Building '{building.BuildingName}' registered with map '{map.MapId}'.");

            // Parent to the MapController (must be a NetworkObject for NGO parenting rules)
            buildingObj.transform.SetParent(map.transform);

            // Add to CommunityData.ConstructedBuildings
            if (MapRegistry.Instance != null)
            {
                CommunityData community = MapRegistry.Instance.GetCommunity(map.MapId);
                if (community != null)
                {
                    if (!community.ConstructedBuildings.Exists(b => b.BuildingId == building.BuildingId))
                    {
                        var saveData = BuildingSaveData.FromBuilding(building, map.transform.position);
                        community.ConstructedBuildings.Add(saveData);
                        Debug.Log($"<color=green>[BuildingPlacementManager]</color> Building '{building.BuildingName}' registered with map '{map.MapId}'. Total buildings: {community.ConstructedBuildings.Count}");
                    }
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[BuildingPlacementManager]</color> MapController '{map.MapId}' has no CommunityData. Building will not survive hibernation.");
                }
            }
        }

        // ────────────────────── Utilities ──────────────────────

        private void EnsureSettings()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            }
        }

        private void OnDestroy()
        {
            CancelPlacement();
        }
    }
}
