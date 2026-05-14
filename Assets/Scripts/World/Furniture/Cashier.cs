using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Customer-facing transaction counter inside a CommercialBuilding (today
/// only ShopBuilding uses this; future BankBuilding etc. may too).
///
/// Three orthogonal state slots:
/// • Occupant (inherited from <see cref="OccupiableFurniture"/>) — vendor currently driving the cashier.
/// • CurrentCustomer — customer mid-transaction (the lock).
/// • Till — money held by this cashier (per-currency).
///
/// Server-authoritative — all mutations gated on IsServer and replicated via
/// the sibling CashierNetSync component.
/// </summary>
[RequireComponent(typeof(CashierInteractable))]
[RequireComponent(typeof(CashierNetSync))]
public class Cashier : OccupiableFurniture
{
    [Header("Cashier")]
    [Tooltip("If false, this is an automatic distributor — no vendor required to serve customers.")]
    [SerializeField] private bool _requiresVendor = true;
    public bool RequiresVendor => _requiresVendor;

    [Tooltip("Radius around InteractionPoint within which an on-shift vendor auto-seats.")]
    [SerializeField] private float _autoSeatRadius = 1.5f;

    private CommercialBuilding _linkedBuilding;
    public CommercialBuilding LinkedBuilding => _linkedBuilding;
    public ShopBuilding LinkedShop => _linkedBuilding as ShopBuilding;

    private Character _currentCustomer;

    /// <summary>
    /// Current customer mid-transaction. Server returns the in-memory field set by
    /// <see cref="TryAcquireCustomerLock"/>. Clients resolve <see cref="CashierNetSync.CurrentCustomerNetworkObjectId"/>
    /// via the NetworkManager spawn table — the field itself is only ever written on
    /// the server (rule #19), so without this resolution clients would see the cashier
    /// as permanently uncontested and skip the local pre-gate.
    /// </summary>
    public Character CurrentCustomer
    {
        get
        {
            if (_netSync == null || _netSync.IsServer || !_netSync.IsSpawned) return _currentCustomer;
            return ResolveCharacterByNetworkObjectId(_netSync.CurrentCustomerNetworkObjectId.Value);
        }
    }

    /// <summary>
    /// Vendor currently seated at the cashier. On the server the base field is
    /// authoritative; on clients it is forever null because <see cref="OccupiableFurniture.Use"/>
    /// only runs on the seating peer. We resolve the replicated
    /// <see cref="CashierNetSync.OccupantNetworkObjectId"/> instead, matching the
    /// server's <see cref="OccupiableFurniture.Occupant"/>. Fixes the multiplayer
    /// "No vendor on duty" pre-gate that previously fired on every non-host peer.
    /// </summary>
    public override Character Occupant
    {
        get
        {
            if (_netSync == null || _netSync.IsServer || !_netSync.IsSpawned) return base.Occupant;
            return ResolveCharacterByNetworkObjectId(_netSync.OccupantNetworkObjectId.Value);
        }
    }

    public bool IsAvailableForCustomer =>
        CurrentCustomer == null
        && (!_requiresVendor || Occupant != null);

    private static Character ResolveCharacterByNetworkObjectId(ulong networkObjectId)
    {
        if (networkObjectId == 0) return null;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;
        if (!nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var obj)) return null;
        return obj != null ? obj.GetComponent<Character>() : null;
    }

    private readonly Dictionary<CurrencyId, int> _till = new();
    public int GetTillBalance(CurrencyId c) => _till.TryGetValue(c, out var v) ? v : 0;
    public IReadOnlyDictionary<CurrencyId, int> GetAllTillBalances() => _till;

    private CashierNetSync _netSync;
    public CashierNetSync NetSync => _netSync;

    private float _autoSeatTimer;
    private const float AUTO_SEAT_TICK_INTERVAL = 1f;
    private static readonly Collider[] _scratchColliders = new Collider[16]; // server-only writes
    private bool _registered;

    protected void Awake()
    {
        _linkedBuilding = GetComponentInParent<CommercialBuilding>();
        _netSync = GetComponent<CashierNetSync>();
    }

    protected void OnEnable()
    {
        // Try to register immediately. If _linkedBuilding wasn't resolvable in Awake
        // (Building._defaultFurnitureLayout spawn pipeline runs Instantiate then SetParent
        // separately, so GetComponentInParent returns null here), the call is a no-op
        // and the parent ShopBuilding.OnNetworkSpawn picks us up via the late-bind path.
        TryRegisterWithShop();
    }

    /// <summary>
    /// Idempotent: re-resolves <see cref="_linkedBuilding"/> if it was null at Awake time
    /// (NGO-spawn race during <c>_defaultFurnitureLayout</c>) and registers with the parent
    /// <see cref="ShopBuilding"/> if not already registered. Safe to call repeatedly —
    /// the contains-check in <see cref="ShopBuilding.RegisterCashier"/> short-circuits
    /// duplicate registration.
    ///
    /// Two callers:
    /// - <see cref="OnEnable"/> — the happy path, fires immediately after Awake.
    /// - <see cref="ShopBuilding.OnNetworkSpawn"/> late-bind — fires after the building's
    ///   NetworkObject is server-spawned and walks every Cashier child to catch any that
    ///   raced ahead during scene load.
    /// </summary>
    public void TryRegisterWithShop()
    {
        if (_registered) return;
        if (_linkedBuilding == null)
        {
            _linkedBuilding = GetComponentInParent<CommercialBuilding>();
        }
        // LinkedShop-side method has its own IsServer check, so this is safe on every peer.
        if (LinkedShop != null)
        {
            LinkedShop.RegisterCashier(this);
            _registered = true;
        }
    }

    protected void OnDisable()
    {
        if (_registered && LinkedShop != null) LinkedShop.UnregisterCashier(this);
        _registered = false;

        // Mid-transaction safety — abort any active customer action.
        if (_currentCustomer != null && _netSync != null && _netSync.IsServer)
        {
            AbortActiveTransactionServerOnly("cashier removed");
        }

        // Drop till coins as WorldItems on the ground.
        if (_netSync != null && _netSync.IsServer && _till.Count > 0)
        {
            DropTillCoinsAsWorldItems();
        }
    }

    private void Update()
    {
        if (_netSync == null || !_netSync.IsServer) return;
        _autoSeatTimer += Time.unscaledDeltaTime;
        if (_autoSeatTimer >= AUTO_SEAT_TICK_INTERVAL)
        {
            _autoSeatTimer = 0f;
            ServerTickAutoOccupy();
        }
    }

    private void ServerTickAutoOccupy()
    {
        if (Occupant != null) return;
        if (_currentCustomer != null) return;

        // Find any character within InteractionPoint range whose CharacterJob is a
        // JobVendor of this shop and who is currently on shift.
        int n = Physics.OverlapSphereNonAlloc(GetInteractionPosition(), _autoSeatRadius, _scratchColliders);
        for (int i = 0; i < n; i++)
        {
            var collider = _scratchColliders[i];
            if (collider == null) continue;
            var character = collider.GetComponentInParent<Character>();
            if (character == null) continue;
            if (character.CharacterJob == null) continue;
            if (character.CharacterJob.CurrentJob is not JobVendor jv) continue;
            if (jv.Workplace != _linkedBuilding) continue;
            // On-shift check: project uses ScheduleActivity.Work as the on-shift signal
            // (CharacterSchedule has no IsOnWorkShiftNow boolean).
            if (character.CharacterSchedule == null
                || character.CharacterSchedule.CurrentActivity != ScheduleActivity.Work) continue;
            Use(character);
            break;
        }
    }

    /// <summary>
    /// Internal — server-only — called when the cashier is being removed mid-transaction.
    /// Cancels the active CharacterAction_BuyFromShop on the customer. The action's
    /// OnCancel path releases the lock and closes any open Player UI.
    /// </summary>
    internal void AbortActiveTransactionServerOnly(string reason)
    {
        if (_currentCustomer == null) return;
        // CharacterActions exposes ClearCurrentAction() (not CancelCurrentAction); it
        // invokes OnCancel on the running action and broadcasts the cancellation RPC.
        _currentCustomer.CharacterActions?.ClearCurrentAction();
        Debug.Log($"[Cashier] Aborted active transaction: {reason}");
    }

    private void DropTillCoinsAsWorldItems()
    {
        // Phase 2b ships till-as-int — coin item drop is deferred until the future
        // currency-as-item refactor (or treasury session). For now, log the lost
        // coins so playtesters know.
        foreach (var kv in _till)
        {
            Debug.LogWarning($"[Cashier] {FurnitureName} removed with {kv.Value} of currency {kv.Key.Id} in till. Coins are lost (TODO: drop as WorldItems once coin items exist).");
        }
        _till.Clear();
    }

    public override bool Use(Character vendor)
    {
        if (!base.Use(vendor)) return false;
        if (_netSync != null && _netSync.IsServer)
        {
            // Replicate occupancy to every peer so client pre-gates and management
            // UI mirror the server state (rule #19). The ClientRpc is kept as a
            // reserved visual-effect hook per CashierNetSync's documented contract.
            _netSync.SetOccupantServer(vendor.NetworkObjectId);
            _netSync.NotifyOccupiedClientRpc(vendor.NetworkObjectId);
            Debug.Log($"<color=cyan>[Cashier]</color> Use server: {FurnitureName} occupant -> {vendor.CharacterName} (NetworkObjectId={vendor.NetworkObjectId}). NetVar now = {_netSync.OccupantNetworkObjectId.Value}, NetSync.IsSpawned={_netSync.IsSpawned}.", this);
        }
        return true;
    }

    public override void Release()
    {
        bool wasOccupied = IsOccupied;
        base.Release();
        if (wasOccupied && _netSync != null && _netSync.IsServer)
        {
            _netSync.SetOccupantServer(0);
            _netSync.NotifyReleasedClientRpc();
            if (_currentCustomer != null)
                AbortActiveTransactionServerOnly("vendor walked off");
        }
    }

    /// <summary>
    /// Server-only — acquires the transaction lock for this customer. Returns false
    /// if the cashier is already busy or has no vendor (when one is required).
    /// </summary>
    public bool TryAcquireCustomerLock(Character customer)
    {
        if (_netSync == null || !_netSync.IsServer) return false;
        if (customer == null) return false;
        if (!IsAvailableForCustomer) return false;

        _currentCustomer = customer;
        _netSync.SetCurrentCustomerServer(customer.NetworkObjectId);
        return true;
    }

    /// <summary>
    /// Server-only — releases the transaction lock. Logs a warning if the caller
    /// is not the holder (defensive — should never happen).
    /// </summary>
    public void ReleaseCustomerLock(Character customer)
    {
        if (_netSync == null || !_netSync.IsServer) return;
        if (_currentCustomer != customer)
        {
            Debug.LogWarning($"[Cashier] ReleaseCustomerLock: caller {customer?.CharacterName ?? "null"} is not the holder ({_currentCustomer?.CharacterName ?? "null"}). Ignored.");
            return;
        }
        _currentCustomer = null;
        _netSync.SetCurrentCustomerServer(0);
    }

    /// <summary>
    /// Server-only — adds coins to the till. Logs and noops on non-positive amounts.
    /// Mirrors the new balance into the replicated NetworkList.
    /// </summary>
    public void CreditTill(CurrencyId currency, int amount, string source)
    {
        if (_netSync == null || !_netSync.IsServer) return;
        if (amount <= 0)
        {
            Debug.LogError($"[Cashier] CreditTill rejected: amount={amount} source={source} on {FurnitureName}");
            return;
        }
        int next = GetTillBalance(currency) + amount;
        _till[currency] = next;
        _netSync.SetTillBalanceServer(currency, next);
    }

    /// <summary>
    /// Server-only — removes coins from the till. Returns false if the till is short.
    /// </summary>
    public bool DebitTill(CurrencyId currency, int amount, string reason)
    {
        if (_netSync == null || !_netSync.IsServer) return false;
        if (amount <= 0)
        {
            Debug.LogError($"[Cashier] DebitTill rejected: amount={amount} reason={reason} on {FurnitureName}");
            return false;
        }
        int current = GetTillBalance(currency);
        if (current < amount) return false;
        int next = current - amount;
        if (next == 0) _till.Remove(currency);
        else _till[currency] = next;
        _netSync.SetTillBalanceServer(currency, next);
        return true;
    }

    // ============================================================================
    // SAVE / LOAD
    // ----------------------------------------------------------------------------
    // Wired through BuildingSaveData.Cashiers (mirrors the StorageFurniture path).
    // - Save: BuildingSaveData.FromBuilding walks every Cashier on the building and
    //   captures its Serialize() output. The composite key
    //   (BuildingSaveData.ComputeFurnitureKey) is the same scheme used for
    //   StorageFurniture, stable across _defaultFurnitureLayout reorders.
    // - Load: MapController.RestoreCashierContents calls RestoreFromSaveData after
    //   the building's default furniture has spawned (so live cashiers exist) and
    //   AFTER the sibling CashierNetSync has finished OnNetworkSpawn (so the
    //   replicated NetworkList exists and SetTillBalanceServer can write to it).
    //
    // Per rule #31: every external surface is wrapped in try/catch upstream
    // (FromBuilding + RestoreCashierContents). The methods below are intentionally
    // small and side-effect-light; defensive guards live at the call sites.
    // ============================================================================

    /// <summary>
    /// Server-only at the call-site (BuildingSaveData.FromBuilding runs only on the
    /// authoritative peer). Returns a portable snapshot of this cashier's gameplay
    /// state — till balances + linkage hint — for round-trip through the world save.
    /// Idempotent and allocation-light: one new <see cref="CashierSaveData"/> per call.
    /// </summary>
    public CashierSaveData Serialize()
    {
        var data = new CashierSaveData
        {
            requiresVendor = _requiresVendor,
            // _linkedBuilding is already typed as CommercialBuilding here; Building.BuildingId
            // is a string getter (the NetworkBuildingId.Value.ToString() roundtrip is
            // already done inside the property). No FixedString → string conversion needed.
            linkedBuildingId = _linkedBuilding != null ? _linkedBuilding.BuildingId : null,
        };
        foreach (var kv in _till)
        {
            data.till.Add(new CurrencyBalanceEntry { currencyId = kv.Key.Id, amount = kv.Value });
        }
        return data;
    }

    /// <summary>
    /// Server-only restore. Repopulates the in-memory till from <paramref name="data"/>
    /// and re-mirrors each balance into the replicated NetworkList via
    /// <see cref="CashierNetSync.SetTillBalanceServer"/> so late-joining clients see
    /// the saved coins on connect.
    ///
    /// Skips zero-or-negative balances (defensive — saved data should already exclude
    /// them; <see cref="DebitTill"/> deletes the entry on draw-down).
    ///
    /// <c>requiresVendor</c> from the save is intentionally ignored — the live value
    /// always comes from the prefab's serialized field. The save's copy is kept only
    /// for diagnostics if the authored value ever drifts between save/load.
    /// </summary>
    public void RestoreFromSaveData(CashierSaveData data)
    {
        if (data == null) return;
        _till.Clear();
        if (data.till != null)
        {
            foreach (var e in data.till)
            {
                if (e == null || e.amount <= 0) continue;
                _till[new CurrencyId(e.currencyId)] = e.amount;
            }
        }
        if (_netSync != null && _netSync.IsServer)
        {
            foreach (var kv in _till)
            {
                _netSync.SetTillBalanceServer(kv.Key, kv.Value);
            }
        }
    }

}
