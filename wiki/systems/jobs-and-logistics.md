---
type: system
title: "Jobs & Logistics"
tags: [jobs, logistics, economy, tier-1]
created: 2026-04-18
updated: 2026-04-22
sources: []
related:
  - "[[world]]"
  - "[[building]]"
  - "[[shops]]"
  - "[[items]]"
  - "[[ai]]"
  - "[[character]]"
  - "[[worker-wages-and-performance]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - world-system-specialist
owner_code_path: "Assets/Scripts/World/Jobs/"
depends_on:
  - "[[building]]"
  - "[[character]]"
  - "[[items]]"
  - "[[ai]]"
  - "[[world]]"
depended_on_by:
  - "[[shops]]"
  - "[[world]]"
  - "[[worker-wages-and-performance]]"
---

# Jobs & Logistics

## Summary
Employment is a triad: `CharacterJob` (the employee component) + `Job` (the pure-data role with working hours) + `CommercialBuilding` (the physical workplace). Characters volunteer for positions or are force-assigned by a community leader. Each building runs a `BuildingLogisticsManager` that tracks five internal lists (`_activeOrders`, `_placedBuyOrders`, `_placedTransportOrders`, `_activeCraftingOrders`, `_pendingOrders`) and coordinates with GOAP actions (`GoapAction_PlaceOrder`, `LoadTransport`, `UnloadTransport`) to physically route items between buildings. A `JobLogisticsManager` worker is the physical actor that executes those orders. Expired orders dock reputation via `CharacterRelation.UpdateRelation`.

## Purpose
Simulate a plausible economy without hand-scripting NPC routines. Jobs push daily schedule slots into characters; logistics turns empty shelves into travelling transporters; crafting produces items on demand. Macro-simulation runs the same loop offline so cities keep functioning while the player is elsewhere.

## Responsibilities
- Registering workers to positions (`CharacterJob.TakeJob`, `ForceAssignJob`).
- Injecting work schedules into `CharacterSchedule` (`InjectWorkSchedule`).
- Detecting schedule overlap to prevent one character holding two conflicting jobs (`DoesScheduleOverlap`).
- Running per-tick job logic (`Job.Execute`) — pushes behaviours (`PerformCraft`, `Wander`, etc.) into the character when appropriate.
- Coordinating the order lifecycle for every commercial building:
  - `BuyOrder` (inter-building commercial contract),
  - `CraftingOrder` (internal production request),
  - `TransportOrder` (physical delivery between buildings),
  - `PendingOrder` (the physical "to-do" list for `GoapAction_PlaceOrder`).
- Handling virtual stock (`_placedBuyOrders` count as in-flight inventory).
- Managing the physical handshake: orders are only "placed" when `InteractionPlaceOrder` completes face-to-face.
- Cancelling / expiring orders symmetrically on both supplier and client sides.
- Running V2 macro-simulation: `VirtualResourceSupplier` injects raw resources from `CommunityData.ResourcePools` on demand.
- Running the physical delivery loop via `JobTransporter`.

**Non-responsibilities**:
- Does **not** own the building hierarchy (Zone/Room/Building) — see [[building]].
- Does **not** own customer queues or shop UI — see [[shops]].
- Does **not** own item data — see [[items]].
- Does **not** own NPC behaviour tree — see [[ai]].

## Key classes / files

### Employment triad
| File | Role |
|------|------|
| `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` | Per-character component. `TakeJob`, `QuitJob`, `ForceAssignJob`, `DoesScheduleOverlap`, `InjectWorkSchedule`. |
| `Assets/Scripts/World/Jobs/Job.cs` | Abstract pure-C# base. `JobTitle`, `Category`, `GetWorkSchedule()`, `Worker`, `Workplace`, `Execute()`. |
| Specialized jobs: `JobVendor`, `JobCrafter`, `JobTransporter`, `JobLogisticsManager`, `JobHarvester`, ... | Each overrides `Execute()` for its role. |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Workplace. `InitializeJobs()`, `AskForJob`, `GetWorkPosition(Character)`. |

