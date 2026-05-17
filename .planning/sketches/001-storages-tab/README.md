---
sketch: 001
name: storages-tab
question: "Which role-widget shape reads cleanest at 5+ storages in the unified Storages tab?"
winner: null
tags: [management-ui, role-assignment, owner-flow, commercial-building]
---

# Sketch 001: Storages Tab

## Design Question
**Which role-widget shape reads cleanest at 5+ storages, and which one scales best when new roles are added later?**

This tab replaces the dedicated Shelves tab in `UI_OwnerManagementPanel`. It lists every `StorageFurniture` child of a `CommercialBuilding` and lets the owner assign one or more **storage roles** per furniture:

- **Tool Storage** — single per building. Designer-time inspector-authored fallback if none chosen.
- **Inventory Storage** — TBD single or multi (sketch proposes multi; can flip).
- **Sell-Shelf** — multi. **Only visible on `ShopBuilding`** (non-Shop buildings hide the column entirely).

Roles persist to save data; the `JobLogisticsManager` re-applies role-driven policies on shift-start (owner overrides may be overwritten — "no win forever" is by design).

## How to View
```
start .planning\sketches\001-storages-tab\index.html   # Windows
open  .planning/sketches/001-storages-tab/index.html   # macOS
xdg-open .planning/sketches/001-storages-tab/index.html # Linux
```

Each variant has a built-in **context switcher** (Forge · Clothing Shop · Empty) inside the tab body so you can compare the non-Shop, Shop, and empty-state layouts under the same widget pattern.

## Variants

- **A · Radios + Sell-Shelf checkbox** — Tool column = radios (single), Inventory column = checkboxes (multi), Sell column = checkboxes (multi, shop-only). Closest to current Unity Inspector convention.
- **B · Dropdown per row** — `Role: [Tool ▾]` dropdown per furniture for the primary role; Sell-Shelf still gets its own checkbox column (because it stacks on top of any other role). Most compact horizontally.
- **C · Cell grid** — pure storage-row × role-column matrix. Cardinality encoded by widget: radios for single roles, checkboxes for multi. Adding a new role = one more column.

## What to Look For

When clicking through:

1. **Q1 — Scannability at scale.** Mentally extend each variant to 10 storages. Which one still reads at a glance? Which one becomes a wall of widgets?
2. **Q2 — Inspector fallback visibility.** Should the auto-fallback (yellow hint banner under the row list, visible on the Shop context) actually surface here, or is it noise? Variant B has no obvious place for it short of an italic "(default)" dropdown entry.
3. **Q3 — Inline vs modal.** All three variants assume **inline** widgets (no per-furniture "edit role" modal). Confirm modal isn't worth the click cost.
4. **Q4 — Contents preview.** All three show slot count + bar only — no per-slot item icons. Worth pursuing a hover-card preview later, but not in the resting state.
5. **Layout fidelity.** Tab body is sized to spec (~660 × 500). The titlebar / tab row / footer hint banner all count against that envelope — make sure the variant doesn't blow past it with 5 rows.

## Open Questions for Decision

- [ ] **Pick a variant** (A / B / C, or a synthesis like "C's grid + B's dropdowns for single roles").
- [ ] **Inventory cardinality** — keep as multi (current sketch) or flip to single?
- [ ] **Show inspector-authored fallback** as a hint banner, an italic row-marker, or hide it (only LogisticsManager + DevMode see it)?
- [ ] **Slot preview** — count-only now, add hover-preview later, or surface item icons up-front?

## Notes for the Backend Path (post-pick)

Whatever wins, the data shape is the same:
```
StorageFurniture (per row):
  • roleTool        : bool (only one in the building is true)
  • roleInventory   : bool (TBD single/multi)
  • roleSellShelf   : bool (shop-only; many)
```
This means the C# / prefab pass is the same regardless of widget choice — the variant decision is purely visual layout. The widget chosen here drives `UI_OwnerManagementPanel`'s new `StoragesTabView` + a per-row `UI_StorageRoleRow` prefab (per project rule #39).
