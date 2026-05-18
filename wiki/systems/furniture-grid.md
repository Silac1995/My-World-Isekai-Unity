---
type: system
title: "Furniture Grid"
tags: [building, furniture, placement, tier-2]
created: 2026-04-19
updated: 2026-05-18
sources: []
related: ["[[building]]", "[[items]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on: ["[[building]]"]
depended_on_by: ["[[building]]"]
---

# Furniture Grid

## Summary
Discrete placement grid within a `Room`. `FurnitureGrid` initializes bounds from the room's `BoxCollider`; `FurnitureManager` is the per-room registry. `CanPlace(prefab, cell)` checks occupancy + collision rules; `Place(instance)` commits and serializes. Community-level permissions gate placement via [[building-placement-manager]].

## Prefab Variant Hierarchy (mandatory — project rule #40)

Every furniture prefab is a Prefab Variant chain rooted at `Assets/Prefabs/Furniture/Furniture_prefab.prefab`. This is enforced by [CLAUDE.md rule #40](../../CLAUDE.md). Two-tier structure:

- **Tier 0 — Root base**: `Furniture_prefab.prefab`. Carries the shared scaffold (collider/zone/component baseline). **No `NetworkObject`** — furniture baked into a runtime-spawned building inherits the building's NO and must not host its own (see [[no-nested-networkobject-in-runtime-spawned-prefab]]).
- **Tier 1 — Type base**: a direct variant of the root base, one per furniture *type*. Carries the type-specific `Furniture` subclass script and shared components. Examples: `Bed.prefab`, `Cashier.prefab`, `CraftingStation.prefab`, `TimeClock.prefab`, `Storage/Crate.prefab`, `Storage/Storage.prefab`, `Safe/Safe Base.prefab`, `Management/Commercial Console.prefab`.
- **Tier 2 — Specific variant**: a Prefab Variant of the matching Tier-1 type base. Examples: `Safe/Safe.prefab` (variant of `Safe Base`), `Storage/Storage Visible Items.prefab` (variant of `Storage`). Tier-2 variants override SerializeFields (icon, name, `FurnitureSO` reference) only — never the script identity.

**Authoring a new furniture type** = variant of `Furniture_prefab.prefab`, attach a new `Furniture` subclass. **Authoring a new specific variant** = variant of the matching Tier-1 type base, NEVER of the root base. Folder layout mirrors the hierarchy: type bases at the folder root or inside a type subfolder (`Storage/`, `Safe/`, `Management/`); specific variants inside the matching type subfolder.

**Matching `FurnitureItemSO` is mandatory** (prefab alone is NOT enough — also enforced by [CLAUDE.md rule #40](../../CLAUDE.md)):

- Create a `FurnitureItemSO` (subclass of `ItemSO`) at `Assets/Resources/Data/Item/Furniture/<Name>.asset` via `Create → Scriptable Objects → Items → Furniture` in the Project view.
- Wire the **bidirectional link**: `FurnitureItemSO._installedFurniturePrefab` references the Furniture prefab; `Furniture._furnitureItemSO` SerializeField on the prefab references back to the SO. Both must be wired — pickup and placement each route through one direction; wiring only one silently breaks the other.
- Existing examples: `Cashier.asset`, `CommercialConsole.asset`, `CraftingStation.asset`, `Crate.asset`, `Safe.asset`, `Time Clock.asset`.
- No global registry — furniture is discovered via `BuildingSO._defaultFurnitureLayout` slot references and `Resources.LoadAll` on the item layer.

**Save-schema reminder**: slot positions inside `BuildingSO._defaultFurnitureLayout` feed `FurnitureKey` — repositioning a slot after a save silently drops the storage contents bound to that key. Treat slot positions as immutable post-ship.

## Editor authoring — `Initialize Furniture Grid` context menu

`FurnitureGrid` is sized at edit-time via the **`Initialize Furniture Grid`** context-menu entry (right-click the component header). The helper reads the host GameObject's `BoxCollider`, derives `_gridWidth = ceil(box.size.x / _cellSize)` and `_gridDepth = ceil(box.size.z / _cellSize)`, and bakes a flat `_cells` list. The grid is also restored at runtime via `RestoreFromSerializedData` (called by `Building` on spawn) — no runtime rebake.

**Auto-fit step (added 2026-05-18):** before reading the BoxCollider the helper auto-resizes it from a subtree of `Renderer`s when either condition holds:

1. The optional `_autoSizeSource` SerializeField is wired — designer enforces auto-fit on every Initialize.
2. The BoxCollider is still at the unset default `center=(0,0,0)` + `size=(1,1,1)` AND a child Transform named **`CompletedVisual`** exists — project convention for the completed-building shell (see `Building._completedVisualRoot` + [[building]] construction loop).

When neither holds (designer manually authored the BoxCollider, no `_autoSizeSource`), the BoxCollider is respected as-is. The auto-fit walks every `Renderer.bounds` inside the chosen subtree, encapsulates the aggregate, and writes `box.center` (via `transform.InverseTransformPoint`) + `box.size` (folding in `lossyScale` for non-unit-scaled roots). This rescues the "1×1 grid on freshly-baked building variants" bug where the author placed visuals under `CompletedVisual` but forgot to size the root BoxCollider.

## Change log
- 2026-05-18 — Editor helper auto-fits the root `BoxCollider` from a `CompletedVisual` subtree (or designer-wired `_autoSizeSource`) before baking the grid, so building variants with a properly authored `CompletedVisual` subtree no longer ship with a 1×1 grid when the BoxCollider was left at default. Hands-off when the BoxCollider has already been sized manually. — claude
- 2026-05-18 — Documented the mandatory two-tier Prefab Variant hierarchy rooted at `Furniture_prefab.prefab`, AND the matching `FurnitureItemSO` contract (one SO at `Assets/Resources/Data/Item/Furniture/` per shipped furniture prefab, bidirectional link via `_installedFurniturePrefab` ↔ `_furnitureItemSO`). Codified in root [CLAUDE.md rule #40](../../CLAUDE.md). — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [Assets/Scripts/World/Buildings/FurnitureGrid.cs](../../Assets/Scripts/World/Buildings/FurnitureGrid.cs) — implementation, including the `Initialize` / `RestoreFromSerializedData` / `InitializeFurnitureGridEditor` lifecycle and the 2026-05-18 `CompletedVisual` auto-fit step.
- [[building]] parent + [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md).
- Root [CLAUDE.md](../../CLAUDE.md) — rule #40 (Furniture & Building Prefab Hierarchy).
