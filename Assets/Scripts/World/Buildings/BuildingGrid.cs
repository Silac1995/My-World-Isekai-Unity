using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Server-only per-<see cref="MapController"/> occupancy grid for building footprints.
    /// Cells are 8 world-units square (8× the crop/furniture sub-cell). Sparse representation:
    /// only occupied cells are stored. World-space origin is configurable but defaults to
    /// <see cref="Vector2.zero"/> so two maps in the same Region share a coordinate system.
    /// <para>
    /// Not a <see cref="NetworkBehaviour"/>. Server-only state. Clients perform their own
    /// SnapToGridCenter math locally for the ghost-preview visual but never query occupancy
    /// (their grid is empty). See <c>wiki/concepts/building-grid.md</c>.
    /// </para>
    /// </summary>
    public class BuildingGrid
    {
        /// <summary>One cell = 8 world units. 11 units = 1.67 m (CLAUDE.md rule #32),
        /// so a cell ≈ 1.21 m — small enough that a 1×1 cottage fits in one cell and
        /// a 2×2 town hall fits in four.</summary>
        public const float CellSizeUnits = 8f;

        /// <summary>Sparse occupancy: key = (cellX, cellZ), value = the Building's NetworkObjectId.</summary>
        private readonly Dictionary<Vector2Int, ulong> _cells = new Dictionary<Vector2Int, ulong>();

        /// <summary>World-space origin of cell (0, 0). Stored as XZ since the grid is flat.</summary>
        private readonly Vector2 _originXZ;

        public BuildingGrid(Vector2 originXZ)
        {
            _originXZ = originXZ;
        }

        /// <summary>
        /// Snaps an arbitrary world position to the centre of the cell it falls in.
        /// Y is preserved unchanged so the building's vertical position (terrain-relative)
        /// is not affected.
        /// </summary>
        public Vector3 SnapToGridCenter(Vector3 worldPos)
        {
            Vector2Int cell = GetCellCoord(worldPos);
            return new Vector3(
                _originXZ.x + (cell.x + 0.5f) * CellSizeUnits,
                worldPos.y,
                _originXZ.y + (cell.y + 0.5f) * CellSizeUnits);
        }

        /// <summary>
        /// Returns the world-space center of <paramref name="cell"/>. Y can be supplied
        /// explicitly (e.g. the placer's terrain-relative Y) or left at 0 for systems
        /// that resolve Y separately.
        /// </summary>
        public Vector3 GetCellCenter(Vector2Int cell, float y = 0f)
        {
            return new Vector3(
                _originXZ.x + (cell.x + 0.5f) * CellSizeUnits,
                y,
                _originXZ.y + (cell.y + 0.5f) * CellSizeUnits);
        }

        /// <summary>Resolves an arbitrary world position to the integer cell coordinate it occupies.</summary>
        public Vector2Int GetCellCoord(Vector3 worldPos)
        {
            int cx = Mathf.FloorToInt((worldPos.x - _originXZ.x) / CellSizeUnits);
            int cz = Mathf.FloorToInt((worldPos.z - _originXZ.y) / CellSizeUnits);
            return new Vector2Int(cx, cz);
        }

        /// <summary>
        /// True iff every cell in the rectangle <paramref name="originCell"/>+<paramref name="sizeInGridCells"/>
        /// is currently unoccupied. Used by <see cref="BuildingPlacementManager"/> as a placement gate.
        /// </summary>
        public bool CanPlace(Vector2Int originCell, Vector2Int sizeInGridCells)
        {
            if (sizeInGridCells.x <= 0 || sizeInGridCells.y <= 0) return false;
            for (int dx = 0; dx < sizeInGridCells.x; dx++)
            {
                for (int dz = 0; dz < sizeInGridCells.y; dz++)
                {
                    if (_cells.ContainsKey(new Vector2Int(originCell.x + dx, originCell.y + dz)))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Marks every cell in the rectangle <paramref name="originCell"/>+<paramref name="sizeInGridCells"/>
        /// as occupied by <paramref name="buildingNetId"/>. Idempotent: re-registering the same building
        /// at the same cells is a no-op. <paramref name="buildingNetId"/> of 0 is the NGO sentinel for
        /// "no owner" and is rejected (no-op) — defensive.
        /// </summary>
        public void Register(ulong buildingNetId, Vector2Int originCell, Vector2Int sizeInGridCells)
        {
            if (buildingNetId == 0) return;
            if (sizeInGridCells.x <= 0 || sizeInGridCells.y <= 0) return;
            for (int dx = 0; dx < sizeInGridCells.x; dx++)
            {
                for (int dz = 0; dz < sizeInGridCells.y; dz++)
                {
                    _cells[new Vector2Int(originCell.x + dx, originCell.y + dz)] = buildingNetId;
                }
            }
        }

        /// <summary>
        /// Frees every cell currently mapped to <paramref name="buildingNetId"/>. O(cell count)
        /// — fine for the projected scale (≤ a few hundred buildings per map).
        /// </summary>
        public void Release(ulong buildingNetId)
        {
            if (buildingNetId == 0) return;
            // Two-pass: collect keys first to avoid mutating during enumeration.
            List<Vector2Int> toRemove = null;
            foreach (var kv in _cells)
            {
                if (kv.Value == buildingNetId)
                {
                    toRemove ??= new List<Vector2Int>(4);
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove == null) return;
            for (int i = 0; i < toRemove.Count; i++) _cells.Remove(toRemove[i]);
        }

        /// <summary>True iff <paramref name="cell"/> is currently occupied by any building.</summary>
        public bool IsOccupied(Vector2Int cell) => _cells.ContainsKey(cell);

        /// <summary>Returns the NetworkObjectId of the building occupying <paramref name="cell"/>, or 0 if empty.</summary>
        public ulong GetOccupant(Vector2Int cell) => _cells.TryGetValue(cell, out ulong id) ? id : 0;

        /// <summary>Read-only count of occupied cells — useful for diagnostic UI / debug overlays.</summary>
        public int OccupiedCellCount => _cells.Count;
    }
}
