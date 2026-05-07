# Shop sell / buy system — design

> Phase 2b of the Living World autonomy track. Sibling to Phase 2a (NPC building
> autonomy), which is specced and waiting on shops to ship before its Tier-1
> sourcing path is wired up.

**Status:** brainstorming complete; ready for `superpowers:writing-plans`.

**Author:** generated through guided brainstorming, 2026-05-07.

**Out-of-scope feature briefs spawned by this session** (each goes to its own
parallel session — see [Section 9](#section-9--migration-deletion-out-of-scope)):

- `docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`
- Treasury (inline brief in conversation; user spins session)
- Known-entities registry (inline brief in conversation; user spins session)

---

## Section 1 — Architecture overview

### Component map

```
ShopBuilding (extends CommercialBuilding)
  ├─ catalog: List<ShopItemEntry>           [networked + runtime-editable + saved]
  ├─ sellShelves: List<StorageFurniture>    [networked + runtime-editable + saved]
  ├─ cashiers: List<Cashier>                [auto-discovered when cashiers register]
  └─ existing inventory / logistics flows

Cashier : Furniture + sibling CashierNetSync (NetworkBehaviour)
  ├─ LinkedBuilding (CommercialBuilding) + LinkedShop convenience downcast
  ├─ Occupant (vendor — reuses Furniture.Use/Release)
  ├─ CurrentCustomer (transaction lock — networked)
  ├─ Till : Dictionary<CurrencyId,int>      [networked + saved]
  └─ _requiresVendor : bool = true           (forward-compat for auto-distributors)

CashierInteractable : InteractableObject
  ├─ Tap-E entry for customer
  └─ Server-relay → Cashier.RequestStartBuyServerRpc

CharacterAction_BuyFromShop : CharacterAction_Continuous
  ├─ Mode { NPC, Player }
  ├─ NPC branch: 2s timer → commit
  ├─ Player branch: ticks until UI Confirm posted via ServerRpc → commit
  └─ commit = transfer items from sellShelves → customer
                (CharacterEquipment.PickUpItem cascade), wallet → till, release lock

GoapAction_GoShopping (refactored)
  ├─ FindShop with desired item in catalog + ≥1 in any sellShelf
  ├─ Walk to a free cashier with vendor
  └─ Enqueue CharacterAction_BuyFromShop(cashier, [_desiredItem]) in NPC mode

JobVendor (refactored — pool model)
  ├─ Picks any free cashier in shop on each shift tick
  ├─ Reserve → walk → Use; race-friendly (loser falls through to next cashier)
  └─ Releases on shift end / cashier removal / mid-transaction abort

Management Panel (consumes parallel-session refactor — see Section 6)
  ├─ Hiring tab (existing functionality, surfaces "Vendor (n of N)" slots)
  ├─ Catalog tab (add / edit / remove; price 0 = use BasePrice)
  ├─ Shelves tab (toggle per-StorageFurniture: is sell-shelf?)
  └─ Cashiers tab (per-cashier till + Withdraw → wallet, future: → Treasury)
```

### Server-authority boundary (rule #18)

Every mutation crosses this fence: customer client → `CashierInteractable.Interact`
→ `RequestStartBuyServerRpc` → server constructs `CharacterAction_BuyFromShop`
→ server-tick `OnTick` → server-only commit. Wallet, till, sell-shelf inventory,
lock state, catalog all replicated to clients via existing NetworkList +
new NetworkVariables. UI on each client reads replicated state, never
authoritative.

### Existing-code disposition

- **`InteractionBuyItem.cs`** — preserved untouched; future character↔character
  trading. Not used for shop buys.
- **`ShopBuilding.JoinQueue / GetNextCustomer / ClearQueue`** — deleted (the
  per-cashier transaction lock replaces the central queue).
- **`ShopBuilding.VendorPoint` + `GetWorkPosition` JobVendor branch** — deleted
  (each cashier's `InteractionPoint` is the new vendor anchor).
- **`JobVendor` "call next customer" loop** — deleted (buys are
  customer-initiated).
- **`GoapAction_GoShopping`** — refactored to enqueue
  `CharacterAction_BuyFromShop` instead of `JoinQueue` + `IsInteracting` polling.

---

## Section 2 — Data model

### `ItemSO` additions

```csharp
[Header("Economy")]
[Tooltip("Default shop sell price. ShopItemEntry.PriceOverride wins when > 0.")]
[SerializeField] private int _basePrice = 0;
public int BasePrice => _basePrice;
```

### `ShopItemEntry` extension

```csharp
[Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock;
    [Tooltip("0 = use ItemSO.BasePrice")]
    public int PriceOverride;
}

// Resolver lives on ShopBuilding for cache-friendliness:
public static int ResolvePrice(ShopItemEntry e) =>
    e.PriceOverride > 0 ? e.PriceOverride : (e.Item != null ? e.Item.BasePrice : 0);
```

Owner-set price of `1` is fully supported. `0` is the "use base price" sentinel;
explicit "free items" not modelled (out of scope).

### `ShopBuilding` runtime state

```csharp
public class ShopBuilding : CommercialBuilding, IStockProvider
{
    private List<ShopItemEntry> _catalog;
    public IReadOnlyList<ShopItemEntry> Catalog => _catalog;

    private List<StorageFurniture> _sellShelves;
    public IReadOnlyList<StorageFurniture> SellShelves => _sellShelves;

    private List<Cashier> _cashiers;
    public IReadOnlyList<Cashier> Cashiers => _cashiers;

    public ShopItemEntry? GetCatalogEntry(ItemSO item) { /* O(N) scan; N small */ }
    public Cashier GetFirstAvailableCashier() { /* picks any free cashier */ }

    [ServerRpc(RequireOwnership=false)] public void AddCatalogEntryServerRpc(...);
    [ServerRpc(RequireOwnership=false)] public void EditCatalogEntryServerRpc(...);
    [ServerRpc(RequireOwnership=false)] public void RemoveCatalogEntryServerRpc(...);
    [ServerRpc(RequireOwnership=false)] public void SetSellShelfFlagServerRpc(...);
    [ServerRpc(RequireOwnership=false)] public void WithdrawCashierTillServerRpc(...);
}
```

Each ServerRpc validates `caller == _owner` server-side (rule #18). Rejected
calls fire `SendUnauthorizedToastClientRpc(senderClientId)` and log a warning.

### `Cashier` furniture

```csharp
[RequireComponent(typeof(CashierInteractable))]
[RequireComponent(typeof(CashierNetSync))]
public class Cashier : Furniture
{
    [Header("Cashier")]
    [Tooltip("If false, this is an automatic distributor — no vendor required.")]
    [SerializeField] private bool _requiresVendor = true;
    public bool RequiresVendor => _requiresVendor;

    private CommercialBuilding _linkedBuilding;
    public CommercialBuilding LinkedBuilding => _linkedBuilding;
    public ShopBuilding LinkedShop => _linkedBuilding as ShopBuilding;

    private Character _currentCustomer;
    public Character CurrentCustomer => _currentCustomer;
    public bool IsAvailableForCustomer =>
        _currentCustomer == null && (!_requiresVendor || Occupant != null);

    private Dictionary<CurrencyId, int> _till = new();
    public int GetTillBalance(CurrencyId c) =>
        _till.TryGetValue(c, out var v) ? v : 0;

    public void CreditTill(CurrencyId c, int amount, string source);  // server-only
    public bool DebitTill(CurrencyId c, int amount, string reason);   // server-only

    public bool TryAcquireCustomerLock(Character customer);            // server-only
    public void ReleaseCustomerLock(Character customer);               // server-only

    private void Awake() { _linkedBuilding = GetComponentInParent<CommercialBuilding>(); }
    private void OnEnable()  { LinkedShop?.RegisterCashier(this); }   // dynamic register
    private void OnDisable() { LinkedShop?.UnregisterCashier(this); } // dynamic unregister
}
```

### `CashierNetSync`

```csharp
public class CashierNetSync : NetworkBehaviour
{
    private NetworkVariable<ulong> _currentCustomerNetworkObjectId =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkList<CurrencyBalanceEntry> _tillBalances;

    private NetworkVariable<NetworkObjectReference> _linkedBuilding;

    [ClientRpc] private void NotifyOccupiedClientRpc(ulong vendorId);
    [ClientRpc] private void NotifyReleasedClientRpc();
    [ClientRpc] private void SendBusyToastClientRpc(ClientRpcParams p);
}
```

### `FurnitureTag`

```csharp
public enum FurnitureTag
{
    None, Bed, Cooking, Crafting, Storage, Seating, TimeClock,
    Cashier,    // NEW
}
```

No `ShopShelf` tag — sell-shelves are tracked by `ShopBuilding._sellShelves`
list (runtime-editable), not by tag.

---

## Section 3 — Cashier lifecycle, occupancy, transaction lock

### State slots (orthogonal)

| Slot | Type | Meaning | Mutated by |
|---|---|---|---|
| `Occupant` (from `Furniture`) | `Character` | Vendor currently driving the cashier | `Cashier.Use(vendor)` / `Cashier.Release()` |
| `CurrentCustomer` | `Character` | Customer mid-transaction (the lock) | `TryAcquireCustomerLock` / `ReleaseCustomerLock` |
| `Till` | `Dictionary<CurrencyId,int>` | Money held by this cashier | `CreditTill` / `DebitTill` |
| `_requiresVendor` | `bool` | Whether a vendor is needed before customers may buy | inspector-authored |

### Composite predicate

```csharp
public bool IsAvailableForCustomer =>
    _currentCustomer == null
    && (!_requiresVendor || Occupant != null);
```

### Vendor occupy / release

`Cashier` overrides `Furniture.Use` / `Release` to broadcast occupancy to all
peers:

```csharp
public override bool Use(Character vendor)
{
    if (!base.Use(vendor)) return false;
    NotifyOccupiedClientRpc(vendor.NetworkObjectId);
    return true;
}

public override void Release()
{
    base.Release();
    NotifyReleasedClientRpc();
    if (_currentCustomer != null) AbortActiveTransactionServerOnly();
}
```

### Customer lock

```csharp
public bool TryAcquireCustomerLock(Character customer)
{
    if (!IsAvailableForCustomer) return false;
    _currentCustomer = customer;
    _netSync.SetCurrentCustomerServer(customer.NetworkObjectId);
    return true;
}

public void ReleaseCustomerLock(Character customer)
{
    if (_currentCustomer != customer)
    {
        Debug.LogWarning($"[Cashier] ReleaseCustomerLock: caller {customer?.CharacterName} is not the holder ({_currentCustomer?.CharacterName}). Ignored.");
        return;
    }
    _currentCustomer = null;
    _netSync.SetCurrentCustomerServer(0);
}
```

### Lifecycle events

| Event | Effect |
|---|---|
| Cashier placed inside `ShopBuilding` | `Awake` resolves `LinkedBuilding`. `OnEnable` → `LinkedShop.RegisterCashier(this)` → adds a JobVendor slot if `_requiresVendor`. |
| Second cashier placed in same shop | Allowed (multi-cashier model). Each registers independently. |
| Cashier picked up | `OnDisable` → `LinkedShop.UnregisterCashier(this)` → removes JobVendor slot. If a customer was mid-transaction, abort. Till coins drop as `WorldItem`s. |
| Shop building destroyed | Each cashier's `OnDisable` fires; same rules apply. |
| Cashier placed outside any commercial building | `LinkedBuilding == null`; logs warning. Furniture is inert. |

### Save / load

Serialised: `till`, `_requiresVendor`, `_linkedBuildingId`. Reset on load:
`Occupant`, `_currentCustomer`. Vendor walks back on first work-shift tick.
Mid-flight transactions never persist (commit-only-at-finish).

---

## Section 4 — Vendor flow & JobVendor refactor (pool model)

### `JobVendor` core

```csharp
public class JobVendor : Job
{
    public override string JobTitle => "Vendor";
    public override JobCategory Category => JobCategory.Service;

    private Cashier _heldCashier;
    private bool _hasReserved;

    public override bool CanExecute() =>
        base.CanExecute() && _workplace is ShopBuilding;
}
```

### `Execute()` — three-phase pool model

```csharp
public override void Execute()
{
    if (_worker == null) return;

    // 1) Already occupying — idle.
    if (_heldCashier != null && _heldCashier.Occupant == _worker) return;

    // 2) Lost it (race / shift change / cashier removed) — drop and re-pick.
    if (_heldCashier != null && _heldCashier.Occupant != _worker)
    {
        if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
        _heldCashier = null;
        _hasReserved = false;
    }

    // 3) Pick a free cashier from the shop and walk to it.
    var shop = _workplace as ShopBuilding;
    if (shop == null) return;

    for (int i = 0; i < shop.Cashiers.Count; i++)
    {
        var c = shop.Cashiers[i];
        if (!c.RequiresVendor) continue;
        if (c.Occupant != null) continue;
        if (c.ReservedBy != null && c.ReservedBy != _worker) continue;
        if (!c.Reserve(_worker)) continue;
        _heldCashier = c;
        _worker.CharacterMovement.SetDestination(c.GetInteractionPosition(_worker.transform.position));
        return;
    }
    // No free cashier — vendor idles in the shop zone.
}
```

### Race semantics (locked in)

- `Furniture.Use(c)` checks only `IsOccupied`, not `_reservedBy` — reservation
  is **advisory**.
- Whoever calls `Use` first wins; loser's reservation is wiped on success.
- Loser's `JobVendor` recovery branch detects stale local state next tick and
  re-picks.

### Player-vendor proximity auto-occupy (rule #22 parity)

Player-vendors aren't driven by `JobVendor.Execute` (rule #33 — input
ownership). The cashier server-ticks at 1 Hz to auto-seat any nearby on-shift
vendor:

```csharp
private void ServerTickAutoOccupy()
{
    if (Occupant != null || _currentCustomer != null) return;

    int n = Physics.OverlapSphereNonAlloc(
        GetInteractionPosition(), _autoSeatRadius, _scratchColliders);
    for (int i = 0; i < n; i++)
    {
        var character = _scratchColliders[i].GetComponentInParent<Character>();
        if (character == null) continue;
        if (character.CharacterJob?.CurrentJob is not JobVendor jv) continue;
        if (jv.Workplace != _linkedBuilding) continue;
        if (!character.CharacterSchedule.IsOnWorkShiftNow) continue;
        Use(character);
        break;
    }
}
```

Symmetrical: NPC vendors path here under `JobVendor.Execute`; player vendors
walk here on their own input. Whoever lands on the InteractionPoint first
wins. Movement away (rule: `AllowsMovementDuringAction = false` while
occupying) → `Release()` automatically.

### `ShopBuilding` registration hooks

```csharp
public void RegisterCashier(Cashier c)
{
    if (!IsServer || _cashiers.Contains(c)) return;
    _cashiers.Add(c);
    _netSync.PushCashierAddedServer(c.NetworkObjectId);
    if (c.RequiresVendor)
    {
        _jobs.Add(new JobVendor());   // pool model — no per-cashier binding
        OnJobsChanged?.Invoke();
    }
}

public void UnregisterCashier(Cashier c)
{
    if (!IsServer) return;
    int idx = _cashiers.IndexOf(c);
    if (idx < 0) return;
    _cashiers.RemoveAt(idx);
    _netSync.PushCashierRemovedServer(c.NetworkObjectId);

    // Pool model: remove ONE generic JobVendor slot when a vendor-required
    // cashier vanishes. Existing fire flow handles the worker.
    if (c.RequiresVendor)
    {
        for (int i = _jobs.Count - 1; i >= 0; i--)
        {
            if (_jobs[i] is JobVendor jv)
            {
                jv.Unassign();
                _jobs.RemoveAt(i);
                break;
            }
        }
        OnJobsChanged?.Invoke();
    }
}
```

### Hiring tab integration

Pool model → slot label: `"Vendor (n of N)"` where N = `cashiersRequiringVendor.Count`.
Generic, not bound to a specific cashier.

### Mid-shift cashier removal

`JobVendor.Unassign` releases the worker; they drop back to default schedule
slot (idle / wander / eat / sleep). No mood penalty, no notification (per
user direction).

---

## Section 5 — Customer flow (NPC + Player)

### `CharacterAction_BuyFromShop`

```csharp
public class CharacterAction_BuyFromShop : CharacterAction_Continuous
{
    public enum BuyMode { NPC, Player }

    private readonly Cashier _cashier;
    private readonly List<ItemSO> _itemsToBuy;
    private readonly Dictionary<ItemSO,int> _quantities;
    private readonly BuyMode _mode;

    private float _elapsed;
    private bool _hasPlayerSelection;
    private bool _commitDone;
    private const float NPC_DURATION = 2f;
    private const float SENTINEL_TIMEOUT = 600f;

    public override bool CanExecute()
    {
        if (_cashier == null || _cashier.LinkedShop == null) return false;
        if (!_cashier.IsAvailableForCustomer) return false;
        return true;
    }

    public override void OnStart()
    {
        if (!_cashier.TryAcquireCustomerLock(character))
        {
            Debug.LogWarning($"[BuyFromShop] {character?.CharacterName} failed to acquire lock at OnStart — aborting.");
            Finish(); return;
        }
        if (_mode == BuyMode.Player)
            OpenBuyPanelClientRpc(character.NetworkObjectId, _cashier.NetworkObjectId);
    }

    public override bool OnTick()
    {
        _elapsed += TickIntervalSeconds;
        if (_elapsed > SENTINEL_TIMEOUT) { Debug.LogWarning("[BuyFromShop] sentinel timeout — aborting."); return true; }

        if (_mode == BuyMode.NPC) return _elapsed >= NPC_DURATION && Commit();
        if (_mode == BuyMode.Player) return _hasPlayerSelection && Commit();
        return false;
    }

    internal void ApplyPlayerSelection(IReadOnlyList<(ItemSO, int)> selections)
    {
        _itemsToBuy.Clear(); _quantities.Clear();
        foreach (var (so, qty) in selections)
        {
            if (so == null || qty <= 0) continue;
            _itemsToBuy.Add(so); _quantities[so] = qty;
        }
        _hasPlayerSelection = true;
    }

    public override void OnCancel()
    {
        if (!_commitDone) RefundAndRelease("cancelled");
        if (_mode == BuyMode.Player) CloseBuyPanelClientRpc(character.NetworkObjectId);
    }
}
```

### Commit (server-only, atomic)

```csharp
private bool Commit()
{
    if (_commitDone) return true;
    var shop = _cashier.LinkedShop;
    if (shop == null) { Abort("shop missing"); return true; }

    // 1) Resolve cost from authoritative catalog.
    int totalCost = 0;
    foreach (var so in _quantities.Keys)
    {
        var entry = shop.GetCatalogEntry(so);
        if (entry == null) { Abort($"item {so.ItemName} not in catalog"); return true; }
        totalCost += ShopBuilding.ResolvePrice(entry.Value) * _quantities[so];
    }

    // 2) Affordability gate.
    if (!character.CharacterWallet.CanAfford(CurrencyId.Default, totalCost))
    {
        Abort("insufficient funds"); return true;
    }

    // 3) Pull each ItemInstance from the sell-shelves; rollback on partial failure.
    var pulled = new List<(StorageFurniture shelf, ItemInstance instance)>();
    foreach (var (so, qty) in _quantities)
    {
        for (int i = 0; i < qty; i++)
        {
            if (!TryPullFromAnyShelf(shop.SellShelves, so, out var shelf, out var instance))
            {
                RollbackPulls(pulled);
                Abort($"{so.ItemName} is no longer available — purchase cancelled"); return true;
            }
            pulled.Add((shelf, instance));
        }
    }

    // 4) Deliver each pulled ItemInstance to the customer.
    foreach (var (_, instance) in pulled)
        DeliverToCustomer(instance);

    // 5) Money: customer wallet → cashier till.
    if (!character.CharacterWallet.RemoveCoins(CurrencyId.Default, totalCost, $"ShopPurchase_{shop.buildingName}"))
    {
        RollbackPulls(pulled); Abort("wallet debit failed"); return true;
    }
    _cashier.CreditTill(CurrencyId.Default, totalCost, $"PurchaseBy_{character.CharacterName}");

    _cashier.ReleaseCustomerLock(character);
    _commitDone = true;
    return true;
}
```

### Item-resolution order

When multiple sell-shelves contain the same `ItemSO`: server picks the **first
match** in `shop.SellShelves` order (no preference rules, MVP).

### Item delivery (NPC + Player both ground-drop overflow)

```csharp
private void DeliverToCustomer(ItemInstance instance)
{
    // bag → hands cascade lives entirely inside CharacterEquipment.PickUpItem
    if (character.CharacterEquipment.PickUpItem(instance)) return;

    // Overflow — both NPC and Player drop on ground. No item destruction.
    SpawnAsWorldItemNextToCharacter(instance);
    if (_mode == BuyMode.Player)
        ToastClientRpc(character.NetworkObjectId,
            $"{instance.ItemSO.ItemName} dropped on the ground", ToastType.Info);
}
```

### Capacity gate (planning-time, SO-only)

```csharp
// IsValid + CanExecute use:
bool hasRoom = worker.CharacterEquipment.HasFreeSpaceForItemSO(_desiredItem)
            || worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
```

`HasFreeSpaceForItemSO` is bag-only (does not consider hands), so we OR with
`AreHandsFree`. Two-line check; no new helper.

### `CashierInteractable`

```csharp
public class CashierInteractable : InteractableObject
{
    private Cashier _cashier;
    void Awake() { _cashier = GetComponent<Cashier>(); }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _cashier == null) return;
        if (!IsCharacterInInteractionZone(interactor)) return;

        if (_cashier.RequiresVendor && _cashier.Occupant == null)
        {
            UI_Toast.Show("No vendor on duty.", ToastType.Warning);
            return;
        }
        if (_cashier.CurrentCustomer != null && _cashier.CurrentCustomer != interactor)
        {
            UI_Toast.Show("Shop vendor is busy with another customer.", ToastType.Warning);
            return;
        }

        _cashier.RequestStartBuyServerRpc(new NetworkBehaviourReference(interactor));
    }

    public override string GetPromptText(Character interactor)
    {
        if (_cashier.RequiresVendor && _cashier.Occupant == null) return _promptNoVendor;
        if (_cashier.CurrentCustomer != null && _cashier.CurrentCustomer != interactor) return _promptBusy;
        return _promptShop;
    }
}
```

### `UI_ShopBuyPanel` (player)

```
┌───────────────────────────────────────────────┐
│  Shop: <buildingName>          [X cancel]     │
├───────────────────────────────────────────────┤
│  [icon] Bread             8 g  [3 in stock]   │
│           [-] [ 2 ] [+]              =  16 g  │
│  [icon] Apple             3 g  [12 in stock]  │
│           [-] [ 0 ] [+]              =   0 g  │
│  …                                            │
├───────────────────────────────────────────────┤
│  Wallet: 50 g     Total: 16 g     [Confirm]   │
└───────────────────────────────────────────────┘
```

Reactive subscriptions on open:
- Each `StorageFurniture.OnInventoryChanged` for shelves in `shop.SellShelves`.
- `ShopBuilding.OnCatalogChanged` for owner-driven catalog edits.
- `CashierNetSync._currentCustomerNetworkObjectId.OnValueChanged` for server
  abort detection.

On any event: re-render, clamp each row's selected qty to `[0, stockAvailable]`,
recompute total, update Confirm enabled state. Rows whose stock drops to 0
stay visible (greyed, qty forced to 0). All subscriptions cleaned up in
`OnDestroy` (rule #16).

Confirm path: `Cashier.SubmitPlayerSelectionServerRpc(selections)` → server
validates → `_action.ApplyPlayerSelection(...)` → next OnTick commits.

Cancel path: `Cashier.CancelPlayerTransactionServerRpc()` → action
`OnCancel` → lock released, panel closed via `CloseBuyPanelClientRpc`.

Out-of-stock toast text: `"{itemName} is no longer available — purchase cancelled."`

### `GoapAction_GoShopping` refactor

```csharp
public override bool IsValid(Character worker)
{
    if (_isComplete) return false;
    if (_chosenShop != null && _chosenCashier != null)
        return _chosenCashier.IsAvailableForCustomer || _actionEnqueued;

    var shop = FindShopWithItem(_desiredItem);
    if (shop == null) return false;

    var entry = shop.GetCatalogEntry(_desiredItem);
    if (!entry.HasValue) return false;
    int price = ShopBuilding.ResolvePrice(entry.Value);
    if (!worker.CharacterWallet.CanAfford(CurrencyId.Default, price)) return false;

    bool hasRoom = worker.CharacterEquipment.HasFreeSpaceForItemSO(_desiredItem)
                || worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
    if (!hasRoom) return false;

    var cashier = shop.GetFirstAvailableCashier();
    if (cashier == null) return false;

    _chosenShop = shop; _chosenCashier = cashier;
    return true;
}

public override void Execute(Character worker)
{
    var movement = worker.CharacterMovement;
    var dest = _chosenCashier.GetInteractionPosition(worker.transform.position);
    if (Vector3.Distance(worker.transform.position, dest) > 1.5f)
    {
        if (!_isMoving) { movement.SetDestination(dest); _isMoving = true; }
        return;
    }

    if (!_actionEnqueued)
    {
        if (!_chosenCashier.IsAvailableForCustomer) { _isComplete = true; return; }

        var action = new CharacterAction_BuyFromShop(
            worker, _chosenCashier, new List<ItemSO> { _desiredItem }, CharacterAction_BuyFromShop.BuyMode.NPC);
        action.OnActionFinished += () => _actionFinished = true;
        worker.CharacterActions.EnqueueAction(action);
        _actionEnqueued = true;
    }

    if (_actionFinished) _isComplete = true;
}
```

---

## Section 6 — Management desk tabs (owner UI)

> Consumes the parallel-session refactor at
> `docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`.
> That session lands the polymorphic `CommercialBuilding.GetManagementTabs()`
> + `IManagementTab` interface + generic `UI_OwnerManagementPanel`. Phase 2b
> consumes it.

### `ShopBuilding.GetManagementTabs()` override

```csharp
public override IReadOnlyList<IManagementTab> GetManagementTabs()
{
    var tabs = new List<IManagementTab>(base.GetManagementTabs());   // brings Hiring
    tabs.Add(new ShopCatalogTab(this));
    tabs.Add(new ShopShelvesTab(this));
    tabs.Add(new ShopCashiersTab(this));
    return tabs;
}
```

No edits to `UI_OwnerManagementPanel`. Adding a new building subtype's UI is a
one-method override per subtype — Open/Closed satisfied.

### Tab 1 — Hiring (existing functionality)

Hiring slots show as `"Vendor (1 of N)"`, `"Vendor (2 of N)"`, etc. (pool
model). Owner picks an applicant for any open slot via the existing apply /
accept flow. No vendor-cashier binding at hire time.

### Tab 2 — Catalog

```
┌── Catalog ──────────────────────────────┐
│ Item             MaxStock  Price        │
│ ─────────────────────────────────────── │
│ [icon] Bread        20      8 g  [✎][✕]│
│ [icon] Apple        50      0 g  [✎][✕]│       ← 0 = use BasePrice
│                              [+ Add]    │
└─────────────────────────────────────────┘
```

- Add → opens `CatalogItemPickerDialog` (currently lists every ItemSO via
  `Resources.LoadAll<ItemSO>("Data/Item")`; future known-entities registry
  swap is a one-line change). Owner enters MaxStock + PriceOverride; submit
  fires `AddCatalogEntryServerRpc(itemId, maxStock, priceOverride)`.
- Edit (✎) → inline editor; submit fires `EditCatalogEntryServerRpc(...)`.
- Remove (✕) → confirm dialog; fires `RemoveCatalogEntryServerRpc(itemId)`.
- Price field helper text: `"0 = use base price ({basePrice} g)"` when the
  picked item has a non-zero `BasePrice`.

### Tab 3 — Shelves

```
┌── Sell-Shelves ─────────────────────────┐
│ Storage in shop:                        │
│ ─────────────────────────────────────── │
│ ☑ Shelf_North        12/16 slots used   │
│ ☑ Shelf_South         3/16 slots used   │
│ ☐ Backroom_Chest      8/40 slots used   │
└─────────────────────────────────────────┘
```

- Lists every `StorageFurniture` whose `GetComponentInParent<ShopBuilding>() == shop`.
- Checkbox toggle fires `SetSellShelfFlagServerRpc(shelfNetworkObjectRef, isSellShelf)`.
- Toggling off does NOT move shelf contents; only stops listing them in the
  buy UI.

### Tab 4 — Cashiers

```
┌── Cashiers ─────────────────────────────┐
│ Cashier         Vendor       Till       │
│ ─────────────────────────────────────── │
│ Cashier_North   Maria Smith  87 g  [↗] │
│ Cashier_South   (vacant)      0 g  [↗] │
│                                         │
│ Cashiers requiring a vendor: 2          │
│ Vendors hired: 1   /   2                │
└─────────────────────────────────────────┘
```

- Row per `Cashier` in `shop.Cashiers`. Vendor column shows
  `cashier.Occupant?.CharacterName ?? "(vacant)"`.
- Withdraw (↗): fires `WithdrawCashierTillServerRpc(cashierRef)`. Server:
  `cashier.DebitTill(CurrencyId.Default, balance, "OwnerWithdraw")` →
  `owner.CharacterWallet.AddCoins(CurrencyId.Default, balance, "FromCashier_<name>")`.
- **Future treasury session** swaps `owner.CharacterWallet.AddCoins` for
  `building.Treasury.AddCoins`. One-line redirect.

### Network safety

Every owner-only mutation is `[ServerRpc(RequireOwnership=false)]` with caller
identity validated server-side against `_owner`. Rejected calls fire
`SendUnauthorizedToastClientRpc(senderClientId)`.

---

## Section 7 — Networking & replication summary

### Authority matrix

| State | Lives on | Replicated by | Read by |
|---|---|---|---|
| `ShopBuilding._catalog` | Server | `ShopBuildingNetSync.NetworkList<ShopItemEntryNet>` | All clients |
| `ShopBuilding._sellShelves` | Server | `ShopBuildingNetSync.NetworkList<NetworkObjectReference>` | All clients |
| `ShopBuilding._cashiers` | Server | `ShopBuildingNetSync.NetworkList<NetworkObjectReference>` | All clients |
| `Cashier._linkedBuilding` | Server | `CashierNetSync._linkedBuilding : NetworkVariable<NetworkObjectReference>` | All clients |
| `Cashier.Occupant` (vendor) | Server | `CashierNetSync.NotifyOccupiedClientRpc / NotifyReleasedClientRpc` | All clients |
| `Cashier._currentCustomer` (lock) | Server | `CashierNetSync._currentCustomerNetworkObjectId : NetworkVariable<ulong>` | All clients |
| `Cashier._till` | Server | `CashierNetSync.NetworkList<CurrencyBalanceEntry>` | All clients |
| `StorageFurniture` slots (sell-shelves) | Server | Existing `StorageFurnitureNetworkSync.NetworkList<...>` | All clients |
| `CharacterWallet` balances | Server (per-character) | Existing `BroadcastBalanceChangeClientRpc` | Owner client + server |
| `CharacterAction_BuyFromShop` runtime state | Server only | Visual proxy via existing `CharacterActions.BroadcastActionVisualsClientRpc` | N/A |
| `JobVendor._heldCashier` | Server only (not networked) | Vendor's position via existing `CharacterMovement → NetworkTransform` | N/A |

### Server RPCs

| RPC | Sender | Validation | Action |
|---|---|---|---|
| `Cashier.RequestStartBuyServerRpc(customerRef)` | Customer client | Sender owns customer | Verify free → enqueue Buy action (Player mode) |
| `Cashier.SubmitPlayerSelectionServerRpc(selections)` | Customer client | Sender == `_currentCustomer` | `_action.ApplyPlayerSelection(...)` |
| `Cashier.CancelPlayerTransactionServerRpc()` | Customer client | Sender == `_currentCustomer` | Abort, release lock, close UI |
| `ShopBuilding.AddCatalogEntryServerRpc(...)` | Owner client | Sender == `_owner` | Append entry, push NetworkList |
| `ShopBuilding.EditCatalogEntryServerRpc(...)` | Owner client | Sender == `_owner` | Mutate entry |
| `ShopBuilding.RemoveCatalogEntryServerRpc(...)` | Owner client | Sender == `_owner` | Remove entry |
| `ShopBuilding.SetSellShelfFlagServerRpc(...)` | Owner client | Sender == `_owner` | Add/remove from `_sellShelves` |
| `ShopBuilding.WithdrawCashierTillServerRpc(...)` | Owner client | Sender == `_owner` | Move till → wallet (future: treasury) |

### Client RPCs

| ClientRpc | Target | Effect |
|---|---|---|
| `OpenBuyPanelClientRpc(customerNetworkObjectId, cashierNetworkObjectId)` | Customer's client | Show `UI_ShopBuyPanel` |
| `CloseBuyPanelClientRpc(customerNetworkObjectId)` | Customer's client | Hide panel |
| `ToastClientRpc(targetNetworkObjectId, message, type)` | Single client | Surface toast |
| `Cashier.NotifyOccupiedClientRpc / NotifyReleasedClientRpc` | Everyone | Update prompt + visual |
| `Cashier.SendBusyToastClientRpc` | Single client | Race-fallback "busy" toast |

### Late-joiner correctness

All replicated state recovers via NetworkList replay or `NetworkVariable`'s
late-join push. In-flight `CharacterAction_BuyFromShop` is server-only;
visual proxy via existing `CharacterActions.BroadcastActionVisualsClientRpc`
keeps customer animation in sync (construction-loop pattern).

### Multiplayer scenarios (rule #19)

Each must be verified — see Section 9 testing scenarios.

### Performance (rule #34)

- Bounded NetworkLists (catalog ≤ ~50, cashiers ≤ ~6, sell-shelves ≤ ~20).
- `_currentCustomerNetworkObjectId` is one ulong NetworkVariable.
- `CharacterAction_BuyFromShop.OnTick` server-ticks at 5 Hz.
- No per-frame string allocs (cached prompts).
- No per-frame LINQ; all iteration via index loops.

---

## Section 8 — Save / load

### Persistence table

| State | Persistence | Owner |
|---|---|---|
| `ShopBuilding._catalog` | Saved | New `ShopBuildingSaveData : BuildingSaveData` — `(itemId, maxStock, priceOverride)` list |
| `ShopBuilding._sellShelves` | Saved | Same — list of furniture IDs resolved on load |
| `ShopBuilding._cashiers` | Recomputed | Auto-discovered when each Cashier re-enables |
| `Cashier.Till` | Saved | New `CashierSaveData` — `List<CurrencyBalanceEntry>` |
| `Cashier._linkedBuildingId` | Saved | Resolves linkage on load |
| `Cashier.Occupant` | NOT saved | Resets to null; vendor walks back |
| `Cashier._currentCustomer` | NOT saved | Lock resets to null; in-flight transactions drop |
| `Cashier._requiresVendor` | Already serialised | Inspector-authored on prefab |
| Sell-shelf slot contents | Already saved | Existing `StorageFurniture.RestoreFromSaveData` |
| `JobVendor` slot list | Recomputed | `RegisterCashier` re-adds slots |
| `CharacterWallet` | Already saved | Existing `WalletSaveData` |
| `CharacterAction_BuyFromShop` runtime | NOT saved | Atomic, commit-only-at-finish |

### Load sequencing

```
ShopBuilding spawned
        │
        ▼
ShopBuilding.RestoreFromSaveData()  ──→ rebuilds _catalog
                                         (sellShelves resolved AFTER cashiers register)
        │
        ▼
Cashier furniture spawn
        │
        ▼
Cashier.OnEnable → RegisterCashier (server-only)
        │
        ▼
ShopBuilding.OnFurnituresLoaded()  ──→ resolves _sellShelves refs by InstanceId
Cashier.RestoreFromSaveData()       ──→ till + link
```

Deferred-resolution pattern matches existing `StorageFurnitureNetworkSync`
behavior — proven path.

### Save shapes

```csharp
[Serializable]
public class ShopBuildingSaveData : BuildingSaveData
{
    public List<CatalogEntrySaveEntry> catalog = new();
    public List<string> sellShelfFurnitureIds = new();
}

[Serializable]
public struct CatalogEntrySaveEntry
{
    public string itemId;
    public int maxStock;
    public int priceOverride;
}

[Serializable]
public class CashierSaveData
{
    public List<CurrencyBalanceEntry> till = new();
    public string linkedBuildingId;
    public bool requiresVendor;
}
```

`CurrencyBalanceEntry` already exists (used by `WalletSaveData`).

---

## Section 9 — Migration, deletion, out-of-scope

### Files & symbols deleted

| Symbol | Reason |
|---|---|
| `ShopBuilding._customerQueue` | Per-cashier transaction lock replaces central queue |
| `ShopBuilding.JoinQueue / GetNextCustomer / ClearQueue` | Same |
| `ShopBuilding.CustomersInQueue` | Debug UI updates to per-cashier `IsAvailableForCustomer` |
| `ShopBuilding._vendorPoint` field + `VendorPoint` getter | Each cashier's `InteractionPoint` is the new vendor anchor |
| `ShopBuilding.GetWorkPosition` JobVendor branch | Vendor walks to chosen cashier's `InteractionPoint` |
| `ShopBuilding.GetVendor()` (singular) | Multi-vendor → `IReadOnlyList<JobVendor> Vendors` |
| `JobVendor._currentClient` + "call next customer" branch | Buys are customer-initiated |
| `JobVendor._isAtCounter` / `_isMovingToCounter` | Renamed `_isOccupyingCashier` / `_isMovingToCashier` |

### Files preserved untouched

- `Assets/Scripts/Character/CharacterInteraction/InteractionBuyItem.cs` —
  reserved for future character↔character trading.

### Files modified

- `ShopBuilding.cs`
- `JobVendor.cs`
- `GoapAction_GoShopping.cs`
- `FurnitureTag.cs` (add `Cashier`)
- `ItemSO.cs` (add `_basePrice`)

### Files added

- `Assets/Scripts/World/Furniture/Cashier.cs`
- `Assets/Scripts/World/Furniture/CashierNetSync.cs`
- `Assets/Scripts/World/Furniture/CashierInteractable.cs`
- `Assets/Scripts/World/Furniture/CashierSaveData.cs`
- `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingNetSync.cs`
- `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingSaveData.cs`
- `Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs`
- `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs` + prefab
- `Assets/Scripts/UI/Management/Tabs/ShopCatalogTab.cs` + view
- `Assets/Scripts/UI/Management/Tabs/ShopShelvesTab.cs` + view
- `Assets/Scripts/UI/Management/Tabs/ShopCashiersTab.cs` + view
- `Assets/Scripts/UI/Management/Tabs/CatalogItemPickerDialog.cs`
- Cashier furniture prefab + sprite asset (designer task; placeholder cube
  acceptable for first review)

### Documentation updates (rules #28, #29b)

- `.agent/skills/shop-system/SKILL.md` — new (or update if exists)
- `.agent/skills/character-actions/SKILL.md` — list `CharacterAction_BuyFromShop`
- `.agent/skills/jobs/SKILL.md` — pool model JobVendor
- `.agent/skills/goap-actions/SKILL.md` — refactored GoapAction_GoShopping
- `wiki/systems/shop-system.md` — new page
- `wiki/systems/cashier-furniture.md` — new page
- `wiki/systems/character-actions.md` — change log entry

### Out-of-scope (separate sessions)

1. **Building Treasury** — base-class money sink. One-line redirect on cashier
   withdraw. Brief inline in conversation; user spins session.
2. **Known-Entities Registry** — per-character "ever encountered" registry for
   items, races, etc. Powers catalog item picker filter. Brief inline in
   conversation.
3. **Management Panel Polymorphic Tabs** — refactor `UI_OwnerHiringPanel` →
   `UI_OwnerManagementPanel` driven by virtual
   `CommercialBuilding.GetManagementTabs()`. Brief written:
   `docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`.
   **Phase 2b consumes the result of this session — must land first.**
4. **Vendor-less automatic distributor cashier prefab** — Phase 2b lays the
   `_requiresVendor` switch + branch logic; designer ships a prefab later.
5. **Multi-currency catalog pricing** — `CurrencyId.Default` is the only
   currency today. Cashier till is already typed for multi-currency.
6. **Shop economy balancing** — dynamic pricing, regional variation, supplier
   restocking, faction discounts.
7. **Character↔character trading** — `InteractionBuyItem.cs` preserved for it.
8. **Catalog item picker advanced filters** — search box, category groups,
   sort. MVP shows alphabetical list.
9. **Cashier audio / animation polish** — coin-drop SFX, "ka-ching" on commit,
   vendor sit/idle anims. Designer pass after data layer ships.

### Testing scenarios (for the implementation plan)

Each must verify on Host↔Client + Client↔Client + Host/Client↔NPC (rule #19):

1. **Catalog mutation** — owner adds/edits/removes; all peers see changes.
2. **Shelf designation** — owner toggles a chest; player buy panel reflects live.
3. **Vendor occupy** — NPC and player vendors auto-seat on shift; release on movement.
4. **Vendor race** — two vendors target same cashier; loser falls through.
5. **Customer transaction (player)** — tap E, pick items, confirm; atomic commit.
6. **Customer transaction (NPC)** — GOAP-driven; 2s timer; commit identical.
7. **Out-of-stock during commit** — concurrent NPC purchase drains shelf;
   player Confirm aborts cleanly with toast.
8. **Insufficient funds** — UI Confirm disabled; server-side commit aborts.
9. **Overflow drop** — player + NPC: ground-spawn next to them.
10. **Vendor leaves mid-transaction** — UI closes with toast; lock cleared.
11. **Cashier picked up mid-transaction** — same abort; till coins drop.
12. **Withdraw from till** — owner clicks; coins move to wallet; balance updates.
13. **Save during transaction** — load resets locks; vendor walks back.
14. **Late joiner during active shop** — sees catalog, shelves, till, occupant.
15. **Auto-distributor mode** (forward-compat smoke) — `_requiresVendor = false`
    cashier accepts customer without vendor.

---

## Open dependencies

This spec assumes the following parallel-session brief has shipped before
implementation begins:

- `docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`
  — provides `IManagementTab`, `CommercialBuilding.GetManagementTabs()`,
  `UI_OwnerManagementPanel`. Section 6 of this spec consumes those.

Other briefs (Treasury, Known-Entities Registry) are forward-compatibility
hooks only — Phase 2b ships without them and absorbs them later via one-line
redirects.

---

*End of design.*
