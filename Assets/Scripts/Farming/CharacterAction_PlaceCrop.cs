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
            if (_crop == null || _map == null) return false;
            var grid = _map.GetComponent<TerrainCellGrid>();
            if (grid == null) return false;
            ref var cell = ref grid.GetCellRef(_cellX, _cellZ);
            return string.IsNullOrEmpty(cell.PlantedCropId);
        }

        public override void OnStart() { }

        public override void OnApplyEffect()
        {
            if (_map == null || _crop == null) return;
            var grid = _map.GetComponent<TerrainCellGrid>();
            if (grid == null) return;

            ref var cell = ref grid.GetCellRef(_cellX, _cellZ);
            cell.IsPlowed = true;
            cell.PlantedCropId = _crop.Id;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;   // sentinel "not depleted"; ignored during growing-crop phase

            // Consume one seed from the carry slot.
            var hands = character.CharacterVisual != null && character.CharacterVisual.BodyPartsController != null
                ? character.CharacterVisual.BodyPartsController.HandsController
                : null;
            if (hands != null && hands.CarriedItem != null && hands.CarriedItem.ItemSO is SeedSO)
                hands.ClearCarriedItem();

            int idx = _cellZ * grid.Width + _cellX;
            _map.NotifyDirtyCells(new[] { idx });
        }
    }
}
