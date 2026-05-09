using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWI.Terrain
{
    public class TerrainCellGrid : MonoBehaviour
    {
        [SerializeField] private float _cellSize = 4f;

        private TerrainCell[] _cells;
        private int _width;
        private int _depth;
        private Vector3 _origin;

        public int Width => _width;
        public int Depth => _depth;
        public float CellSize => _cellSize;
        public int CellCount => _cells?.Length ?? 0;

        // --- Initialization ---

        public void Initialize(Bounds mapBounds)
        {
            _origin = new Vector3(mapBounds.min.x, 0f, mapBounds.min.z);
            _width = Mathf.CeilToInt(mapBounds.size.x / _cellSize);
            _depth = Mathf.CeilToInt(mapBounds.size.z / _cellSize);
            _cells = new TerrainCell[_width * _depth];

            Debug.Log($"[TerrainCellGrid] Initialized {_width}x{_depth} = {_cells.Length} cells " +
                      $"(cellSize={_cellSize}, origin={_origin})");
        }

        public void InitializeFromPatches(List<TerrainPatch> patches)
        {
            if (_cells == null)
            {
                Debug.LogError("[TerrainCellGrid] Call Initialize() before InitializeFromPatches().");
                return;
            }

            // Sort patches by priority (lowest first, so highest overwrites)
            var sorted = patches.OrderBy(p => p.Priority).ToList();

            for (int z = 0; z < _depth; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    Vector3 worldPos = GridToWorld(x, z);
                    TerrainPatch bestPatch = null;

                    foreach (var patch in sorted)
                    {
                        if (patch.Bounds.Contains(worldPos))
                            bestPatch = patch; // Higher priority overwrites
                    }

                    int idx = z * _width + x;
                    if (bestPatch != null)
                    {
                        _cells[idx].BaseTypeId = bestPatch.BaseTerrainType.TypeId;
                        _cells[idx].CurrentTypeId = bestPatch.BaseTerrainType.TypeId;
                        _cells[idx].Fertility = bestPatch.BaseTerrainType.CanGrowVegetation
                            ? bestPatch.BaseFertility : 0f;
                    }
                }
            }
        }

        // --- Queries ---

        public TerrainType GetTerrainAt(Vector3 worldPos)
        {
            if (!WorldToGrid(worldPos, out int x, out int z)) return null;
            return _cells[z * _width + x].GetCurrentType();
        }

        public ref TerrainCell GetCellRef(int x, int z)
        {
            return ref _cells[z * _width + x];
        }

        public TerrainCell GetCellAt(Vector3 worldPos)
        {
            if (!WorldToGrid(worldPos, out int x, out int z)) return default;
            return _cells[z * _width + x];
        }

        // --- Coordinate conversion ---

        public bool WorldToGrid(Vector3 worldPos, out int x, out int z)
        {
            x = Mathf.FloorToInt((worldPos.x - _origin.x) / _cellSize);
            z = Mathf.FloorToInt((worldPos.z - _origin.z) / _cellSize);
            bool inBounds = x >= 0 && x < _width && z >= 0 && z < _depth;
            if (!inBounds) { x = -1; z = -1; }
            return inBounds;
        }

        public Vector3 GridToWorld(int x, int z)
        {
            return new Vector3(
                _origin.x + (x + 0.5f) * _cellSize,
                _origin.y,
                _origin.z + (z + 0.5f) * _cellSize
            );
        }

        // --- Serialization ---

        public TerrainCellSaveData[] SerializeCells()
        {
            if (_cells == null) return null;
            var data = new TerrainCellSaveData[_cells.Length];
            for (int i = 0; i < _cells.Length; i++)
                data[i] = TerrainCellSaveData.FromCell(_cells[i]);
            return data;
        }

        public void RestoreFromSaveData(TerrainCellSaveData[] data)
        {
            if (data == null || _cells == null) return;
            int count = Mathf.Min(data.Length, _cells.Length);
            for (int i = 0; i < count; i++)
                _cells[i] = data[i].ToCell();
        }

        // --- Grid iteration helpers (for TerrainWeatherProcessor) ---

        /// <summary>
        /// Returns the inclusive grid coordinate range covered by the given world bounds.
        /// Note: if worldBounds is entirely outside the grid, maxX may be less than minX
        /// (or maxZ less than minZ). Callers should use for(z = minZ; z <= maxZ; z++) which
        /// naturally produces zero iterations in that case.
        /// </summary>
        public void GetCellRangeForBounds(Bounds worldBounds, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            WorldToGrid(worldBounds.min, out minX, out minZ);
            WorldToGrid(worldBounds.max, out maxX, out maxZ);
            minX = Mathf.Max(0, minX);
            minZ = Mathf.Max(0, minZ);
            maxX = Mathf.Min(_width - 1, maxX);
            maxZ = Mathf.Min(_depth - 1, maxZ);
        }
    }
}
