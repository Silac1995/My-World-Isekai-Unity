---
type: system
title: "Commercial Treasury"
tags: [building, furniture, currency, logistics, network, tier-2]
created: 2026-05-09
updated: 2026-05-17b
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
  - "[[player-hud]]"
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

// Safe role taxonomy + owner mutator pair (Phase 1.7 — 2026-05-17b).
// Mirror of the storage-role pair (TrySetStorageRoleServerRpc / DoSetStorageRole /
// TrySetStorageRoleServer). Both the player UI ServerRpc and the NPC shift-punch
// auto-assign (BuildingLogisticsManager.AssignSafeRolesForShift) converge through
// DoSetSafeRole — any future side-effect added there lands on both code paths.
public virtual IReadOnlyList<SafeRoleDescriptor> SupportedSafeRoles { get; } // default: SafeRoleCatalog.Generic
[ServerRpc(RequireOwnership=false)]
public void TrySetSafeRoleServerRpc(NetworkObjectReference furnitureRef, SafeRoleType newRole, ServerRpcParams p = default);
private void DoSetSafeRole(SafeFurniture safe, SafeRoleType newRole);                 // canonical server-only mutator
internal bool TrySetSafeRoleServer(SafeFurniture safe, SafeRoleType newRole);          // non-RPC entry for AssignSafeRolesForShift
```

**Auto-assign (`BuildingLogisticsManager`):**

```csharp
public void AssignSafeRolesForShift(); // sibling of AssignStorageRolesForShift; flips None safes to Treasury
```

**B2B preference scan (`LogisticsStockEvaluator`):**

```csharp
private bool TryB2BPurchaseFromShop(ItemSO itemSO, int quantityToOrder); // called from RequestStock
```

**Player UI surface — added 2026-05-16:**

The deposit/withdraw player UI is the human-facing path into Treasury safes. Lives entirely in the [[player-hud]] system but the gameplay effect routes through this system.

```csharp
// SafeFurniture — added 2026-05-16 (UI integration)
public override bool OnInteract(Character interactor); // owning-player gate, opens panel via PlayerUI
public override List<InteractionOption> GetExtraInteractionOptions(Character interactor); // hold-E menu "Open Safe" verb
public SafeFurnitureNetworkSync NetSync { get; } // lazy getter — used by actions to fire result toasts

