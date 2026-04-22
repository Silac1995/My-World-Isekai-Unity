---
type: system
title: "Crafting Loop"
tags: [crafting, jobs, tier-2]
created: 2026-04-19
updated: 2026-04-22
sources: []
related: ["[[jobs-and-logistics]]", "[[building]]", "[[commercial-building]]", "[[building-logistics-manager]]", "[[items]]", "[[character-skills]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[jobs-and-logistics]]", "[[commercial-building]]"]
depended_on_by: ["[[jobs-and-logistics]]"]
---

# Crafting Loop

## Summary
`CraftingBuilding : CommercialBuilding` scans its `ComplexRoom`s for `CraftingStation`s and publishes a list of craftable items (`GetCraftableItems()`). `JobCrafter` requires a `SkillSO` at a minimum tier; it is **demand-driven** — wakes up only on an active `CraftingOrder`. `BTAction_PerformCraft` finds the right station, plays the animation, produces the `ItemInstance`.

As of the 2026-04-21 refactor, `CraftingBuilding` additionally implements `IStockProvider` and exposes an Inspector-authored `_inputStockTargets` list. On every `OnWorkerPunchIn`, the `BuildingLogisticsManager` proactively requests input materials that fall below `MinStock`, independently of whether any `CraftingOrder` exists. This closes the pre-refactor bug where forges sat idle with empty input bins waiting for a commission to arrive.

## Key classes / files

| File | Role |
|---|---|
| `Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs` | Abstract subclass of `CommercialBuilding`. Hosts `CraftingStation`s, implements `IStockProvider`, exposes `InputStockTargets`. |
| `Assets/Scripts/World/Jobs/CraftingJobs/JobCrafter.cs` | Demand-driven crafter job. |
| `Assets/Scripts/AI/Actions/BTAction_PerformCraft.cs` | Behaviour-tree execution node. |

## Two restock sources feed a crafting workshop

On every `OnWorkerPunchIn`, the logistics manager runs two passes on the workshop:

1. **Proactive input restock** — `LogisticsStockEvaluator.CheckStockTargets(building as IStockProvider)` reads `_inputStockTargets`, computes virtual stock (physical + placed `BuyOrder`s), lets the `ILogisticsPolicy` decide how much to order, and enqueues any shortfall as fresh `BuyOrder`s.
2. **Commission aggregation** — `LogisticsStockEvaluator.CheckCraftingIngredients(crafting)` runs whenever `ActiveCraftingOrders` is non-empty. It aggregates every commission's missing ingredients and emits additional `BuyOrder`s on top of the proactive pass.

The two passes cooperate: the proactive pass keeps the shop usable even when no external commission has been placed; the commission pass ensures a big order isn't bottlenecked by the input-stock baseline.

## The craft itself

`JobCrafter.Execute()` is demand-driven:
1. Check the building's `ActiveCraftingOrders`. If none, idle.
2. Pick the next order, find a `CraftingStation` in a sub-room that can produce the target item.
3. Push `BTAction_PerformCraft` — walks to station, plays animation, fires `UpdateCraftingOrderProgress` via animation event, produces `ItemInstance` into the building's inventory.

Requirements:
- Worker's `CharacterSkills` has the right `SkillSO` at the required tier.
- Input ingredients are in the building's storage (this is what the proactive restock ensures).

## Dependencies

### Upstream
- [[commercial-building]] — inheritance + `IStockProvider` contract.
- [[building-logistics-manager]] — runs both restock passes.
- [[jobs-and-logistics]] — `CraftingOrder`, `JobCrafter`, `JobLogisticsManager`.
- [[items]] — produced `ItemInstance`, input `ItemSO`.
- [[character-skills]] — tier gating on `JobCrafter`.

## Gotchas

- **Forgetting to author `_inputStockTargets`** — a `CraftingBuilding` prefab with an empty `_inputStockTargets` list will only restock once a commission arrives (pre-refactor behaviour). Designers must author the per-prefab targets explicitly.
- **A station present but no matching skill on any NPC** — `AskForJob` refuses employment. Workshop stays unstaffed.
- **`CraftingStation`s must live on sub-rooms**, not on the building root — `GetCraftableItems()` scans `Rooms`, not the building's top-level transform.
- **Craft-to-storage race used to cause over-production** — just-spawned items sit at `CraftingStation._outputPoint` for 1–N ticks before `GatherStorageItems` / `RefreshStorageInventory` Pass 2 absorbs them. Before 2026-04-22, `LogisticsTransportDispatcher.HandleInsufficientStock` misread "completed craft + zero inventory" as theft on every tick (and every Manager pickup, since pickup despawns the WorldItem), cloning the whole `CraftingOrder` and making the crafter restart the batch (3-quantity order → 10 spawned items). The dispatcher now gates the theft branch on `CommercialBuilding.CountUnabsorbedItemsInBuildingZone(itemSO)` which counts both loose items in the zone AND items carried by the building's own workers. See [[building-logistics-manager]] gotchas for details.

## Change log
- 2026-04-22 — Documented the theft-gate fix (over-production on Quantity=N craft orders via false-theft detection during craft-to-storage transit + Manager pickup window) — claude
- 2026-04-21 — Added IStockProvider + InputStockTargets autonomous input restock pass. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §4.
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md) §1d.
- [CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs).
- [[jobs-and-logistics]] + [[building-logistics-manager]].
