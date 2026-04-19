---
type: system
title: "Shop Building"
tags: [shops, building, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[shops]]", "[[building]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[shops]]", "[[building]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Building

## Summary
`ShopBuilding : CommercialBuilding`. Holds `Items To Sell` (list of `ItemSO`), physical `Inventory` (`List<ItemInstance>`), and `Customer Queue` (`Queue<Character>`). Employs `JobLogisticsManager` for restock + one or more `JobVendor` for sales.

## See parent
Full details in [[shops]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[shops]].
