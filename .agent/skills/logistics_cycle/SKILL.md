---
name: logistics-cycle
description: The supply chain cycle between CommercialBuildings and JobLogisticsManagers — how shops restock via CraftingOrders, order expiration, and inter-building logistics.
---

# Logistics Cycle

This skill documents the complete supply chain that keeps shops stocked and buildings supplied. It covers how a `JobLogisticsManager` detects missing inventory, places production orders at `CraftingBuilding`s via character interactions, and how the crafted goods flow back.

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

### 2. Shop Catalogue: ShopItemEntry

Each `ShopBuilding` defines its catalogue via a `List<ShopItemEntry>`:
```csharp
[System.Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock; // target stock qty (defaults to 5 if 0)
}
```
- `ShopEntries` exposes the full list.
- `GetStockCount(ItemSO)` returns how many of that item are currently in inventory.
- `NeedsRestock(ItemSO, maxStock)` returns `true` if current stock < max target.

### 3. The Restock Flow (ShopBuilding → CraftingBuilding)

```
OnNewDay
  └─ CheckShopInventory(shop)
       └─ For each ShopItemEntry:
            ├─ NeedsRestock(item, maxStock)? → No? Skip (✓ log)
            ├─ FindSupplierFor(itemSO) → finds a CraftingBuilding
            ├─ Supplier has a LogisticsManager with a worker? → No? Skip (⚠ log)
            ├─ Already a CraftingOrder for this item at supplier? → Skip (⏳ log)
            └─ Place CraftingOrder via InteractionPlaceOrder
                 └─ _worker.CharacterInteraction.StartInteractionWith(
                        supplierLogistics.Worker,
                        new InteractionPlaceOrder(craftingOrder)
                    )
```

> [!IMPORTANT]
> **Orders MUST go through `InteractionPlaceOrder`**. The shop's LogisticsManager character physically interacts with the supplier's LogisticsManager character. This is a conscious design choice — orders are social transactions, not data-only operations.

### 4. InteractionPlaceOrder

`InteractionPlaceOrder` implements `ICharacterInteractionAction` and supports both order types:
- `new InteractionPlaceOrder(BuyOrder order)` — for transport orders.
- `new InteractionPlaceOrder(CraftingOrder order)` — for crafting requests.

In `Execute(source, target)`:
1. Finds the `JobLogisticsManager` on the **target** character.
2. Calls `PlaceBuyOrder()` or `PlaceCraftingOrder()` accordingly.
3. Applies a +2 relation boost on both sides for the commercial agreement.
4. Logs the result.

### 5. Order Types

| Type | Purpose | Placed By | Consumed By |
|------|---------|-----------|-------------|
| `CraftingOrder` | Request item production | Shop's LogisticsManager (via interaction) | CraftingBuilding's `JobCrafter` (via BT) |
| `BuyOrder` | Transport items between buildings | Any LogisticsManager (via interaction) | `JobTransporter` (via BT) |

### 6. Order Lifecycle

Both order types follow this lifecycle:
1. **Created** → added to `_activeOrders` or `_activeCraftingOrders`.
2. **Progress** → `RecordDelivery(amount)` / `RecordCraft(amount)` updates quantity.
3. **Completed** → `IsCompleted` returns true, order is removed from the list.
4. **Expired** → `RemainingDays` hits 0, reputation penalties applied.

### 7. Inter-Building Discovery

`FindSupplierFor(ItemSO)` iterates over `BuildingManager.Instance.allBuildings`:
- Skips the current workplace.
- Checks if the building is a `CraftingBuilding`.
- Calls `craftingBuilding.GetCraftableItems().Contains(item)` to match.

## Debugging Checklist

If items are not being restocked:
1. Does the `ShopBuilding` have `ShopItemEntry`s configured in the inspector? (Item + MaxStock)
2. Is a `JobLogisticsManager` assigned and has a worker?
3. Is `TimeManager.OnNewDay` firing correctly?
4. Does a `CraftingBuilding` exist that can craft the item? (`GetCraftableItems()`)
5. Does that `CraftingBuilding` have its own `JobLogisticsManager` with a **worker assigned**?
6. Check the console for `[Logistics]` logs — they detail every step of the check.
7. Does the crafter have a `JobCrafter` and the required skill level?
