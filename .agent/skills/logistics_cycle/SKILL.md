---
name: logistics-cycle
description: The complete supply chain cycle and internal mechanics of JobLogisticsManager, detailing order queuing, stock reservation, shared references, and GOAP logic.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied, with a deep dive into how `JobLogisticsManager` operates internally to prevent duplicate orders, manage shared object references, and dispatch transporters via GOAP without looping.

## When to use this skill
- To understand how a `ShopBuilding` restocks its inventory automatically.
- To debug why orders aren't being placed, why transporters are clustering or freezing, or why deliveries complete prematurely.
- To safely modify `JobLogisticsManager` or `JobTransporter` without breaking the delicate order lifecycle.

## The Event-Driven Order Architecture

The Logistics cycle relies on a completely asynchronous, event-driven architecture heavily dependent on physical interactions and GOAP priority handling.

### 1. The Internal Order Lists (JobLogisticsManager)
Understanding `JobLogisticsManager` requires knowing its internal tracking lists. Never bypass these lists.
**Rule:** Orders are objects passed by reference. Operations on a single shared order must be strictly centralized.
- `_activeOrders`: Commercial requests received from other clients.
- `_placedBuyOrders`: Requests we made to suppliers.
- `_placedTransportOrders`: Physical delivery requests sent to a `TransporterBuilding`.
- `_pendingOrders`: The physical "To-Do" list. Enqueued orders are popped by `GoapAction_PlaceOrder` which forces the manager to physically walk to the target and perform `InteractionPlaceOrder`.

### 2. Shared References and Double-Increments
**Rule:** When a Transporter completes a delivery, they must invoke `AssociatedBuyOrder.RecordDelivery()` exactly **ONCE**.
Because `BuyOrder` instances are shared by reference between the Client, Supplier, and the Transporter's `TransportOrder`, both the Client (`AcknowledgeDeliveryProgress`) and the Supplier (`OnItemsDeliveredByTransporter`) must act purely as observers checking if `order.IsCompleted` is true to clean up their internal tracking lists. If both sides increment the delivery manually, it results in a double-increment bug.

### 3. GOAP Priority Starvation (Gathering vs. Dispatching)
**Rule:** Logistics managers must be allowed to interrupt low-priority cleanup tasks to execute high-priority orders.
- A `JobLogisticsManager` cleans the physical floor (e.g., crafter drops) using `GoapAction_GatherStorageItems`.
- To prevent an infinite gathering loop (where crafters produce faster than the manager cleans, starving the `GoapAction_PlaceOrder` execution), the Gather action MUST include an explicit bailout: `if (_manager.HasPendingOrders) { return false; }` in `IsValid` or `Execute`. This forces GOAP to replan and prioritize dispatching.

### 4. Transporter Targeting and Clustering
**Rule:** When multiple Transporters fetch identical items, their targets must be randomized.
- If Transporters search for valid `WorldItem`s on the floor deterministically (e.g., picking the first item in the list), they will all target the exact same item position, resulting in a "clown car" clustering bug where they freeze and block each other.
- Collect all valid visible reserved items into a list and select the target using `Random.Range()`.

### 5. Proper Inventory Checking
**Rule:** To verify if a character is currently carrying an item during GOAP states, NEVER exclusively check their hands (`AreHandsFree()`). Characters use backpacks/item slots. Always use `Inventory.GetCarriedItem(worker) != null` to ensure the entire capacity is validated to prevent freezing during drop-offs.

## Existing Components
- `JobLogisticsManager` -> Scans inventory, creates Pending Orders, and dispatches Transporters.
- `JobTransporter` -> Accepts `TransportOrder`s, navigates to the physical supplier to pickup items, and delivers them.
- `InteractionPlaceOrder` -> The physical handshake executing the enqueued Pending Order between two active characters.
