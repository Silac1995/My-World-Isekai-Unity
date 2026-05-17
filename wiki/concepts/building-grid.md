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
  - "[Building.cs](../../Assets/Scripts/World/Buildings/Building.cs)"
related:
  - "[[building]]"
  - "[[world-community]]"
status: active
confidence: high
---

# Building Grid

## Summary
The **BuildingGrid** is a per-`MapController` 8-unit cell grid that gates building placement
and snaps the ghost preview to cell centres. It is a plain C# class on the server side —
not a `NetworkBehaviour`, not persisted. Occupancy is *derived state*: every
`Building.OnNetworkSpawn` re-registers itself on its host map's grid, so wake-up and scene
load both produce a correct grid without a separate save channel.

## Why 8 units per cell

11 Unity units = 1.67 m (CLAUDE.md rule #32), so 8 units ≈ 1.21 m. That is:
- Small enough that a 1×1 footprint fits a personal cottage.
- Large enough that a 4×4 footprint fits a town hall without feeling claustrophobic.
- 8× the [[furniture-grid]] cell (1 unit) — handy for cross-grid math.

## Why sparse (Dictionary<Vector2Int, ulong>)

The grid implicitly spans the *entire* Region (no bounded `ulong[,]`). Sparse storage means:
- Zero allocation until cells are occupied.
- `MapController.ExpandBoundsToInclude` (which grows the map's BoxCollider when a new
  building is placed near an existing map) does not need a corresponding grid resize.
- The save format is naturally minimal — but there is no save format at all because of
  the derived-state design (see below).

## Why no BuildingGridSaveData

Grid occupancy = `{(cellX, cellZ) → buildingNetId}` is a pure function of
`{building → (worldPos, footprintCells)}`. Both inputs are already persisted
(`BuildingSaveData.Position` + `BuildingSO.GridFootprintCells`). On
`MapController.SpawnSavedBuildings`, each restored Building's `OnNetworkSpawn` server
branch calls `BuildingGrid.Register(networkObjectId, origin, size)` automatically. There is
no separate serializer and no risk of save-time / load-time grid drift.

This mirrors how [[world]] does not separately persist derived spatial state — one source of
truth, one persistence path.

## Network semantics

- **Server**: authoritative occupancy. Reads on `Register` (during spawn), `Release` (during
  despawn), and `CanPlace` (during placement validation).
- **Client**: their local `BuildingGrid` is always empty (no `Register` calls fire on the
  client because the `OnNetworkSpawn` server branch is gated by `IsServer`). The ghost
  preview's `SnapToGridCenter` still works client-side because it is pure math with no
  occupancy lookup. `CanPlace` returns `true` on the client, so the ghost may show green on
  cells the server rejects — the server-side rejection toast handles the user-visible feedback.

A future revision could replicate occupancy via a `NetworkList` for accurate client-side
green/red previews, but it is not required for correctness.

## Lifecycle

1. **`MapController.Awake`** creates the grid with origin `Vector2.zero`. The same world-space
   origin across every map means cells are world-space-indexed, not map-relative.
2. **`Building.OnNetworkSpawn`** (server) — `Register(NetworkObjectId, GetCellCoord(transform.position), GridFootprintCells)`.
3. **`Building.OnNetworkDespawn`** (server) — `Release(NetworkObjectId)`.
4. **`BuildingPlacementManager.UpdateGhostPosition`** — `SnapToGridCenter(hit.point)` positions
   the ghost visual.
5. **`BuildingPlacementManager.ValidatePlacement`** — gate #5 calls `CanPlace`.

## Open questions / TODO

- *Plan 5* — admin-console RTS placement bypasses the ghost flow and calls a new
  `RequestPlaceCityBlueprintServerRpc` directly. It should reuse the same `CanPlace` gate.
- *Plan Next* — replicate occupancy via `NetworkList` for accurate client-side ghost colour.

## Links

- [[building]] — the system that consumes BuildingGrid for spawn/despawn registration.
- [[world-community]] — city founding triggers the first placements that populate the grid.
- [[construction]] — construction-complete transitions a Building from Scaffold to Complete;
  the grid cell is occupied from OnNetworkSpawn, before construction finishes.

## Sources

- [BuildingGrid.cs](../../Assets/Scripts/World/Buildings/BuildingGrid.cs) — primary implementation
- [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `_buildingGrid` field, `Awake` initialisation, `BuildingGrid` getter
- [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `GridFootprintCells` getter, `OnNetworkSpawn` / `OnNetworkDespawn` register / release
- [BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — ghost snap + `CanPlace` gate
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) — §`BuildingGrid` design source
- [docs/superpowers/plans/2026-05-17-building-grid-foundation.md](../../docs/superpowers/plans/2026-05-17-building-grid-foundation.md) — Plan 2 implementation
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — procedural authoring details
