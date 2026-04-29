using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Per-character crop placement + watering system. Mirrors FurniturePlacementManager —
    /// ghost is the actual CropHarvestable prefab with physics/network disabled, snapped to
    /// the TerrainCellGrid. See farming spec §5.2 + §7.
    /// </summary>
    public class CropPlacementManager : CharacterSystem
    {
        [Header("Settings")]
        [SerializeField] private LayerMask _groundLayer = ~0;
        [SerializeField] private float _maxRange = 25f;
        [SerializeField] private Material _ghostMaterialValid;
        [SerializeField] private Material _ghostMaterialInvalid;

        private GameObject _ghostInstance;
        private CropSO _activeCrop;
        private MapController _activeMap;
        private Vector3 _lastSnappedPosition;
        private int _lastCellX, _lastCellZ;
        private bool _lastValid;
        private Mode _mode = Mode.Off;

        private enum Mode { Off, Placing, Watering }
        public bool IsActive => _mode != Mode.Off;

        // ────────────────────── Entry Points ──────────────────────

        public void StartPlacement(ItemInstance seedInstance)
        {
            if (!(seedInstance?.ItemSO is SeedSO seedSO) || seedSO.CropToPlant == null) return;
            ClearGhost();
            _activeCrop = seedSO.CropToPlant;
            _activeMap = MapController.GetMapAtPosition(_character.transform.position);
            if (_activeMap == null)
            {
                Debug.LogWarning("[CropPlacement] Cannot start placement — no MapController at character position.");
                return;
            }
            EnsureGridInitialized(_activeMap);

            // Spawn the actual CropHarvestable prefab as the ghost; disable everything that would interfere.
            if (_activeCrop.HarvestablePrefab != null)
            {
                _ghostInstance = Instantiate(_activeCrop.HarvestablePrefab);
                _ghostInstance.name = "CropPlacementGhost_" + _activeCrop.Id;
            }
            else
            {
                // Fallback: simple sprite quad if the crop has no prefab.
                _ghostInstance = new GameObject("CropPlacementGhost_" + _activeCrop.Id);
                _ghostInstance.AddComponent<SpriteRenderer>();
            }
            DisableGhostInterference(_ghostInstance);
            ApplyGhostMaterials(_ghostMaterialValid);

            _mode = Mode.Placing;
            ResetWarnFlags();
            Debug.Log($"[CropPlacement] StartPlacement crop='{_activeCrop.Id}', map='{_activeMap.name}', ghost spawned at {_ghostInstance.transform.position}.");
            if (!_character.IsBuilding) _character.SetBuildingState(true);
        }

        public void StartWatering()
        {
            ClearGhost();
            _activeMap = MapController.GetMapAtPosition(_character.transform.position);
            if (_activeMap == null)
            {
                Debug.LogWarning("[CropPlacement] Cannot start watering — no MapController at character position.");
                return;
            }
            EnsureGridInitialized(_activeMap);

            // Watering uses a generic semi-transparent quad (no prefab).
            _ghostInstance = new GameObject("CropPlacementGhost_Water");
            var sr = _ghostInstance.AddComponent<SpriteRenderer>();
            sr.color = new Color(0.4f, 0.6f, 1f, 0.6f);
            DisableGhostInterference(_ghostInstance);
            ApplyGhostMaterials(_ghostMaterialValid);

            _mode = Mode.Watering;
            ResetWarnFlags();
            if (!_character.IsBuilding) _character.SetBuildingState(true);
        }

        private void ResetWarnFlags()
        {
            _warnedNoCamera = _warnedNoGrid = _warnedRayMiss = _warnedOutOfGrid = false;
        }

        // Defensive bootstrap. The TerrainCellGrid has Initialize(Bounds) but no caller in the
        // existing terrain pipeline ever invokes it for a live (non-hibernated) map — pre-existing
        // gap that farming exposes first. If the grid is empty (Width=0) when we need it, we
        // initialize it from the MapController's own BoxCollider bounds.
        private static void EnsureGridInitialized(MapController map)
        {
            var grid = map.GetComponent<TerrainCellGrid>();
            if (grid == null) return;
            if (grid.Width > 0 && grid.Depth > 0) return;

            var box = map.GetComponent<BoxCollider>();
            if (box == null)
            {
                Debug.LogError($"[CropPlacement] {map.name} has no BoxCollider — cannot bootstrap TerrainCellGrid.");
                return;
            }
            grid.Initialize(box.bounds);
            Debug.Log($"[CropPlacement] Bootstrapped TerrainCellGrid on {map.name} from BoxCollider bounds (Width={grid.Width}, Depth={grid.Depth}).");
        }

        public void CancelPlacement()
        {
            ClearGhost();
            if (_character != null) _character.SetBuildingState(false);
        }

        private void ClearGhost()
        {
            if (_ghostInstance != null)
            {
                Destroy(_ghostInstance);
                _ghostInstance = null;
            }
            _activeCrop = null;
            _activeMap = null;
            _lastValid = false;
            _mode = Mode.Off;
        }

        protected override void HandleIncapacitated(Character character)
        {
            base.HandleIncapacitated(character);
            CancelPlacement();
        }

        protected override void HandleCombatStateChanged(bool inCombat)
        {
            base.HandleCombatStateChanged(inCombat);
            if (inCombat) CancelPlacement();
        }

        private void OnDestroy() => CancelPlacement();

        // ────────────────────── Frame Update (Player only) ──────────────────────

        private void Update()
        {
            if (_mode == Mode.Off || !IsOwner) return;
            if (_activeMap == null || _ghostInstance == null) return;

            UpdateGhostPosition();
            HandlePlacementInput();
        }

        private void UpdateGhostPosition()
        {
            if (Camera.main == null)
            {
                if (!_warnedNoCamera) { Debug.LogWarning("[CropPlacement] Camera.main is null — no MainCamera tag in scene."); _warnedNoCamera = true; }
                return;
            }
            var grid = _activeMap.GetComponent<TerrainCellGrid>();
            if (grid == null)
            {
                if (!_warnedNoGrid) { Debug.LogWarning("[CropPlacement] No TerrainCellGrid on the active MapController."); _warnedNoGrid = true; }
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, _groundLayer, QueryTriggerInteraction.Ignore))
            {
                if (!_warnedRayMiss) { Debug.LogWarning($"[CropPlacement] Raycast missed ground. Cursor={Input.mousePosition}, ray.dir={ray.direction}, _groundLayer.value={_groundLayer.value}. Check that ground colliders are on a layer in _groundLayer."); _warnedRayMiss = true; }
                return;   // Leave ghost at last position.
            }

            // Always position ghost at hit.point first — so the player sees it follow even when
            // the cell snap fails. Then refine to snapped pos if grid lookup succeeds.
            _ghostInstance.transform.position = hit.point;

            if (!grid.WorldToGrid(hit.point, out int x, out int z))
            {
                if (!_warnedOutOfGrid) { Debug.LogWarning($"[CropPlacement] Hit point {hit.point} is outside terrain grid bounds. Width={grid.Width}, Depth={grid.Depth}."); _warnedOutOfGrid = true; }
                _lastValid = false;
                ApplyGhostMaterials(_ghostMaterialInvalid);
                return;
            }

            _lastCellX = x; _lastCellZ = z;
            _lastSnappedPosition = grid.GridToWorld(x, z);
            _ghostInstance.transform.position = _lastSnappedPosition;

            ref var cell = ref grid.GetCellRef(x, z);
            _lastValid = ValidateCell(in cell, _lastSnappedPosition);
            ApplyGhostMaterials(_lastValid ? _ghostMaterialValid : _ghostMaterialInvalid);
        }

        // Diagnostic toggles — first-time-warn-once per placement session.
        private bool _warnedNoCamera;
        private bool _warnedNoGrid;
        private bool _warnedRayMiss;
        private bool _warnedOutOfGrid;

        private bool ValidateCell(in TerrainCell cell, Vector3 cellWorldPos)
        {
            float dist = Vector3.Distance(_character.transform.position, cellWorldPos);
            if (dist > _maxRange)
            {
                _invalidReason = $"out of range (dist={dist:F1} > maxRange={_maxRange:F1})";
                return false;
            }

            if (_mode == Mode.Placing)
            {
                var type = cell.GetCurrentType();
                if (type != null && !type.CanGrowVegetation)
                {
                    _invalidReason = $"terrain type '{cell.CurrentTypeId}' does not allow vegetation";
                    return false;
                }
                if (!string.IsNullOrEmpty(cell.PlantedCropId))
                {
                    _invalidReason = $"cell already has crop '{cell.PlantedCropId}'";
                    return false;
                }
            }
            _invalidReason = null;
            return true;
        }

        private string _invalidReason;

        private void HandlePlacementInput()
        {
            // Left-click: confirm placement.
            if (Input.GetMouseButtonDown(0))
            {
                if (!_lastValid)
                {
                    Debug.Log($"[CropPlacement] Click rejected — {_invalidReason ?? "cell invalid (no specific reason recorded)"}.");
                }
                else
                {
                    if (_mode == Mode.Placing)
                    {
                        Debug.Log($"[CropPlacement] Click accepted at cell ({_lastCellX},{_lastCellZ}) → RequestPlaceCropServerRpc(crop={_activeCrop.Id}).");
                        // HandsController is a non-networked MonoBehaviour — the server can't see
                        // the client's CarriedItem. Consume the seed locally on the OWNING peer
                        // before issuing the RPC; the server's seed-consume in
                        // CharacterAction_PlaceCrop.OnApplyEffect is then a no-op for dedicated
                        // clients (server hands empty) and a redundant clear for host (same
                        // HandsController instance). On host this means the seed is cleared on
                        // click rather than after the plant duration — acceptable trade-off.
                        ConsumeHeldSeedLocally(_activeCrop.Id);
                        RequestPlaceCropServerRpc(_lastCellX, _lastCellZ, _activeCrop.Id);
                    }
                    else
                    {
                        Debug.Log($"[CropPlacement] Click accepted at cell ({_lastCellX},{_lastCellZ}) → RequestWaterCellServerRpc.");
                        RequestWaterCellServerRpc(_lastCellX, _lastCellZ, GetActiveCanMoistureValue());
                    }
                    CancelPlacement();
                    return;
                }
            }
            // Right-click: clear ghost (could re-pick another seed).
            if (Input.GetMouseButtonDown(1))
            {
                ClearGhost();
                return;
            }
            // Escape: exit placement entirely (release building state).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
            }
        }

        // ────────────────────── Helpers ──────────────────────

        private float GetActiveCanMoistureValue()
        {
            var hands = _character.CharacterVisual != null && _character.CharacterVisual.BodyPartsController != null
                ? _character.CharacterVisual.BodyPartsController.HandsController
                : null;
            return hands?.CarriedItem?.ItemSO is WateringCanSO can ? can.MoistureSetTo : 1f;
        }

        /// <summary>
        /// Local-only seed consumption on the owning peer. Called immediately before the
        /// place-crop ServerRpc because HandsController.CarriedItem is not a NetworkVariable —
        /// clearing it on the server would have no effect on the dedicated client whose hand
        /// actually holds the seed. Verifies the held item still matches the requested crop;
        /// if it doesn't (e.g. the player swapped while in placement mode), no-op so we don't
        /// destroy an unrelated item.
        /// </summary>
        private void ConsumeHeldSeedLocally(string expectedCropId)
        {
            var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands == null || hands.CarriedItem == null) return;
            if (!(hands.CarriedItem.ItemSO is SeedSO seedSO)) return;
            if (seedSO.CropToPlant == null || seedSO.CropToPlant.Id != expectedCropId) return;
            hands.ClearCarriedItem();
            Debug.Log($"[CropPlacement] Seed '{seedSO.name}' consumed locally on owning peer for crop '{expectedCropId}'.");
        }

        // Strip everything that would interfere with the ghost being a passive cursor:
        // network identity (clients shouldn't see it), colliders (don't block raycast or push),
        // rigidbodies (don't fall), and put it on Ignore Raycast.
        private static void DisableGhostInterference(GameObject ghost)
        {
            if (ghost.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;
            if (ghost.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
            foreach (var col in ghost.GetComponentsInChildren<Collider>()) col.enabled = false;
            int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreLayer >= 0) SetLayerRecursive(ghost, ignoreLayer);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        private void ApplyGhostMaterials(Material mat)
        {
            if (_ghostInstance == null) return;
            // Materials only apply to MeshRenderers. For SpriteRenderer-based ghosts (crops), tint instead.
            bool tintedAny = false;
            foreach (var sr in _ghostInstance.GetComponentsInChildren<SpriteRenderer>())
            {
                sr.color = (mat == _ghostMaterialInvalid)
                    ? new Color(1f, 0.4f, 0.4f, 0.7f)
                    : new Color(1f, 1f, 1f, 0.7f);
                tintedAny = true;
            }
            if (!tintedAny && mat != null)
            {
                foreach (var r in _ghostInstance.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = mat;
            }
        }

        // ────────────────────── Server RPCs ──────────────────────

        [Rpc(SendTo.Server)]
        private void RequestPlaceCropServerRpc(int x, int z, string cropId, RpcParams rpcParams = default)
        {
            Debug.Log($"[CropPlacement.Server] RequestPlaceCropServerRpc cell=({x},{z}) crop='{cropId}'.");
            var crop = CropRegistry.Get(cropId);
            if (crop == null) { Debug.LogWarning($"[CropPlacement.Server] Bail — crop '{cropId}' not in registry."); return; }
            var map = MapController.GetMapAtPosition(_character.transform.position);
            if (map == null) { Debug.LogWarning("[CropPlacement.Server] Bail — no map at character position."); return; }
            EnsureGridInitialized(map);
            var grid = map.GetComponent<TerrainCellGrid>();
            if (grid == null) { Debug.LogWarning("[CropPlacement.Server] Bail — no TerrainCellGrid on map."); return; }

            ref var cell = ref grid.GetCellRef(x, z);
            if (!string.IsNullOrEmpty(cell.PlantedCropId))
            {
                Debug.LogWarning($"[CropPlacement.Server] Bail — cell ({x},{z}) already has crop '{cell.PlantedCropId}' (race lost).");
                return;
            }

            // Note: no held-seed check here. HandsController.CarriedItem is a non-networked
            // MonoBehaviour field — the server's view of a dedicated-client player's hand is
            // always empty. The owning client validates locally before issuing this RPC and
            // consumes the seed via ConsumeHeldSeedLocally. For a malicious client this trusts
            // them to have the seed; acceptable for co-op. Add a server-side inventory model
            // later if anti-cheat is required.

            Debug.Log($"[CropPlacement.Server] Queuing CharacterAction_PlaceCrop for {_character.name} at cell ({x},{z}).");
            _character.CharacterActions.ExecuteAction(
                new CharacterAction_PlaceCrop(_character, map, x, z, crop));
        }

        [Rpc(SendTo.Server)]
        private void RequestWaterCellServerRpc(int x, int z, float moistureSetTo, RpcParams rpcParams = default)
        {
            var map = MapController.GetMapAtPosition(_character.transform.position);
            if (map == null) return;
            _character.CharacterActions.ExecuteAction(
                new CharacterAction_WaterCrop(_character, map, x, z, moistureSetTo));
        }
    }
}
