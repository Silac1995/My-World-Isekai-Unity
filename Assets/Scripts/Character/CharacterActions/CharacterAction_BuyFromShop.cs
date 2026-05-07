using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative shop-purchase action. Single class, two completion
/// gates (NPC: 2s timer; Player: UI Confirm via ServerRpc).
///
/// Lifecycle:
/// 1. CanExecute — cashier free + has vendor (when required) + customer not null.
/// 2. OnStart   — TryAcquireCustomerLock; opens UI on customer client (Player mode).
/// 3. OnTick    — NPC: timer; Player: waits for _hasPlayerSelection.
/// 4. Commit    — atomic: validate funds, pull items from sell-shelves with
///                rollback on partial failure, deliver via PickUpItem cascade,
///                debit wallet, credit till, release lock.
/// 5. OnCancel  — refund/release if not yet committed; close UI on customer client.
///
/// Mirrors CharacterAction_FinishConstruction (2026-05-06 spec) for pattern.
/// </summary>
public class CharacterAction_BuyFromShop : CharacterAction_Continuous
{
    public enum BuyMode { NPC, Player }

    private readonly Cashier _cashier;
    private readonly List<ItemSO> _itemsToBuy;
    private readonly Dictionary<ItemSO, int> _quantities;
    private readonly BuyMode _mode;

    private float _elapsed;
    private bool _hasPlayerSelection;
    private bool _commitDone;

    private const float NPC_DURATION = 2f;
    private const float SENTINEL_TIMEOUT = 600f;

    public Cashier Cashier => _cashier;
    public BuyMode Mode => _mode;

    public CharacterAction_BuyFromShop(Character customer, Cashier cashier, List<ItemSO> itemsToBuy, BuyMode mode)
        : base(customer)
    {
        _cashier = cashier;
        _itemsToBuy = itemsToBuy ?? new List<ItemSO>();
        _quantities = new Dictionary<ItemSO, int>();
        _mode = mode;
        TickIntervalSeconds = 0.2f;

        if (mode == BuyMode.NPC)
        {
            for (int i = 0; i < _itemsToBuy.Count; i++)
            {
                var so = _itemsToBuy[i];
                if (so == null) continue;
                _quantities[so] = (_quantities.TryGetValue(so, out var v) ? v : 0) + 1;
            }
        }
    }

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
            Finish();
            return;
        }
        if (_mode == BuyMode.Player && _cashier.NetSync != null)
        {
            ulong customerId = character != null ? character.NetworkObjectId : 0UL;
            ulong cashierId = _cashier.NetSync.NetworkObjectId;
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.OpenBuyPanelClientRpc(customerId, cashierId, p);
        }
    }

    public override bool OnTick()
    {
        _elapsed += TickIntervalSeconds;
        if (_elapsed > SENTINEL_TIMEOUT)
        {
            Debug.LogWarning("[BuyFromShop] sentinel timeout — aborting.");
            return true; // OnCancel will run via the Finish path; lock cleanup happens there.
        }

        if (_mode == BuyMode.NPC) return _elapsed >= NPC_DURATION && Commit();
        if (_mode == BuyMode.Player) return _hasPlayerSelection && Commit();
        return false;
    }

    /// <summary>
    /// Server-only — called by CashierNetSync.SubmitPlayerSelectionServerRpc when
    /// the player presses Confirm in the buy UI.
    /// </summary>
    internal void ApplyPlayerSelection(IReadOnlyList<(ItemSO item, int qty)> selections)
    {
        _itemsToBuy.Clear();
        _quantities.Clear();
        if (selections == null) { _hasPlayerSelection = true; return; }
        for (int i = 0; i < selections.Count; i++)
        {
            var (so, qty) = selections[i];
            if (so == null || qty <= 0) continue;
            _itemsToBuy.Add(so);
            _quantities[so] = qty;
        }
        _hasPlayerSelection = true;
    }

    public override void OnCancel()
    {
        if (!_commitDone) ReleaseLockOnly();
        if (_mode == BuyMode.Player && _cashier?.NetSync != null && character != null)
        {
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.CloseBuyPanelClientRpc(character.NetworkObjectId, p);
        }
    }

    private void ReleaseLockOnly()
    {
        if (_cashier != null && character != null) _cashier.ReleaseCustomerLock(character);
    }

    // Commit + helpers filled in Task 20.
    private bool Commit() { return false; }
}
