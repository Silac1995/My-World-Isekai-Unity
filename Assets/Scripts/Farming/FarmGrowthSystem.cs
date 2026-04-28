using System.Collections.Generic;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only daily tick. One instance per active MapController. See farming spec §4 + §9.2.
    ///
    /// This file provides the minimum surface needed by CropHarvestable to compile.
    /// Daily pipeline + PostWakeSweep are added in a later task.
    /// </summary>
    public class FarmGrowthSystem : MonoBehaviour
    {
        private TerrainCellGrid _grid;
        private MapController _map;
        private readonly Dictionary<int, CropHarvestable> _activeHarvestables = new Dictionary<int, CropHarvestable>(64);

        public void Initialize(TerrainCellGrid grid, MapController map)
        {
            _grid = grid;
            _map = map;
        }

        public void RegisterHarvestable(int x, int z, CropHarvestable h)
        {
            if (_grid == null) return;
            _activeHarvestables[LinearIndex(x, z)] = h;
        }

        public void UnregisterHarvestable(int x, int z)
        {
            if (_grid == null) return;
            _activeHarvestables.Remove(LinearIndex(x, z));
        }

        private int LinearIndex(int x, int z) => z * _grid.Width + x;
    }
}
