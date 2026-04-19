---
type: system
title: "Building Logistics Manager"
tags: [logistics, jobs, orders, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
  - "[[building]]"
  - "[[items]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - item-inventory-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on:
  - "[[building]]"
  - "[[jobs-and-logistics]]"
  - "[[items]]"
  - "[[character]]"
depended_on_by:
  - "[[shops]]"
  - "[[jobs-and-logistics]]"
---

# Building Logistics Manager

## Summary
Per-building brain for the supply chain. Maintains five internal lists — `_activeOrders`, `_placedBuyOrders`, `_placedTransportOrders`, `_activeCraftingOrders`, `_pendingOrders` — and orchestrates order creation, placement (physical handshake via `InteractionPlaceOrder`), fulfillment, and expiration. Virtual stock (`physical + _placedBuyOrders`) drives understock detection. Cancellations must cascade to counterparts to prevent desync.

## Purpose
Give every commercial building a single, predictable order-lifecycle authority so shops, crafters, and transporters can cooperate without race conditions or ghost orders.

## Responsibilities
- Inventory monitoring: physical audit vs virtual stock on `OnWorkerPunchIn` / `OnNewDay`.
- Supplier sourcing: `FindSupplierFor(ItemSO)`.
- Order creation: `BuyOrder`, `CraftingOrder`, `TransportOrder`.
- Pending-order queue: `_pendingOrders` drives `GoapAction_PlaceOrder`.
- Active-order tracking: `_activeOrders` (we are supplier), `_placedBuyOrders` (we are client).
- Fulfillment: `ProcessActiveBuyOrders` dispatches to transporters or crafters.
- Cancellation: `CancelBuyOrder(BuyOrder)` cascades removal on both sides.
- Acknowledgement: `AcknowledgeDeliveryProgress(transportOrder)` on delivery drop.
- Expiration: time-based penalty via `CharacterRelation.UpdateRelation`.
- V2 virtual supply: `VirtualResourceSupplier.TryFulfillOrder` injects raw resources from `CommunityData.ResourcePools`.

**Non-responsibilities**:
- Does **not** execute physical movement — `JobTransporter` + GOAP actions do.
- Does **not** own `CraftingStation`s — see `[[crafting-loop]]`.
- Does **not** own task blackboard — see `[[building-task-manager]]`.

## The 5 lists

| List | Meaning |
|---|---|
| `_activeOrders` (`List<BuyOrder>`) | Requests received from **other** clients. We are the supplier. |
| `_placedBuyOrders` (`List<BuyOrder>`) | Requests **we** sent to suppliers. We are the client. Counts as virtual stock. |
| `_placedTransportOrders` (`List<TransportOrder>`) | Delivery orders we sent to a `TransporterBuilding`. |
| `_activeCraftingOrders` (`List<CraftingOrder>`) | Internal requests for our `JobCrafter`s. |
| `_pendingOrders` (`Queue<PendingOrder>`) | Physical "to-do" list for `GoapAction_PlaceOrder` — walk there, handshake. |

## Public API

- `logistics.CheckShopInventory()` — audits virtual stock vs `Items To Sell`.
- `logistics.FindSupplierFor(ItemSO)` — returns best candidate supplier building.
- `logistics.EnqueueBuyOrder(item, qty)` — adds to `_placedBuyOrders` + `_pendingOrders`.
- `logistics.ProcessActiveBuyOrders()` — dispatch phase (creates `TransportOrder` or `CraftingOrder`).
- `logistics.AcknowledgeDeliveryProgress(transportOrder)` — called on delivery drop.
- `logistics.CancelBuyOrder(BuyOrder)` — cascades to counterparty.
- `logistics.OnWorkerPunchIn` / `OnNewDay` — event-driven triggers.

## Data flow

Full cycle (shop needs item):
```
OnWorkerPunchIn fires on JobLogisticsManager arriving at work
       │
       ▼
CheckShopInventory — virtual = physical + _placedBuyOrders
       │
       ▼
For each understock:
       │
       ▼
EnqueueBuyOrder — add to _placedBuyOrders, add PendingOrder
       │
       ▼
JobLogisticsManager GOAP pops PendingOrder → GoapAction_PlaceOrder
       │
       ▼
Walks to supplier, initiates InteractionPlaceOrder
       │
       ├── Success ──► supplier._activeOrders += order (IsPlaced = true)
       └── Fail   ──► PendingOrder stays, retry later (IsPlaced false)
       │
       ▼
Supplier.ProcessActiveBuyOrders
       │
       ├── Has stock? ──► create TransportOrder, add to _placedTransportOrders
       └── No stock? ──► create internal CraftingOrder, add to _activeCraftingOrders
                                    │
                                    ▼
                              JobCrafter picks up via BT, produces ItemInstance
       │
       ▼
JobTransporter moves items → NotifyDeliveryProgress
       │
       ▼
Supplier.AcknowledgeDeliveryProgress — remove TransportOrder from _placedTransportOrders
       │
       ▼
Client receives items → remove from _placedBuyOrders
```

## Cancellation cascade

```
CancelBuyOrder(order)
       │
       ├── Remove from my side (_activeOrders or _placedBuyOrders)
       ├── Cascade to counterparty: remove their mirror entry
       └── Drop any linked pending TransportOrder safely
```

Skipping the cascade leaves one side waiting for something the other already forgot. Always use the public `CancelBuyOrder`, never manually `Remove`.

## Known gotchas

- **Virtual stock = physical + placed** — reading only physical over-orders. Use `CheckShopInventory` helper.
- **Physical handshake is mandatory** — orders are **not** live until `InteractionPlaceOrder` returns success. Retry if target busy.
- **Duplicate check before enqueue** — always scan `_placedBuyOrders` / `_placedTransportOrders` before creating a new one.
- **In-flight tracking is global** — use `InTransitQuantity` globally (not per-transporter) to avoid over-delivery.
- **Cancellation must cascade** — never raw-`.Remove` from either list.
- **Reputation penalty on expiration** — expired `BuyOrder` calls `UpdateRelation(client, negative)` on the supplier side. Multiple expirations ruin the supplier's reputation.
- **VirtualResourceSupplier** — when the supplier is a `VirtualResourceSupplier`, `TryFulfillOrder` dynamically creates `ItemInstance`s from `CommunityData.ResourcePools` in the same frame.

## Dependencies

### Upstream
- [[building]] — attaches to `CommercialBuilding` as a component.
- [[character]] — `InteractionPlaceOrder` uses character interaction.
- [[items]] — orders move `ItemInstance`s.

### Downstream
- [[shops]] — consumes `ProcessActiveBuyOrders` for shop restock.
- `[[crafting-loop]]` — `_activeCraftingOrders` feeds `JobCrafter`.
- `[[job-roles]]` — `JobTransporter` fulfills `TransportOrder`s.
- [[character-relation]] — expiration penalties.

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md)
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §5.
- [[jobs-and-logistics]] parent.
