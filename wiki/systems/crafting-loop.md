---
type: system
title: "Crafting Loop"
tags: [crafting, jobs, tier-2]
created: 2026-04-19
updated: 2026-04-25
sources: []
related: ["[[jobs-and-logistics]]", "[[building]]", "[[commercial-building]]", "[[building-logistics-manager]]", "[[items]]", "[[character-skills]]", "[[furnituremanager-replace-style-rescan]]", "[[kevin]]"]
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
- **Station-discovery has primary + fallback paths.** `CraftingBuilding.GetCraftableItems` / `GetStationsOfType` / `GetAllStations` walk every `Room.FurnitureManager._furnitures` list (recursive over MainRoom + SubRooms via `ComplexRoom.GetAllRooms()`) **and then** supplement with a transform-tree scan via `GetComponentsInChildren<CraftingStation>(true)`, deduping the union. Worker job code (e.g. `JobBlacksmith.HandleSearchOrder`) uses `GetAllStations()` so it sees stations even when the canonical room-list path missed them.

  Since the `LoadExistingFurniture` additivity fix (2026-04-25, see [[furnituremanager-replace-style-rescan]]), the canonical path correctly contains every station authored via `_defaultFurnitureLayout` on the server. The transform-tree supplement is **defense-in-depth** for prefab misconfiguration — `slot.TargetRoom` left unset, the target Room missing a `FurnitureGrid`, or future paths that register a station outside `RegisterSpawnedFurnitureUnchecked`. When the supplement turns up a station the primary path missed, it emits a `LogWarning` naming the unregistered station so authors fix the prefab; treat the warning as a real signal, not console noise. Pure clients today still rely on the supplement (server-side `RegisterSpawnedFurnitureUnchecked` doesn't replicate to client `_furnitures` lists) — that gap is acceptable while the only client-side reader is `GetCraftableItems` itself, which carries the supplement.
- **Craft-to-storage race used to cause over-production** — just-spawned items sit at `CraftingStation._outputPoint` for 1–N ticks before `GatherStorageItems` / `RefreshStorageInventory` Pass 2 absorbs them. Before 2026-04-22, `LogisticsTransportDispatcher.HandleInsufficientStock` misread "completed craft + zero inventory" as theft on every tick (and every Manager pickup, since pickup despawns the WorldItem), cloning the whole `CraftingOrder` and making the crafter restart the batch (3-quantity order → 10 spawned items). The dispatcher now gates the theft branch on `CommercialBuilding.CountUnabsorbedItemsInBuildingZone(itemSO)` which counts both loose items in the zone AND items carried by the building's own workers. See [[building-logistics-manager]] gotchas for details.

- **Proximity gate at the station is `InteractableObject.IsCharacterInInteractionZone(character)`, not bounds math.** The pre-2026-04-25 `JobBlacksmith.HandleMovementToStation` used `Vector3.Distance(workerPos, station.InteractionPoint.position) <= StoppingDistance + 0.5f`, which is sensitive to authored Y of the InteractionPoint Transform — when the point sits on top of the anvil mesh and the worker stands on the floor, 3D distance never crosses the threshold, the NavMeshAgent has already arrived in 2D, and the worker loops between SetDestination and "still too far". Now both `JobBlacksmith.HandleMovementToStation` and `CharacterCraftAction.CanExecute` / `OnApplyEffect` resolve the station's paired `InteractableObject` (typically `CraftingFurnitureInteractable`) and call `IsCharacterInInteractionZone`. The zone is the single source of truth across BT, GOAP, server RPCs, and player input. A one-shot `LogWarning` falls back to a horizontal-distance check if the station has no paired interactable, so legacy prefabs still work — the warning names the prefab so the author can add a `CraftingFurnitureInteractable` sibling. `OnApplyEffect` re-checks the zone, so a craft that started in-zone but where the worker drifted out (knockback, station despawned, station picked up) doesn't ghost-produce the item.

## Change log
- 2026-04-25 — Reframed the station-discovery gotcha now that `FurnitureManager.LoadExistingFurniture` is additive (see [[furnituremanager-replace-style-rescan]]). The room-list primary path now reliably contains every server-spawned `_defaultFurnitureLayout` station; the transform-tree supplement in `GetCraftableItems` / `GetAllStations` / `GetStationsOfType` is defense-in-depth for prefab misconfiguration (and pure clients today, where `RegisterSpawnedFurnitureUnchecked` doesn't replicate). Treat the supplement's `LogWarning` as a real signal. — claude
- 2026-04-25 — Station-discovery hardened against `_defaultFurnitureLayout` registration race: `GetCraftableItems` / `GetStationsOfType` now do primary `Room.FurnitureManager._furnitures` walk + transform-tree fallback (deduped). New `CraftingBuilding.GetAllStations()` helper used by `JobBlacksmith.HandleSearchOrder` so the worker can find stations registered to *no* room. Replaced `JobBlacksmith.HandleMovementToStation` 3D-distance arrival check with `InteractableObject.IsCharacterInInteractionZone(_worker)`; `CharacterCraftAction.CanExecute` and `OnApplyEffect` apply the same gate so a craft can't start (or finish) without the worker being inside the station's `InteractionZone`. — claude
- 2026-04-22 — Documented the theft-gate fix (over-production on Quantity=N craft orders via false-theft detection during craft-to-storage transit + Manager pickup window) — claude
- 2026-04-21 — Added IStockProvider + InputStockTargets autonomous input restock pass. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §4.
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md) §1d.
- [CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs).
- [[jobs-and-logistics]] + [[building-logistics-manager]].
