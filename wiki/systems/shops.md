---
type: system
title: "Shops"
tags: [shops, economy, commerce, tier-1]
created: 2026-04-18
updated: 2026-05-15
sources: []
related:
  - "[[building]]"
  - "[[jobs-and-logistics]]"
  - "[[items]]"
  - "[[character]]"
  - "[[ai]]"
  - "[[player-ui]]"
  - "[[tmp-inputfield-needs-text-subtree]]"
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
  - "[[character-needs]]"
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
- [[shop-queue]] — `Customer Queue`, `WaitInQueueBehaviour`, `ClearQueue` kick. **Note: replaced by per-cashier transaction lock per the 2026-05-07 refactor.**
- [[shop-vendor]] — `JobVendor`, sale execution, shift handling. **Note: refactored to pool model per the 2026-05-07 refactor.**
- [[shop-customer-ai]] — need-driven shopping, GOAP wiring.

## Player buy UI (2026-05-13)

The player-facing buy flow lives in `MWI.UI.Shop.UI_ShopBuyPanel` (and per-row `UI_ShopBuyRow`), implemented as a [[player-ui]] HUD window — scene child of `UI_PlayerHUD/Canvas`, wired via `[SerializeField] _shopBuyPanel` on `PlayerUI`, inheriting from `UI_WindowBase` for the auto-wired close button.

**Open flow (refactored 2026-05-14)**: tap-E on a `CashierInteractable` routes through one of two paths.

1. **Seated occupant** (`Cashier.Occupant == interactor`, resolved via `CashierNetSync.OccupantNetworkObjectId`) → `CharacterActions.RequestLeaveOccupiedFurnitureServerRpc` → server `ClearCurrentAction` → the `CharacterAction_OccupyFurniture.OnCancel` fires `Leave` and releases the seat (the "vendor stepping away from the counter" path).
2. **Anyone else** → single `CashierNetSync.RequestUseCashierServerRpc(NetworkBehaviourReference customerRef)` with server-side role routing. Server checks `Cashier.IsCharacterAllowedToOccupy(customer)` (which requires `JobVendor` for the linked shop when `RequiresVendor`); if the seat is free and the gate passes → queue `CharacterAction_OccupyFurniture` (vendor path). Otherwise → queue `CharacterAction_BuyFromShop` (Player mode) + targeted `OpenBuyPanelClientRpc` → on the owning client `PlayerUI.Instance.OpenShopBuyPanel(cashier, customer)` → `_shopBuyPanel.Initialize(cashier, customer)`.

Server-side role routing is necessary because `CharacterJob._activeJobs` is not NetVar-replicated — remote-client owners can't determine their own role locally (see [[host-only-state-blindspot]] for the case study). `RequestStartBuyServerRpc` still exists as the legacy direct buy entry but is no longer used by the player UI tap-E path.

**Render contract**: the panel iterates `_shop.Catalog` (NOT `SellShelves`) to build rows. Each row's stock is aggregated across `SellShelves` matching the catalog `ItemSO`. Items present in shelves but NOT in the catalog are invisible to buyers (intentional — catalog defines what is FOR SALE). Items in catalog with no shelf stock show "0 in stock" greyed-out.

**Confirm path**: row stepper writes to `_quantities`, footer total reflects `ShopBuilding.ResolvePrice(entry) * qty`. Confirm posts `Cashier.SubmitPlayerSelectionServerRpc(payload)` → server `ApplyPlayerSelection` → next `CharacterAction_BuyFromShop.OnTick` commits (atomic transfer + wallet debit + till credit + lock release).

**Prefab paths**:
- `Assets/UI/Player HUD/UI_ShopBuyPanel.prefab` (root + Panel/HeaderRow/ScrollView/FooterRow tree, embedded under `UI_PlayerHUD/Canvas`)
- `Assets/UI/Player HUD/UI_ShopBuyRow.prefab` (catalog row template with icon, name, price, stock, +/- stepper, subtotal)

**Stale references on this page**: `InteractionBuyItem` is preserved for future character↔character trading but no longer used by `ShopBuilding`. `Customer Queue` / `JoinQueue` / `ClearQueue` are gone — see the 2026-05-07 spec for the cashier per-transaction-lock model.

## Change log
- 2026-05-15 — Hungry NPCs now buy from shops. [[character-needs]] `NeedHunger` registers a new `GoapAction_BuyFood` that scans every `ShopBuilding` for affordable `FoodSO` catalog entries, picks the best HungerRestored/price ratio, and queues `CharacterAction_BuyFromShop(BuyMode.NPC)` against a chosen `Cashier`. Same buy commit as players (cashier lock → wallet debit → till credit → sell-shelf pull → deliver). No new RPCs. Workplace-storage and ground-pickup paths are no longer the default — the ground path is now reserved for hunger emergencies (need ≥ 90%). — claude
- 2026-05-14 — Tap-E on a `CashierInteractable` rewired to two branches: seated occupant → `RequestLeaveOccupiedFurnitureServerRpc`; everyone else → new `CashierNetSync.RequestUseCashierServerRpc` with server-side role routing (vendor → `CharacterAction_OccupyFurniture`; customer → `CharacterAction_BuyFromShop`). Closes the remote-client player-vendor regression that surfaced when `Cashier.ServerTickAutoOccupy` was removed (CharacterJob._activeJobs is not NetVar-replicated). See [[shop-vendor]] + [[host-only-state-blindspot]]. — claude
- 2026-05-13 — Added Player buy UI section: `UI_ShopBuyPanel` adopts the canonical HUD-window pattern (UI_WindowBase + scene child of `UI_PlayerHUD/Canvas` + SerializeField on PlayerUI). Prefabs authored at `Assets/UI/Player HUD/`. Flagged stale queue/JobVendor references that predate the 2026-05-07 cashier refactor. — Claude / [[kevin]]
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md)
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- `Assets/Scripts/World/Buildings/ShopBuilding.cs` (inferred path).
- `Assets/Scripts/World/Jobs/JobVendor.cs` (inferred path).
- 2026-04-18 conversation with [[kevin]].
