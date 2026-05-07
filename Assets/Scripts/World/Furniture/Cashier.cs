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

    protected void Awake()
    {
        _linkedBuilding = GetComponentInParent<CommercialBuilding>();
        _netSync = GetComponent<CashierNetSync>();
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

    // Lifecycle hooks for register/unregister filled in Wave 3 (Task 11).
    // Server-only logic (lock, till mutations, auto-occupy) filled in Wave 3.
}
