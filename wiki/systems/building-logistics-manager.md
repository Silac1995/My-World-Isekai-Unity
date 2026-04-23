---
type: system
title: "Building Logistics Manager"
tags: [logistics, jobs, orders, tier-2]
created: 2026-04-19
updated: 2026-04-23
sources: []
related:
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
  - "[[building]]"
  - "[[commercial-building]]"
  - "[[crafting-loop]]"
  - "[[items]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - item-inventory-specialist
  - npc-ai-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on:
  - "[[building]]"
  - "[[jobs-and-logistics]]"
  - "[[items]]"
  - "[[character]]"
depended_on_by:
  - "[[shops]]"
  - "[[commercial-building]]"
  - "[[crafting-loop]]"
  - "[[jobs-and-logistics]]"
---

# Building Logistics Manager

## Summary
Per-building brain for the supply chain. As of the 2026-04-21 refactor, the MonoBehaviour is a **thin facade** that delegates to three plain-C# collaborators: `LogisticsOrderBook` (state), `LogisticsTransportDispatcher` (reserve + transport), `LogisticsStockEvaluator` (policy-driven stock checks + supplier lookup). Public API is byte-stable so every external caller (`JobLogisticsManager`, `InteractionPlaceOrder`, `GoapAction_PlaceOrder`, tests) compiles unchanged. Autonomous restock runs through the `IStockProvider` contract — `ShopBuilding` and `CraftingBuilding` both implement it — and the "how much to order" decision is delegated to a pluggable `LogisticsPolicy` ScriptableObject.

## Purpose
Give every commercial building a single, predictable order-lifecycle authority so shops, crafters, and transporters can cooperate without race conditions or ghost orders — and make the stocking strategy authorable per-building without code changes.

## Responsibilities
- Inventory monitoring: `LogisticsStockEvaluator.CheckStockTargets(IStockProvider)` on `OnWorkerPunchIn` / `OnNewDay`.
- Commission-driven crafting aggregation: `LogisticsStockEvaluator.CheckCraftingIngredients(CraftingBuilding)`.
- Supplier sourcing: `FindSupplierFor(ItemSO)` (first-match).
- Order creation: `BuyOrder`, `CraftingOrder`, `TransportOrder`.
- Pending-order queue: drives `GoapAction_PlaceOrder`.
- Active-order tracking: `ActiveOrders` (we are supplier), `PlacedBuyOrders` (we are client).
- Fulfillment: `LogisticsTransportDispatcher.ProcessActiveBuyOrders` dispatches to transporters or crafters.
- Cancellation: `CancelBuyOrder(BuyOrder)` cascades removal on both sides.
- Acknowledgement: `AcknowledgeDeliveryProgress(transportOrder)` on delivery drop.
- Expiration: time-based penalty via `CharacterRelation.UpdateRelation`.
- V2 virtual supply: `VirtualResourceSupplier.TryFulfillOrder` injects raw resources from `CommunityData.ResourcePools`.

**Non-responsibilities**:
- Does **not** execute physical movement — `JobTransporter` + GOAP actions do.
- Does **not** own `CraftingStation`s — see [[crafting-loop]].
- Does **not** own task blackboard — see [[building-task-manager]].
- Does **not** decide *how much* to order — delegated to `ILogisticsPolicy`.

## Facade + 3 sub-components

```
BuildingLogisticsManager  (MonoBehaviour, facade)
 ├── LogisticsOrderBook            (state: all order dicts/lists, pending queue)
 ├── LogisticsTransportDispatcher  (reserve ItemInstances, create TransportOrders,
 │                                  FindTransporterBuilding, ProcessActiveBuyOrders)
 └── LogisticsStockEvaluator       (CheckStockTargets, CheckCraftingIngredients,
                                    RequestStock, FindSupplierFor, policy evaluation)
```

- Exposed via `OrderBook`, `Dispatcher`, `Evaluator` properties for tests + advanced tooling.
- Nested `PendingOrder` struct and `OrderType` enum stay on the facade for serialization compat.
- Sub-components do not know about `MonoBehaviour`/Unity lifecycle — they receive references in the facade's `Awake()`.

## The 5 lists (owned by `LogisticsOrderBook`)

