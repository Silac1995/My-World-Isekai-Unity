---
name: logistics-cycle
description: The supply chain cycle between CommercialBuildings and JobLogisticsManagers — how shops restock via CraftingOrders, order expiration, and inter-building logistics.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied. It covers how a `JobLogisticsManager` detects missing inventory, places production orders at `CraftingBuilding`s via character interactions, and how the crafted goods flow back through `TransporterBuilding`s.

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
  │     Queues PendingOrder (physical visit)
  ▼
Work Cycle (JobLogisticsManager.Execute)
  │  2. Dequeues PendingOrder → Pushes PlaceOrderBehaviour
  │     Manager physically walks to Supplier building
  │     Manager starts InteractionPlaceOrder with Supplier Manager
  ▼
CraftingBuilding (produces items)
  │
  │  3. Crafting Manager receives CraftingOrder → CheckCraftingIngredients()
  │     Scans active orders and checks inventory (StorageZone).
  │     IF ingredients missing → Queues BuyOrder (physical visit to supplier).
  │
  │  4. JobCrafter picks up CraftingOrder (BTAction_PerformCraft)
  │     Crafts the items → items go into building Inventory (StorageZone)
  │
  │  5. LogisticsManager sees completed order + items in Inventory
  │     Queues BuyOrder (transport) for delivery
  ▼
TransporterBuilding (moves items)
  │
  │  6. LogisticsManager accepts BuyOrder
  │     JobTransporter picks it up → physically carries items
  ▼
ShopBuilding (receives items)
     7. Items delivered to Shop's Inventory (StorageZone)
```

The `JobLogisticsManager` checks inventory on **Punch-In** (arrival at work in ANY building):
- `WorkBehaviour` → `WorkerStartingShift(worker)` → `CommercialBuilding` (base class) detects logistics worker → calls `OnWorkerPunchIn()`.
- `OnWorkerPunchIn()` triggers:
  - `CheckShopInventory()` if workplace is a `ShopBuilding`.
  - `CheckCraftingIngredients()` if workplace is a `CraftingBuilding`.
- `OnWorkerPunchIn()` is gated by `IsOwnerOrOnSchedule()`:
  - **Owner** can act anytime.
  - **Employees** only act during their scheduled `ScheduleActivity.Work` hours.

### 3. Step 1: Shop Places CraftingOrder

`CheckShopInventory(ShopBuilding shop)` iterates `ShopEntries`:
```
For each ShopItemEntry (Item + MaxStock):
  ├─ NeedsRestock(item, maxStock)? → No? Skip (✓ log)
  ├─ FindSupplierFor(itemSO) → finds a CraftingBuilding
  ├─ Supplier has LogisticsManager with worker? → No? Skip (⚠ log)
  ├─ Already a CraftingOrder for this item? → Skip (⏳ log)
  └─ Place CraftingOrder via InteractionPlaceOrder
       → _worker.CharacterInteraction.StartInteractionWith(
              supplierLogistics.Worker,
              new InteractionPlaceOrder(craftingOrder)
          )
```

### 4. Step 2-3: CraftingBuilding Produces & Ships (Implemented)

When a `CraftingBuilding`'s LogisticsManager receives a `CraftingOrder`:
1. The `JobCrafter`'s BT (`BTAction_PerformCraft`) picks up the order.
2. Crafter produces items → calls `UpdateCraftingOrderProgress()`.
3. When `IsCompleted` → `PlaceTransportOrder()` is auto-called:
   - Finds a `TransporterBuilding` via `FindTransporterBuilding()`.
   - Creates a `BuyOrder` (source = CraftingBuilding, dest = `order.CustomerBuilding`).
   - Places it via `InteractionPlaceOrder` with the TransporterBuilding's worker.

The `CraftingOrder.CustomerBuilding` field tracks the final destination (set when the shop places the order).

### 5. Step 4-5: TransporterBuilding Delivers

`TransporterBuilding` has:
- 1 `JobLogisticsManager` ("Head of Logistics") — accepts `BuyOrder`s.
- N `JobTransporter`s — physically carry items from source → destination.

When a `BuyOrder` is accepted:
1. A `JobTransporter` picks it up.
2. Transporter goes to the source building, picks up items.
3. Transporter travels to the destination building, delivers items.
4. `ShopBuilding.AddToInventory()` is called.

### 6. InteractionPlaceOrder (Mandatory)

**All orders MUST go through `InteractionPlaceOrder`** — character-to-character interaction:
- `new InteractionPlaceOrder(BuyOrder)` — transport orders.
- `new InteractionPlaceOrder(CraftingOrder)` — crafting requests.
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
| `CraftingOrder` | Request item production | Shop's LogisticsManager | CraftingBuilding's `JobCrafter` |
| `BuyOrder` | Transport items A→B | Any LogisticsManager | TransporterBuilding's `JobTransporter` |

### 9. Schedule Guard

`IsOwnerOrOnSchedule()` gates all logistics actions:
- **Owner** → always allowed.
- **Employee** → only during `ScheduleActivity.Work`.

## Debugging Checklist

| Step | What to check |
|------|--------------|
| Shop has items? | `ShopItemEntry`s configured? `MaxStock` > 0? |
| Manager assigned? | `JobLogisticsManager` on ShopBuilding has a worker? |
| Punch-in fires? | `[Shop] LogisticsManager X a pointé` in logs? |
| Schedule ok? | `IsOwnerOrOnSchedule()` returns true? Check schedule. |
| Supplier exists? | `CraftingBuilding` in scene with matching craftable item? |
| Supplier has manager? | Supplier's `JobLogisticsManager` has a worker assigned? |
| Order placed? | `[Order] CraftingOrder de Nx ... acceptée` in logs? |
| Crafter picks up? | `BTAction_PerformCraft` fires on CraftingBuilding? |
| Transport ordered? | `BuyOrder` placed with `TransporterBuilding`? |
| Transporter delivers? | `JobTransporter` picks up and delivers items? |
