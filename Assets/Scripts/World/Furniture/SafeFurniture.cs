using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Currency-only furniture (sibling to <see cref="StorageFurniture"/>). Holds a
/// per-<see cref="MWI.Economy.CurrencyId"/> integer balance — no item slots, no
/// visual content. Designed to be pre-placed inside any building (commercial,
/// residential, future variants) via the building prefab's furniture authoring
/// layout. Authored 2026-05-09 as the data carrier for the building Treasury
/// aggregator behind the unified B2B shop-buy logistics path.
///
/// <para>
/// Like <see cref="StorageFurniture"/>, the actual replicated state (per-currency
/// balance NetworkList + role NetworkVariable) lives on the sibling
/// <see cref="SafeFurnitureNetworkSync"/> component. This class is the
/// plain-Component view that consumers (Treasury aggregator, management UI,
/// future banker AI) read against. Server-side mutators forward to the
/// sync component; clients see updates via <see cref="ApplyRoleFromNetwork"/>
/// / <see cref="ApplyBalancesFromNetwork"/> drive-throughs.
/// </para>
/// </summary>
public class SafeFurniture : Furniture
{
    [Header("Safe — Role")]
    [Tooltip("Initial role at scene-spawn / on a fresh save. The runtime value is server-authoritative + replicated via the sibling SafeFurnitureNetworkSync; this field is the seed before network spawn AND the fallback when the safe isn't networked. Pre-placed safes inside a CommercialBuilding default to Treasury so the building has working treasury out of the box.")]
    [SerializeField] private SafeRoleType _initialRole = SafeRoleType.Treasury;

    [Header("Safe — Designer Seed Balance")]
    [Tooltip("Initial currency balances at scene-spawn / on a fresh save. The NPC LogisticsManager auto-assigns the Treasury role on shift-punch even when this seed is empty, so leaving this empty is fine for a brand-new safe.")]
    [SerializeField] private List<CurrencyBalanceEntry> _initialBalances = new List<CurrencyBalanceEntry>();

    /// <summary>
    /// Runtime role mirror — authoritative copy lives on the sibling
    /// <see cref="SafeFurnitureNetworkSync"/>'s <c>NetworkVariable&lt;SafeRoleType&gt;</c>.
    /// The sync component pushes value-changes here via
    /// <see cref="ApplyRoleFromNetwork"/> so consumers can read the role from
    /// the plain <see cref="SafeFurniture"/> reference.
    /// </summary>
    private SafeRoleType _runtimeRole;

    /// <summary>
    /// Server-side currency-balance dictionary. Mutated only on the server.
    /// Clients see balances through <see cref="ApplyBalancesFromNetwork"/>.
    /// Keyed by <c>CurrencyId.Id</c> (int) for cheap lookup and to match the
    /// wire-format <see cref="BuildingTreasuryEntry"/>.
    /// </summary>
    private readonly Dictionary<int, int> _balances = new Dictionary<int, int>();

    /// <summary>Fired on every peer (server + clients) when the balance changes.</summary>
    public event Action OnBalanceChanged;

    /// <summary>Fired on every peer when <see cref="Role"/> changes.</summary>
    public event Action<SafeRoleType> OnRoleChanged;

    /// <summary>Owner-assigned safe role (or <see cref="SafeRoleType.None"/>).</summary>
    public SafeRoleType Role => _runtimeRole;

    /// <summary>Inspector-authored seed for <see cref="Role"/>. Read by the sync component on first server spawn.</summary>
    public SafeRoleType InitialRole => _initialRole;

    /// <summary>Inspector-authored seed for the per-currency balance. Read by the sync component on first server spawn.</summary>
    public IReadOnlyList<CurrencyBalanceEntry> InitialBalances => _initialBalances;

    /// <summary>
    /// Lazy-cached sibling <see cref="SafeFurnitureNetworkSync"/> reference. Used by
    /// server-side actions (<c>CharacterAction_DepositToSafe</c>,
    /// <c>CharacterAction_WithdrawFromSafe</c>) to route per-client failure
    /// notifications through the sync component's ClientRpc dispatcher. Null when
    /// the safe is offline / non-networked (defensive — actions null-check).
    /// </summary>
    private SafeFurnitureNetworkSync _netSync;
    public SafeFurnitureNetworkSync NetSync
    {
        get
        {
            if (_netSync == null) _netSync = GetComponent<SafeFurnitureNetworkSync>();
            return _netSync;
        }
    }

    /// <summary>
    /// Player-only interaction entry-point. Pressing E inside the
    /// <see cref="FurnitureInteractable.InteractionZone"/> routes here via
    /// <see cref="Furniture.OnInteract"/>. We open the safe deposit/withdraw
    /// panel for the owning player only — non-owner peers and non-player
    /// characters (NPCs) take the GOAP/CharacterAction path instead
    /// (rule #22 player↔NPC parity: NPCs queue
    /// <c>CharacterAction_DepositToSafe</c> / <c>CharacterAction_WithdrawFromSafe</c>
    /// directly without going through the Interact UI).
    ///
    /// The InteractionZone proximity gate is enforced upstream by
    /// <see cref="FurnitureInteractable.Interact"/> before this is called
    /// (rule #36 — no inline distance math).
    /// </summary>
    public override bool OnInteract(Character interactor)
    {
        if (interactor == null) return false;
        if (!interactor.IsOwner || !interactor.IsPlayer()) return false;
        PlayerUI.Instance?.OpenSafePanel(this, interactor);
        return true;
    }

