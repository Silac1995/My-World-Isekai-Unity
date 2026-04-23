---
name: logistics-cycle
description: The complete supply chain cycle. BuildingLogisticsManager facade (order book + dispatcher + stock evaluator), pluggable LogisticsPolicy SOs, IStockProvider contract on ShopBuilding/CraftingBuilding, JobLogisticsManager physical fulfilment, diagnostics toggle, and the Editor capability report.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied, with a deep dive into how `BuildingLogisticsManager` manages data and `JobLogisticsManager` operates internally to execute orders physically.

## When to use this skill
- When debugging why a shop has empty shelves, why orders aren't being placed/delivered, or why transporters are duplicating orders.
- When you need to understand the internal list structures (`_activeOrders`, `_pendingOrders`, `_placedTransportOrders`) of `BuildingLogisticsManager`.
- When modifying `BuildingLogisticsManager` or `JobTransporter` without breaking the delicate order lifecycle.

## The Logistics Cycle Architecture

### 1. BuildingLogisticsManager facade + three sub-components (Layer C)

As of Layer C, `BuildingLogisticsManager` is a **thin MonoBehaviour facade** that delegates to three plain-C# collaborators (mirroring the Character facade pattern at the building scale):

- **`LogisticsOrderBook`** — state only. Owns `_activeOrders`, `_placedBuyOrders`, `_placedTransportOrders`, `_activeTransportOrders`, `_activeCraftingOrders`, and the `_pendingOrders` queue. No knowledge of the scene.
- **`LogisticsTransportDispatcher`** — reserves physical `ItemInstance`s, creates `TransportOrder`s, finds the scene's `TransporterBuilding`. Owns `ProcessActiveBuyOrders` and `RetryUnplacedOrders`.
- **`LogisticsStockEvaluator`** — drives `CheckStockTargets(IStockProvider)` and `CheckCraftingIngredients`. Delegates the "how much to order" decision to a pluggable `ILogisticsPolicy` SO.

Files: `Assets/Scripts/World/Buildings/Logistics/`.

**Public API on the facade is unchanged** — every external caller (`JobLogisticsManager`, `InteractionPlaceOrder`, `GoapAction_PlaceOrder`, `CommercialBuilding`, `HarvestingBuilding`, tests) compiles and runs unchanged. The nested `PendingOrder` struct and `OrderType` enum remain on `BuildingLogisticsManager` for the same reason.

`JobLogisticsManager` still handles the worker-side GOAP logic that physically fulfils the `_pendingOrders` queue.

**Rule:** Never bypass internal tracking lists. Use the facade API or go through `LogisticsOrderBook` directly if you're inside a collaborator.
- `ActiveOrders` (`List<BuyOrder>`): Commercial requests received from *other* clients. We are the supplier.
- `PlacedBuyOrders` (`List<BuyOrder>`): Requests *we* made to suppliers. We are the client.
- `PlacedTransportOrders` (`List<TransportOrder>`): Physical delivery requests we sent to a `TransporterBuilding`.
- `ActiveCraftingOrders` (`List<CraftingOrder>`): Internal requests for our `JobCrafter`s.
- Pending queue: accessed via `PeekPendingOrder` / `DequeuePendingOrder` / `EnqueuePendingOrder` on the facade.

### 1b. Pluggable ILogisticsPolicy (Layer C)

Stocking strategy is authorable per-building via a `LogisticsPolicy` ScriptableObject in the Inspector. Three policies ship:

- **`MinStockPolicy`** (default): orders `MinStock - current` whenever current < MinStock. Byte-identical to Layers A+B.
- **`ReorderPointPolicy`**: fires only when stock falls below `MinStock * _reorderThresholdPct`, then refills up to `MinStock * _orderMultiplier`.
- **`JustInTimePolicy`**: orders one `_batchSize` per tick until MinStock is reached.

If the Inspector slot is empty, the facade resolves via `Resources.Load<LogisticsPolicy>("Data/Logistics/DefaultMinStockPolicy")`; if that is absent, it creates a runtime `MinStockPolicy` instance and logs a warning. Byte-identical to A+B either way.

All three policies implement `ILogisticsPolicy.ComputeReorderQuantity(int currentVirtualStock, StockTarget target)` — add new policies by inheriting `LogisticsPolicy`.

> **Designer action item:** `DefaultMinStockPolicy.asset` is **not** shipped in `Resources/Data/Logistics/` — the asset needs Editor interaction to author (`Assets > Create > MWI > Logistics > MinStockPolicy`). Until it exists, every building falls through to the runtime-instance path and emits the one-time warning. Behaviour is correct either way; this is cleanup, not a bug.

