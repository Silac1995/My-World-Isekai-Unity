using NUnit.Framework;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Tests.Buildings
{
    public class BuildingGridTests
    {
        [Test]
        public void SnapToGridCenter_rounds_to_cell_center_at_world_origin()
        {
            var grid = new BuildingGrid(Vector2.zero);
            // Cell (0,0) center is at (4, *, 4) with CellSizeUnits = 8.
            Vector3 snapped = grid.SnapToGridCenter(new Vector3(0.1f, 5f, 0.1f));
            Assert.AreEqual(4f, snapped.x, 1e-4f);
            Assert.AreEqual(4f, snapped.z, 1e-4f);
            Assert.AreEqual(5f, snapped.y, 1e-4f, "Y is preserved unchanged.");
        }

        [Test]
        public void SnapToGridCenter_handles_negative_coordinates()
        {
            var grid = new BuildingGrid(Vector2.zero);
            // Cell (-1, -1) center is at (-4, *, -4).
            Vector3 snapped = grid.SnapToGridCenter(new Vector3(-1f, 0f, -1f));
            Assert.AreEqual(-4f, snapped.x, 1e-4f);
            Assert.AreEqual(-4f, snapped.z, 1e-4f);
        }

        [Test]
        public void GetCellCoord_round_trips_via_SnapToGridCenter()
        {
            var grid = new BuildingGrid(Vector2.zero);
            Vector3 worldPos = new Vector3(17.3f, 0f, -22.8f);
            Vector3 snapped = grid.SnapToGridCenter(worldPos);
            Vector2Int cellA = grid.GetCellCoord(worldPos);
            Vector2Int cellB = grid.GetCellCoord(snapped);
            Assert.AreEqual(cellA, cellB, "Snap then re-resolve must give the same cell.");
        }

        [Test]
        public void CanPlace_empty_grid_accepts_anywhere()
        {
            var grid = new BuildingGrid(Vector2.zero);
            Assert.IsTrue(grid.CanPlace(new Vector2Int(0, 0), new Vector2Int(1, 1)));
            Assert.IsTrue(grid.CanPlace(new Vector2Int(-5, 100), new Vector2Int(3, 3)));
        }

        [Test]
        public void Register_then_CanPlace_rejects_overlap()
        {
            var grid = new BuildingGrid(Vector2.zero);
            grid.Register(buildingNetId: 42, new Vector2Int(0, 0), new Vector2Int(2, 2));
            Assert.IsFalse(grid.CanPlace(new Vector2Int(0, 0), new Vector2Int(1, 1)),
                "Overlap at exact origin must reject.");
            Assert.IsFalse(grid.CanPlace(new Vector2Int(1, 1), new Vector2Int(1, 1)),
                "Overlap at far corner of footprint must reject.");
            Assert.IsTrue(grid.CanPlace(new Vector2Int(2, 0), new Vector2Int(1, 1)),
                "Adjacent (non-overlapping) cell must accept.");
        }

        [Test]
        public void Release_frees_all_cells_owned_by_building()
        {
            var grid = new BuildingGrid(Vector2.zero);
            grid.Register(buildingNetId: 7, new Vector2Int(3, 4), new Vector2Int(2, 2));
            Assert.IsFalse(grid.CanPlace(new Vector2Int(3, 4), new Vector2Int(2, 2)));
            grid.Release(buildingNetId: 7);
            Assert.IsTrue(grid.CanPlace(new Vector2Int(3, 4), new Vector2Int(2, 2)),
                "After Release, the cells are free again.");
        }

        [Test]
        public void Register_idempotent_for_same_building()
        {
            var grid = new BuildingGrid(Vector2.zero);
            grid.Register(buildingNetId: 1, new Vector2Int(0, 0), new Vector2Int(1, 1));
            grid.Register(buildingNetId: 1, new Vector2Int(0, 0), new Vector2Int(1, 1));
            grid.Release(buildingNetId: 1);
            Assert.IsTrue(grid.CanPlace(new Vector2Int(0, 0), new Vector2Int(1, 1)),
                "Double-register followed by single Release must leave the cell free (idempotent).");
        }

        [Test]
        public void Register_zero_netId_is_noop()
        {
            var grid = new BuildingGrid(Vector2.zero);
            grid.Register(buildingNetId: 0, new Vector2Int(0, 0), new Vector2Int(1, 1));
            Assert.IsTrue(grid.CanPlace(new Vector2Int(0, 0), new Vector2Int(1, 1)),
                "NetId 0 is the sentinel for 'no owner' — must NOT occupy the cell.");
        }

        [Test]
        public void IsOccupied_reports_correct_state()
        {
            var grid = new BuildingGrid(Vector2.zero);
            Assert.IsFalse(grid.IsOccupied(new Vector2Int(5, 5)));
            grid.Register(buildingNetId: 1, new Vector2Int(5, 5), new Vector2Int(1, 1));
            Assert.IsTrue(grid.IsOccupied(new Vector2Int(5, 5)));
            grid.Release(buildingNetId: 1);
            Assert.IsFalse(grid.IsOccupied(new Vector2Int(5, 5)));
        }
    }
}