| List | Meaning |
|---|---|
| `ActiveOrders` (`List<BuyOrder>`) | Requests received from **other** clients. We are the supplier. |
| `PlacedBuyOrders` (`List<BuyOrder>`) | Requests **we** sent to suppliers. We are the client. Counts as virtual stock. |
| `PlacedTransportOrders` (`List<TransportOrder>`) | Delivery orders we sent to a `TransporterBuilding`. |
| `ActiveCraftingOrders` (`List<CraftingOrder>`) | Internal requests for our `JobCrafter`s. |
| `PendingOrder` queue | Physical "to-do" list for `GoapAction_PlaceOrder` — walk there, handshake. Accessed via `PeekPendingOrder` / `DequeuePendingOrder` / `EnqueuePendingOrder`. |

## `IStockProvider` contract

Any `CommercialBuilding` that wants autonomous restock implements `IStockProvider` and yields `StockTarget { ItemSO ItemToStock; int MinStock }` from `GetStockTargets()`.

| Implementer | Source of targets |
|---|---|
| `ShopBuilding` | Projects its `_itemsToSell` list (`ShopItemEntry` = item + `MaxStock`). Zero/negative `MaxStock` → default 5 (preserves pre-refactor behaviour). |
| `CraftingBuilding` | Inspector-authored `_inputStockTargets` (`List<StockTarget>`). Added by the Layer A fix so forges proactively request input materials instead of waiting for a commission. |

On `OnWorkerPunchIn` the evaluator runs both passes:
1. `CheckStockTargets(IStockProvider)` — proactive restock of declared targets.
2. `CheckCraftingIngredients(CraftingBuilding)` — commission-driven aggregation (still runs whenever `ActiveCraftingOrders` is non-empty; unchanged from pre-refactor).

## Pluggable `LogisticsPolicy` (ScriptableObject)

The "how much to order when virtual stock < target" decision is delegated to an `ILogisticsPolicy`. Three shipping policies:

| Policy | Trigger | Order quantity |
|---|---|---|
| `MinStockPolicy` (default) | `current < MinStock` | `MinStock - current` |
| `ReorderPointPolicy` | `current < MinStock * ReorderThresholdPct` | `MinStock * OrderMultiplier - current` |
| `JustInTimePolicy` | `current < MinStock` | Fixed `BatchSize` (may take multiple ticks) |

Resolution order in the facade's `EnsurePolicy()`:
1. Inspector slot (`_logisticsPolicy`).
2. `Resources.Load<LogisticsPolicy>("Data/Logistics/DefaultMinStockPolicy")`.
3. Runtime `ScriptableObject.CreateInstance<MinStockPolicy>()` + one-time `Debug.LogWarning`.

Byte-identical to Layers A+B at every fallback tier.

## Public API (byte-stable across the refactor)

State readers (delegate to `OrderBook`):
- `ActiveOrders`, `PlacedBuyOrders`, `PlacedTransportOrders`, `ActiveTransportOrders`, `ActiveCraftingOrders`, `HasPendingOrders`.
- Sub-component handles: `OrderBook`, `Dispatcher`, `Evaluator`, `Policy`.

Diagnostics:
- `LogLogisticsFlow` (Inspector bool) — toggles `[LogisticsDBG]` traces through the whole chain.

Entry points (event-driven):
- `OnWorkerPunchIn(Character worker)` — punch-in tick. Runs `CheckStockTargets` + `CheckCraftingIngredients`.
- Subscribes to `TimeManager.OnNewDay` for expiration sweeps.

Placement / lifecycle:
- `PlaceBuyOrder`, `PlaceCraftingOrder`, `PlaceTransportOrder`.
- `ProcessActiveBuyOrders()` / `RetryUnplacedOrders()` (delegate to dispatcher).
- `UpdateOrderProgress`, `UpdateTransportOrderProgress`, `UpdateCraftingOrderProgress`.
- `AcknowledgeDeliveryProgress`, `OnItemsDeliveredByTransporter`.
- `CancelBuyOrder`, `CancelActiveTransportOrder`, `ReportCancelledTransporter`, `ReportMissingReservedItem`.

## Data flow

