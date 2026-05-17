# Building Grid Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-`MapController` `BuildingGrid` (8-unit cells) that gates and snaps every building placement. New `BuildingSO` fields (`GridFootprintCells`, `BlueprintCategory`, `MinTier`) encode each blueprint's cell footprint and city-system metadata. `BuildingPlacementManager` ghost preview snaps to the cell under the cursor, server-side `ValidatePlacement` adds a `BuildingGrid.CanPlace` gate, and `Building.OnNetworkSpawn` / `OnNetworkDespawn` keep grid occupancy live with no separate save channel.

**Architecture:**
- **`BuildingGrid`** is a plain C# class held per `MapController` (server-only field). It stores cell occupancy in a sparse `Dictionary<Vector2Int, ulong>` keyed by `(cellX, cellZ)` with values = `Building.NetworkObjectId`. Sparse-by-design so the grid spans the *full Region* implicitly — there's no need to resize when `MapController.ExpandBoundsToInclude` grows the map bounds. Cell math is world-space via `_originXZ = Vector2.zero` (cells are indexed from world origin, not from map center) so two maps in the same region trivially share a coordinate system.
- **No `BuildingGridSaveData`** (deviation from spec line 1062-1071). Grid occupancy is *derived* from live `Building` instances + their `BuildingSO.GridFootprintCells`. On wake-up, `MapController.SpawnSavedBuildings` already iterates every `BuildingSaveData`, instantiates each, and calls `OnNetworkSpawn`. Adding `BuildingGrid.Register(this, footprintCells)` to `Building.OnNetworkSpawn` (server-only branch) re-registers each restored building automatically. This mirrors how `TerrainCellGrid` does NOT separately persist its derived state from `TerrainCellSaveData[]` — one source of truth, no desync class possible.
- **`BuildingSO`** gains three placement-related fields: `_gridFootprintCells : Vector2Int` (default 1×1), `_blueprintCategory : BlueprintCategory` (enum `Personal | Civic`, default `Personal`), `_minTier : CommunityLevel` (default `SmallGroup`, only meaningful for `Civic`). The latter two are consumed by Plan 5's admin-console authority gate; Plan 2 just exposes the getters.
- **`Building.GridFootprintCells`** is a convenience getter reading from `_blueprint.GridFootprintCells` (defaults to 1×1 when blueprint is null — same defensive pattern as the existing `BuildingName` / `BuildingType` getters).
- **`BuildingPlacementManager.UpdateGhostPosition`** snaps `_ghostInstance.transform.position` to `MapController.BuildingGrid.SnapToGridCenter(hit.point)` *when the cursor is inside a map* (falls back to raw `hit.point` when outside any map — placement is then rejected anyway by `IsInsideRegion`). `ValidatePlacement` adds `BuildingGrid.CanPlace(cellOrigin, footprintCells)` as its 5th gate.
- **Cell occupancy lifecycle**: `Building.OnNetworkSpawn` (server) calls `EnclosingMap.BuildingGrid.Register(this, footprintCells)`. `Building.OnNetworkDespawn` calls `Release(this)`. Building's `EnclosingMap` resolves via `GetComponentInParent<MapController>()` with a `null` fallback (defensive — interior buildings + buildings in flight from spawn-race conditions just skip the grid op, with a verbose-gated log).

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9 / .NET Framework 4.8, NUnit EditMode tests via `tests-run` MCP tool. No new assemblies, no new dependencies, no new SerializeReference, no `NetworkBehaviour` — `BuildingGrid` is a plain C# server-side class.

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first), #9-#14 (SOLID — `BuildingGrid` has one job: cell occupancy), #15 (`_underscorePrefix` private fields), #16 (no event subscriptions — pure data class), #18/#19/#19b (server-only state — full network audit below), #22 (player↔NPC parity — `ValidatePlacement` is shared between player ghost and NPC AI per existing comment on the method), #28/#29/#29b (skill + agent + wiki updates), #31 (defensive null-check on `EnclosingMap`), #34 (no per-frame allocs — Dictionary.Add/Remove cost paid once per spawn/despawn, not per frame).