    /// <summary>
    /// Contributes an "Open Safe" verb to the player's hold-E menu. Identical
    /// effect to tap-E (both call <see cref="PlayerUI.OpenSafePanel"/>) — the
    /// menu entry just makes the verb discoverable when the player isn't sure
    /// what a safe does. Owning-player gate mirrors <see cref="OnInteract"/>;
    /// non-owner peers and NPCs never see the option.
    /// </summary>
    public override System.Collections.Generic.List<InteractionOption> GetExtraInteractionOptions(Character interactor)
    {
        if (interactor == null) return null;
        if (!interactor.IsOwner || !interactor.IsPlayer()) return null;

        var options = new System.Collections.Generic.List<InteractionOption>(1);
        options.Add(new InteractionOption
        {
            Name = "Open Safe",
            Action = () => PlayerUI.Instance?.OpenSafePanel(this, interactor)
        });
        return options;
    }

    /// <summary>Read a currency balance. Server + client safe. Returns 0 for absent currencies.</summary>
    public int GetBalance(MWI.Economy.CurrencyId currency)
    {
        return _balances.TryGetValue(currency.Id, out var amount) ? amount : 0;
    }

    /// <summary>Predicate. Server + client safe.</summary>
    public bool CanAfford(MWI.Economy.CurrencyId currency, int amount)
        => amount <= 0 || GetBalance(currency) >= amount;

    /// <summary>
    /// Server-only mutator. Attempts to deduct <paramref name="amount"/> from
    /// the per-currency balance; returns true on success. Returns false on
    /// insufficient funds. Forwards the new balance to the sibling sync
    /// component so clients mirror it.
    /// </summary>
    public bool TryDebit(MWI.Economy.CurrencyId currency, int amount, string reason)
    {
        if (amount < 0)
        {
            Debug.LogError($"[SafeFurniture] {FurnitureName}: TryDebit rejected negative amount {amount} (reason='{reason}').");
            return false;
        }
        if (amount == 0) return true;
        int id = currency.Id;
        if (!_balances.TryGetValue(id, out var current)) current = 0;
        if (current < amount) return false;
        int newAmount = current - amount;
        if (newAmount == 0) _balances.Remove(id);
        else _balances[id] = newAmount;
        OnBalanceChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Server-only mutator. Adds <paramref name="amount"/> to the per-currency
    /// balance. Rejects non-positive amounts with a LogError (no silent
    /// corruption). Forwards the new balance to the sibling sync component.
    /// </summary>
    public void Credit(MWI.Economy.CurrencyId currency, int amount, string reason)
    {
        if (amount <= 0)
        {
            Debug.LogError($"[SafeFurniture] {FurnitureName}: Credit rejected non-positive amount {amount} (reason='{reason}').");
            return;
        }
        int id = currency.Id;
        if (!_balances.TryGetValue(id, out var current)) current = 0;
        _balances[id] = current + amount;
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Internal — called by <see cref="SafeFurnitureNetworkSync"/> when the
    /// replicated role changes (server-write or client-receive). Updates the
    /// local mirror and fires <see cref="OnRoleChanged"/>.
    /// </summary>
    internal void ApplyRoleFromNetwork(SafeRoleType newRole)
    {
        if (_runtimeRole == newRole) return;
        _runtimeRole = newRole;
        OnRoleChanged?.Invoke(newRole);
    }

    /// <summary>
    /// Internal — called by <see cref="SafeFurnitureNetworkSync"/> when the
    /// replicated balance list changes. Rebuilds <see cref="_balances"/>
    /// from the supplied snapshot and fires <see cref="OnBalanceChanged"/>.
    /// Used by both the server's own re-emit (so server-side reads stay
    /// consistent without each mutator touching the dict twice) and the
    /// client's mirror pass.
    /// </summary>
    internal void ApplyBalancesFromNetwork(IReadOnlyList<BuildingTreasuryEntry> entries)
    {
        _balances.Clear();
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Amount <= 0) continue;
                _balances[e.CurrencyId] = e.Amount;
            }
        }
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Server-only — wipes the safe and rewrites it from <paramref name="entries"/>.
    /// Used by save/load restore. Atomic from the server perspective.
    /// </summary>
    public void RestoreFromSaveData(IReadOnlyList<CurrencyBalanceEntry> entries)
    {
        _balances.Clear();
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.amount <= 0) continue;
                _balances[e.currencyId] = e.amount;
            }
        }
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Server-only — snapshots the current balance for save data. Returns a fresh list.
    /// Empty list when the safe is empty.
    /// </summary>
    public List<CurrencyBalanceEntry> CaptureBalancesForSave()
    {
        var list = new List<CurrencyBalanceEntry>(_balances.Count);
        foreach (var kv in _balances)
        {
            if (kv.Value <= 0) continue;
            list.Add(new CurrencyBalanceEntry { currencyId = kv.Key, amount = kv.Value });
        }
        return list;
    }

    /// <summary>
    /// Server + client safe enumeration of current balances. Allocates per call —
    /// callers should cache references in hot paths. Used by the management-UI
    /// safe row to display "Treasury: 123 g, 5 silver".
    /// </summary>
    public IReadOnlyList<BuildingTreasuryEntry> Balances
    {
        get
        {
            var list = new List<BuildingTreasuryEntry>(_balances.Count);
            foreach (var kv in _balances)
            {
                if (kv.Value <= 0) continue;
                list.Add(new BuildingTreasuryEntry { CurrencyId = kv.Key, Amount = kv.Value });
            }
            return list;
        }
    }
}
