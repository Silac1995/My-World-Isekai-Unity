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
        [SerializeField] private float _maxRange = 5f;
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
            if (dist > _maxRange) return false;

            if (_mode == Mode.Placing)
            {
                var type = cell.GetCurrentType();
                if (type != null && !type.CanGrowVegetation) return false;
                return string.IsNullOrEmpty(cell.PlantedCropId);
            }
            return true;
        }

        private void HandlePlacementInput()
        {
            // Left-click: confirm placement.
            if (_lastValid && Input.GetMouseButtonDown(0))
            {
                if (_mode == Mode.Placing)
                    RequestPlaceCropServerRpc(_lastCellX, _lastCellZ, _activeCrop.Id);
                else
                    RequestWaterCellServerRpc(_lastCellX, _lastCellZ, GetActiveCanMoistureValue());
                CancelPlacement();
                return;
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
            var crop = CropRegistry.Get(cropId);
            if (crop == null) return;
            var map = MapController.GetMapAtPosition(_character.transform.position);
            if (map == null) return;
            var grid = map.GetComponent<TerrainCellGrid>();
            if (grid == null) return;

            ref var cell = ref grid.GetCellRef(x, z);
            if (!string.IsNullOrEmpty(cell.PlantedCropId)) return;   // race: someone else planted

            // Re-verify the caller still holds a SeedSO whose CropToPlant matches cropId.
            var hands = _character.CharacterVisual != null && _character.CharacterVisual.BodyPartsController != null
                ? _character.CharacterVisual.BodyPartsController.HandsController
                : null;
            if (!(hands?.CarriedItem?.ItemSO is SeedSO seedSO) || seedSO.CropToPlant == null || seedSO.CropToPlant.Id != cropId)
                return;

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
