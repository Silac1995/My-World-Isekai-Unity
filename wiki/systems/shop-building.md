---
type: system
title: "Shop Building"
tags: [shops, building, tier-2]
created: 2026-04-19
updated: 2026-04-21
sources: []
related: ["[[shops]]", "[[building]]", "[[commercial-building]]", "[[building-logistics-manager]]", "[[jobs-and-logistics]]", "[[kevin]]"]
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
- 2026-04-21 — Shop now implements IStockProvider; restock goes through unified CheckStockTargets + LogisticsPolicy. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs).
- [[shops]] + [[building-logistics-manager]].