### 1d. IStockProvider contract (Layer A/B)

Any `CommercialBuilding` that wants proactive restocking implements `IStockProvider` and yields a set of `StockTarget { ItemSO ItemToStock; int MinStock }` from `GetStockTargets()`.

Two shipping implementers:
- **`ShopBuilding`** — projects its `_itemsToSell` (`ShopItemEntry` = item + `MaxStock`) into `StockTarget`s. Zero/negative `MaxStock` defaults to 5 (preserves pre-refactor behaviour for un-tuned shops).
- **`CraftingBuilding`** — exposes `[SerializeField] List<StockTarget> _inputStockTargets`. This is the bug-fix payload of Layer A: before the refactor, a forge with no commissions sat idle because `CheckCraftingIngredients` only ran once a `CraftingOrder` was active. Now every `OnWorkerPunchIn`, the evaluator runs both passes:
  1. `CheckStockTargets(IStockProvider)` — proactive restock of declared input materials.
  2. `CheckCraftingIngredients(CraftingBuilding)` — commission-driven aggregation (unchanged, still runs when `ActiveCraftingOrders` is non-empty).

Add `IStockProvider` to any future commercial building subclass that needs autonomous restock. Yielding zero targets is fine — the evaluator no-ops.

### 1c. Editor capability report (Layer C)

Menu: `MWI → Logistics → Capability Report`. Opens `LogisticsCapabilityWindow` (Editor-only, `#if UNITY_EDITOR`, file at `Assets/Editor/Buildings/LogisticsCapabilityWindow.cs`). Scans the currently open scene(s) and lists:
- **Demanded but unsuppliable** (RED) — items some building declares as a `StockTarget` (or crafting-order ingredient at runtime) with no building whose `ProducesItem(item)` returns true.
- **Supplied but undemanded** (GRAY) — informational.

Click any demand/supplier row to ping the offending `CommercialBuilding` in the hierarchy.

**Scope limit:** scans loaded scenes only — does **not** enumerate hibernated `CommunityData.ConstructedBuildings` entries. A map that is asleep during the report will under-represent both sides of the equation. Future work if offline validation matters.

**Edit-time safety:** `CraftingBuilding.ProducesItem()` reads `_furnitureManager.Furnitures`, which is only populated in `Room.Awake()` → `LoadExistingFurniture()`. Outside Play mode that list is empty and a naive `ProducesItem` query would false-negative. The window therefore precomputes each `CraftingBuilding`'s craftable set via `GetComponentsInChildren<CraftingStation>(true)` + `station.CraftableItems` directly. `HarvestingBuilding` and `VirtualResourceSupplier` don't need runtime state, so they still use `ProducesItem` normally.

### 1e. Diagnostics toggle (Layer C)

`BuildingLogisticsManager` exposes an Inspector checkbox `_logLogisticsFlow` (property `LogLogisticsFlow`). When ON, the whole chain emits `[LogisticsDBG]` traces at every decision point:

```
OnWorkerPunchIn  →  CheckStockTargets  →  RequestStock  →  FindSupplierFor
                                                            │
                                                            ▼
                                                  DispatchTransportOrder
                                                            │
                                                            ▼
                                                  FindTransporterBuilding
```

`JobLogisticsManager` routes its own early-exit log through the same flag so a designer can turn on diagnostics for a single forge (or shop) without drowning the console. Defaults to OFF. Never ship with it ON.

**Missing-`TransporterBuilding` failure** is now `Debug.LogError` with full source/dest/item/qty context (was a silent `LogWarning` pre-refactor). If you see it and no `TransporterBuilding` exists on the map, the whole delivery chain is dead — build or stream one in.

### 2. The Supply Chain Flow
The lifecycle of an item moving between buildings involves several state changes:
1. **Detection**: `BuildingLogisticsManager.OnWorkerPunchIn()` delegates to `LogisticsStockEvaluator`, which runs `CheckStockTargets(IStockProvider)` for every `IStockProvider` (shops *and* crafting workshops) and `CheckCraftingIngredients(CraftingBuilding)` for aggregated commission demand. Virtual stock below the policy's reorder point creates a `BuyOrder`, adds it to `_placedBuyOrders` (virtual stock), and enqueues a `PendingOrder`.
2. **Placement**: The active `JobLogisticsManager` worker physically walks to the supplier and initiates `InteractionPlaceOrder`. Supplier accepts, adds to `_activeOrders`. Handshake occurs.
3. **Fulfillment (Supplier Side)**: During operations, supplier calls `BuildingLogisticsManager.ProcessActiveBuyOrders()`. Creates `TransportOrder` tracking or `CraftingOrder`.
4. **Delivery**: `JobTransporter` physically moves items. `NotifyDeliveryProgress()` triggered on drop.
5. **Acknowledgment**: Supplier calls `BuildingLogisticsManager.AcknowledgeDeliveryProgress()`, removes `TransportOrder` from `_placedTransportOrders`. 

