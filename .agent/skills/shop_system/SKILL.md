---
name: shop-system
description: Architecture of the commercial Shop System, from Logistics restocks to Customer queuing and sales.
---

# Shop System

This document details how the Shop Economy functions, which acts as the final destination for items crafted or harvested in the world. 
The system relies on a continuous loop of: **Restocking (Logistics)** -> **Waiting for Customers (Vendor)** -> **Queuing & Buying (Customers)**.

## Architecture & Responsibilities

### 1. `ShopBuilding`
The `ShopBuilding` inherits from `CommercialBuilding` but introduces several crucial business features:
- **`Items To Sell`**: A predefined list of `ItemSO` that dictates what the shop trades in (e.g., Swords, Apples, Clothes).
- **`Inventory`**: The physical stock currently sitting in the shop (`List<ItemInstance>`).
- **`Customer Queue`**: A waiting list (`Queue<Character>`) for customers who arrive while all vendors are busy.

### 2. Restocking (`JobLogisticsManager`)
Every Shop employs at least one `JobLogisticsManager`. Their sole duty is maintaining the inventory:
- On each **new day** (`OnNewDay`), they check `Inventory` against `Items To Sell`.
- If stock is low, the manager finds a `CraftingBuilding` that can produce the item (via `FindSupplierFor`) and places a `CraftingOrder` on that building's `JobLogisticsManager`.
- The crafter at the CraftingBuilding picks up the `CraftingOrder` via their BT (`BTAction_PerformCraft`) and produces the items.
- A `JobTransporter` will eventually deliver the crafted items into the Shop's `Inventory`.
- The LogisticsManager is **event-driven** (`HasWorkToDo() => false`), so the character goes on break at the shop between events.

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

## Multiplayer occupancy contract

`Cashier` inherits `_occupant` from `OccupiableFurniture`, but that field is server-only — `ServerTickAutoOccupy` seats the vendor via `Use()` on the host peer, and the base class has no replication channel. `CashierNetSync` therefore owns two `NetworkVariable<ulong>` mirrors written from the server-side paths of `Cashier`:

- `OccupantNetworkObjectId` — vendor (set in `Use`, cleared in `Release`).
- `CurrentCustomerNetworkObjectId` — customer mid-transaction (set in `TryAcquireCustomerLock`, cleared in `ReleaseCustomerLock`).

`Cashier.Occupant` (override) and `Cashier.CurrentCustomer` resolve those ids through `NetworkManager.SpawnManager.SpawnedObjects` on non-server peers, returning the local in-memory field on the server. Every consumer (`CashierInteractable` pre-gate, `ShopCashiersTabRow` / `ShopCashiersTabView` management UI, `IsAvailableForCustomer`) reads through these properties — never the private field — so host and client see identical occupancy state.

`CashierNetSync.OnNetworkSpawn` runs a server-side backfill: if the cashier already has an occupant when its NetSync component first comes online (save/load restore, hot-reload, or pre-NetSync-spawn race), the current id is pushed into the NetVar so the NGO initial-state sync delivers the truth to every joining peer.

The legacy `NotifyOccupiedClientRpc` / `NotifyReleasedClientRpc` are kept as no-op visual hooks; do not rely on them for state transport.

### Vendor seat is a symmetric state machine — Validate + AutoOccupy

`Cashier` ticks at 1 Hz (server only) and runs **both halves** of the seat state machine in `Update()`:

- `ServerTickValidateOccupant()` evicts the seated vendor when they can no longer vend.
- `ServerTickAutoOccupy()` seats a fresh on-shift vendor in range.

Eviction conditions (any one fires `Release()`):
- Occupant destroyed / dead / unconscious — `!Occupant.IsAlive()` also covers map-hibernation despawn since destroyed Unity refs compare null.
- Vendor pulled into a battle — `Occupant.CharacterCombat.IsInBattle`.
- Vendor's job is no longer `JobVendor` for this cashier's shop.
- Vendor's schedule is no longer `ScheduleActivity.Work`.
- Vendor walked / was knocked out of the auto-seat radius.

