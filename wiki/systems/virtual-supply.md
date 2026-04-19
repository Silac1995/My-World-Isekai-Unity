---
type: system
title: "Virtual Supply"
tags: [jobs, macro-simulation, resources, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[jobs-and-logistics]]", "[[world]]", "[[world-macro-simulation]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[jobs-and-logistics]]", "[[world]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Virtual Supply

## Summary
V2 of macro-sim raw resource sourcing. `VirtualResourceSupplier : CommercialBuilding` reads from `CommunityData.ResourcePools` rather than a physical inventory. `TryFulfillOrder(BuyOrder, remainingToDispatch)` dynamically calls `ItemSO.CreateInstance` to inject real `ItemInstance`s into the supplier's inventory in the same frame — depleting the virtual pool — so the logistics manager can instantly create a `TransportOrder`.

## Rule
Raw resources don't physically exist on a map until virtual-supply injection. This keeps macro-sim cheap.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md) §4.
