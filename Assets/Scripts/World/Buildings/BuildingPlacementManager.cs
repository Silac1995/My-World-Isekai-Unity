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
        private bool _outOfRegionToastShown; // Same spam-guard for the out-of-region toast
        private bool _mapFitToastShown;     // Same spam-guard for region-boundary fit toast
        private bool _mapMinSepToastShown;  // Same spam-guard for MapMinSeparation toast

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

                bool insideRegion = IsInsideRegion(hit.point);
                bool hasPermission = HasCommunityPlacementPermission(hit.point);
                bool isValid = ValidatePlacement(hit.point);
                ApplyGhostMaterials(isValid ? _ghostMaterialValid : _ghostMaterialInvalid);

                // Toast: outside any Region takes precedence over permission toast.
                if (!insideRegion && !_outOfRegionToastShown)
                {
                    _outOfRegionToastShown = true;
                    _permissionToastShown = false;
                    if (_toastChannel != null)
                    {
                        _toastChannel.Raise(new ToastNotificationPayload(
                            message: "You can't build outside a Region. Move closer to a settlement or explored area.",
                            type: ToastType.Warning,
                            duration: 4f
                        ));
                    }
                }
                else if (insideRegion)
                {
                    _outOfRegionToastShown = false;

                    // Spatial-fit toasts only apply when we'd need to create a new wild map.
                    bool needsNewMap = MapController.GetMapAtPosition(hit.point) == null;
                    bool mapFits = !needsNewMap || WouldNewMapFitInRegion(hit.point);
                    bool passesMinSep = !needsNewMap || MapRegistry.Instance == null
                        || !MapRegistry.Instance.WouldViolateMapMinSeparation(hit.point);

                    if (!mapFits && !_mapFitToastShown)
                    {
                        _mapFitToastShown = true;
                        _mapMinSepToastShown = false;
                        _permissionToastShown = false;
                        if (_toastChannel != null)
                        {
                            _toastChannel.Raise(new ToastNotificationPayload(
                                message: "Too close to the region border — a new map wouldn't fit. Move further inside.",
                                type: ToastType.Warning,
                                duration: 4f
                            ));
                        }
                    }
                    else if (mapFits && !passesMinSep && !_mapMinSepToastShown)
                    {
                        _mapMinSepToastShown = true;
                        _mapFitToastShown = false;
                        _permissionToastShown = false;
                        if (_toastChannel != null)
                        {
                            _toastChannel.Raise(new ToastNotificationPayload(
                                message: "Too close to an existing map. Either build inside it or move further away.",
                                type: ToastType.Warning,
                                duration: 4f
                            ));
                        }
                    }
                    else if (mapFits && passesMinSep)
                    {
                        _mapFitToastShown = false;
                        _mapMinSepToastShown = false;

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

            // 3. Must be inside an authored Region. Buildings outside any Region are rejected —
            //    every placement must land in a spatial scope that owns persistence and (future)
            //    navmesh tiling.
            if (!IsInsideRegion(position)) return false;

            // 4. Spatial consistency — if the placement is NOT already inside a MapController,
            //    a new wild map has to be created. Verify it would actually fit:
            //      (a) entirely inside the target Region's bounds, and
            //      (b) not too close to another map in the same Region (MapMinSeparation).
            //    Without this, silent server rejections leave the building orphaned and the
            //    player confused.
            MapController enclosingMap = MapController.GetMapAtPosition(position);
            if (enclosingMap == null)
            {
                if (!WouldNewMapFitInRegion(position)) return false;
                if (MapRegistry.Instance != null && MapRegistry.Instance.WouldViolateMapMinSeparation(position)) return false;
            }

            // 5. Community zone permission check
            if (!HasCommunityPlacementPermission(position)) return false;

            return true;
        }

        /// <summary>Public so NPC AI + server validation can reuse the same gate.</summary>
        public static bool IsInsideRegion(Vector3 worldPosition)
            => Region.GetRegionAtPosition(worldPosition) != null;

        /// <summary>
        /// Would a new wild MapController's BoxCollider (size derived from the prefab)
        /// fit entirely inside the containing Region's BoxCollider bounds if spawned at
        /// <paramref name="worldPosition"/>?
        /// </summary>
        public static bool WouldNewMapFitInRegion(Vector3 worldPosition)
        {
            Region region = Region.GetRegionAtPosition(worldPosition);
            if (region == null) return false;

            var regionCol = region.GetComponent<BoxCollider>();
            if (regionCol == null) return false;

            Vector3 prefabSize = MapRegistry.Instance != null
                ? MapRegistry.Instance.GetMapControllerPrefabSize()
                : Vector3.zero;
            if (prefabSize == Vector3.zero) return true; // unknown prefab size — don't block

            Vector3 hypotheticalHalf = prefabSize * 0.5f;
            Bounds regionBounds = regionCol.bounds;

            // Check every corner of the hypothetical MapController box.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = worldPosition + new Vector3(
                    sx * hypotheticalHalf.x,
                    sy * hypotheticalHalf.y,
                    sz * hypotheticalHalf.z);
                if (!regionBounds.Contains(corner)) return false;
            }
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

            // Server-side out-of-Region re-validation (client already gates this but trust nothing).
            if (!IsInsideRegion(position))
            {
                Debug.LogWarning($"<color=red>[BuildingPlacementManager]</color> Server rejected placement at {position}: outside any Region.");
                return;
            }

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

            // 3. No enclosing map yet. Placement is guaranteed inside a Region
            //    (ValidatePlacement + server re-validate gates on IsInsideRegion).
            //    Create a new wild MapController in that Region.
            if (map == null)
            {
                Region parentRegion = Region.GetRegionAtPosition(worldPosition);
                if (parentRegion == null || MapRegistry.Instance == null)
                {
                    // Should be impossible (validation gate above), but fail safely.
                    Debug.LogError($"<color=red>[BuildingPlacementManager:Register]</color> Reached RegisterBuildingWithMap with no parent Region for '{building.BuildingName}' at {worldPosition}. ValidatePlacement was bypassed.");
                    return;
                }

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