### 2b. PickupZone staging + NavMesh safety net

Transporters used to walk directly to `TargetWorldItem.transform.position`, which sits inside `StorageZone`. If the interior wasn't NavMesh-reachable (doorway too narrow, multi-room building, item settled behind a wall), the transporter squeezed in, picked up, then couldn't path out — the refund at `GoapAction_PickupItem.OnActionFailed/OnActionFinished` left the item physically on the ground and the order never moved. Two layers now prevent that:

**PickupZone (`CommercialBuilding._pickupZone`, optional).** Mirrors `DeliveryZone`. If authored, it's an outdoor / entrance-side `Zone` guaranteed reachable on NavMesh. The transporter flow becomes:
- `GoapAction_MoveToItem.GetDestinationPoint` — returns `source.PickupZone.GetRandomPointInZone()` when set; else falls back to the raw `TargetWorldItem` position (backward-compatible — every existing scene with `_pickupZone == null` keeps pre-change behaviour).
- `GoapAction_PickupItem.IsValid` — when `source.PickupZone != null`, requires the target item's position to lie inside the PickupZone's collider bounds. If not, returns false and the action waits one tick → replan → the source's own `JobLogisticsManager` runs the staging action first.
- `GoapAction_StageItemForPickup` (new, `Cost = 0.2f`) — the source's JobLogisticsManager picks up a reserved `ItemInstance` from `StorageZone` and drops it inside `PickupZone`. Only valid when `_pickupZone != null`, the source has `TransportOrder`s where `Source == this`, and the reserved item isn't already inside PickupZone bounds. Cost ordering: `GoapAction_PlaceOrder` (0.1f) > `StageItemForPickup` (0.2f) > `GatherStorageItems` (0.5f) > `IdleInCommercialBuilding` — placement still beats staging, staging beats inbound gather, nothing idle pre-empts either.
- `GoapAction_GatherStorageItems.FindLooseWorldItem` — explicitly excludes items inside PickupZone so staged shipments aren't gathered back into storage the same tick.
- `CommercialBuilding.RefreshStorageInventory` Pass 1 — PickupZone contents are merged into the `physicalItems` set before the ghost-detection scan, so staged items don't get treated as missing from storage. Pass 2 absorption logic is unchanged.

**NavMesh reachability probe (Phase B safety net).** Both `GoapAction_MoveToItem` and `GoapAction_MoveToDestination` now invoke `NavMesh.CalculatePath` before committing the first `SetDestination`. If the status is `PathInvalid` or `PathPartial`, the transporter aborts cleanly (no half-move, no item drop) and the action fires a virtual `OnPathUnreachable(worker, dest, status)` hook on `GoapAction_MoveToTarget`. The transporter overrides log `Debug.LogError`, call `supplier.LogisticsManager.ReportMissingReservedItem(order)` to release reservations, and `_job.CancelCurrentOrder(true)`.

**Reachability loop breaker.** `BuyOrder.PathUnreachableCount` (new field) increments on every `RecordPathUnreachable()` call, fired from the MoveToItem/MoveToDestination `OnPathUnreachable` path. After 3 failures, `BuyOrder.IsReachabilityStalled == true`, and `LogisticsTransportDispatcher.ProcessActiveBuyOrders` skips the order so the supplier stops re-dispatching transporters into the same dead end. The client still holds a record and the order will expire normally via the `TimeManager.OnNewDay` sweep — at which point the client's stock check places a fresh order that gets re-dispatched under current conditions.

**Designer guidance.** Author `PickupZone` on any building whose `StorageZone` sits deep inside the interior. For single-room buildings where `StorageZone` is itself reachable, leaving `_pickupZone == null` is fine and costs nothing.

### 3. Order Expiration, Cancellation, and Virtual Stock
**Rule:** Ensure expired or cancelled orders are systematically cleaned from both the supplier's memory AND the client's memory.
- `CheckStockTargets` (Layer B+C) and `CheckCraftingIngredients` use "Virtual Stock" = physical stock + active uncompleted `PlacedBuyOrders`. The policy compares the virtual stock against the `StockTarget`, never the raw physical count.
- If an order is canceled or expires, it must be removed from BOTH buildings. Use `CancelBuyOrder(BuyOrder)` to ensure the removal cascades to the counterpart building (Source/Destination) and drops linked pending `TransportOrder`s safely. This avoids desynchronization where a client awaits an order the supplier already deleted, or vice versa.
- Partial deliveries check against `InTransitQuantity` globally, rather than just locally per transporter, to avoid over-delivery logic traps.