// SafeFurnitureNetworkSync — added 2026-05-16
[ServerRpc(RequireOwnership=false)]
public void RequestDepositServerRpc (NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p);
[ServerRpc(RequireOwnership=false)]
public void RequestWithdrawServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p);
public void NotifyOperationResult(ulong targetClientId, bool success, string reason); // -> targeted ClientRpc
```

**Anti-cheat validation chain** in both ServerRpcs (in order): `characterRef.TryGet` resolve → `character.OwnerClientId == p.Receive.SenderClientId` (ownership) → `InteractableObject.IsCharacterInInteractionZone(character)` (rule #36 proximity) → `amount > 0` → queue the CharacterAction. Each failure fires a targeted `OperationResultClientRpc` with a wire-format reason (`out-of-zone` / `invalid-amount` from the RPC, `insufficient-wallet` / `insufficient-safe` from the action).

**Player↔NPC parity (rule #22)** — both directions go through new `CharacterAction`s, not raw RPC mutations:

```csharp
public sealed class CharacterAction_DepositToSafe  : CharacterAction  // wallet.RemoveCoins, then safe.Credit (atomic)
public sealed class CharacterAction_WithdrawFromSafe : CharacterAction // safe.TryDebit,  then wallet.AddCoins (atomic)
```

The player UI queues these via `character.CharacterActions.ExecuteAction(...)` from the ServerRpc body; a future NPC banker / treasurer / pickpocket AI queues the exact same actions from its GOAP / BT layer. The atomic guard (wallet/safe debit FIRST, return false → no opposite-side credit) prevents the "wallet debited but safe never credited" hazard. v1 behavior: **clamp-on-submit** — if the user types more than the source balance, the action receives `Mathf.Min(typed, sourceBalance)` (Stardew-style — see commit `7f0e6097`).

**HUD panel** ([[player-hud]] variants):
- `Assets/Scripts/UI/Furniture/UI_SafePanel.cs` — variant of [[player-hud]] `UI_WindowBase`. Subscribes to `SafeFurniture.OnBalanceChanged` + `CharacterWallet.OnBalanceChanged` for live repaint. Auto-closes on out-of-zone (1Hz `IsCharacterInInteractionZone` poll), ESC, target despawn.
- `Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs` — one row per `CurrencyId`. Single amount input + MAX + Deposit + Withdraw. Forward-compat for multi-currency Kingdom system (renders one row per currency in `safe.Balances`).
- Asset locations: `Assets/UI/Player HUD/UI_SafePanel.prefab` (Variant of `UI_WindowBase.prefab` per rule #39), `Assets/UI/Player HUD/UI_SafeCurrencyRow.prefab` (leaf).

**Owner UI — added 2026-05-17b (Phase 1.7):**

The owner-facing role editor lives inside the existing [[commercial-storage-roles|StorageRolesTab]] — a unified Storages tab that already surfaces every `StorageFurniture` child with a per-storage role dropdown. Phase 1.7 extends that same tab with a parallel **Safes** section so a single panel exposes both furniture-type role taxonomies side by side.

```
StorageRolesTab.prefab tree (post-Phase-1.7):
  StoragesHeader        — TMP label "Storages"
  RowsParent            — VerticalLayoutGroup, holds StorageRolesTabRow instances
  EmptyStateLabel       — "Place a storage furniture inside the building to assign roles."
  SafesHeader           — TMP label "Safes"
  SafesRowsParent       — VerticalLayoutGroup, holds StorageRolesTabSafeRow instances
  SafesEmptyStateLabel  — "Place a Safe Base furniture inside to track treasury."
