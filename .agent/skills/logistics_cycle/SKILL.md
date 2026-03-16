---
name: logistics-cycle
description: The complete supply chain cycle and internal mechanics of JobLogisticsManager, detailing order queuing, stock reservation, and physical interactions.
---

# Logistics Cycle (JobLogisticsManager)

This skill documents the complete supply chain that keeps shops stocked and buildings supplied, with a deep dive into how `JobLogisticsManager` operates internally to prevent duplicate orders, manage stock reservations, and dispatch transporters.

## When to use this skill
- To understand how a `ShopBuilding` restocks its inventory automatically.
- To debug why a shop has empty shelves, why orders aren't being placed/delivered, or why transporters are duplicating orders.
- To understand the internal list structures (`_activeOrders`, `_pendingOrders`, `_placedTransportOrders`) of `JobLogisticsManager`.
- To safely modify `JobLogisticsManager` or `JobTransporter` without breaking the delicate order lifecycle.

## How to use it

### 1. The Core Responsibilities of JobLogisticsManager
`JobLogisticsManager` acts as the brain of any commercial building. It handles:
- **Inventory Monitoring**: Checking if `ShopEntries` fall below `MaxStock`.
- **Supplier Sourcing**: Finding other buildings that produce missing items via `FindSupplierFor`.
- **Order Queueing (`_pendingOrders`)**: Storing orders that need to be physically placed via character interactions.
- **Fulfillment (`ProcessActiveBuyOrders`)**: Dispatching `TransportOrder`s to Transporters or `CraftingOrder`s to internal Crafters when clients request items.

### 2. The Internal Order Lists
Understanding `JobLogisticsManager` requires knowing its internal tracking lists. Never bypass these lists when modifying the system.
- `_activeOrders (List<BuyOrder>)`: Commercial requests received from *other* clients. We are the supplier.
- `_placedBuyOrders (List<BuyOrder>)`: Requests *we* made to suppliers. We are the client.
- `_placedTransportOrders (List<TransportOrder>)`: Physical delivery requests we sent to a `TransporterBuilding` to deliver our items to our clients.
- `_activeCraftingOrders (List<CraftingOrder>)`: Internal requests for our `JobCrafter`s to make items to fulfill `_activeOrders`.
- `_pendingOrders (Queue<PendingOrder>)`: The physical "To-Do" list. Whenever the manager decides to place a Buy/Craft/Transport order, they queue it here. The GOAP action `GoapAction_PlaceOrder` dequeues these and physically walks the manager to the target to perform an `InteractionPlaceOrder`.

### 3. The Supply Chain Flow
The lifecycle of an item moving between buildings involves several state changes:
1. **Detection**: `OnWorkerPunchIn()` reads the `ShopBuilding` inventory. If stock is low, it calls `RequestStock()`, which creates a `BuyOrder`, adds it to `_placedBuyOrders`, and enqueues a `PendingOrder`.
2. **Placement**: The logistics manager physically walks to the supplier and initiates `InteractionPlaceOrder`. The supplier accepts it and adds it to their `_activeOrders`. Both parties gain +2 relationship.
3. **Fulfillment (Supplier Side)**: During `Execute()`, the supplier calls `ProcessActiveBuyOrders()`.
   - Checks physical stock minus `reservedStockThisTick`.
   - If enough stock: Reserves it, creates a `TransportOrder`, adds it to `_placedTransportOrders`, and enqueues a `PendingOrder` for the `TransporterBuilding`.
   - If not enough stock: Creates a `CraftingOrder` for the internal `JobCrafter`.
4. **Delivery**: The `JobTransporter` physically moves items. When dropped, they trigger `NotifyDeliveryProgress()`.
5. **Acknowledgment**: The supplier calls `AcknowledgeDeliveryProgress()`, which removes the `TransportOrder` from `_placedTransportOrders`.
6. **Completion & Social Reward**: When the delivery is fully acknowledged, the `BuyOrder` is removed from both building's logs. The ClientBoss and SupplierBoss mutually grant each other +5 relationship points for a successful trade.

### 4. Stock Reservation (Anti-Double Booking)
To prevent generating duplicate `TransportOrder`s for the same physical items, `JobLogisticsManager` uses a local `Dictionary<ItemSO, int> reservedStockThisTick` inside `ProcessActiveBuyOrders()`.
- It tracks how many items are currently assigned to a `TransportOrder` *in the same evaluation frame*.
- `actuallyAvailableStock = physicalStock - reservedStockThisTick`.
- This ensures that if we have 10 items physically, we don't dynamically dispatch two separate orders demanding 10 items each.

## Examples

### Example 1: Debugging Phantom Transport Orders
**Scenario**: A supplier keeps sending out transport orders to the TransporterBuilding, but there are no items left in the storage. Transporters arrive and find nothing.
**Solution**: Check `JobLogisticsManager.ProcessActiveBuyOrders()`. Ensure that `reservedStockThisTick` is being correctly incremented relative to physical stock:
```csharp
int getReserved(ItemSO item) => reservedStockThisTick.ContainsKey(item) ? reservedStockThisTick[item] : 0;
int actuallyAvailableStock = physicalStock - getReserved(buyOrder.ItemToTransport);

if (actuallyAvailableStock >= remainingToDispatch) {
    // Reserve it so the next BuyOrder in the loop doesn't claim it!
    if (reservedStockThisTick.ContainsKey(buyOrder.ItemToTransport))
        reservedStockThisTick[buyOrder.ItemToTransport] += remainingToDispatch;
    else
        reservedStockThisTick.Add(buyOrder.ItemToTransport, remainingToDispatch);
        
    // ... Queue TransportOrder ...
}
```

### Example 2: Safely Managing In-Transit Quantities
**Scenario**: Transporters are refusing to pick up an active `TransportOrder` because they think it's already fully handled by other transporters, causing the delivery to permanently halt.
**Solution**: When `JobTransporter` picks up an item, `AddInTransit(amount)` is called to lock the quota. You **must** ensure that `RemoveInTransit(amount)` is systematically called if the order completes, fails, or the item is dropped.
```csharp
public void CancelCurrentOrder()
{
    // Clean up the transit quota so other transporters can take over the job
    if (CurrentOrder != null && CarriedItems.Count > 0)
    {
        CurrentOrder.RemoveInTransit(CarriedItems.Count);
    }
    CurrentOrder = null;
    CarriedItems.Clear();
}
```

### Example 3: Adding a New Order Segment
If you introduce a new type of logistics transaction (e.g., `CleaningOrder`), you must adapt the full cycle:
1. Update `struct PendingOrder` to include a constructor for `CleaningOrder`.
2. In `JobLogisticsManager.Execute()`, evaluate active `CleaningOrder`s.
3. Queue it using `_pendingOrders.Enqueue(new PendingOrder(cleaningOrder, targetBuilding))`.
4. Ensure `InteractionPlaceOrder` acts as the handshake, supporting the passing of `CleaningOrder` to the target character/building.
