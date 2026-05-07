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

    // Lifecycle hooks for register/unregister filled in Wave 3 (Task 11).
    // Server-only logic (lock, till mutations, auto-occupy) filled in Wave 3.
}