```

```csharp
// Per-safe row, mirror of StorageRolesTabRow (lives at Assets/Scripts/UI/Management/StorageRolesTabSafeRow.cs).
public sealed class StorageRolesTabSafeRow : MonoBehaviour
{
    public void Bind(CommercialBuilding building, SafeFurniture safe,
                     IReadOnlyList<SafeRoleDescriptor> supportedRoles);
}
```

- Row label format: `"{SafeName}    {Role}: {amount} {Currency}, …"` (e.g. `Safe (north)    Treasury: 120 Coin`). Empty balance shows `"Treasury: empty"` so the owner can see whether a safe is contributing zero or carries funds.
- Subscribes to **both** `SafeFurniture.OnRoleChanged` AND `SafeFurniture.OnBalanceChanged` per safe — deposits / withdraws / B2B debits / refunds all repaint the row without a full tab rebuild.
- Dropdown change fires `CommercialBuilding.TrySetSafeRoleServerRpc` (owner-gated server-side). Optimistic UI is corrected via the `OnRoleChanged` round-trip — rejected calls revert visually because the authoritative `_networkRole` doesn't change.
- View subscribes to `CommercialBuilding.OnTreasuryChanged` for the full-section rebuild. The aggregate event fires on every peer through the existing per-safe fan-out chain (`_networkRole.OnValueChanged` / `_networkBalances.OnListChanged` → `safe.OnRoleChanged` / `OnBalanceChanged` → `HandleSafeRoleChanged` / `HandleSafeBalanceChanged` → `OnTreasuryChanged`).

**Convergence with the NPC path** (2026-05-17b): `BuildingLogisticsManager.AssignSafeRolesForShift` now routes through `building.TrySetSafeRoleServer` → `DoSetSafeRole` so any future side-effect (cache invalidation, audit log, broadcast event) added to the canonical mutator lands on both the player UI path AND the NPC shift-punch path. Same divergence-proof pattern shipped 2026-05-14b for the storage-role pair.

**Assets:** `Assets/Resources/UI/Management/StorageRolesSafeRow.prefab` (row prefab), `Assets/Resources/UI/Management/StorageRolesTab.prefab` (tab — extended with the Safes section).

**Dev-mode mirror:** the host-only `BuildingConsoleManagementSubTab` (Inspect → `[DEV] Console Management`) now also exposes a **Safes** section between Storage Roles and Catalog. One row per safe (current role + per-currency balance + a small button per supported role) plus a header line showing the aggregate Treasury in `CurrencyId.Default` across the building. Buttons fire `CommercialBuilding.DevForceSetSafeRole(SafeFurniture, SafeRoleType)` — wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, gated by `DevAssertHostAndDevMode`, routes through the canonical `DoSetSafeRole` helper so dev / player UI / NPC shift-punch all converge on identical fan-out. Bypasses the owner gate (dev mode is host-only) but still enforces the `SupportedSafeRoles` subtype filter. See [[dev-mode]].

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

## Reputation

Per-building consequence layer that closes the loop on **failed deliveries**. Authored 2026-05-16 — see [[commercial-building]] for the field shape.

- **`CommercialBuilding._reputation : NetworkVariable<int>`** — replicated, 0–100, default 50 (`ReputationDefault`). Inspector seed `_initialReputation`. Mutated server-only via `TryChangeReputation(delta, reason)` (clamps + logs); event `OnReputationChanged(int oldVal, int newVal)` fans out on every peer. Constants `ReputationMin = 0`, `ReputationMax = 100`, `ReputationDefault = 50`, `ReputationB2BMinimum = 20`.
- **Persistence** — `BuildingSaveData.Reputation` (default 50 for back-compat). Captured per-building in `BuildingSaveData.FromBuilding` when the building is a `CommercialBuilding`. Restored via `MapController` → `CommercialBuilding.RestoreReputationFromSave` after the safe-content pass.
- **Events shipped today:**
  - **−5 on order expiration with undelivered units (supplier).** Applies to BOTH B2B orders (Source is `ShopBuilding`, `IsPlaced=true`) AND producer-side orders. The supplier promised delivery and the supply chain failed to honour it. Same penalty per expired order regardless of who's at fault inside the chain.
  - **−5 on order expiration with undelivered units (transporter)** *(2026-05-17)*. Walks the supplier's `_orderBook.PlacedTransportOrders` for any `!IsCompleted` TO whose `AssociatedBuyOrder == expired`, then docks each `TransportOrder.HostTransporter` (captured at dispatch time in `LogisticsTransportDispatcher.DispatchTransportOrder`). Multiple TOs from the same carrier compound — failing several runs costs more rep than failing one.
  - **+1 on full BuyOrder completion (supplier).** Fires once, the moment `BuyOrder.RecordDelivery` flips `IsCompleted` true (inside `BuildingLogisticsManager.UpdateTransportOrderProgress`). Awards the supplier (`order.AssociatedBuyOrder.Source`).
- **B2B preference gate (consumer):** `LogisticsStockEvaluator.TryB2BPurchaseFromShop` skips shops with `shop.Reputation < ReputationB2BMinimum` (20). Low-rep shops are invisible to the B2B preference scan until they recover through successful future deliveries; the buyer falls through to the producer path.
- **Not shipped (tracked as follow-ups):**
  - Reputation-driven shop sort (closest / cheapest / best-rep).
  - Customer-NPC reputation effects (visit rate, queue priority, price tolerance).
  - Reputation tooltip + display on the owner management panel.
  - Decay-over-time.
  - Hiring-pool effects (workers refusing low-rep workplaces).

## Refund-on-expiration

Atomic financial reverse for the undelivered half of an expired B2B order. Authored 2026-05-16. Lives in `BuildingLogisticsManager.TryRefundB2BExpiration`, invoked from inside the `expiredSupplierOrders` loop of `CheckExpiredBuyOrders` (which already runs on `TimeManager.OnNewDay`).

**Trigger conditions** (all must hold):
- The expired order's `Source` is **this** building, and this building is a `ShopBuilding`.
- `BuyOrder.IsPlaced == true` (the atomic B2B commit ran — money + items moved).
- `BuyOrder.Quantity > BuyOrder.DeliveredQuantity` (something didn't make it).

**Refund payment chain** (Cashier till → shop Treasury → partial accept):
1. **Tier 1 — `shop.Cashiers[0].DebitTill`** for the full `undelivered × unitPrice`. Symmetric with the commit path (till was credited at commit, debit it back). Returns `false` on insufficient funds; falls through.
2. **Tier 2 — `shop.TryDebitTreasury`** for whatever Tier 1 didn't cover. The shop's own treasury safes can absorb the shortfall.
3. **Tier 3 — partial refund + warning.** If till + treasury together can't cover the owed amount, the buyer is credited only the amount actually covered and a `LogWarning` is emitted. Money is **never printed**.

**Item return** — best-effort: `ReturnExpiredOrderItemsToShelf` walks `shop.Inventory` for the expired order's `ItemSO` and moves up to `undelivered` units back to a free sell-shelf slot. Items that don't fit (shelves full) stay in `shop.Inventory`. Items already in transit when the order expired are lost — the shop can't refund what isn't there. The financial refund covers the lost units.

**Refund unit price is re-resolved from the live catalog** at expiration time. If the shop has removed the item from its catalog between commit and expiration, refund is **skipped with a LogWarning** — financial state may drift, but the alternative (refunding at a stale committed price that's no longer canonical) is worse.

## State & persistence

- **Per-safe runtime state** lives on `SafeFurniture` (server-side dict) and the sibling `SafeFurnitureNetworkSync` (replicated `NetworkVariable` + `NetworkList`). Mirrors the StorageFurniture pattern.
- **Designer seed** is `_initialRole` (defaults to `Treasury`) + `_initialBalances` (defaults to empty). Read on first server `OnNetworkSpawn` when the network state is still default.
- **Persistence** is via `SafeFurnitureSaveEntry` on `BuildingSaveData.Safes` (default-empty for back-compat). Captured in `BuildingSaveData.FromBuilding` for every Building subtype (homes can carry safes too). Restored in `MapController.RestoreSafeContents`.
- **Per-building reputation** lives on `CommercialBuilding._reputation` (`NetworkVariable<int>`, default 50). Persisted via `BuildingSaveData.Reputation` (default 50 for back-compat). Restored after `RestoreSafeContents`.
- **B2B transaction state** is NOT persisted. A B2B BuyOrder is just a regular BuyOrder once committed (with `IsPlaced=true`, `Source` is the shop). The standard BuyOrder save/load handles it. Refund-on-expiration also runs purely from the live `_orderBook.ActiveOrders` set on `OnNewDay`; expired orders are flushed via `CancelBuyOrder` immediately after the refund + rep hit.

## Known gotchas / edge cases

- **Shop must have a hired LogisticsManager** for B2B orders to actually dispatch transporters. The shop's `BuildingLogisticsManager.ProcessActiveBuyOrders` only runs from inside `JobLogisticsManager.Execute` — no NPC, no tick, no dispatch. Same constraint as producer-side BuyOrders today.
- **Partial refund when shop's till + treasury are both empty.** `TryRefundB2BExpiration` (2026-05-16) covers the buyer atomically up to what the shop can pay; the shortfall is eaten by the buyer and logged as a `LogWarning`. The supplier still takes the full `-5` reputation hit. Owners who drain their cashier till mid-order risk this outcome — the system never prints money to cover a broke supplier.
- **Item return is best-effort.** Items still physically in `shop.Inventory` at expiration time return to a sell-shelf slot via `ReturnExpiredOrderItemsToShelf`; items already with the transporter when the order expired are lost (the courier was robbed / died / hit reachability stall). The financial refund covers the lost units; the items themselves are gone from the supply chain.
- **Refund skipped if catalog entry removed.** If the shop removed the item from its catalog between commit and expiration, the refund pass logs a warning and skips — there's no canonical unit price to refund at. Rare edge case; trade-off favours stable accounting over silently using stale prices.
- **Race between human/NPC buy and B2B commit** is handled best-effort. After `CountItemOnSellShelves` reports availability, a human customer can swoop in and buy some of the items before `MoveSellShelfItemsToShopInventory` runs. The B2B code detects the shortfall, refunds the missing units (till debit + treasury credit), and shrinks the order to what actually moved. If zero items move, the order is abandoned and the scan continues to the next shop. The atomic guard is "all-or-zero-or-partial-with-refund", not strict atomicity.
- **Cashier picked is `Cashiers[0]`** — first-in-list. No round-robin / least-balance / etc. Acceptable starting point; polish later if till accounting needs balancing.
- **Cross-map B2B is NOT supported.** Buyer and shop must share a `MapController` (compared by `MapId`). A future trade-route feature could lift this; today the scan early-exits on map mismatch.
- **B2B BuyOrders don't appear in the buyer's `GoapAction_PlaceOrder` queue.** Background commit was the locked design — the buyer NPC never walks to the shop. This is by design but may surprise debugging: a `_placedBuyOrders` entry exists without a corresponding `_pendingOrders` entry on the buyer side.
- **Treasury aggregator is server + client safe**, but `TryDebitTreasury` / `CreditTreasury` are server-only (mutators). Calling from a client logs nothing — the call silently returns without effect. Don't call these from UI code without an RPC wrapper.

## Open questions / TODO

- **Reputation-driven shop sort**: `TryB2BPurchaseFromShop` currently filters by `Reputation >= ReputationB2BMinimum` (20) but still uses first-found-wins among the qualifiers. Adding a sort by reputation (descending) gives high-rep shops a structural advantage. Cheap to add; bundled with closest/cheapest sort.
- **Customer-NPC reputation effects**: visit rate, queue priority, price tolerance. Bigger feature — needs design for how customer AI consumes the score.
- **Reputation UI**: tooltip on shop hover + permanent slot on the owner management panel. Designer pass.
- **Decay-over-time**: today reputation only changes on order events. A daily decay (e.g. drift towards 50) would prevent permanent stigma. Optional — measure first.
- **Hiring-pool reputation gate**: workers refusing to apply to low-rep workplaces. Cross-cuts with [[help-wanted-and-hiring]].
- **Aggregate "Treasury" widget on the management panel**: today the per-safe rows in the Safes section (Phase 1.7) show each safe's balance individually but the panel has no rollup number. Per-safe is the source of truth for B2B selection (largest-safe-first); a header chip showing `Σ Treasury = N Coin` would let the owner see at-a-glance funding pressure without summing rows mentally. Cheap to add — `GetTreasuryBalance(CurrencyId.Default)`.
- **Owner-side deposit/withdraw of building treasury**: the per-safe player UI (`UI_SafePanel`) already moves coins between wallet ↔ safe, but only through proximity interaction at the safe itself. A management-panel button (Deposit-from-wallet / Withdraw-to-wallet) would let an owner top up Treasury from anywhere they can open the management panel. Cross-cuts with rule #36 (proximity gate would need to be relaxed for this specific action — or the panel would queue the same `CharacterAction_DepositToSafe` / `CharacterAction_WithdrawFromSafe` once the owner gets near a safe).
- **Pricing logic for closest / cheapest shop**: first-found-wins is acceptable for sparse-shop builds, but with multiple shops on the same map a closer or cheaper shop should win. Sort `BuildingManager.allBuildings` by distance or unit price before the scan.
- **Owner-allied scoping**: should a building only B2B-buy from shops owned by the same Community / owner / faction? Currently any same-map shop qualifies. Locked product decision was "same-map only" but a follow-up "owner-allied" filter is an obvious extension.
- **Currency-as-item migration**: when coin stacks become `ItemInstance`s, `SafeFurniture` could merge into the regular `StorageFurniture` system (currency-typed slots). At that point this page collapses into [[commercial-storage-roles]].
- **Refunds via building Treasury rather than Cashier till**: the user-locked design is Cashier till credit (symmetric with human/NPC buys). If a future product decision flips that, the destination is `shop.Treasury` instead — single-line change in `TryB2BPurchaseFromShop`.

## Change log

- 2026-05-17c — **Dev-mode mirror of the Safes section.** `BuildingConsoleManagementSubTab` ([DEV] Console Management) gets a new Safes section between Storage Roles and Catalog: per-safe row (role + per-currency balance) + a button per supported role + an aggregate-treasury header line. New `CommercialBuilding.DevForceSetSafeRole(SafeFurniture, SafeRoleType)` host-only mutator (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`, `DevAssertHostAndDevMode`, routes through `DoSetSafeRole`). New `SafeRoleCatalog.Get(SafeRoleType)` symmetric descriptor lookup (mirror of `StorageRoleCatalog.Get`). Bypasses owner gate (dev mode is host-only) but enforces `SupportedSafeRoles` subtype filter. — claude
- 2026-05-17b — **Phase 1.7: owner-facing Safes section in the management panel.** New `CommercialBuilding.SupportedSafeRoles` virtual + `TrySetSafeRoleServerRpc` + canonical `DoSetSafeRole` helper + `TrySetSafeRoleServer` internal entry — mirror of the 2026-05-14b storage-role convergence pair. `BuildingLogisticsManager.AssignSafeRolesForShift` migrated to route through `TrySetSafeRoleServer` so the player UI path and the NPC shift-punch path share identical validation + side-effects. New `StorageRolesTabSafeRow` MonoBehaviour + `StorageRolesSafeRow.prefab` (mirrors `StorageRolesTabRow` / `StorageRolesRow.prefab`). `StorageRolesTabView` extended with a Safes section (header + rows + empty-state label) that subscribes to `OnTreasuryChanged` for repaint on every balance + role change. Row label shows per-currency balance with role prefix. Network safety: owner gate matches `TrySetStorageRoleServerRpc` exactly (`p.Receive.SenderClientId` → `ConnectedClients[…].PlayerObject.GetComponent<Character>()` → `caller == Owner` else reject), subtype filter against `SupportedSafeRoles`, late-joiner safe via existing per-safe `NetworkVariable<SafeRoleType>` + `NetworkList<BuildingTreasuryEntry>` (auto-delivered on subscription, no extra channel introduced). `StorageRolesTab.prefab` tree now has `StoragesHeader` + `SafesHeader` so the two sections read symmetrically. — claude
- 2026-05-17 — Phase C3: transporter Building reputation hit on `TransportOrder` failure. New `TransportOrder.HostTransporter` field (nullable `CommercialBuilding`) tagged at dispatch in `LogisticsTransportDispatcher.DispatchTransportOrder`. `CheckExpiredBuyOrders` now docks every `HostTransporter` whose `AssociatedBuyOrder == expired && !IsCompleted` by −5, mirroring the supplier penalty. Closes the user's original "transporter also impacted" design intent. Commit `e5b48cb8`. — claude
- 2026-05-17 — Reputation + refund-on-expiration shipped. New `CommercialBuilding._reputation` NetworkVariable + `TryChangeReputation` mutator + `OnReputationChanged` event + Inspector seed + save-load (`BuildingSaveData.Reputation`, default 50). Hooks: −5 on any expired BuyOrder with undelivered units, +1 on full BuyOrder completion via `UpdateTransportOrderProgress`. Refund-on-expiration: `BuildingLogisticsManager.TryRefundB2BExpiration` atomically debits the shop's cashier till (Tier 1) or treasury (Tier 2) by `undelivered × unitPrice` and credits the buyer's treasury — never prints money, partial refund + warning on empty till+treasury. `ReturnExpiredOrderItemsToShelf` walks `shop.Inventory` and returns matching items to a sell-shelf slot best-effort. B2B preference gate: shops with `Reputation < 20` skipped by `TryB2BPurchaseFromShop`. Transporter-Building rep penalty deferred (needs TransportOrder linkage). Commit `70e29003`. — claude
- 2026-05-16 — BaseTreasury seed source documented. Seeding fires from CommercialBuilding.OnDefaultFurnitureSpawned via SeedTreasuryIfNeeded helper; idempotent via _treasurySeeded server-only flag. — claude
- 2026-05-09 — Initial implementation. SafeFurniture + SafeFurnitureNetworkSync + SafeRoleType + SafeRoleCatalog data layer. Save/load schema (`SafeFurnitureSaveEntry`, `BuildingSaveData.Safes`, `MapController.RestoreSafeContents`). Treasury aggregator on `CommercialBuilding` (`GetTreasuryBalance` / `TryDebitTreasury` / `CreditTreasury` / `OnTreasuryChanged`). `BuildingLogisticsManager.AssignSafeRolesForShift` auto-assigns safes to Treasury on shift-punch. `LogisticsStockEvaluator.TryB2BPurchaseFromShop` scans same-map shops and atomically commits B2B purchases (debit treasury → credit shop cashier till → move items to shop inventory → IsPlaced BuyOrder). Background commit (no NPC walk). Same-map scope. — claude
- 2026-05-16 — Player UI surface shipped. Tap-E on a SafeFurniture opens `UI_SafePanel` (Variant of `UI_WindowBase.prefab` per rule #39, ScreenSpaceCamera, 560×480 frame, ScrollRect for future overflow). Per-currency rows with single amount input + MAX + Deposit + Withdraw + Stardew-style clamp-on-submit (typed > balance → submit clamps to balance). Hold-E also surfaces "Open Safe" verb via new `Furniture.GetExtraInteractionOptions` virtual + `FurnitureInteractable.GetHoldInteractionOptions` integration. Gameplay routes through new `CharacterAction_DepositToSafe` / `CharacterAction_WithdrawFromSafe` for rule #22 NPC parity; raw `safe.Credit` / `safe.TryDebit` continue serving B2B paths. Two new ServerRpcs on `SafeFurnitureNetworkSync` (`RequestDepositServerRpc` / `RequestWithdrawServerRpc`) with the rule-#36 proximity re-validation chain + targeted `OperationResultClientRpc` for failure toasts. Late-joiner replication uses existing `_networkBalances` NetworkList + `CharacterWallet` ClientRpc broadcast; rule #19b repro verified live by Kevin (host + client). Also fixed an inherited Safe.prefab `InteractionZone` BoxCollider that was disabled (rule #36 broken state — see commit `43b5daae`). — claude

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
- [Assets/Scripts/UI/Management/StorageRolesTabSafeRow.cs](../../Assets/Scripts/UI/Management/StorageRolesTabSafeRow.cs) — Phase 1.7 owner-facing per-safe row.
- [Assets/Scripts/UI/Management/StorageRolesTabView.cs](../../Assets/Scripts/UI/Management/StorageRolesTabView.cs) — Phase 1.7 Safes section host.
- 2026-05-09 conversation with [[kevin]] driving the design (Safe-as-furniture pivot, same-map scope, transporter delivery, Cashier till credit, background commit).
- 2026-05-17b session with [[kevin]] — Phase 1.7 Safes section in StorageRolesTab.
