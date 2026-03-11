---
name: logistics-cycle
description: The supply chain cycle between CommercialBuildings and JobLogisticsManagers — how shops restock via CraftingOrders, order expiration, and inter-building logistics.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied. It covers how a `JobLogisticsManager` detects missing inventory, places production orders at `CraftingBuilding`s, and how the crafted goods flow back.

## When to use this skill
- To understand how a `ShopBuilding` restocks its inventory automatically.
- To debug why a shop has empty shelves or why a `CraftingBuilding` never receives orders.
- To add a new type of order or supply chain interaction between buildings.
- To understand the relationship between `BuyOrder`, `CraftingOrder`, and the buildings that use them.

## How to use it

### 1. The Restock Trigger

The `JobLogisticsManager` subscribes to `TimeManager.OnNewDay`. On each new day, it runs `CheckExpiredOrders()` which handles:
1. **Expiring old orders** (both `BuyOrder` and `CraftingOrder`) and applying reputation penalties.
2. **Restocking** — if the workplace is a `ShopBuilding`, it calls `CheckShopInventory(shop)`.

### 2. The Restock Flow (ShopBuilding → CraftingBuilding)

```
OnNewDay
  └─ CheckShopInventory(shop)
       └─ For each ItemSO in shop.ItemsToSell:
            ├─ Is it in stock? → Skip
            ├─ FindSupplierFor(itemSO) → finds a CraftingBuilding
            ├─ Is there already a CraftingOrder for this item at supplier? → Skip
            └─ Place CraftingOrder on supplier's JobLogisticsManager
                 └─ Crafter's BT (BTAction_PerformCraft) picks it up and crafts the item
```

**Key methods in `JobLogisticsManager`:**
- `CheckShopInventory(ShopBuilding)` — scans `ItemsToSell` vs `Inventory`, places `CraftingOrder`s.
- `FindSupplierFor(ItemSO)` — searches all `CraftingBuilding`s via `BuildingManager` for one that can produce the item (`GetCraftableItems().Contains(item)`).
- `PlaceCraftingOrder(CraftingOrder)` — registers the order in `_activeCraftingOrders`.

### 3. Order Types

| Type | Purpose | Placed By | Consumed By |
|------|---------|-----------|-------------|
| `CraftingOrder` | Request item production | Shop's LogisticsManager | CraftingBuilding's `JobCrafter` (via BT) |
| `BuyOrder` | Transport items between buildings | Any LogisticsManager | `JobTransporter` (via BT) |

### 4. Order Lifecycle

Both order types follow this lifecycle:
1. **Created** → added to `_activeOrders` or `_activeCraftingOrders`.
2. **Progress** → `RecordDelivery(amount)` / `RecordCraft(amount)` updates quantity.
3. **Completed** → `IsCompleted` returns true, order is removed from the list.
4. **Expired** → `RemainingDays` hits 0, reputation penalties applied to `ClientBoss` and `IntermediaryBoss`.

### 5. Duplicate Prevention

Before placing a new `CraftingOrder`, the shop's manager checks the **supplier's** `ActiveCraftingOrders`:
```csharp
bool alreadyOrdered = supplierLogistics.ActiveCraftingOrders.Any(o => o.ItemToCraft == itemSO);
```
This prevents flooding a crafter with duplicate orders for the same item.

### 6. Inter-Building Discovery

`FindSupplierFor(ItemSO)` iterates over `BuildingManager.Instance.allBuildings`:
- Skips the current workplace.
- Checks if the building is a `CraftingBuilding`.
- Calls `craftingBuilding.GetCraftableItems().Contains(item)` to match.

## Debugging Checklist

If items are not being restocked:
1. Does the `ShopBuilding` have `ItemsToSell` configured in the inspector?
2. Is a `JobLogisticsManager` assigned and has a worker?
3. Is `TimeManager.OnNewDay` firing correctly?
4. Does a `CraftingBuilding` exist in the scene that can craft the item?
5. Does that `CraftingBuilding` have its own `JobLogisticsManager` with a worker?
6. Does the crafter at that building have a `JobCrafter` and the required skill?
7. Is `BTAction_PerformCraft` picking up the `CraftingOrder`?
