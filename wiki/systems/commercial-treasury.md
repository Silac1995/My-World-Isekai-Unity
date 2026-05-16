---
type: system
title: "Commercial Treasury"
tags: [building, furniture, currency, logistics, network, tier-2]
created: 2026-05-09
updated: 2026-05-16
sources: []
related:
  - "[[commercial-building]]"
  - "[[commercial-storage-roles]]"
  - "[[shops]]"
  - "[[shop-building]]"
  - "[[building]]"
  - "[[building-logistics-manager]]"
  - "[[jobs-and-logistics]]"
  - "[[construction]]"
  - "[[management-panel-architecture]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - item-inventory-specialist
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on:
  - "[[commercial-building]]"
  - "[[commercial-storage-roles]]"
  - "[[shops]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
  - "[[building-logistics-manager]]"
---

# Commercial Treasury

## Summary
Per-building currency reserve that funds B2B shop purchases. Money lives on dedicated [[SafeFurniture]] instances (a parallel furniture type to [[storage-furniture|StorageFurniture]]) with a per-`CurrencyId` integer balance. Each safe carries a `SafeRoleType` (None / Treasury) — only `Treasury`-role safes contribute to the building's spendable aggregate. The `CommercialBuilding.GetTreasuryBalance` / `TryDebitTreasury` / `CreditTreasury` API is a thin aggregator over `TreasurySafes` (no NetworkList on the building itself — replication piggybacks on each safe's `SafeFurnitureNetworkSync`).

Authored 2026-05-09 as the financial backbone of the unified **B2B shop-buy logistics path**: when a building needs to restock an ingredient, the `LogisticsStockEvaluator` scans same-map [[shops|ShopBuilding]]s for inventory already on the shelf; if a shop carries the item and the buyer's treasury can afford it, the purchase commits atomically (debit treasury → credit shop cashier till → move items to shop inventory → BuyOrder.IsPlaced=true) and a transporter ships the items. The producer-based BuyOrder path is the fallback when no shop match exists.

## Purpose
Before this system, the supply chain only knew producer-to-buyer flows: when CraftingBuilding A needed wood, it routed a BuyOrder to a HarvestingBuilding that produces wood. A nearby ShopBuilding with wood already on the shelf was invisible — the buyer would post a producer order and wait for the producer's pipeline to craft / harvest fresh wood, even when an off-the-shelf alternative was sitting one street over.