Full cycle (autonomous restock — shop or crafter):
```
OnWorkerPunchIn (JobLogisticsManager arrives at work)
       │
       ▼
LogisticsStockEvaluator.CheckStockTargets(building as IStockProvider)
       │
       ▼  virtual = physical + placedBuyOrders
       │
For each target where ILogisticsPolicy says "order":
       │
       ▼
LogisticsStockEvaluator.RequestStock(item, qty)
       │
       ▼
FindSupplierFor(item) → supplier CommercialBuilding
       │
       ▼
EnqueueBuyOrder — add to PlacedBuyOrders, add PendingOrder
       │
       ▼
(If crafting workshop) CheckCraftingIngredients also aggregates commission demand.
       │
       ▼
JobLogisticsManager GOAP pops PendingOrder → GoapAction_PlaceOrder
       │
       ▼
Walks to supplier, initiates InteractionPlaceOrder
       │
       ├── Success ──► supplier.ActiveOrders += order (IsPlaced = true)
       └── Fail   ──► PendingOrder stays, retry later (IsPlaced false)
       │
       ▼
Supplier.ProcessActiveBuyOrders  (LogisticsTransportDispatcher)
       │
       ├── Has stock? ──► create TransportOrder, add to PlacedTransportOrders
       │                  FindTransporterBuilding (or LogError if none)
       └── No stock? ──► create internal CraftingOrder, add to ActiveCraftingOrders
                                    │
                                    ▼
                              JobCrafter picks up via BT, produces ItemInstance
       │
       ▼
JobTransporter moves items → NotifyDeliveryProgress
       │
       ▼
Supplier.AcknowledgeDeliveryProgress — remove TransportOrder from PlacedTransportOrders
       │
       ▼
Client receives items → remove from PlacedBuyOrders
```

## Transporter pickup routing & NavMesh safety net

Between "Supplier.ProcessActiveBuyOrders creates TransportOrder" and "JobTransporter delivers", the transporter has to physically reach the source building's stock, pick it up, and carry it out. The source's interior `StorageZone` is frequently not reachable on NavMesh — doorways too narrow, multi-room interiors, items settled behind furniture. Two layers now handle that:

