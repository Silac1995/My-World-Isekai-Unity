using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Per-character crop placement + watering system. Mirrors BuildingPlacementManager in
    /// shape (CharacterSystem with ghost lifecycle, ServerRpc to commit) but snaps to the
    /// terrain cell grid and validates farm-cell rules. See farming spec §5.2 + §7.
    /// </summary>
    public class CropPlacementManager : CharacterSystem
    {
        [Header("Settings")]
        [SerializeField] private LayerMask _groundLayer = ~0;
        [SerializeField] private float _maxRange = 5f;
        [SerializeField] private GameObject _ghostPrefab;     // optional; falls back to a procedural marker

        private GameObject _ghost;
        private SpriteRenderer _ghostSprite;
        private CropSO _activeCrop;
        private MapController _activeMap;
        private Mode _mode = Mode.Off;

        private enum Mode { Off, Placing, Watering }
        public bool IsActive => _mode != Mode.Off;

        public void StartPlacement(ItemInstance seedInstance)
        {
            if (!(seedInstance?.ItemSO is SeedSO seedSO) || seedSO.CropToPlant == null) return;
            CancelPlacement();
            _activeCrop = seedSO.CropToPlant;
            _activeMap = MapController.GetMapAtPosition(_character.transform.position);
            EnsureGhost();
            if (_ghostSprite != null) _ghostSprite.sprite = _activeCrop.GetStageSprite(0);
            _mode = Mode.Placing;
        }

        public void StartWatering()
        {
            CancelPlacement();
            _activeMap = MapController.GetMapAtPosition(_character.transform.position);
            EnsureGhost();
            if (_ghostSprite != null) _ghostSprite.sprite = null;
            _mode = Mode.Watering;
        }

        public void CancelPlacement()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null; _ghostSprite = null;
            _activeCrop = null; _activeMap = null;
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

        private void Update()
        {
            if (_mode == Mode.Off || !IsOwner) return;
            if (_activeMap == null || _ghost == null) return;

            var grid = _activeMap.GetComponent<TerrainCellGrid>();
            if (grid == null) return;

            // Raycast mouse to ground.
            if (Camera.main == null) return;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 100f, _groundLayer)) return;

            if (!grid.WorldToGrid(hit.point, out int x, out int z))
            {
                _ghost.SetActive(false);
                return;
            }
            _ghost.SetActive(true);
            _ghost.transform.position = grid.GridToWorld(x, z);

            ref var cell = ref grid.GetCellRef(x, z);
            bool valid = ValidateCell(in cell, _ghost.transform.position);
            ApplyGhostTint(valid);

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
                return;
            }
            if (valid && Input.GetMouseButtonDown(0))
            {
                if (_mode == Mode.Placing)
                    RequestPlaceCropServerRpc(x, z, _activeCrop.Id);
                else
                    RequestWaterCellServerRpc(x, z, GetActiveCanMoistureValue());
                CancelPlacement();
            }
        }

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
            // Watering: any cell in range is valid.
            return true;
        }

        private float GetActiveCanMoistureValue()
        {
            var hands = _character.CharacterVisual != null && _character.CharacterVisual.BodyPartsController != null
                ? _character.CharacterVisual.BodyPartsController.HandsController
                : null;
            var carried = hands?.CarriedItem?.ItemSO as WateringCanSO;
            return carried != null ? carried.MoistureSetTo : 1f;
        }

        private void EnsureGhost()
        {
            if (_ghost != null) return;
            if (_ghostPrefab != null)
            {
                _ghost = Instantiate(_ghostPrefab);
            }
            else
            {
                // Procedural fallback: a small semi-transparent quad with a SpriteRenderer.
                _ghost = new GameObject("CropPlacementGhost");
                _ghostSprite = _ghost.AddComponent<SpriteRenderer>();
                _ghostSprite.color = new Color(1f, 1f, 1f, 0.5f);
                _ghost.transform.localScale = Vector3.one * 1.5f;
                return;
            }
            if (_ghostSprite == null) _ghostSprite = _ghost.GetComponentInChildren<SpriteRenderer>();
        }

        private void ApplyGhostTint(bool valid)
        {
            if (_ghostSprite == null) return;
            _ghostSprite.color = valid
                ? new Color(1f, 1f, 1f, 0.7f)
                : new Color(1f, 0.4f, 0.4f, 0.7f);
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
