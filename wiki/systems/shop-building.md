---
type: system
title: "Shop Building"
tags: [shops, building, tier-2]
created: 2026-04-19
updated: 2026-05-14
sources: []
related: ["[[shops]]", "[[building]]", "[[commercial-building]]", "[[building-logistics-manager]]", "[[jobs-and-logistics]]", "[[host-only-state-blindspot]]", "[[interactable-system]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[shops]]", "[[building]]", "[[commercial-building]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Building

## Summary
`ShopBuilding : CommercialBuilding`. Holds `ShopEntries` (list of `ShopItemEntry { Item; MaxStock }`), physical `Inventory` (`List<ItemInstance>`), and `Customer Queue` (`Queue<Character>`). Employs `JobLogisticsManager` for restock + one or more `JobVendor` for sales.

As of the 2026-04-21 refactor, `ShopBuilding` implements `IStockProvider` — its `GetStockTargets()` projects every `ShopItemEntry` into a `StockTarget { ItemToStock; MinStock }`, preserving the legacy default "treat zero/negative `MaxStock` as 5". This plugs shop restock into the same unified path that drives `CraftingBuilding` input restock (`LogisticsStockEvaluator.CheckStockTargets`) — the pluggable `LogisticsPolicy` now decides the reorder quantity.

## See parent
Full details in [[shops]] and [[building-logistics-manager]].

## Change log
- 2026-05-14 — Cashier multiplayer fix: `Cashier.Occupant` + `CurrentCustomer` now resolve via `CashierNetSync` NetworkVariable mirrors (`OccupantNetworkObjectId`, `CurrentCustomerNetworkObjectId`) on non-server peers, so client pre-gates and management UI mirror the server state. `OnNetworkSpawn` server-side backfill handles late-joiners. `UI_ShopBuyPanel.Initialize` carries a `TryRegisterWithShop` late-bind fallback for the `LinkedShop`-null spawn race on joining clients. Surfaced the [[host-only-state-blindspot]] gotcha and the project-wide client-side audit rule (CLAUDE.md #19b). — claude
- 2026-05-14 — `GoapAction_RestockSellShelves` now gated by a dirty-flag pattern on `BuildingLogisticsManager` (`_restockDirty` + `IsRestockDirty` / `MarkRestockDirty` / `ClearRestockDirty`). `ShopBuilding.OnCatalogChanged` is one of the dirty-set sources (catalog add/remove/edit wakes the next planner tick); inventory mutations + per-storage `OnInventoryChanged` + `OnStorageRolesChanged` also flip the flag. The expensive `GetItemsInStorageFurniture × FindShelfWithSpace` walk only runs when something changed since the last clean scan. See [[building-logistics-manager]] and [[performance-conventions]] Pattern 1. — claude
- 2026-05-14 — Sell-shelf restock loop closed: (1) routing — `FindStorageFurnitureForItem` adds a SellShelf pre-pass for catalog items so supplier deliveries land on a shelf directly; (2) heal — new `GoapAction_RestockSellShelves` (cost 0.3f, in `JobLogisticsManager` plan) sweeps every non-SellShelf storage for catalog items and transfers them onto a shelf with free space. The logistics-manager NPC therefore physically rebalances misplaced stock on every shift. Reservation-aware (defers to `GoapAction_StageItemForPickup` for items already reserved by an outbound transport). See [[building-logistics-manager]]. — claude
- 2026-05-14 — Shift-punch storage-role assignment now also targets shops: on every worker punch-in, the first storage is auto-assigned `SellShelf` and the rest `InventoryStorage` — unless the shop's `GetToolStockItems()` yields anything, in which case the tool-priority rule wins (first → `ToolStorage`). Logic lives in `BuildingLogisticsManager.AssignStorageRolesForShift`; see [[commercial-storage-roles]]. — claude
- 2026-04-21 — Shop now implements IStockProvider; restock goes through unified CheckStockTargets + LogisticsPolicy. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs).
- [[shops]] + [[building-logistics-manager]].
