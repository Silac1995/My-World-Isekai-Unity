---
type: system
title: "Shops"
tags: [shops, economy, commerce, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[building]]"
  - "[[jobs-and-logistics]]"
  - "[[items]]"
  - "[[character]]"
  - "[[ai]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - item-inventory-specialist
  - npc-ai-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on:
  - "[[building]]"
  - "[[jobs-and-logistics]]"
  - "[[items]]"
  - "[[character]]"
  - "[[ai]]"
depended_on_by:
  - "[[world]]"
---

# Shops

## Summary
A `ShopBuilding` inherits from `CommercialBuilding` (see [[building]]) and adds three business features: a list of `Items To Sell`, a physical `Inventory` (`List<ItemInstance>`), and a `Customer Queue` (`Queue<Character>`). One or more `JobLogisticsManager` workers restock by placing `BuyOrder`s; one or more `JobVendor` workers handle sales via `InteractionBuyItem`. Customers are NPCs driven by needs (`NeedItem`, `NeedFood`), who path to a shop, join the queue if all vendors are busy, and wait via `WaitInQueueBehaviour`.

## Purpose
Close the economic loop — items crafted in workshops end up on shelves players and NPCs actually buy from. The shop is the interface where need-driven behaviour (hunger, wardrobe, equipment replacement) meets supply-chain output.

## Responsibilities
- Holding the list of tradable `ItemSO`s per shop (`Items To Sell`).
- Maintaining physical inventory via logistics (delegated to [[jobs-and-logistics]]).
- Managing the customer queue (FIFO, with one "active" customer per vendor).
- Gating entry — customers can only buy when a vendor is available.
- Running vendor workflow: call next customer → wait for `InteractionBuyItem` → transfer item + money.
- Clearing the queue on last-vendor shift end (kick remaining customers).
- Driving customer-side behaviour: join queue, wait, approach vendor, execute buy interaction.

**Non-responsibilities**:
- Does **not** own restock logistics — see [[jobs-and-logistics]] (`BuildingLogisticsManager`, `BuyOrder`, `CraftingOrder`, `TransportOrder`).
- Does **not** own item data — see [[items]].
- Does **not** own pricing tables — assumed on `ItemSO` (confirm).

## Key classes / files

| File | Role |
|------|------|
| `Assets/Scripts/World/Buildings/ShopBuilding.cs` | `CommercialBuilding` subclass. Owns `Items To Sell`, `Inventory`, `Customer Queue`. `JoinQueue`, `CallNextCustomer`. |
| `Assets/Scripts/World/Jobs/JobVendor.cs` | Vendor role. Constantly checks the queue, calls customers, handles `WorkerEndingShift` kick-out. |
| `Assets/Scripts/World/Jobs/JobLogisticsManager.cs` | Restock worker. See [[jobs-and-logistics]]. |
| `Assets/Scripts/Character/CharacterInteraction/InteractionBuyItem.cs` | The face-to-face sale; deducts money, transfers `ItemInstance`. |
| `Assets/Scripts/AI/Behaviours/WaitInQueueBehaviour.cs` | Customer queue-waiting behaviour. |

## Public API / entry points

Customer side:
- `ShopBuilding.CanServeNow(customer)` — "any vendor free right now?"
- `ShopBuilding.JoinQueue(customer)`.
- `ShopBuilding.LeaveQueue(customer)`.

Vendor side:
- `ShopBuilding.CallNextCustomer()` — pops queue, assigns to self.
- `ShopBuilding.ClearQueue()` — shift-end kick; vendor announces closure.

Sale:
- `InteractionBuyItem` action — initiated by customer on vendor. Server-authoritative money + inventory mutation.

Restock (delegates):
- `BuildingLogisticsManager.EnqueueBuyOrder(item, qty)` — see [[jobs-and-logistics]].

## Data flow

Restock:
```
OnNewDay or OnWorkerPunchIn
    │
    ▼
JobLogisticsManager reviews _inventory vs Items To Sell
    │
    ▼
EnqueueBuyOrder for each understock
    │
    ├── See jobs-and-logistics.md for rest of pipeline
    │
    ▼
Eventually: JobTransporter drops ItemInstance into _inventory
```

Sale:
```
Customer's NeedItem need fires
    │
    ▼
GOAP pathfinds to nearest ShopBuilding selling the item
    │
    ▼
ShopBuilding.CanServeNow(customer)?
    │
    ├── Yes ──► walks straight to free vendor
    │             │
    │             ▼
    │        InteractionBuyItem
    │             │
    │             ├── Deduct gold
    │             ├── Transfer ItemInstance to customer Inventory
    │             └── Remove from shop _inventory
    │
    └── No ──► JoinQueue → WaitInQueueBehaviour
                    │
                    ▼
                CallNextCustomer eventually triggers walk + buy
```

Closure:
```
Last vendor's shift ends (WorkerEndingShift)
    │
    ▼
ShopBuilding.ClearQueue()
    │
    ▼
Every queued customer: emit "shop is closed" notification → leave
```

## Dependencies

### Upstream
- [[building]] — `ShopBuilding : CommercialBuilding`; inherits all building infrastructure.
- [[jobs-and-logistics]] — restock, logistics manager, vendor employment, transporter delivery.
- [[items]] — `ItemSO`, `ItemInstance`, inventory data.
- [[character]] — customer Inventory, money; `InteractionBuyItem` action.
- [[ai]] — customer GOAP (`NeedItem` → shopping behaviour) + `WaitInQueueBehaviour`.

### Downstream
- [[world]] — shops contribute to community-level economic evaluation (indirect).

## State & persistence

- `Items To Sell`, `Inventory`, `Customer Queue`, vendor employment state — all save via map save data (see [[save-load]]).
- Queue is rebuilt from scratch on load (no in-flight customers persist). Active `InteractionBuyItem` cancels on session boundary.

## Known gotchas / edge cases

- **Empty shelves = broken chain** — if shelves are empty, debug the full logistics chain (see [[jobs-and-logistics]] §Flow Priority checklist).
- **Queue FIFO strictness** — a customer can't skip the queue by "just approaching" a vendor; `InteractionBuyItem` will reject if the customer hasn't been called.
- **Closure edge case** — multiple vendors shift-ending simultaneously should not double-clear. `ClearQueue` is gated to "last vendor leaving".
- **Vendor "trapped" at counter** — intentional; they shouldn't wander away if customers are queuing.
- **Need-driven customers don't know about specific shops** — they only know "a shop selling X". First-come-first-served pathing can cluster to one shop.

## Open questions / TODO

- [ ] Pricing model location — assumed on `ItemSO` or per-shop modifier. Confirm. Tracked in [[TODO-docs]].
- [ ] Per-character gold balance source — is it on `CharacterEquipment` (coin purse?) or a standalone `CharacterWallet`? Needs enumeration.

## Child sub-pages (to be written in Batch 2)

- [[shop-building]] — `ShopBuilding` class, Items To Sell, Inventory.
- [[shop-queue]] — `Customer Queue`, `WaitInQueueBehaviour`, `ClearQueue` kick.
- [[shop-vendor]] — `JobVendor`, sale execution, shift handling.
- [[shop-customer-ai]] — need-driven shopping, GOAP wiring.

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md)
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- `Assets/Scripts/World/Buildings/ShopBuilding.cs` (inferred path).
- `Assets/Scripts/World/Jobs/JobVendor.cs` (inferred path).
- 2026-04-18 conversation with [[kevin]].
