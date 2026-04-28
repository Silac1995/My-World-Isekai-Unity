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
            // TimeSinceLastWatered stays as-is — only meaningful when cell is in perennial-refill phase,
            // and refill catch-up is FarmGrowthSystem's job, not the watering action's.

            int idx = _cellZ * grid.Width + _cellX;
            _map.NotifyDirtyCells(new[] { idx });
        }
    }
}
