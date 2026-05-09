using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only moisture set on a single cell. Watering an empty cell is allowed —
    /// raises moisture and drives the existing terrain transitions (Dirt → Mud) as a
    /// side effect, consistent with rain. See farming spec §7.
    /// </summary>
    public class CharacterAction_WaterCrop : CharacterAction
    {
        private readonly int _cellX, _cellZ;
        private readonly float _moistureSetTo;
        private readonly MapController _map;

        public CharacterAction_WaterCrop(Character actor, MapController map, int cellX, int cellZ, float moistureSetTo)
            : base(actor, 0.5f)
        {
            _map = map;
            _cellX = cellX;
            _cellZ = cellZ;
            _moistureSetTo = moistureSetTo;
        }

        public override string ActionName => "Water";

        public override bool CanExecute() => _map != null;

        public override void OnStart() { }

        public override void OnApplyEffect()
        {
            if (_map == null) return;
            var grid = _map.GetComponent<TerrainCellGrid>();
            if (grid == null) return;

            ref var cell = ref grid.GetCellRef(_cellX, _cellZ);
            cell.Moisture = _moistureSetTo;
            // For GROWING cells, also reset TimeSinceLastWatered to 0 — mirrors what
            // TerrainWeatherProcessor does on rain (line 95). This protects watering against
            // the ambient-decay loop incrementing it past a "drought" threshold over time.
            // For MATURE perennials, leave TimeSinceLastWatered untouched (it's the refill
            // counter / -1 sentinel; we don't want watering to interfere with that semantic).
            var crop = !string.IsNullOrEmpty(cell.PlantedCropId) ? CropRegistry.Get(cell.PlantedCropId) : null;
            if (crop != null && cell.GrowthTimer < crop.DaysToMature)
                cell.TimeSinceLastWatered = 0f;

            int idx = _cellZ * grid.Width + _cellX;
            _map.NotifyDirtyCells(new[] { idx });
            Debug.Log($"[WaterAction] Cell ({_cellX},{_cellZ}) watered. Moisture={cell.Moisture}, TimeSinceLastWatered={cell.TimeSinceLastWatered}, PlantedCropId='{cell.PlantedCropId}', GrowthTimer={cell.GrowthTimer}.");
        }
    }
}
