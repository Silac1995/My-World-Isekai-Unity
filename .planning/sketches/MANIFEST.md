# Sketch Manifest

## Design Direction
Dark Unity-style management UI for **CommercialBuilding owner panel**. Replaces the dedicated Shelves tab with a unified **Storages tab** that lists every `StorageFurniture` child of the building and lets the owner assign one or more **storage roles** per furniture (Tool Storage, Inventory Storage, Sell-Shelf). The Sell-Shelf column only appears for ShopBuildings. Backend invariants come from the May 2026 Treasury / LogisticsManager work: roles are owner-mutable, persisted, and re-evaluated by the LogisticsManager on shift-start (no `_roleLocked` — "no win forever" is by design).

## Reference Points
- Existing in-game `UI_OwnerManagementPanel` tab body (~660×500, dark, TMP labels)
- `HiringTabView.cs` aesthetic (current sibling tab)
- Unity Inspector dropdown / toggle conventions

## Sketches

| #   | Name           | Design Question                                                  | Winner | Tags |
|-----|----------------|------------------------------------------------------------------|--------|------|
| 001 | storages-tab   | Which role-widget shape reads cleanest at 5+ storages?           | TBD    | management-ui, role-assignment, owner-flow |