**Network safety audit (rule #19b — performed BEFORE writing the plan):**
1. **Who writes `BuildingGrid` occupancy?** Server-only (`Building.OnNetworkSpawn` server branch, `Building.OnNetworkDespawn`). No client write paths.
2. **What replication channel?** **None** — `BuildingGrid` is server-only state. Clients never read it directly. The ghost preview's `SnapToGridCenter` is a *pure math function* that runs client-side too without needing replicated occupancy (client passes the world position, gets the snapped position back — no occupancy lookup needed for the visual). `ValidatePlacement.CanPlace` runs both client-side (for the ghost color hint) and server-side (for the authoritative gate). The client-side `CanPlace` reads the client's local `BuildingGrid` — but the client's `BuildingGrid` is *empty* (no register calls fire client-side since `OnNetworkSpawn` server branch gates them). This means client-side ghost color shows green even on occupied cells; the server still rejects via `CanPlace`. That's an acceptable visual lag for v1 — the toast-on-server-reject path already exists. **Deferred**: a future task could replicate occupancy via a NetworkList for accurate client-side green/red, but it's not required to make Plan 2 work correctly.
3. **Late-joiner sees?** Same as today — buildings still spawn via the existing `MapController.SpawnSavedBuildings` path, and the server's `BuildingGrid` is rebuilt as each `Building.OnNetworkSpawn` fires. A joining client never directly observes the grid; they just observe the spawned buildings. No new replication.
4. **Client-side pre-gate?** `BuildingPlacementManager.ValidatePlacement` runs both client and server. Server is authoritative (cheat-proof). Client-side mismatch on `CanPlace` (because client's grid is empty) means the visual is "optimistic green" — server toast handles the rejection. Acceptable per #2 above.
5. **`GetComponentInParent` spawn-race?** `Building.OnNetworkSpawn` calls `GetComponentInParent<MapController>()` — at this point the building has already been `transform.SetParent(map.transform)` by `BuildingPlacementManager.RegisterBuildingWithMap` (or `MapController.SpawnSavedBuildings`). Both code paths SetParent *before* the network spawn fires. A null result here means we have an interior building (where MapController is the interior map) or a building somehow detached from any map — log + skip the grid op (defensive, doesn't block spawn).
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?** N/A — Plan 2 doesn't add any new NPC↔interactable surface.

The audit's full conclusion is committed alongside the implementation in the final summary commit, mirroring Plan 1's pattern.

---

## File Structure

**New files:**
- `Assets/Scripts/World/Buildings/BuildingGrid.cs` — plain C# class, server-only field on MapController.
- `Assets/Scripts/World/Data/BlueprintCategory.cs` — enum file (separate from BuildingSO so other files can reference it cleanly).
- `Assets/Editor/Tests/Buildings/BuildingGridTests.cs` — EditMode unit tests on the grid math + occupancy.

**Modified files:**
- `Assets/Scripts/World/Data/BuildingSO.cs` — add `_gridFootprintCells` (Vector2Int), `_blueprintCategory` (BlueprintCategory), `_minTier` (CommunityLevel) fields + matching getters.
- `Assets/Scripts/World/Buildings/Building.cs` — add `GridFootprintCells` convenience getter (reads from `_blueprint`); add `BuildingGrid.Register` call in `OnNetworkSpawn` server branch; add `Release` call in `OnNetworkDespawn`.
- `Assets/Scripts/World/MapSystem/MapController.cs` — add `private BuildingGrid _buildingGrid` + public getter; initialise in `Awake` (after `_mapTrigger` resolves); leave its world-space origin at Vector2.zero (sparse dict — no resize on bounds change).
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — `UpdateGhostPosition` snaps to `MapController.BuildingGrid.SnapToGridCenter(hit.point)` when a map is found at the hit position; `ValidatePlacement` adds the `CanPlace` gate.

**Docs updated:**
- `.agent/skills/building_system/SKILL.md` — `BuildingGrid` section + `BuildingSO.GridFootprintCells/BlueprintCategory/MinTier` table entries.
- `wiki/systems/building.md` — Public API + State & persistence + Change log + cross-link to `[[city-founding-and-administrative-building]]` spec.
- `wiki/systems/building-placement.md` — if exists, otherwise leave a note in `wiki/systems/building.md` mentioning the snap-to-grid behaviour.
- `wiki/concepts/building-grid.md` (NEW) — concept page (cell-size rationale, sparse representation, derived-state design choice).

**Out of scope (deferred to later plans):**
- `BuildingGridSaveData` — replaced by derived-state-from-buildings design (documented in Architecture above).
- `BuildingPlacementManager.RequestPlaceCityBlueprintServerRpc` (RTS placement from admin console) — Plan 5.
- The `MinTier` / `BlueprintCategory.Civic` authority gate — Plan 5.
- Client-side accurate ghost-color (would require occupancy replication) — Future.
- `BuildingGrid.Register` returning a `Vector2Int cellOrigin` for save round-trip — N/A (derived).

---

## Task 1: Add `BlueprintCategory` enum + `BuildingSO` placement fields

**Files:**
- Create: `Assets/Scripts/World/Data/BlueprintCategory.cs`
- Modify: `Assets/Scripts/World/Data/BuildingSO.cs` (add 3 fields + 3 getters)

- [ ] **Step 1: Create the enum file**

`Assets/Scripts/World/Data/BlueprintCategory.cs`:

```csharp
namespace MWI.WorldSystem
{
    /// <summary>
    /// Top-level placement gate for <see cref="BuildingSO"/>:
    /// <list type="bullet">
    /// <item><c>Personal</c> — any character with the blueprint can place it via the normal
    /// <see cref="BuildingPlacementManager"/> ghost flow (e.g. a House on your own land).</item>
    /// <item><c>Civic</c> — only a community leader can place it via the admin console
    /// (Plan 5), and the community must have reached <see cref="BuildingSO.MinTier"/>
    /// (e.g. a Town Hall).</item>
    /// </list>
    /// Plan 2 only exposes the field. The authority gate ships with the admin console.
    /// </summary>
    public enum BlueprintCategory
    {
        Personal = 0,
        Civic = 1,
    }
}
```

- [ ] **Step 2: Add fields + getters to `BuildingSO.cs`**

In `Assets/Scripts/World/Data/BuildingSO.cs`, after the existing `[Header("Default Furniture")]` block (around line 47) and before the property block (line 50), add:

```csharp
        [Header("Placement (Plan 2 — City Founding)")]
        [Tooltip("Footprint in BuildingGrid cells (default 1×1 = one 8-unit cell). Larger blueprints occupy a rectangle of cells; placement is rejected if any cell is occupied. Authoritative dimension; ghost preview snaps the bottom-left cell under the cursor.")]
        [SerializeField] private Vector2Int _gridFootprintCells = new Vector2Int(1, 1);

        [Tooltip("Placement-authority category. Personal = anyone with the blueprint can place via the normal ghost flow. Civic = only a community leader can place via the admin console (Plan 5).")]
        [SerializeField] private BlueprintCategory _blueprintCategory = BlueprintCategory.Personal;

        [Tooltip("Minimum community tier required for placement. Only enforced for Civic blueprints by Plan 5's admin-console authority gate.")]
        [SerializeField] private CommunityLevel _minTier = CommunityLevel.SmallGroup;
```

And after the existing getters block (around line 58), add:

```csharp
        public Vector2Int GridFootprintCells => _gridFootprintCells;
        public BlueprintCategory BlueprintCategory => _blueprintCategory;
        public CommunityLevel MinTier => _minTier;
```

- [ ] **Step 3: Compile-check via Unity console**

Use `assets-refresh`, then `console-get-logs` filtering on compile errors. Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Data/BlueprintCategory.cs Assets/Scripts/World/Data/BuildingSO.cs
git commit -m "$(cat <<'EOF'
feat(building-so): add GridFootprintCells + BlueprintCategory + MinTier

Three additive fields on BuildingSO for Plan 2 (BuildingGrid integration) and
Plan 5 (admin-console RTS placement authority gate):

- _gridFootprintCells : Vector2Int — cell footprint (default 1×1)
- _blueprintCategory : BlueprintCategory { Personal, Civic } — placement gate
- _minTier : CommunityLevel — Civic-only tier requirement (enforced by Plan 5)

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 2: Add `Building.GridFootprintCells` convenience getter

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

- [ ] **Step 1: Add the getter**

In `Assets/Scripts/World/Buildings/Building.cs`, after the existing blueprint-derived getter block (around line 175 where `BuildingType` is defined), add:

```csharp
    /// <summary>
    /// Footprint size in <see cref="BuildingGrid"/> cells, sourced from
    /// <see cref="_blueprint"/>.GridFootprintCells. Defaults to (1, 1) when no blueprint
    /// is assigned — same defensive shape as <see cref="BuildingName"/> / <see cref="BuildingType"/>.
    /// </summary>
    public Vector2Int GridFootprintCells =>
        _blueprint != null ? _blueprint.GridFootprintCells : new Vector2Int(1, 1);
```

- [ ] **Step 2: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "$(cat <<'EOF'
feat(building): add GridFootprintCells convenience getter on Building

Reads from _blueprint.GridFootprintCells, defaults to (1, 1) when the blueprint
is null. Mirrors the defensive shape of BuildingName / BuildingType getters.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 3: Create `BuildingGrid` class + EditMode tests

**Files:**
- Create: `Assets/Scripts/World/Buildings/BuildingGrid.cs`
- Create: `Assets/Editor/Tests/Buildings/BuildingGridTests.cs`

- [ ] **Step 1: Write the failing tests**

`Assets/Editor/Tests/Buildings/BuildingGridTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Buildings.BuildingGridTests`. Expected: FAIL with "BuildingGrid type does not exist".

- [ ] **Step 3: Create `BuildingGrid.cs`**

`Assets/Scripts/World/Buildings/BuildingGrid.cs`:

```csharp
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

        /// <summary>Resolves an arbitrary world position to the integer cell coordinate it occupies.</summary>
        public Vector2Int GetCellCoord(Vector3 worldPos)
        {
            int cx = Mathf.FloorToInt((worldPos.x - _originXZ.x) / CellSizeUnits);
            int cz = Mathf.FloorToInt((worldPos.z - _originXZ.y) / CellSizeUnits);
            return new Vector2Int(cx, cz);
        }

        /// <summary>
        /// True iff every cell in the rectangle <paramref name="originCell"/>+<paramref name="sizeInGridCells"/>
        /// is currently unoccupied. Used by <see cref="BuildingPlacementManager.ValidatePlacement"/>
        /// as a placement gate.
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
        /// at the same cells is a no-op. <see cref="buildingNetId"/> of 0 is the NGO sentinel for
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
```

- [ ] **Step 4: Re-run the tests**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Buildings.BuildingGridTests`. Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingGrid.cs Assets/Editor/Tests/Buildings/BuildingGridTests.cs
git commit -m "$(cat <<'EOF'
feat(building-grid): add BuildingGrid sparse per-map occupancy class

Plain C# class, server-only state on MapController (Task 4 wires it up).
Sparse Dictionary<Vector2Int, ulong> keyed by cell, value = Building.NetworkObjectId.

Public API:
- SnapToGridCenter(Vector3 worldPos) → Vector3 (cell-centre snap, Y preserved)
- GetCellCoord(Vector3 worldPos) → Vector2Int
- CanPlace(Vector2Int origin, Vector2Int size) → bool
- Register(ulong netId, Vector2Int origin, Vector2Int size) → void (idempotent)
- Release(ulong netId) → void
- IsOccupied(Vector2Int) / GetOccupant(Vector2Int) / OccupiedCellCount

CellSizeUnits = 8 (8× crop/furniture sub-cell, ≈1.21 m per rule #32).

NetId 0 (NGO "no owner" sentinel) is rejected on Register as a safety guard.
Negative size on CanPlace / Register is also rejected.

9 EditMode tests cover: snap correctness (positive + negative coords),
round-trip, CanPlace empty/overlap/adjacent, Register/Release lifecycle,
idempotency, sentinel handling, and IsOccupied state.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 4: Integrate `BuildingGrid` into `MapController`

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`

- [ ] **Step 1: Add the field + getter**

In `Assets/Scripts/World/MapSystem/MapController.cs`, find the existing field block near `_mapTrigger` (around line 70-90). Add right after `_mapTrigger`:

```csharp
        /// <summary>
        /// Server-only per-map building occupancy grid. Initialised in <see cref="Awake"/>;
        /// populated lazily as each <see cref="Building.OnNetworkSpawn"/> fires. Never replicated.
        /// </summary>
        private BuildingGrid _buildingGrid;

        /// <summary>
        /// The per-map <see cref="BuildingGrid"/>. Returns a usable instance on every spawned
        /// MapController (even pre-OnNetworkSpawn, as long as Awake has fired). Returns null
        /// only for un-instantiated MapControllers (test fixtures using <see cref="AddComponent"/>
        /// inside the same frame without Awake having run — extremely rare in production).
        /// </summary>
        public BuildingGrid BuildingGrid => _buildingGrid;
```

(Field location: pick the same visual block as `_mapTrigger`. If the file uses `#region` markers, place inside the "Fields" or equivalent section. Either way, keep it private + underscore-prefixed per rule #15.)

- [ ] **Step 2: Initialise in `Awake`**

In the existing `Awake()` method (around line 220), after `_mapTrigger = GetComponent<BoxCollider>();`, add:

```csharp
            // BuildingGrid is created with a world-space origin of Vector2.zero so cells
            // are indexed from world origin (not map center). This means two maps in the
            // same Region share a coordinate system — never confusing two cells that look
            // close visually but belong to different maps. Sparse-by-design (Dictionary
            // under the hood) so the grid implicitly spans the entire Region with zero
            // allocation cost until cells are actually occupied.
            _buildingGrid = new BuildingGrid(Vector2.zero);
```

- [ ] **Step 3: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapController.cs
git commit -m "$(cat <<'EOF'
feat(map-controller): hold a per-map BuildingGrid instance

- _buildingGrid : BuildingGrid (private, server-side)
- BuildingGrid public getter (read-only)
- Initialised in Awake with Vector2.zero origin so two maps in the same Region
  share a coordinate system

Task 5 wires Building.OnNetworkSpawn / OnNetworkDespawn to register/release on
the host map's grid; Task 6 wires the placement-manager snap + CanPlace gate.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 5: Hook `Building` lifecycle into the grid (register on spawn, release on despawn)

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Building.cs`

- [ ] **Step 1: Add a private helper to resolve the enclosing MapController**

In `Building.cs`, find a sensible private-method area (near the existing OwnerRestore helpers, or just before `OnNetworkDespawn`). Add:

```csharp
    /// <summary>
    /// Server-only. Resolves the <see cref="MapController"/> this building lives under.
    /// Returns null for interior-only buildings or for buildings whose parent isn't a
    /// MapController (e.g. mid-spawn before <c>SetParent</c> has run — defensive).
    /// Used by the <see cref="BuildingGrid"/> register/release calls.
    /// </summary>
    private MapController GetEnclosingMap()
    {
        // GetComponentInParent walks the transform tree upward — preferred over a
        // FindObjectsOfType scan because BuildingPlacementManager.RegisterBuildingWithMap
        // (or MapController.SpawnSavedBuildings) parents us to the map BEFORE OnNetworkSpawn.
        return GetComponentInParent<MWI.WorldSystem.MapController>();
    }
```

- [ ] **Step 2: Register in `OnNetworkSpawn`'s server branch**

Find the existing `OnNetworkSpawn` method (around line 339). Inside the server-only block (likely gated by `if (!IsServer) return;` or `if (IsServer) { … }`), after the construction-state init but before the method ends, add:

```csharp
        // Register this building's footprint on the host map's BuildingGrid (server-only).
        // The grid is derived state — re-built on every wake-up because every Building.OnNetworkSpawn
        // fires for restored buildings too. No separate save channel needed.
        if (IsServer)
        {
            var map = GetEnclosingMap();
            if (map != null && map.BuildingGrid != null)
            {
                Vector3 worldPos = transform.position;
                Vector2Int originCell = map.BuildingGrid.GetCellCoord(worldPos);
                Vector2Int footprint = GridFootprintCells;
                map.BuildingGrid.Register(NetworkObjectId, originCell, footprint);
            }
            // No log on the "map == null" branch in normal flow — interior buildings hit this path
            // by design (their parent IS a MapController, but conceptually they're "indoors").
            // If you need diagnostics, gate behind a verbose toggle (rule #34).
        }
```

If `OnNetworkSpawn` already has an `if (IsServer)` block, place the new code inside it; do NOT add a second `if (IsServer)`.

- [ ] **Step 3: Release in `OnNetworkDespawn`**

Find `OnNetworkDespawn` (around line 1809). Add at the start of the method body (before the existing `UnsubscribeOwnerRestoreListener();`):

```csharp
        // Release this building's grid cells (server-only) so a future placement can reuse them.
        if (IsServer)
        {
            var map = GetEnclosingMap();
            if (map != null && map.BuildingGrid != null)
            {
                map.BuildingGrid.Release(NetworkObjectId);
            }
        }
```

- [ ] **Step 4: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/Building.cs
git commit -m "$(cat <<'EOF'
feat(building): register/release on the host map's BuildingGrid

Server-only Building.OnNetworkSpawn calls BuildingGrid.Register with the
building's NetworkObjectId + cell origin + GridFootprintCells.
Server-only Building.OnNetworkDespawn calls Release.

GetEnclosingMap() helper walks the transform tree upward — buildings are
parented to their MapController before OnNetworkSpawn fires
(BuildingPlacementManager.RegisterBuildingWithMap + MapController.SpawnSavedBuildings
both SetParent before spawn).

No persistence channel for the grid (derived state from buildings — see plan
header). Wake-up re-runs Building.OnNetworkSpawn for every restored building,
which re-registers them on the freshly-Awake'd BuildingGrid.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 6: Snap ghost + add `CanPlace` gate in `BuildingPlacementManager`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`

- [ ] **Step 1: Snap the ghost position to the cell centre**

Find `UpdateGhostPosition()` (around line 208). Replace the body. The current body is:

```csharp
private void UpdateGhostPosition()
{
    if (_ghostInstance == null || Camera.main == null) return;

    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
    if (Physics.Raycast(ray, out RaycastHit hit, 100f, _groundLayer))
    {
        _ghostInstance.transform.position = hit.point;

        bool insideRegion = IsInsideRegion(hit.point);
        // ... existing logic ...
```

Replace just the `_ghostInstance.transform.position = hit.point;` line with the snap:

```csharp
        // Snap the ghost to the cell centre of the host MapController's BuildingGrid.
        // Falls back to the raw hit point when no map is found (placement will then be
        // rejected by IsInsideRegion / map-discovery anyway).
        Vector3 snapped = hit.point;
        MapController hostMap = MapController.GetMapAtPosition(hit.point);
        if (hostMap != null && hostMap.BuildingGrid != null)
        {
            snapped = hostMap.BuildingGrid.SnapToGridCenter(hit.point);
        }
        _ghostInstance.transform.position = snapped;
```

(Everything else in `UpdateGhostPosition` stays the same — toast logic, ghost material logic, etc.)

- [ ] **Step 2: Add the `CanPlace` gate to `ValidatePlacement`**

Find `ValidatePlacement(Vector3 position)` (around line 299). After the existing gates (range, obstacle, IsInsideRegion, community permission) and before the final `return true;`, add gate #5:

```csharp
            // 5. BuildingGrid occupancy check.
            //    - On the server (authoritative): rejects overlap with any other building's footprint.
            //    - On the client (ghost preview): the client's BuildingGrid is empty (no register
            //      calls fire client-side), so this gate is always TRUE client-side. That means
            //      the visual stays green on what the server might reject — acceptable v1
            //      compromise; the server toast handles the actual rejection.
            MapController hostMap = MapController.GetMapAtPosition(position);
            if (hostMap != null && hostMap.BuildingGrid != null)
            {
                Vector2Int originCell = hostMap.BuildingGrid.GetCellCoord(position);
                Vector2Int footprint = _ghostBuildingComponent != null
                    ? _ghostBuildingComponent.GridFootprintCells
                    : new Vector2Int(1, 1);
                if (!hostMap.BuildingGrid.CanPlace(originCell, footprint)) return false;
            }
            // Note: when hostMap is null we DON'T reject here — the existing map-discovery
            // logic in RegisterBuildingWithMap will either expand a nearby map or spawn a new
            // wild map. Both paths land the building inside SOME map's grid post-spawn.
```

- [ ] **Step 3: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingPlacementManager.cs
git commit -m "$(cat <<'EOF'
feat(placement): snap ghost to BuildingGrid cell + add CanPlace validation gate

- UpdateGhostPosition snaps _ghostInstance.transform.position to the cell centre
  of MapController.BuildingGrid.SnapToGridCenter(hit.point) when the cursor is
  over a known map. Falls back to raw hit.point when outside any map (placement
  is then rejected by IsInsideRegion).
- ValidatePlacement adds gate #5: BuildingGrid.CanPlace(cellOrigin, footprint).
  Client-side this is always TRUE (client grid is empty by design — see plan
  header network audit); server-side is authoritative.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 7: Documentation + wiki updates

**Files:**
- Modify: `.agent/skills/building_system/SKILL.md`
- Modify: `wiki/systems/building.md`
- Create: `wiki/concepts/building-grid.md`

- [ ] **Step 1: Read wiki/CLAUDE.md** for schema rules, and `wiki/systems/building.md` to know its current shape.

- [ ] **Step 2: Create `wiki/concepts/building-grid.md`**

```markdown
---
type: concept
title: "Building Grid"
tags: [building, placement, map-controller, city-founding]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[BuildingGrid.cs](../../Assets/Scripts/World/Buildings/BuildingGrid.cs)"
  - "[MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs)"
  - "[BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs)"
related:
  - "[[building]]"
  - "[[world-community]]"
status: active
confidence: high
---

# Building Grid

## Summary
The **BuildingGrid** is a per-`MapController` 8-unit cell grid that gates building placement
and snaps the ghost preview to cell centres. It's a plain C# class on the server side —
not a `NetworkBehaviour`, not persisted. Occupancy is *derived state*: every `Building.OnNetworkSpawn`
re-registers itself on its host map's grid, so wake-up + scene load both produce a correct
grid without a separate save channel.

## Why 8 units per cell

11 units = 1.67 m (CLAUDE.md rule #32), so 8 units ≈ 1.21 m. That's:
- Small enough that a 1×1 footprint fits a personal cottage.
- Large enough that a 4×4 footprint fits a town hall without feeling claustrophobic.
- 8× the [[crop-grid]] / [[furniture-grid]] cell — handy for cross-grid math.

## Why sparse (Dictionary<Vector2Int, ulong>)

The grid implicitly spans the *entire* Region (no bounded `ulong[,]`). Sparse storage
means:
- Zero allocation until cells are occupied.
- `MapController.ExpandBoundsToInclude` (which grows the map's BoxCollider when a new
  building is placed near an existing map) doesn't need a corresponding grid resize.
- Save format is naturally minimal — but we don't even need a save format because of
  the derived-state design (see below).

## Why no `BuildingGridSaveData`

Grid occupancy = `{(cellX, cellZ) → buildingNetId}` is a pure function of `{building → (worldPos, footprintCells)}`.
Both inputs are already persisted (BuildingSaveData.Position + BuildingSO.GridFootprintCells).
On `MapController.SpawnSavedBuildings`, each restored Building's `OnNetworkSpawn` server
branch calls `BuildingGrid.Register(this, footprintCells)` automatically. No separate
serializer, no risk of save-time/load-time grid drift.

This mirrors how [[terrain]] doesn't separately persist its derived `TerrainCellGrid` —
one source of truth, one persistence path.

## Network semantics

- **Server**: authoritative occupancy. Reads on Register (during spawn), Release (during
  despawn), and CanPlace (during placement validation).
- **Client**: their local `BuildingGrid` is always empty (no Register calls fire on
  the client because the OnNetworkSpawn server branch is gated by `IsServer`). The
  ghost preview's `SnapToGridCenter` still works client-side because it's pure math
  with no occupancy lookup. `CanPlace` returns true on the client, so the ghost may
  show green on cells the server rejects — server toast handles the user-visible rejection.

A future revision could replicate occupancy via a `NetworkList` for accurate client-side
green/red previews, but it's not required for correctness.

## Lifecycle

1. **`MapController.Awake`** creates the grid with origin (0, 0). Same Vector2.zero
   origin across every map → cells are world-space-indexed, not map-relative.
2. **`Building.OnNetworkSpawn`** (server) — `Register(NetworkObjectId, GetCellCoord(transform.position), GridFootprintCells)`.
3. **`Building.OnNetworkDespawn`** (server) — `Release(NetworkObjectId)`.
4. **`BuildingPlacementManager.UpdateGhostPosition`** — `SnapToGridCenter(hit.point)`
   to position the ghost visual.
5. **`BuildingPlacementManager.ValidatePlacement`** — gate #5 calls `CanPlace`.

## Open questions / TODO
- *Plan 5* — admin-console RTS placement bypasses the ghost flow and calls a new
  ServerRpc (`RequestPlaceCityBlueprintServerRpc`) directly. It should reuse the
  same `CanPlace` gate.
- *Plan Next* — replicate occupancy for accurate client-side ghost colour.

## Sources
- [BuildingGrid.cs](../../Assets/Scripts/World/Buildings/BuildingGrid.cs)
- [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `_buildingGrid` field, `Awake` initialisation, `BuildingGrid` getter
- [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `GridFootprintCells` getter, `OnNetworkSpawn`/`OnNetworkDespawn` register/release
- [BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — ghost snap + `CanPlace` gate
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §`BuildingGrid` (line 586) — design source
- [docs/superpowers/plans/2026-05-17-building-grid-foundation.md](../../docs/superpowers/plans/2026-05-17-building-grid-foundation.md) — Plan 2 implementation
```

- [ ] **Step 3: Update `wiki/systems/building.md`**

Read the file first. Then apply:

- Bump `updated:` to `2026-05-17`.
- In `## Public API`, add a row for `Building.GridFootprintCells : Vector2Int`.
- In `## Data flow` (or equivalent), add a section "Building Grid integration" referencing `[[building-grid]]` — see [BuildingGrid.cs](../../Assets/Scripts/World/Buildings/BuildingGrid.cs).
- In `## Key classes / files`, add `BuildingGrid` + `BlueprintCategory`.
- In `## State & persistence`, add a brief paragraph: "Grid occupancy is derived state; not persisted separately. See [[building-grid]] for the rationale."
- In `## Change log`, append `- 2026-05-17 — BuildingGrid (8-unit per-MapController cell grid) wired into placement flow. BuildingSO gains GridFootprintCells / BlueprintCategory / MinTier. — claude`
- Update `related:` to include `[[building-grid]]`.

- [ ] **Step 4: Update `.agent/skills/building_system/SKILL.md`**

Read the file first. Apply:

- Add a new section "BuildingGrid (placement-cell occupancy)" with the public-API table from `BuildingGrid.cs` (SnapToGridCenter, GetCellCoord, CanPlace, Register, Release, IsOccupied, OccupiedCellCount).
- Add `Building.GridFootprintCells` and the three new `BuildingSO` fields to the existing public-API tables.
- Note the "derived state — no separate save channel" design choice in the appropriate section.
- If the skill has a footer / change-log, append `2026-05-17 — BuildingGrid foundation (Plan 2)`.

- [ ] **Step 5: Sanity grep**

Run greps:
- `grep -rn "BuildingGrid" wiki/ .agent/` — expect at least 3-4 hits in the new + updated docs.
- `grep -rn "GridFootprintCells" .agent/` — expect at least 1 hit in the skill file.

- [ ] **Step 6: Commit**

```bash
git add wiki/concepts/building-grid.md wiki/systems/building.md .agent/skills/building_system/
git commit -m "$(cat <<'EOF'
docs(building-grid): wiki + skill updates for Plan 2

- wiki/concepts/building-grid.md (NEW) — concept page explaining cell size,
  sparse representation, derived-state design (no separate save channel),
  and network semantics
- wiki/systems/building.md — Public API + change log + cross-link to building-grid
- .agent/skills/building_system/SKILL.md — BuildingGrid section + BuildingSO
  field updates

Per rules #28, #29b: every system touched in Plan 2 has SKILL.md + wiki page
updated.

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 8: Final verification + summary commit

**Files:** none (verification only).

- [ ] **Step 1: Full EditMode test sweep**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.*`. Expected:
- All pre-existing tests still pass.
- All 9 new `BuildingGridTests` pass.
- All 12 `MWI.Tests.Community.*` tests from Plan 1 still pass.

Total expected: 134 + 9 = 143 EditMode tests (assuming Plan 1 baseline of 134).

- [ ] **Step 2: Compile sweep**

Use `assets-refresh` + `console-get-logs` filtering on compile errors. Expected: clean.

- [ ] **Step 3: Grep sanity**

Confirm no stale TODOs / "BuildingGrid" references left in code outside what we authored:
- `grep -rn "TODO.*BuildingGrid" Assets/Scripts` — expect 0.
- `grep -rn "class BuildingGrid" Assets/Scripts` — expect exactly 1 (our new file).

- [ ] **Step 4: Final summary commit**

```bash
git commit --allow-empty -m "$(cat <<'EOF'
chore(building-grid): Plan 2 of 5 complete — building grid foundation

Plan 2 of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).

Network safety (rule #19b):
Plan 2 adds NO new client-visible state. BuildingGrid is a server-only plain C#
class on MapController. Clients have an empty local grid (their SnapToGridCenter
math runs locally without occupancy lookup, used purely for the ghost-preview
visual). Server-authoritative CanPlace gate runs in BuildingPlacementManager
both client-side (always true on client) and server-side (real check + toast
on reject). No NetworkBehaviour was modified.

A late-joining client sees the same building world they would have seen before;
the grid is rebuilt fresh on their machine the first time they place a building.

Design deviation from spec:
The spec called for a separate BuildingGridSaveData channel persisting cell
occupancy. We replaced it with derived state — each restored Building's
OnNetworkSpawn re-registers itself on wake-up. Single source of truth
(BuildingSaveData.Position + BuildingSO.GridFootprintCells), zero save bloat,
no desync class possible. Mirrors how TerrainCellGrid doesn't separately
persist its derived state.

Tests: 9 new EditMode tests under MWI.Tests.Buildings.BuildingGridTests
(snap correctness positive + negative coords, round-trip, CanPlace empty/overlap/
adjacent, Register/Release lifecycle, idempotency, NetId 0 sentinel, IsOccupied).

Commit sequence:
- Task 1: BuildingSO additions (GridFootprintCells + BlueprintCategory + MinTier)
- Task 2: Building.GridFootprintCells convenience getter
- Task 3: BuildingGrid class + 9 EditMode tests
- Task 4: MapController._buildingGrid + Awake init
- Task 5: Building.OnNetworkSpawn/OnNetworkDespawn register/release
- Task 6: BuildingPlacementManager ghost snap + CanPlace gate
- Task 7: wiki + skill docs

Ready for Plan 3 (Ambition_FoundACity), Plan 4 (AdministrativeBuilding + JobBuilder),
and Plan 5 (admin console UI) to consume:
- BuildingSO.GridFootprintCells / BlueprintCategory / MinTier (admin-console gate)
- MapController.BuildingGrid (RTS placement validation)
- Building.GridFootprintCells (every consumer that needs cell footprint)

Plan 2 of 5 for the City Founding spec.
EOF
)"
```

---

## Self-Review Notes (post-write)

Re-checked against the user-stated Plan-2 scope ("BuildingGrid"):

- ✅ **BuildingGrid class** — Task 3 + tests.
- ✅ **BuildingSO additions** — Task 1.
- ✅ **MapController integration** — Task 4.
- ✅ **Building lifecycle hooks** — Task 5.
- ✅ **BuildingPlacementManager snap + CanPlace** — Task 6.
- ✅ **Building.GridFootprintCells convenience** — Task 2.
- ✅ **Docs (wiki + skill)** — Task 7.
- ✅ **Save round-trip** — deferred to derived-state-on-spawn (explicit deviation documented in plan header + Task 8 commit + concept page).
- ✅ **Network audit per Rule #19b** — recorded in plan header + final summary commit.

Placeholder scan: no "TODO" / "TBD" / vague step descriptions. Every code step has the actual code.

Type consistency:
- `Vector2Int GridFootprintCells` — used identically in Task 1 (BuildingSO), Task 2 (Building), Task 3 (BuildingGrid API), Task 5 (Building.OnNetworkSpawn), Task 6 (ValidatePlacement gate).
- `BlueprintCategory` enum — defined in Task 1, referenced in Task 1's BuildingSO field. Plan 5 will consume it.
- `CommunityLevel` MinTier — defined in Task 1, references the existing CommunityLevel enum.
- `BuildingGrid` class name — used consistently across Tasks 3-7.
- `NetworkObjectId` (ulong) — consumed in Task 5 register/release, matches BuildingGrid API in Task 3.

Plan length: 8 tasks. Each ends with a commit. Estimated 1.5-2 hours for a focused engineer.