### 3b. Physical ↔ logical inventory sync (CommercialBuilding.RefreshStorageInventory)

`GetItemCount(ItemSO)` reads the **logical** `_inventory` list. Items only land there if someone calls `AddToInventory` — e.g. `GoapAction_GatherStorageItems.FinishDropoff` when a logistics worker picks up loose WorldItems in the building and stashes them. Any path that physically drops items into the StorageZone without going through `AddToInventory` (harvesters dropping straight into a DepositZone that overlaps the StorageZone, couriers dropping at a destination, player drops) would desync — the physical items sit in the zone but `GetItemCount` returns 0, so the stock check keeps re-ordering stock that already exists.

`RefreshStorageInventory()` is triggered from three places: every `OnWorkerPunchIn` via `BuildingLogisticsManager`, on every `PlaceBuyOrder` and every `PlaceCraftingOrder` reception (so the next `ProcessActiveBuyOrders` tick decides dispatch-vs-craft against accurate stock instead of waiting for the next punch-in / new day), and from the `GoapAction_LocateItem` fallback when a transporter can't find its reserved items. Not invoked on `PlaceTransportOrder` since a `TransporterBuilding` has no physical stock to reconcile. It is a **two-way sync**:
- **Pass 1 (remove ghosts):** logical entries with no physical counterpart are dropped. Any ghost referenced by a live TransportOrder is reported via `ReportMissingReservedItem` so the order can recompute. **Exception — reserved items are protected:** any `ItemInstance` currently referenced by a `TransportOrder.ReservedItems` in `LogisticsManager.PlacedTransportOrders` is skipped by this pass. Rationale: with non-kinematic WorldItem physics (post-`FreezeOnGround` removal), a settling item can be briefly outside the StorageZone's `BoxCollider` bounds during a single `Physics.OverlapBox` query. Ghosting a reserved instance there would cascade into `ReportMissingReservedItem` and kill a valid in-flight transport. Truly missing reservations are detected at pickup time instead (see Pickup self-heal below).
- **Pass 2 (absorb orphans):** physical WorldItems in the StorageZone that aren't tracked logically are added to `_inventory`. Items currently carried (`IsBeingCarried`) are skipped.

This makes the system self-healing — no matter which path put the item into the zone, the next punch-in audit pulls it into the count.

**Pickup self-heal (`GoapAction_PickupItem.PrepareAction`):** when the transporter arrives and the reserved WorldItem is in front of it, the action calls `source.RemoveExactItemFromInventory(worldItem.ItemInstance)`. If that returns false BUT `_job.CurrentOrder.ReservedItems.Contains(instance)` is still true, the pickup proceeds anyway (warning-logged as a self-heal). This covers the narrow window where Pass 1 already mutated `_inventory` before the protection above was in place, or any future logical/physical desync where the reservation is still authoritative and the WorldItem is physically present. True reservation loss still takes the `ReportMissingReservedItem` + cancel path.

**Theft-detection gate (`LogisticsTransportDispatcher.HandleInsufficientStock`):** after a `CraftingOrder` completes, the just-spawned `WorldItem`s sit at the `CraftingStation._outputPoint` for 1–N ticks until `GatherStorageItems` (or `RefreshStorageInventory` Pass 2, if the output point overlaps the StorageZone) moves them into `_inventory`. In that window, a buy order that hasn't dispatched yet sees 0 stock alongside a completed craft — which used to trigger the "🚨 VOL DETECTÉ" branch and clone the whole craft order. That made the blacksmith re-craft the batch (3-quantity order → 10 spawned items). The dispatcher now calls `CommercialBuilding.CountUnabsorbedItemsInBuildingZone(itemSO)` before firing the theft branch; if `safeAvailable + unabsorbedInBuilding ≥ stolenProvenOrder.Quantity` the branch is skipped and the dispatcher waits for absorption to catch up. The helper counts **two** in-flight states: (a) loose `WorldItem`s still inside `Building.BuildingZone` that haven't been absorbed into `_inventory` yet, AND (b) matching `ItemInstance`s held by any of the building's own assigned workers (their equipment inventory + `HandsController.CarriedItem`). Case (b) is critical: when the Logistics Manager picks up a crafted item to move it to storage, the `WorldItem` is despawned during the carry phase, so case (a) alone would miss it and the gate would still false-positive theft on every Manager pickup. Real theft (items genuinely gone from the building and from all its workers) still flows through the original warning + re-order path.