### Logistics
| File | Role |
|------|------|
| `Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs` | Facade over 3 sub-components. Exposes `ProcessActiveBuyOrders`, `FindSupplierFor`, `AcknowledgeDeliveryProgress`, `CancelBuyOrder`, `LogLogisticsFlow`. See [[building-logistics-manager]]. |
| `Assets/Scripts/World/Buildings/Logistics/` | `LogisticsOrderBook` (state), `LogisticsTransportDispatcher` (reserve + dispatch), `LogisticsStockEvaluator` (policy + stock checks + supplier lookup), `ILogisticsPolicy` + `LogisticsPolicy` + `Policies/` (`MinStockPolicy`, `ReorderPointPolicy`, `JustInTimePolicy`). |
| `Assets/Scripts/World/Buildings/IStockProvider.cs` | Contract + `StockTarget` struct. Implemented by `ShopBuilding` and `CraftingBuilding`. |
| `Assets/Editor/Buildings/LogisticsCapabilityWindow.cs` | Editor-only `MWI > Logistics > Capability Report` diagnostic. |
| `Assets/Scripts/World/Buildings/BuildingTaskManager.cs` | Blackboard pattern. `ClaimBestTask<T>()`. |
| `Assets/Scripts/World/Jobs/BuyOrder.cs`, `CraftingOrder.cs`, `TransportOrder.cs`, `PendingOrder.cs` | Order types. |
| `Assets/Scripts/AI/Actions/GoapAction_PlaceOrder.cs` | Physical order placement via `InteractionPlaceOrder`. |
| `Assets/Scripts/AI/Actions/GoapAction_LoadTransport.cs`, `GoapAction_UnloadTransport.cs` | Transporter physical actions. |
| `Assets/Scripts/World/Buildings/VirtualResourceSupplier.cs` | V2 macro-sim: calls `ItemSO.CreateInstance` on demand from `CommunityData.ResourcePools`. |
| `Assets/Scripts/World/Jobs/JobYieldRegistry.cs` (conceptual) | Biome-driven yield recipes consumed offline by [[world]]'s `MacroSimulator`. |

### Crafting overlay
| File | Role |
|------|------|
| `Assets/Scripts/World/Buildings/CraftingBuilding.cs` | `CommercialBuilding` that hosts `CraftingStation`s; publishes `GetCraftableItems()`. |
| `Assets/Scripts/AI/Actions/` — `BTAction_PerformCraft` | BT-native crafting execution. |
| `CraftingStation` | Per-station runtime; animation events fire produce. |

## Public API / entry points

Employment:
- `CharacterJob.TakeJob(Job)` / `QuitJob(Job)`.
- `CommercialBuilding.AskForJob(Character)` — volunteer path.
- `CommunityTracker.ImposeJobOnCitizen(character, job)` — force-assign path.

Logistics:
- `BuildingLogisticsManager.EnqueueBuyOrder(item, qty)`.
- `BuildingLogisticsManager.ProcessActiveBuyOrders()` — called on worker punch-in and new day.
- `BuildingLogisticsManager.CancelBuyOrder(BuyOrder)` — cascades removal on both sides.
- `BuildingLogisticsManager.AcknowledgeDeliveryProgress(transportOrder)`.

Task blackboard:
- `BuildingTaskManager.RegisterTask(task)` — resource appears, furniture drops an item, etc.
- `BuildingTaskManager.ClaimBestTask<T>()` — GOAP workers pull atomically.

V2 macro-sim:
- `VirtualResourceSupplier.TryFulfillOrder(BuyOrder, int remaining)` — injects physical `ItemInstance`s from virtual pool.

## Data flow

Employment:
```
Character asks a building for work
        │
        ▼
CommercialBuilding.AskForJob(character)
        │
        ├── HasOwner OR HasCommunityLeader?       ──► gate
        ├── Position exists & is vacant?          ──► gate
        ├── CharacterJob.DoesScheduleOverlap?     ──► reject if conflict
        │
        ▼
Job.Assign(worker) + CharacterJob.InjectWorkSchedule
```

Logistics cycle (shop or crafter — unified `IStockProvider` path):
```
OnWorkerPunchIn or OnNewDay
        │
        ▼
LogisticsStockEvaluator.CheckStockTargets(building as IStockProvider)
        │
        ├── Physical stock + _placedBuyOrders = Virtual stock
        ├── ILogisticsPolicy decides: order how much? (MinStock / ReorderPoint / JIT)
        │
        ▼
For each understocked target:
        │
        ▼
EnqueueBuyOrder ──► add to _placedBuyOrders ──► add PendingOrder
        │
        ▼
JobLogisticsManager GOAP pops PendingOrder
        │
        ▼
GoapAction_PlaceOrder
        │
        ├── Walk to supplier
        └── InteractionPlaceOrder (face-to-face handshake)
              │
              ├── Success ──► supplier._activeOrders += order (IsPlaced = true)
              └── Fail   ──► requeue (IsPlaced remains false)
```

Delivery:
```
Supplier.ProcessActiveBuyOrders
        │
        ├── Has physical stock? ──► create TransportOrder
        └── No stock?           ──► create internal CraftingOrder
                                          │
                                          ▼
                                    JobCrafter picks up via BT
                                          │
                                          ▼
                                    produces ItemInstance
        │
        ▼
JobTransporter physically moves items
        │
        ▼
Deliver drop ──► NotifyDeliveryProgress
        │
        ▼
AcknowledgeDeliveryProgress removes TransportOrder from _placedTransportOrders
```

