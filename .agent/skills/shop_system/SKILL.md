---
description: Architecture of the commercial Shop System, from Logistics restocks to Customer queuing and sales.
---

# Shop System Skill

This document details how the Shop Economy functions, which acts as the final destination for items crafted or gathered in the world. 
The system relies on a continuous loop of: **Restocking (Logistics)** -> **Waiting for Customers (Vendor)** -> **Queuing & Buying (Customers)**.

## Architecture & Responsibilities

### 1. `ShopBuilding`
The `ShopBuilding` inherits from `CommercialBuilding` but introduces several crucial business features:
- **`Items To Sell`**: A predefined list of `ItemSO` that dictates what the shop trades in (e.g., Swords, Apples, Clothes).
- **`Inventory`**: The physical stock currently sitting in the shop (`List<ItemInstance>`).
- **`Customer Queue`**: A waiting list (`Queue<Character>`) for customers who arrive while all vendors are busy.

### 2. Restocking (`JobLogisticsManager`)
Every Shop employs at least one `JobLogisticsManager`. Their sole duty is maintaining the inventory:
- They periodically check `Inventory` against `Items To Sell`.
- If stock is low, the manager generates a `BuyOrder` (e.g., "I need 10 Iron Swords").
- This `BuyOrder` is sent out to production buildings (e.g., the Forge), where a `JobTransporter` will eventually deliver the requested items directly into the Shop's real-time `Inventory`.

### 3. Selling (`JobVendor`)
Vendors are the face of the shop. Their GOAP/BT behavior traps them behind the counter.
- A vendor constantly checks the Shop's `Customer Queue`.
- If the queue has people, the Vendor "calls" the next person in line.
- The called Customer approaches the Vendor and initiates an **`InteractionBuyItem`**.
- If the Vendor's shift ends (`WorkerEndingShift`), if they are the last vendor in the building, they must forcibly **Clear the Queue**, effectively kicking out remaining customers and telling them the shop is closed.

### 4. Buying (`Customer NPCs`)
NPCs driven by needs (e.g., `NeedItem`, `NeedFood`) trigger a Shopping behavior.
- The NPC paths to the nearest Shop selling the item they want.
- Upon arrival, they ask the Shop: "Are any Vendors free?"
  - **Yes**: They walk straight to the Vendor.
  - **No**: They enqueue themselves in the Shop's `Customer Queue` and switch to a waiting behavior (`WaitInQueueBehaviour`), standing patiently in a line area.
- Once called by a Vendor, they walk up and execute `InteractionBuyItem`, where money is deducted, and the item transfers from the Shop's `Inventory` to the NPC's Inventory.

## Flow Priority (Checklist for Debugging)
If a shop isn't working, verify this chain:
1. Does the Shop have a `JobLogisticsManager`?
2. Did the Manager successfully place a `BuyOrder` for the missing stock?
3. Was the order delivered by a `JobTransporter` into `_inventory`?
4. Is there an active `JobVendor` standing at the counter?
5. Did the Customer successfully query `ShopBuilding.JoinQueue()`?
6. Did the Vendor call them out of the queue?
