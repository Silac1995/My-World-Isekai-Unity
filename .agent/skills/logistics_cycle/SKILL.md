---
name: logistics-cycle
description: The supply chain cycle between CommercialBuildings and JobLogisticsManagers — how shops restock via BuyOrders, order expiration, and inter-building logistics.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied. It covers how a `JobLogisticsManager` detects missing inventory, places commercial orders (`BuyOrder`) via character interactions, and how the crafted goods produce internal `CraftingOrder`s before flowing back through `TransporterBuilding`s.

## When to use this skill
- To understand how a `ShopBuilding` restocks its inventory automatically.
- To debug why a shop has empty shelves or why orders aren't being placed/delivered.
- To add a new type of order or supply chain interaction between buildings.
- To understand the full cycle: ShopBuilding → CraftingBuilding → TransporterBuilding → ShopBuilding.

## How to use it

### 1. The Full Supply Chain Cycle

```
ShopBuilding (needs items)
  │
  │  1. LogisticsManager punches in → CheckShopInventory()
  │     Queues PendingOrder (physical visit to supplier)
  ▼
Work Cycle (JobLogisticsManager.Execute)
  │  2. Dequeues PendingOrder → Pushes PlaceOrderBehaviour
  │     Manager physically walks to Supplier building
  │     Manager starts InteractionPlaceOrder with Supplier Manager, placing a BuyOrder
  ▼
CraftingBuilding (produces items)
  │
  │  3. Crafting Manager receives BuyOrder. Checks if they have the stock:
  │     IF Stock YES → Skip to Step 6.
  │     IF Stock NO → CheckCraftingIngredients().
  │          Are ingredients missing? → Queue BuyOrder to another supplier (recursive).
  │          All ingredients present? → Queue internal CraftingOrder.
  │
  │  4. JobCrafter picks up CraftingOrder (BTAction_PerformCraft)
  │     Crafts the items → items go into building Inventory (StorageZone)
  │
  │  5. LogisticsManager sees completed CraftingOrder + items in Inventory
  │     CraftingOrder fulfills the waiting BuyOrder requirements.
  │     Queues TransportOrder (transport) for delivery to original ClientBuilding.
  ▼
TransporterBuilding (moves items)
  │
  │  6. LogisticsManager accepts TransportOrder
  │     JobTransporter picks it up → physically carries items from Source StorageZone
  ▼
ShopBuilding (receives items)
     7. Items delivered to Shop's Inventory (StorageZone). BuyOrder is Complete.
```

The `JobLogisticsManager` checks inventory on **Punch-In** (arrival at work in ANY building):
- `WorkBehaviour` → `WorkerStartingShift(worker)` → `CommercialBuilding` (base class) detects logistics worker → calls `OnWorkerPunchIn()`.
- `OnWorkerPunchIn()` triggers:
  - `CheckShopInventory()` if workplace is a `ShopBuilding`.
  - `CheckCraftingIngredients()` if workplace is a `CraftingBuilding`.
- `OnWorkerPunchIn()` is gated by `IsOwnerOrOnSchedule()`:
  - **Owner** can act anytime.
  - **Employees** only act during their scheduled `ScheduleActivity.Work` hours.

### 2. Step 1: Client Places BuyOrder

`CheckShopInventory()` or `CheckCraftingIngredients()` identifies missing items.
```
  ├─ FindSupplierFor(itemSO) → finds a CommercialBuilding (e.g., CraftingBuilding)
  ├─ Provider LogisticsManager active?
  ├─ Already ordered? → Skip
  └─ Place BuyOrder via InteractionPlaceOrder
       → _worker.CharacterInteraction.ForceExecuteInteraction(new InteractionPlaceOrder(...))
```

### 3. Step 2 & 3: Supplier Internal Production

When a Supplier's LogisticsManager receives a `BuyOrder`:
1. It registers the order as an active commitment.
2. Internally, if not enough stock exists, a `CraftingOrder` is created (invisible to the external client).
3. The `JobCrafter` natively checks active `CraftingOrder`s via `BTAction_PerformCraft`.
4. The Crafter builds the `WorldItem`s and LogisticsManager stores them in the `StorageZone` (via `GoapAction_GatherStorageItems`).

### 4. Step 4: Transport and Delivery (TransportOrder)

Once a `BuyOrder` has enough physical items in the supplier's inventory to be completed:
1. Supplier's LogisticsManager spawns a `TransportOrder` (source = Supplier, dest = `BuyOrder.ClientBuilding`).
2. Transporter from a `TransporterBuilding` is summoned:
   - Walks to source's `StorageZone`, takes items.
   - Walks to destination's `DeliveryZone`, adds items to Client's inventory.
3. Once fulfilled, the `BuyOrder` is removed from logs.

### 5. Step 4-5: TransporterBuilding Delivers

`TransporterBuilding` has:
- 1 `JobLogisticsManager` ("Head of Logistics") — accepts `TransportOrder`s.
- N `JobTransporter`s — physically carry items from source → destination.

When a `TransportOrder` is accepted:
1. A `JobTransporter` picks it up.
2. Transporter walks to the source building's `StorageZone`, plays pick up animation, takes items from building Inventory into equipment.
3. Transporter travels to the destination building's `DeliveryZone`, plays drop off animation, delivers items.
4. `ShopBuilding.AddToInventory()` is called recursively.

### 6. InteractionPlaceOrder (Mandatory)

**All orders MUST go through `InteractionPlaceOrder`** — character-to-character interaction:
- `new InteractionPlaceOrder(BuyOrder)` — commercial contracts.
- `new InteractionPlaceOrder(CraftingOrder)` — internal production.
- `new InteractionPlaceOrder(TransportOrder)` — physical delivery routing.
- Applies +2 relation boost on both sides.
- Requires the target character to have a `JobLogisticsManager`.

### 7. Shop Catalogue: ShopItemEntry

```csharp
[System.Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock; // target stock (defaults to 5 if 0)
}
```
- `GetStockCount(ItemSO)` — count in inventory.
- `NeedsRestock(ItemSO, maxStock)` — current stock < target?

### 8. Order Types

| Type | Purpose | Placed By | Consumed By |
|------|---------|-----------|-------------|
| `BuyOrder` | Inter-building Commercial Contract | Client's LogisticsManager | Supplier's LogisticsManager |
| `CraftingOrder` | Internal production request | Supplier's LogisticsManager | Supplier's `JobCrafter` |
| `TransportOrder` | Physical movement request | Supplier's LogisticsManager | TransporterBuilding's `JobTransporter` |
| Punch-in fires? | `[Shop] LogisticsManager X a pointé` in logs? |
| Schedule ok? | `IsOwnerOrOnSchedule()` returns true? Check schedule. |
| Supplier exists? | `CraftingBuilding` in scene with matching craftable item? |
| Supplier has manager? | Supplier's `JobLogisticsManager` has a worker assigned? |
| Order placed? | `[Order] BuyOrder de Nx ... acceptée` in logs? |
| Crafter picks up? | `BTAction_PerformCraft` fires on CraftingBuilding? |
| Transport ordered? | `TransportOrder` placed with `TransporterBuilding`? |
| Transporter delivers? | `JobTransporter` picks up and delivers items? |
| Ghost Deliveries? | Ensure `JobTransporter.NotifyDeliveryProgress` rigorously checks `CarriedItem != null` before granting progress. |
| Manager freezes? | If the `JobLogisticsManager` freezes endlessly over loose items, verify their `GoapAction_GatherStorageItems` dynamically ignores the `FindingItem` phase if their hands are currently full. |
