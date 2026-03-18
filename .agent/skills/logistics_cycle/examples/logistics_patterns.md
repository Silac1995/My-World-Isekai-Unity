# Logistics Patterns

## Example 1: Debugging Phantom Transport Orders

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

## Example 2: Safely Managing In-Transit Quantities

**Scenario**: Transporters are refusing to pick up an active `TransportOrder` because they think it's already fully handled by other transporters, causing the delivery to permanently halt. Or alternatively, multiple transporters are picking up too many items for a single order because they fail to account for what others are already carrying.

**Solution**: 
When `JobTransporter` picks up an item, `AddInTransit(amount)` is called to lock the quota. 
You **must** calculate the truly remaining needed capacity by subtracting both delivered and in-transit items:
```csharp
int globallyStillNeeded = CurrentOrder.Quantity - CurrentOrder.DeliveredQuantity - CurrentOrder.InTransitQuantity;
bool hasEnough = globallyStillNeeded <= 0;
```

You **must** also ensure that `RemoveInTransit(amount)` is systematically called if the order completes, fails, or the item is dropped before delivery.
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

## Example 3: Adding a New Order Segment

If you introduce a new type of logistics transaction (e.g., `CleaningOrder`), you must adapt the full cycle:
1. Update `struct PendingOrder` to include a constructor for `CleaningOrder`.
2. In `JobLogisticsManager.Execute()`, evaluate active `CleaningOrder`s.
3. Queue it using `_pendingOrders.Enqueue(new PendingOrder(cleaningOrder, targetBuilding))`.
4. Ensure `InteractionPlaceOrder` acts as the handshake, supporting the passing of `CleaningOrder` to the target character/building.
