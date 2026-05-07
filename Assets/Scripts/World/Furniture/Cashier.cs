using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Customer-facing transaction counter inside a CommercialBuilding (today
/// only ShopBuilding uses this; future BankBuilding etc. may too).
///
/// Three orthogonal state slots:
/// • Occupant (inherited from Furniture) — vendor currently driving the cashier.
/// • CurrentCustomer — customer mid-transaction (the lock).
/// • Till — money held by this cashier (per-currency).
///
/// Server-authoritative — all mutations gated on IsServer and replicated via
/// the sibling CashierNetSync component.
/// </summary>
[RequireComponent(typeof(CashierInteractable))]
[RequireComponent(typeof(CashierNetSync))]
public class Cashier : Furniture
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
    public Character CurrentCustomer => _currentCustomer;

    public bool IsAvailableForCustomer =>
        _currentCustomer == null
        && (!_requiresVendor || Occupant != null);

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
        // Server-only registration; the LinkedShop-side method's IsServer check is
        // inside RegisterCashier so this is safe to call on every peer.
        if (LinkedShop != null) { LinkedShop.RegisterCashier(this); _registered = true; }
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
            _netSync.NotifyOccupiedClientRpc(vendor.NetworkObjectId);
        return true;
    }

    public override void Release()
    {
        bool wasOccupied = IsOccupied;
        base.Release();
        if (wasOccupied && _netSync != null && _netSync.IsServer)
        {
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

}