### 4. V2 Virtual Stock Injection (Harvesting & Raw Resources)
In V2 of the macro-simulation, raw resources do not physically exist in map inventories until they are harvested or transported.
- The `VirtualResourceSupplier` (inheriting from `CommercialBuilding`) handles raw material sourcing directly from the `CommunityData.ResourcePools`. 
- When `BuildingLogisticsManager` processes a `BuyOrder`, it calls `_building.TryFulfillOrder(BuyOrder, remainingToDispatch)`.
- If the building is a `VirtualResourceSupplier`, it dynamically calls `ItemSO.CreateInstance()` to inject actual physical `ItemInstance` objects into its own inventory (depleting the virtual pool) in the same frame, allowing the logistics manager to instantly generate a `TransportOrder` to pick them up.

[Note: Put detailed code implementation patterns in `examples/logistics_patterns.md` instead of cluttering this file.]

## Deferred work (tracked, not hidden)

Known gaps left intentionally open by the Layer A+B+C pass:

- `FindSupplierFor` is first-match; no ranking by distance, reputation, or price. Cheapest path forward is to score candidates and sort before picking — but the current behaviour matches Layers A+B exactly, so no regression risk in deferring.
- `FindTransporterBuilding` is first-match; no load balancing across multiple `TransporterBuilding`s on the same map. A small map with one transporter is fine; multi-transporter maps will oversubscribe the first one.
- `LogisticsCapabilityWindow` only scans loaded scenes. Hibernated buildings inside `CommunityData.ConstructedBuildings` are invisible to the report. Offline validation passes need a separate enumeration path.
- `DefaultMinStockPolicy.asset` must be authored in the Editor and dropped into `Resources/Data/Logistics/` before shipping — otherwise every building pays a per-instance `CreateInstance<MinStockPolicy>()` allocation and logs the warning.

## Worker Performance Hooks

The logistics cycle is also the credit pipeline that feeds the wage system. Each productive step inside the supply chain books a unit-of-work against the acting worker's `CharacterWorkLog`, which `WageSystemService` later converts into pay.

- **Transporter deliveries**: `JobTransporter.NotifyDeliveryProgress` credits `worker.CharacterWorkLog.LogShiftUnit(worker.CharacterJob.GetCurrentWorkplace(), unloadedQty)` for each item unloaded at the destination. **Critical:** the credit goes to the worker's EMPLOYER (the `TransporterBuilding` they punched in at), NOT the destination building they just delivered to. Otherwise every transporter would silently work for whichever shop they happened to drop off at.
- **Harvester deposits**: `GoapAction_DepositResources` credits the harvester's `CharacterWorkLog` with the **deficit-bounded** portion of each deposit, computed via `HarvesterCreditCalculator.GetCreditedAmount(harvestingBuilding, itemSO, depositedQty)`. The intent is that a harvester only earns wages for items the building actually needs (i.e., closes a `StockTarget` deficit) — dumping 99 logs into a forge that already has 100 should not pay 99 units.
- **Dormant deficit cap**: `HarvesterCreditCalculator` only enforces the deficit ceiling when the workplace implements `IStockProvider`. Currently `HarvestingBuilding` does **not** implement `IStockProvider`, so `GetCreditedAmount` falls back to crediting **1 unit per deposit** regardless of stock state. This is intentional for now — it preserves wage flow until the stock-target authoring lands. Future fix: have `HarvestingBuilding` implement `IStockProvider` so the deficit cap activates.
- **Crafter completions**: `JobCrafter` credits one unit per completed craft (hook lives in the craft-completion callback inside the job class itself, not in a logistics action).
- See `.agent/skills/wage-system/SKILL.md` for how these credited units feed the per-shift wage formula and the payer architecture.
- See `.agent/skills/character-worklog/SKILL.md` for the `LogShiftUnit(workplace, units)` API contract and shift-window enforcement.

## Last updated

2026-04-23 — Added Worker Performance Hooks section documenting transporter / harvester / crafter unit-of-work credit into `CharacterWorkLog` and the dormant `IStockProvider` deficit cap on `HarvestingBuilding`.

2026-04-21 — Layers A + B + C landed: `IStockProvider` contract, `CraftingBuilding._inputStockTargets` autonomous restock, pluggable `LogisticsPolicy` SO (`MinStock` / `ReorderPoint` / `JustInTime`), three-component facade split (`LogisticsOrderBook` / `LogisticsTransportDispatcher` / `LogisticsStockEvaluator`), `LogLogisticsFlow` diagnostics toggle, missing-transporter now `LogError`, Editor `Capability Report` window.
