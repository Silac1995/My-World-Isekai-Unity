---
name: logistics-cycle
description: The complete supply chain cycle and internal mechanics of BuildingLogisticsManager and JobLogisticsManager, detailing order queuing, stock reservation, and physical interactions.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied, with a deep dive into how `BuildingLogisticsManager` manages data and `JobLogisticsManager` operates internally to execute orders physically.

## When to use this skill
- When debugging why a shop has empty shelves, why orders aren't being placed/delivered, or why transporters are duplicating orders.
- When you need to understand the internal list structures (`_activeOrders`, `_pendingOrders`, `_placedTransportOrders`) of `BuildingLogisticsManager`.
- When modifying `BuildingLogisticsManager` or `JobTransporter` without breaking the delicate order lifecycle.

## The Logistics Cycle Architecture

### 1. BuildingLogisticsManager & JobLogisticsManager Responsibilities
`BuildingLogisticsManager` acts as the brain of any commercial building. It handles:
- **Inventory Monitoring**: Checking if `ShopEntries` fall below `MaxStock` via physical storage audit vs virtual stock (`OnWorkerPunchIn`).
- **Supplier Sourcing**: Finding other buildings that produce missing items via `FindSupplierFor`.
- **Order Queueing (`_pendingOrders`)**: Storing orders that need to be physically placed via character interactions.
- **Fulfillment (`ProcessActiveBuyOrders`)**: Dispatching `TransportOrder`s to Transporters or `CraftingOrder`s to internal Crafters when clients request items.

Meanwhile, `JobLogisticsManager` handles the worker logic. It acts via GOAP to physically fulfill the `_pendingOrders` queue.

**Rule:** Never bypass internal tracking lists in `BuildingLogisticsManager` when modifying the system.
- `_activeOrders` (`List<BuyOrder>`): Commercial requests received from *other* clients. We are the supplier.
- `_placedBuyOrders` (`List<BuyOrder>`): Requests *we* made to suppliers. We are the client.
- `_placedTransportOrders` (`List<TransportOrder>`): Physical delivery requests we sent to a `TransporterBuilding`.
- `_activeCraftingOrders` (`List<CraftingOrder>`): Internal requests for our `JobCrafter`s.
- `_pendingOrders` (`Queue<PendingOrder>`): The physical "To-Do" list for `GoapAction_PlaceOrder`.

### 2. The Supply Chain Flow
The lifecycle of an item moving between buildings involves several state changes:
1. **Detection**: `BuildingLogisticsManager.OnWorkerPunchIn()` reads the `ShopBuilding` inventory. Low stock creates a `BuyOrder`, adds it to `_placedBuyOrders` (virtual stock), and enqueues a `PendingOrder`.
2. **Placement**: The active `JobLogisticsManager` worker physically walks to the supplier and initiates `InteractionPlaceOrder`. Supplier accepts, adds to `_activeOrders`. Handshake occurs.
3. **Fulfillment (Supplier Side)**: During operations, supplier calls `BuildingLogisticsManager.ProcessActiveBuyOrders()`. Creates `TransportOrder` tracking or `CraftingOrder`.
4. **Delivery**: `JobTransporter` physically moves items. `NotifyDeliveryProgress()` triggered on drop.
5. **Acknowledgment**: Supplier calls `BuildingLogisticsManager.AcknowledgeDeliveryProgress()`, removes `TransportOrder` from `_placedTransportOrders`. 

### 3. Order Expiration, Cancellation, and Virtual Stock
**Rule:** Ensure expired or cancelled orders are systematically cleaned from both the supplier's memory AND the client's memory.
- `CheckShopInventory` uses "Virtual Stock", which is Physical Stock + active uncompleted `_placedBuyOrders`.
- If an order is canceled or expires, it must be removed from BOTH buildings. Use `CancelBuyOrder(BuyOrder)` to ensure the removal cascades to the counterpart building (Source/Destination) and drops linked pending `TransportOrder`s safely. This avoids desynchronization where a client awaits an order the supplier already deleted, or vice versa.
- Partial deliveries check against `InTransitQuantity` globally, rather than just locally per transporter, to avoid over-delivery logic traps.

[Note: Put detailed code implementation patterns in `examples/logistics_patterns.md` instead of cluttering this file.]