**Optional `PickupZone` on `CommercialBuilding`.** Mirrors `DeliveryZone`. Authored on reachable ground at the building's entrance. When set:
- `GoapAction_MoveToItem` targets `source.PickupZone.GetRandomPointInZone()` instead of the raw WorldItem position (backward-compatible — null PickupZone preserves legacy path-to-WorldItem).
- `GoapAction_PickupItem.IsValid` waits until the reserved WorldItem is physically inside PickupZone bounds, so the transporter doesn't grab-and-run from StorageZone directly.
- `GoapAction_StageItemForPickup` (new, `Cost = 0.2f`, owned by the source's own `JobLogisticsManager`) picks up reserved `ItemInstance`s from StorageZone and drops them in PickupZone. Ordering: `PlaceOrder` (0.1) > `StageForPickup` (0.2) > `GatherStorageItems` (0.5) > `IdleInCommercialBuilding`.
- `GoapAction_GatherStorageItems.FindLooseWorldItem` excludes anything already inside PickupZone so the same tick doesn't gather-back.
- `CommercialBuilding.RefreshStorageInventory` Pass 1 merges PickupZone contents into the `physicalItems` set so staged shipments aren't flagged as missing from storage.

**NavMesh reachability probe.** Both `GoapAction_MoveToItem` and `GoapAction_MoveToDestination` now invoke `NavMesh.CalculatePath` before committing the first `SetDestination`. If the status is `PathInvalid` or `PathPartial`, the action calls the virtual `OnPathUnreachable(worker, dest, status)` hook on `GoapAction_MoveToTarget`. Transporter overrides log an error, call `supplier.LogisticsManager.ReportMissingReservedItem(order)` to release reservations, and `_job.CancelCurrentOrder(true)`. No half-moves, no silent item drops.

**Loop breaker.** `BuyOrder.PathUnreachableCount` increments on every `RecordPathUnreachable()`. After 3 failures the order's `IsReachabilityStalled` flips true and `LogisticsTransportDispatcher.ProcessActiveBuyOrders` skips it, stopping the supplier from re-dispatching transporters into the same dead end. The stale order expires normally via `TimeManager.OnNewDay`; the client's stock check then places a fresh order under current conditions.

## Cancellation cascade

```
CancelBuyOrder(order)
       │
       ├── Remove from my side (ActiveOrders or PlacedBuyOrders)
       ├── Cascade to counterparty: remove their mirror entry
       └── Drop any linked pending TransportOrder safely
```

Skipping the cascade leaves one side waiting for something the other already forgot. Always use the public `CancelBuyOrder`, never manually `Remove`.

## State & persistence

All five lists are per-building runtime state. Map hibernation does not currently persist the in-flight queues — orders re-emerge the next time a worker punches in. Reputation deltas from expired orders are persisted on `CharacterRelation`.

## Known gotchas / edge cases

- **Virtual stock = physical + placed** — reading only physical over-orders. The evaluator always asks the policy.
- **Physical handshake is mandatory** — orders are **not** live until `InteractionPlaceOrder` returns success. Retry if target busy.
- **Duplicate check before enqueue** — always scan `PlacedBuyOrders` / `PlacedTransportOrders` before creating a new one (the evaluator does this).
- **In-flight tracking is global** — use `InTransitQuantity` globally (not per-transporter) to avoid over-delivery.
- **Cancellation must cascade** — never raw-`.Remove` from either list.
- **Reputation penalty on expiration** — expired `BuyOrder` calls `UpdateRelation(client, negative)` on the supplier side. Multiple expirations ruin the supplier's reputation.
- **VirtualResourceSupplier** — when the supplier is a `VirtualResourceSupplier`, `TryFulfillOrder` dynamically creates `ItemInstance`s from `CommunityData.ResourcePools` in the same frame.
- **Missing TransporterBuilding is now a `Debug.LogError`** — pre-refactor this was a silent `LogWarning`. If you see it, the delivery chain is dead — build/stream a `TransporterBuilding` into the map.
- **Diagnostics flag must be off at ship** — `_logLogisticsFlow` is per-building so designers can isolate one forge; never commit it enabled in a prefab.
- **Reserved items are protected from the ghost-pass** — `CommercialBuilding.RefreshStorageInventory` Pass 1 now skips any `ItemInstance` currently referenced by a `TransportOrder.ReservedItems`. Non-kinematic `WorldItem` physics (post-`FreezeOnGround` removal) can leave an item momentarily outside the StorageZone's `BoxCollider` during a single `Physics.OverlapBox` query; without this protection, the pass would false-positive the instance as a ghost, `ReportMissingReservedItem` would cascade, and the in-flight transport would die. True reservation loss is detected at pickup time by `GoapAction_PickupItem`, which also self-heals if the logical `_inventory` lost an entry that's still actively reserved and physically present.
- **Theft detection is gated by a building-wide scan** — after a `CraftingOrder` completes, just-spawned items sit at the crafting station's `_outputPoint` for a few ticks before `GatherStorageItems` / `RefreshStorageInventory` Pass 2 absorbs them into `_inventory`. During that gap, `LogisticsTransportDispatcher.HandleInsufficientStock` used to interpret "completed craft + zero available stock" as theft and clone the whole craft order, making the blacksmith over-produce (e.g. 10 items for a Quantity=3 order). The dispatcher now calls `CommercialBuilding.CountUnabsorbedItemsInBuildingZone(itemSO)` before firing the theft branch; if `inventory + in-flight ≥ the completed craft's Quantity`, the items are assumed in-transit and the branch is skipped. The helper counts **both** loose `WorldItem`s inside `BuildingZone` AND items currently carried by this building's own workers (Logistics Manager inventory + `HandsController.CarriedItem`) — necessary because the Manager's pickup despawns the `WorldItem` from the scene, which would otherwise make the gate false-positive on every Manager pickup. Genuine theft (items gone from both the zone and all workers) still triggers the warning + replacement order.

## Open questions / TODO

- [ ] `DefaultMinStockPolicy.asset` needs to be authored in the Editor and dropped into `Resources/Data/Logistics/`. Until then, every building pays an allocation + logs a warning on first punch-in. Designer action item.
- [ ] `FindSupplierFor` is first-match — no ranking by distance, reputation, or price. Multi-supplier maps will always hit the same one.
- [ ] `FindTransporterBuilding` is first-match — no load balancing across multiple transporters on the same map.
- [ ] `LogisticsCapabilityWindow` scans loaded scenes only; hibernated `CommunityData.ConstructedBuildings` are invisible to the report. Offline validation needs a separate enumeration.

## Dependencies

### Upstream
- [[building]] — attaches to `CommercialBuilding` as a component.
- [[character]] — `InteractionPlaceOrder` uses character interaction.
- [[items]] — orders move `ItemInstance`s.

### Downstream
- [[shops]] — consumes `ProcessActiveBuyOrders` for shop restock; `ShopBuilding` implements `IStockProvider`.
- [[crafting-loop]] — `ActiveCraftingOrders` feeds `JobCrafter`; `CraftingBuilding` implements `IStockProvider` and exposes `InputStockTargets`.
- [[job-roles]] — `JobTransporter` fulfills `TransportOrder`s; `JobLogisticsManager` reads `LogLogisticsFlow`.
- [[character-relation]] — expiration penalties.

## Change log
- 2026-04-23 — Added optional `PickupZone` routing for transporters + NavMesh upfront reachability probe on `MoveToItem` / `MoveToDestination` + `BuyOrder.PathUnreachableCount` loop breaker + new `GoapAction_StageItemForPickup` on source-side `JobLogisticsManager`. Fixes transporter stalling when `StorageZone` is unreachable. — claude
- 2026-04-22 — Extend theft-gate scan to also count items carried by the building's own workers; was firing every time the Logistics Manager picked up a crafted item to move it to storage, since the WorldItem temporarily despawns during carry — claude
- 2026-04-22 — Gate `HandleInsufficientStock`'s "theft detected" branch via `CountUnabsorbedItemsInBuildingZone` so the dispatcher doesn't clone a completed `CraftingOrder` while items are still mid-transit from the station output to storage (fixes over-production: 10 spawned for a Quantity=3 order) — claude
- 2026-04-22 — `PlaceBuyOrder` and `PlaceCraftingOrder` now refresh physical storage on reception so the supplier/crafter decides dispatch-vs-craft against fresh stock, not stale punch-in data — claude
- 2026-04-22 — Protect reserved items from `RefreshStorageInventory` ghost-pass + add pickup self-heal in `GoapAction_PickupItem` — fixes transport stall caused by non-kinematic WorldItem physics racing `Physics.OverlapBox` — claude
- 2026-04-21 — Logistics refactor: IStockProvider + pluggable LogisticsPolicy SO + facade split + input stock contract on CraftingBuilding — claude
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md) — operational procedures (single source of truth for how).
- [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — `RefreshStorageInventory` reserved-item protection + `CountUnabsorbedItemsInBuildingZone` helper.
- [GoapAction_PickupItem.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupItem.cs) — transporter pickup self-heal + PickupZone gating.
- [GoapAction_MoveToItem.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_MoveToItem.cs) — PickupZone-first destination + `OnPathUnreachable` override.
- [GoapAction_MoveToDestination.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_MoveToDestination.cs) — `OnPathUnreachable` override.
- [GoapAction_StageItemForPickup.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_StageItemForPickup.cs) — source-side staging action.
- [Base/GoapAction_MoveToTarget.cs](../../Assets/Scripts/AI/GOAP/Actions/Base/GoapAction_MoveToTarget.cs) — NavMesh reachability probe.
- [BuyOrder.cs](../../Assets/Scripts/World/Jobs/BuyOrder.cs) — `PathUnreachableCount` + `IsReachabilityStalled`.
- [LogisticsTransportDispatcher.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs) — theft-branch gating in `HandleInsufficientStock`.
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §5.
- [BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs) — facade.
- [Logistics/](../../Assets/Scripts/World/Buildings/Logistics/) — sub-components + policies + `ILogisticsPolicy` / `LogisticsPolicy`.
- [IStockProvider.cs](../../Assets/Scripts/World/Buildings/IStockProvider.cs) — contract + `StockTarget` struct.
- [CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs) — `_inputStockTargets`.
- [ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs) — `_itemsToSell` projection.
- [LogisticsCapabilityWindow.cs](../../Assets/Editor/Buildings/LogisticsCapabilityWindow.cs) — Editor diagnostic.
- [[jobs-and-logistics]] parent.