Macro-sim offline:
```
Map wakes up (see world.md)
        │
        ▼
MacroSimulator uses JobYieldRegistry + BiomeDefinition to compute offline yields
        │
        ▼
VirtualResourceSupplier.TryFulfillOrder fills pending orders from virtual pools
```

## Dependencies

### Upstream
- [[building]] — `CommercialBuilding`, `BuildingTaskManager`, `BuildingLogisticsManager` all live there.
- [[character]] — `CharacterJob`, `CharacterSchedule` sit on the character.
- [[items]] — orders move `ItemInstance`s; `ItemSO.CreateInstance` used in V2 virtual supply.
- [[ai]] — GOAP actions (`PlaceOrder`, `LoadTransport`, `UnloadTransport`) drive physical execution; BT's crafting branch.
- [[world]] — macro-simulation catch-up hooks in through `JobYieldRegistry` + `BiomeDefinition`; `CommunityTracker.ImposeJobOnCitizen`.

### Downstream
- [[shops]] — `JobVendor` on `ShopBuilding`; customer queue; restock logistics.
- [[world]] — community promotion/demotion tracks employment metrics (partial — confirm).

## State & persistence

- Per-character: current jobs (`JobAssignment` dictionary), work-schedule time slots, ownership flag.
- Per-building: `_activeOrders`, `_placedBuyOrders`, `_placedTransportOrders`, `_activeCraftingOrders`, `_pendingOrders` — all persisted to map save data.
- Macro-sim: `CommunityData.ResourcePools` hold virtual stock that decays/regenerates offline.

## Known gotchas / edge cases

- **Schedule overlap check is mandatory** — skipping `DoesScheduleOverlap` lets one character double-book and deadlock.
- **Physical handshake `IsPlaced`** — a `BuyOrder` is **not** live until `InteractionPlaceOrder` succeeds face-to-face. Failures must re-enqueue; GOAP action will retry.
- **Duplicate prevention** — always check `_placedBuyOrders` / `_placedTransportOrders` before enqueuing — otherwise duplicate orders clog the queue.
- **Expiration cascades both sides** — `CancelBuyOrder` **must** cascade to the counterpart building, or stale orders leak in virtual stock.
- **Virtual stock = physical + placed** — reading only physical stock causes over-ordering; always include `_placedBuyOrders`.
- **Transport accounting global, not per-transporter** — use `InTransitQuantity` globally to avoid over-delivery.
- **Crafters are demand-driven** — `JobCrafter` does **not** craft in a vacuum; it waits for an active `CraftingOrder` on its building's logistics manager.
- **`ImposeJobOnCitizen` overrides consent** — community leaders can force-assign work; intentionally dissolves overlapping jobs.

## Open questions / TODO

- [ ] Precise file path of `CommunityTracker.ImposeJobOnCitizen` — assumed in `World/Community/`, confirm.
- [ ] `JobYieldRegistry.cs` — exact location and schema. Tracked in [[TODO-skills]] if no SKILL.md.
- [ ] Reputation penalty magnitudes on order expiration — need numeric review.

## Child sub-pages (to be written in Batch 2)

- [[job-employment]] — `CharacterJob`, `Job`, `CommercialBuilding.AskForJob`, schedule injection.
- [[job-roles]] — `JobVendor`, `JobCrafter`, `JobTransporter`, `JobLogisticsManager`, `JobHarvester`.
- [[building-logistics-manager]] — the 5 lists, ProcessActiveBuyOrders, CancelBuyOrder cascade.
- [[building-task-manager]] — blackboard pattern, `ClaimBestTask<T>`.
- [[order-types]] — `BuyOrder`, `CraftingOrder`, `TransportOrder`, `PendingOrder`.
- [[virtual-supply]] — `VirtualResourceSupplier`, V2 macro-sim injection.
- [[crafting-loop]] — `CraftingBuilding`, `CraftingStation`, `JobCrafter` demand-driven flow.

## Change log
- 2026-04-22 — Wage and worklog hooks added: punch-in/out wage payment via [[worker-wages-and-performance]], per-job credit hooks (deposit / craft / delivery), JobAssignment now carries wage fields seeded at hire-time — claude
- 2026-04-21 — Logistics refactor: IStockProvider + pluggable LogisticsPolicy SO + facade split + input stock contract on CraftingBuilding — claude
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md)
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md)
- [.agent/skills/wage-system/SKILL.md](../../.agent/skills/wage-system/SKILL.md)
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- [wiki/systems/worker-wages-and-performance.md](worker-wages-and-performance.md)
- `Assets/Scripts/World/Jobs/` (16 files).
- `Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs`.
- `Assets/Scripts/AI/Actions/` — GOAP action library.
- 2026-04-18 conversation with [[kevin]].