`Release()` handles all replication and side-effects in one place: it clears `_occupant` on the base class, writes `OccupantNetworkObjectId = 0`, fires `NotifyReleasedClientRpc`, and aborts any in-flight customer transaction via `AbortActiveTransactionServerOnly("vendor walked off")`. Adding a new eviction trigger means adding a branch inside `ServerTickValidateOccupant` — never resubscribing to character events from the furniture (the seat is held on the cashier, not on a running `CharacterAction`, so `Character.OnCombatStateChanged` does not unwind it on its own).

Why a tick validator instead of pure event subscription: vendor combat/incapacitation/death events DO now fire from `Character` and auto-call `OccupiableFurniture.Leave(this)` via the central `AutoLeaveOccupiedFurniture` helper (see `character_core/SKILL.md` §3.b). The cashier-side tick is **defense-in-depth** for the three triggers `Character` has no event for: vendor walked / was knocked out of the seat radius, schedule flipped to non-Work, or job was reassigned to a non-`JobVendor` of this shop. The 1 Hz symmetric tick costs O(1) per cashier per second, runs server-only, allocates nothing, and closes the last gaps. Per rule #34 this is the symmetric inverse of an existing tick, not idempotent polling on stable state.

## Multiplayer panel-binding contract

`UI_ShopBuyPanel.Initialize` reads `cashier.LinkedShop` (which is `Cashier._linkedBuilding as ShopBuilding`). On a joining client, the Cashier furniture is spawned via the `Building._defaultFurnitureLayout` pipeline — `Instantiate` runs first, then `SetParent` re-parents under the shop's NetworkObject. `Cashier.Awake`'s `GetComponentInParent<CommercialBuilding>()` therefore returns `null` until NGO's `AutoObjectParentSync` applies the re-parent, leaving `LinkedShop` null even after `OnNetworkSpawn` fires on the NetSync.

`UI_ShopBuyPanel.Initialize` defends against this race with a one-shot late-bind: if `_shop == null` on entry, it calls `cashier.TryRegisterWithShop()` (the idempotent re-resolution path `ShopBuilding.OnNetworkSpawn` uses server-side) and re-reads `LinkedShop`. Only after that fallback fails does the panel error out and close. Without this fallback, the joining-client buy panel silently `CloseWindow`s and the user sees "nothing happens" when pressing E on a vendor-seated cashier.

See [[host-only-state-blindspot]] for the broader recurring class of bugs this falls under, and the audit checklist that catches them before shipping.

## Multiplayer item-delivery authority (2026-05-14)

`CharacterAction_BuyFromShop.DeliverToCustomer` is server-only (the action is a `CharacterAction_Continuous`, and `CharacterActions.ExecuteAction` short-circuits non-server peers for continuous actions). It used to call `character.CharacterEquipment.PickUpItem(instance)` unconditionally on the server. That works for NPC customers and host customers (both have `Character.IsOwnedByServer == true`), but for a remote-client customer the bag-inventory mutation lands only on the server's shadow copy — the owning client's `_bag.Inventory.ItemSlots` is never touched, so the item silently disappears.

The fix mirrors the same ownership branch in `WorldItem.RequestInteractServerRpc`: when the customer's Character is spawned and **not** owned by the server, build a `NetworkItemData` from the pulled `ItemInstance` and call `character.CharacterActions.ReceiveItemPickupClientRpc(itemData)`. The owner client deserializes the item via `ItemSO.CreateInstance()` + `JsonUtility.FromJsonOverwrite` (preserves `WeaponInstance` / `WearableInstance` / etc. polymorphism) and runs `PickUpItem` locally. The NPC / host path is unchanged.

**Cost / risk**: the routed-through-owner path has no callback for `PickUpItem` failure. If the client's bag is full **and** their hands are full, the item is lost. Same risk shape as `WorldItem.RequestInteractServerRpc` (which despawns optimistically before the ClientRpc lands). The shop UI's Confirm gate reads client-side bag space and pre-rejects when the bag won't accept the purchase, so the failure mode is rare in practice.

**Audit checklist when changing this path again** — re-run the six questions in [[host-only-state-blindspot]] and specifically verify:
- Item delivered into the owner client's UI inventory (open the bag panel on the joining client after Confirm).
- After portal-gate return / save, the bought item persists in the client's character profile.
- The buy panel's Confirm button greys when client-side bag has no space.