Treasury closes this gap by giving every building a budget of its own, separate from the owner's [[character-wallet|CharacterWallet]] (personal) and the [[cashier|Cashier]] till (revenue accumulator). Funds flow:
- **Safe seed** — designer pre-populates `SafeFurniture._initialBalances` on the prefab.
- **BaseTreasury seed (2026-05-16)** — `BuildingCommercialSO.BaseTreasury` credits the Treasury-role safe once on construction-complete. See [Seed source](#seed-source) below + [[commercial-building#Treasury seed flow]].
- **Treasury accumulation (future)** — owner deposits via management panel; commission payments routed to treasury; etc.
- **Treasury spend** — B2B shop purchases via `LogisticsStockEvaluator.TryB2BPurchaseFromShop`.

### Seed source

`BuildingCommercialSO.BaseTreasury` (introduced 2026-05-16, see [[building#Public API / entry points]] for the BuildingSO blueprint) seeds the building's Treasury safe **once**, on construction-complete, via `CommercialBuilding.OnDefaultFurnitureSpawned` invoking the `SeedTreasuryIfNeeded()` helper. Currency is resolved at that moment from `MapController.NativeCurrency` (which reads `CommunityData.NativeCurrency`); buildings placed outside any `MapController` fall back to `CurrencyId.Default`. The seed runs in all four spawn paths: cooperative finalize (see [[construction]]), `_spawnAsComplete` designer flag, debug `BuildInstantly`, and `RestoreFromSaveData` Complete-branch. Idempotency is guaranteed by `_treasurySeeded` (server-only runtime flag) persisted to `BuildingSaveData.TreasurySeeded`. The credit itself goes through `CreditTreasury`, so multi-safe ordering follows the standard largest-safe-first selection.

The user-locked product decisions (2026-05-09):
- **Money source**: new building Treasury (not owner wallet).
- **Shop scope**: same map only.
- **Delivery**: reuse the existing transporter pipeline (no NPC walking to the shop).
- **Payment lands**: shop's Cashier till (symmetric with human / NPC personal buys).
- **Commit timing**: background — atomic at order-post time, no buyer-side `GoapAction_PlaceOrder` walk.

## Responsibilities
- Defining the safe-role taxonomy (`SafeRoleType` enum, `SafeRoleDescriptor`, `SafeRoleCatalog`).
- Holding per-safe runtime currency balance + role, replicated server-authoritatively to every peer.
- Aggregating balances across treasury-role safes for the building-level `GetTreasuryBalance` / `TryDebitTreasury` / `CreditTreasury` API.
- Atomically debiting treasury + crediting shop cashier till on B2B commit.
- Persistence across `MapController.Hibernate`/`WakeUp` and game-session reload (`BuildingSaveData.Safes`).
- Auto-assigning every unrolled safe to `Treasury` on the LogisticsManager NPC's shift-punch (mirrors the storage-role auto-assign pass).

**Non-responsibilities:**
- **Does not** model currency-as-item (coin stacks). Currency is per-CurrencyId int; a future refactor (referenced in [[cashier]] docs as "currency-as-item lands") may unify safes with regular storages. Out of scope today.
- **Does not** implement refund-on-expiration. If a B2B BuyOrder expires via `DecreaseRemainingDays` before the transporter completes delivery, the buyer's coins stay in the shop's till and items stay in shop inventory. Tracked as follow-up.
- **Does not** support cross-map B2B. Same-map only — a future "trade-route" feature could lift this.
- **Does not** prioritise shops (closest, cheapest, owner-allied). First-found wins. Pricing improvements deferred.

## Key classes / files

| File | Purpose |
|---|---|
| [Assets/Scripts/World/Furniture/SafeRoleType.cs](../../Assets/Scripts/World/Furniture/SafeRoleType.cs) | `SafeRoleType` enum + `SafeRoleDescriptor` struct + `SafeRoleCatalog.Generic` (None / Treasury). |
| [Assets/Scripts/World/Furniture/SafeFurniture.cs](../../Assets/Scripts/World/Furniture/SafeFurniture.cs) | Currency-only furniture (parallel to `StorageFurniture`). Per-`CurrencyId` balance dict + role mirror. Server mutators: `Credit`, `TryDebit`. `OnBalanceChanged` / `OnRoleChanged` events. |
| [Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs) | Sibling `NetworkBehaviour`. `NetworkVariable<SafeRoleType>` + `NetworkList<BuildingTreasuryEntry>`. Server seeds from Inspector; clients mirror via `OnValueChanged` / `OnListChanged` drive-through to `ApplyRoleFromNetwork` / `ApplyBalancesFromNetwork`. |
| [Assets/Scripts/World/Buildings/BuildingTreasuryEntry.cs](../../Assets/Scripts/World/Buildings/BuildingTreasuryEntry.cs) | Wire-format `INetworkSerializable` struct `{ CurrencyId, Amount }`. Sibling to `CashierTillEntry`. |
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — Treasury aggregator section | `Safes`, `TreasurySafes` getters. `GetTreasuryBalance`, `CanAffordFromTreasury`, `TryDebitTreasury` (drains largest-safe-first), `CreditTreasury` (credits largest safe). `OnTreasuryChanged` event fans out per-safe events. `RefreshSafeSubscriptions` diff-subscribes from `OnNetworkSpawn`. |
| [Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs) — `AssignSafeRolesForShift` | NPC LogisticsManager auto-assigns every `Role == None` safe to `Treasury` on shift-punch. Sibling of the existing `AssignStorageRolesForShift`. |
| [Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) — `TryB2BPurchaseFromShop` | B2B preference scan. Atomically debits buyer treasury, credits shop cashier till, moves items from sell-shelves into shop inventory, registers BuyOrder with `IsPlaced=true`. Falls through to `FindSupplierFor` (producer path) on no match. |
| [Assets/Scripts/World/MapSystem/MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) — `SafeFurnitureSaveEntry` + `BuildingSaveData.Safes` | Save schema: per-safe `{ FurnitureKey, Balances, Role }`. Captured by `BuildingSaveData.FromBuilding` for every `Building` subtype (homes can hold safes). |
| [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `RestoreSafeContents` | Restores balances via `SafeFurniture.RestoreFromSaveData`; restores role via the sibling `SafeFurnitureNetworkSync.SetRoleServer`. Runs after `RestoreCashierContents`. |

## Public API / entry points

**Type catalog:**

```csharp
public enum SafeRoleType { None = 0, Treasury = 1 }
public struct SafeRoleDescriptor { public SafeRoleType Type; public string DisplayName; public string Icon; }
public static class SafeRoleCatalog {
    public static readonly SafeRoleDescriptor None, Treasury;
    public static readonly IReadOnlyList<SafeRoleDescriptor> Generic; // None, Treasury
}
```

**Safe-side (`SafeFurniture`):**

```csharp
[SerializeField] SafeRoleType _initialRole = SafeRoleType.Treasury; // designer seed
[SerializeField] List<CurrencyBalanceEntry> _initialBalances;
public SafeRoleType Role { get; }
public IReadOnlyList<BuildingTreasuryEntry> Balances { get; }
public int  GetBalance(CurrencyId currency);
public bool CanAfford(CurrencyId currency, int amount);
public bool TryDebit(CurrencyId currency, int amount, string reason);
public void Credit (CurrencyId currency, int amount, string reason);
public event Action OnBalanceChanged;
public event Action<SafeRoleType> OnRoleChanged;
```

**Replication (`SafeFurnitureNetworkSync`):**

```csharp
public void SetRoleServer(SafeRoleType newRole); // server-only mutator
```

**Building aggregator (`CommercialBuilding`):**

```csharp
public IReadOnlyList<SafeFurniture> Safes         { get; } // every safe child
public IReadOnlyList<SafeFurniture> TreasurySafes { get; } // Role == Treasury
public int  GetTreasuryBalance(CurrencyId currency);
public bool CanAffordFromTreasury(CurrencyId currency, int amount);
public bool TryDebitTreasury(CurrencyId currency, int amount, string reason); // drains largest safe first
public void CreditTreasury  (CurrencyId currency, int amount, string reason); // credits largest safe
public event Action OnTreasuryChanged;
```

**Auto-assign (`BuildingLogisticsManager`):**

```csharp
public void AssignSafeRolesForShift(); // sibling of AssignStorageRolesForShift; flips None safes to Treasury
```

**B2B preference scan (`LogisticsStockEvaluator`):**

```csharp
private bool TryB2BPurchaseFromShop(ItemSO itemSO, int quantityToOrder); // called from RequestStock
```

## Data flow

```
Designer authors a SafeFurniture inside a building prefab.
  _initialRole defaults to Treasury (pre-placed safes are usable immediately).
  _initialBalances optionally seeded.
        │
        ▼
SafeFurniture spawns.
        │
        ▼
SafeFurnitureNetworkSync.OnNetworkSpawn (server)
   ├─ seeds NetworkVariable<SafeRoleType> from _initialRole (if still None)
   ├─ subscribes to safe.OnBalanceChanged → rebuild NetworkList<BuildingTreasuryEntry>
   ├─ writes initial balances through safe.RestoreFromSaveData(_initialBalances)
   └─ subscribes _networkRole.OnValueChanged → safe.ApplyRoleFromNetwork

  (every peer)
   _networkRole.OnValueChanged → safe.ApplyRoleFromNetwork → safe.OnRoleChanged
   _networkBalances.OnListChanged → safe.ApplyBalancesFromNetwork → safe.OnBalanceChanged

  (CommercialBuilding aggregator, every peer)
   RefreshSafeSubscriptions (OnNetworkSpawn) diff-subscribes safe.OnBalanceChanged + safe.OnRoleChanged
     → OnTreasuryChanged event fans out to UI / dev tools / other consumers.


NPC LogisticsManager punches in for shift.
        │
        ▼
CommercialBuilding.WorkerStartingShift
        │
        ├─ AssignStorageRolesForShift (existing)
        └─ AssignSafeRolesForShift (NEW)
              For every safe with Role == None:
                 sync.SetRoleServer(Treasury)
                 → _networkRole.Value = Treasury (replicates)
                 → safe.OnRoleChanged → building.OnTreasuryChanged


Buyer building's stock check (LogisticsStockEvaluator.RequestStock).
        │
        ▼
TryB2BPurchaseFromShop(itemSO, qty)
   foreach ShopBuilding on same map:
     - shop has the item in catalog?
     - aggregate stock on shop.SellShelves ≥ qty?
     - totalCost = qty * ShopBuilding.ResolvePrice(catalogEntry)
     - CanAffordFromTreasury(currency, totalCost)?
     - shop has at least one Cashier?
   On first match → atomic block:
     1. _building.TryDebitTreasury(currency, totalCost, ...)
     2. cashier.CreditTill(currency, totalCost, ...)
     3. MoveSellShelfItemsToShopInventory — RemoveItem from shelf + AddToInventory on shop
     4. new BuyOrder(itemSO, qty, shop, _building, ...); buyOrder.IsPlaced = true
     5. _orderBook.AddPlacedBuyOrder(buyOrder)        // buyer side tracking
        shop.LogisticsManager.OrderBook.AddActiveOrder(buyOrder) // shop side (dispatch)
   Return true → RequestStock short-circuits before FindSupplierFor.


Shop's NPC LogisticsManager ticks (JobLogisticsManager.Execute).
        │
        ▼
shop.LogisticsManager.ProcessActiveBuyOrders
   sees the new B2B BuyOrder in _activeOrders
   reads shop.Inventory → finds the items we just moved in
   creates TransportOrder, dispatches a transporter via TransporterBuilding
   transporter walks to shop → picks up reserved items → walks to buyer
   transporter delivers → buyer.AddToInventory → BuyOrder.RecordDelivery → IsCompleted
```

## Dependencies

### Upstream
- [[storage-furniture]] — `SafeFurniture` mirrors the role-replication structure.
- [[commercial-building]] — owns the Treasury aggregator + the auto-assign call site.
- [[building-logistics-manager]] — owns `AssignSafeRolesForShift` + the B2B preference scan in the StockEvaluator.
- [[shops]] / [[shop-building]] — provides `SellShelves`, `GetCatalogEntry`, `Cashiers`, `ResolvePrice` consumed by the B2B scan.
- [[network-architecture]] — server-authoritative `NetworkVariable<SafeRoleType>` + `NetworkList<BuildingTreasuryEntry>` replication.
- [[save-load]] — `SafeFurnitureSaveEntry` on `BuildingSaveData.Safes`.

### Downstream
- [[jobs-and-logistics]] — the producer-path BuyOrder fallback is unchanged; the B2B preference is opt-in at the building's call site (`RequestStock`).
- [[building-logistics-manager]] — same dispatcher handles shop-source BuyOrders identically to producer-source (because items live in shop's `Inventory` after commit).

## State & persistence

- **Per-safe runtime state** lives on `SafeFurniture` (server-side dict) and the sibling `SafeFurnitureNetworkSync` (replicated `NetworkVariable` + `NetworkList`). Mirrors the StorageFurniture pattern.
- **Designer seed** is `_initialRole` (defaults to `Treasury`) + `_initialBalances` (defaults to empty). Read on first server `OnNetworkSpawn` when the network state is still default.
- **Persistence** is via `SafeFurnitureSaveEntry` on `BuildingSaveData.Safes` (default-empty for back-compat). Captured in `BuildingSaveData.FromBuilding` for every Building subtype (homes can carry safes too). Restored in `MapController.RestoreSafeContents`.
- **B2B transaction state** is NOT persisted. A B2B BuyOrder is just a regular BuyOrder once committed (with `IsPlaced=true`, `Source` is the shop). The standard BuyOrder save/load handles it.

## Known gotchas / edge cases

- **Shop must have a hired LogisticsManager** for B2B orders to actually dispatch transporters. The shop's `BuildingLogisticsManager.ProcessActiveBuyOrders` only runs from inside `JobLogisticsManager.Execute` — no NPC, no tick, no dispatch. Same constraint as producer-side BuyOrders today.
- **No refund-on-expiration.** If a B2B BuyOrder expires via `DecreaseRemainingDays` before the transporter delivers, the coins stay in the shop's cashier till and the items stay in the shop's inventory. The buyer building loses the money and never receives the goods. Acceptable for MVP — flagged as the highest-priority follow-up.
- **Race between human/NPC buy and B2B commit** is handled best-effort. After `CountItemOnSellShelves` reports availability, a human customer can swoop in and buy some of the items before `MoveSellShelfItemsToShopInventory` runs. The B2B code detects the shortfall, refunds the missing units (till debit + treasury credit), and shrinks the order to what actually moved. If zero items move, the order is abandoned and the scan continues to the next shop. The atomic guard is "all-or-zero-or-partial-with-refund", not strict atomicity.
- **Cashier picked is `Cashiers[0]`** — first-in-list. No round-robin / least-balance / etc. Acceptable starting point; polish later if till accounting needs balancing.
- **Cross-map B2B is NOT supported.** Buyer and shop must share a `MapController` (compared by `MapId`). A future trade-route feature could lift this; today the scan early-exits on map mismatch.
- **B2B BuyOrders don't appear in the buyer's `GoapAction_PlaceOrder` queue.** Background commit was the locked design — the buyer NPC never walks to the shop. This is by design but may surprise debugging: a `_placedBuyOrders` entry exists without a corresponding `_pendingOrders` entry on the buyer side.
- **Treasury aggregator is server + client safe**, but `TryDebitTreasury` / `CreditTreasury` are server-only (mutators). Calling from a client logs nothing — the call silently returns without effect. Don't call these from UI code without an RPC wrapper.

## Open questions / TODO

- **Refund-on-expiration**: extend `BuildingLogisticsManager.CheckExpiredOrders` (subscribed on `TimeManager.OnNewDay`) to detect expired B2B BuyOrders and atomically reverse the till credit + treasury debit + return items to sell-shelves. Highest-priority follow-up.
- **Management UI for Treasury balance + deposit/withdraw**: today the owner has no in-game way to see the treasury or move funds. Phase 1.7 (Safes section in StorageRolesTab) is deferred to a designer pass; a separate "Treasury" widget on the management panel would surface the aggregate balance + buttons to deposit from owner wallet / withdraw to owner wallet.
- **Pricing logic for closest / cheapest shop**: first-found-wins is acceptable for sparse-shop builds, but with multiple shops on the same map a closer or cheaper shop should win. Sort `BuildingManager.allBuildings` by distance or unit price before the scan.
- **Owner-allied scoping**: should a building only B2B-buy from shops owned by the same Community / owner / faction? Currently any same-map shop qualifies. Locked product decision was "same-map only" but a follow-up "owner-allied" filter is an obvious extension.
- **Currency-as-item migration**: when coin stacks become `ItemInstance`s, `SafeFurniture` could merge into the regular `StorageFurniture` system (currency-typed slots). At that point this page collapses into [[commercial-storage-roles]].
- **Refunds via building Treasury rather than Cashier till**: the user-locked design is Cashier till credit (symmetric with human/NPC buys). If a future product decision flips that, the destination is `shop.Treasury` instead — single-line change in `TryB2BPurchaseFromShop`.

## Change log

- 2026-05-16 — BaseTreasury seed source documented. Seeding fires from CommercialBuilding.OnDefaultFurnitureSpawned via SeedTreasuryIfNeeded helper; idempotent via _treasurySeeded server-only flag. — claude
- 2026-05-09 — Initial implementation. SafeFurniture + SafeFurnitureNetworkSync + SafeRoleType + SafeRoleCatalog data layer. Save/load schema (`SafeFurnitureSaveEntry`, `BuildingSaveData.Safes`, `MapController.RestoreSafeContents`). Treasury aggregator on `CommercialBuilding` (`GetTreasuryBalance` / `TryDebitTreasury` / `CreditTreasury` / `OnTreasuryChanged`). `BuildingLogisticsManager.AssignSafeRolesForShift` auto-assigns safes to Treasury on shift-punch. `LogisticsStockEvaluator.TryB2BPurchaseFromShop` scans same-map shops and atomically commits B2B purchases (debit treasury → credit shop cashier till → move items to shop inventory → IsPlaced BuyOrder). Background commit (no NPC walk). Same-map scope. — claude

## Sources

- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md)
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md)
- [Assets/Scripts/World/Furniture/SafeRoleType.cs](../../Assets/Scripts/World/Furniture/SafeRoleType.cs)
- [Assets/Scripts/World/Furniture/SafeFurniture.cs](../../Assets/Scripts/World/Furniture/SafeFurniture.cs)
- [Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs)
- [Assets/Scripts/World/Buildings/BuildingTreasuryEntry.cs](../../Assets/Scripts/World/Buildings/BuildingTreasuryEntry.cs)
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — Treasury aggregator section.
- [Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs) — `AssignSafeRolesForShift`.
- [Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) — `TryB2BPurchaseFromShop`.
- 2026-05-09 conversation with [[kevin]] driving the design (Safe-as-furniture pivot, same-map scope, transporter delivery, Cashier till credit, background commit).
