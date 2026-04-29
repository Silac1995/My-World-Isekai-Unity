using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only cell mutation: marks a cell plowed, plants the crop, consumes one seed.
    /// Queued by CropPlacementManager.RequestPlaceCropServerRpc after validation.
    /// NPC GOAP/BT can construct it directly (rule #22 player↔NPC parity).
    /// See farming spec §5.3.
    /// </summary>
    public class CharacterAction_PlaceCrop : CharacterAction
    {
        private readonly int _cellX, _cellZ;
        private readonly CropSO _crop;
        private readonly MapController _map;

        public CharacterAction_PlaceCrop(Character actor, MapController map, int cellX, int cellZ, CropSO crop)
            : base(actor, crop != null ? crop.PlantDuration : 1f)
        {
            _map = map;
            _cellX = cellX;
            _cellZ = cellZ;
            _crop = crop;
        }

        public override string ActionName => "Plant";

        public override bool CanExecute()
        {
            if (_crop == null || _map == null)
            {
                Debug.LogWarning("[PlantAction] CanExecute=false — _crop or _map null.");
                return false;
            }
            var grid = _map.GetComponent<TerrainCellGrid>();
            if (grid == null)
            {
                Debug.LogWarning("[PlantAction] CanExecute=false — no TerrainCellGrid on map.");
                return false;
            }
            ref var cell = ref grid.GetCellRef(_cellX, _cellZ);
            bool canExec = string.IsNullOrEmpty(cell.PlantedCropId);
            if (!canExec) Debug.LogWarning($"[PlantAction] CanExecute=false — cell ({_cellX},{_cellZ}) already has crop '{cell.PlantedCropId}'.");
            return canExec;
        }

        public override void OnStart()
        {
            Debug.Log($"[PlantAction] OnStart — {character.name} starts planting '{_crop.Id}' at ({_cellX},{_cellZ}), Duration={Duration}s.");
        }

        public override void OnApplyEffect()
        {
            Debug.Log($"[PlantAction] OnApplyEffect — {character.name} applying plant of '{_crop?.Id}' at ({_cellX},{_cellZ}).");
            if (_map == null || _crop == null) { Debug.LogWarning("[PlantAction] Bail — _map or _crop null."); return; }
            var grid = _map.GetComponent<TerrainCellGrid>();
            if (grid == null) { Debug.LogWarning("[PlantAction] Bail — no TerrainCellGrid."); return; }

            ref var cell = ref grid.GetCellRef(_cellX, _cellZ);
            cell.IsPlowed = true;
            cell.PlantedCropId = _crop.Id;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;
            Debug.Log($"[PlantAction] Cell mutated. PlantedCropId='{cell.PlantedCropId}', IsPlowed={cell.IsPlowed}.");

            var hands = character.CharacterVisual != null && character.CharacterVisual.BodyPartsController != null
                ? character.CharacterVisual.BodyPartsController.HandsController
                : null;
            if (hands != null && hands.CarriedItem != null && hands.CarriedItem.ItemSO is SeedSO)
            {
                hands.ClearCarriedItem();
                Debug.Log("[PlantAction] Seed consumed from hand.");
            }

            int idx = _cellZ * grid.Width + _cellX;
            _map.NotifyDirtyCells(new[] { idx });
            Debug.Log($"[PlantAction] NotifyDirtyCells fired for index {idx}.");
        }

        public override void OnCancel()
        {
            Debug.LogWarning($"[PlantAction] OnCancel — plant of '{_crop?.Id}' at ({_cellX},{_cellZ}) cancelled.");
        }
    }
}
