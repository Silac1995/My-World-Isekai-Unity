using System;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network sibling for Cashier. Owns:
/// • CurrentCustomerNetworkObjectId: NetworkVariable&lt;ulong&gt; — the lock state.
/// • TillBalances: NetworkList&lt;CashierTillEntry&gt; — replicated till.
/// • LinkedBuildingRef: NetworkVariable&lt;NetworkObjectReference&gt; — for late-joiners.
///
/// Also surfaces the customer-facing ServerRpcs (RequestStartBuy / SubmitPlayerSelection /
/// CancelPlayerTransaction) and the vendor-occupied notification ClientRpcs.
///
/// Sealed bridge between Cashier (plain Furniture, owns the in-memory state) and
/// the network mirror — Cashier never derives from NetworkBehaviour directly,
/// preserving the existing Furniture base contract.
/// </summary>
[RequireComponent(typeof(Cashier))]
public class CashierNetSync : NetworkBehaviour
{
    private Cashier _cashier;

    private CharacterAction_BuyFromShop _activeAction;
    public CharacterAction_BuyFromShop ActiveAction => _activeAction;
    public void SetActiveActionServer(CharacterAction_BuyFromShop action) { if (IsServer) _activeAction = action; }

    public NetworkVariable<ulong> CurrentCustomerNetworkObjectId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Replicated mirror of <see cref="Cashier.Occupant"/>. The base
    /// <see cref="OccupiableFurniture._occupant"/> field is server-only (only the peer
    /// running <c>Use()</c> sets it), so client peers used to see the cashier as
    /// permanently vacant and the player pre-gate fired "No vendor on duty" even when
    /// a vendor was seated on the host (rule #19 violation).
    ///
    /// Set server-side in <see cref="Cashier.Use"/> / <see cref="Cashier.Release"/>;
    /// read on every peer via <see cref="Cashier.Occupant"/> override. 0 = vacant.
    /// </summary>
    public NetworkVariable<ulong> OccupantNetworkObjectId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkList<CashierTillEntry> TillBalances;

    public NetworkVariable<NetworkObjectReference> LinkedBuildingRef = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    protected void Awake()
    {
        _cashier = GetComponent<Cashier>();
        TillBalances = new NetworkList<CashierTillEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Server-side backfill: a vendor may already be seated (via
        // CharacterAction_OccupyFurniture, save/load, or a pre-NetSync-spawn
        // race) by the time this NetSync's NetworkVariables come online. Push
        // the current occupant id into the replicated NetVar so any
        // client that connects after this point — and the late-join
        // initial-state sync NGO performs at connect — sees the truth.
        if (IsServer && _cashier != null && _cashier.Occupant != null)
        {
            OccupantNetworkObjectId.Value = _cashier.Occupant.NetworkObjectId;
        }
    }

    // ----- Server-side helpers -----

    public void SetCurrentCustomerServer(ulong networkObjectId)
    {
        if (!IsServer) return;
        CurrentCustomerNetworkObjectId.Value = networkObjectId;
    }

    public void SetOccupantServer(ulong networkObjectId)
    {
        if (!IsServer) return;
        OccupantNetworkObjectId.Value = networkObjectId;
    }

    public void SetLinkedBuildingServer(NetworkObjectReference reference)
    {
        if (!IsServer) return;
        LinkedBuildingRef.Value = reference;
    }

    public void SetTillBalanceServer(CurrencyId currency, int balance)
    {
        if (!IsServer) return;
        for (int i = 0; i < TillBalances.Count; i++)
        {
            if (TillBalances[i].CurrencyId == currency.Id)
            {
                if (balance == 0) { TillBalances.RemoveAt(i); return; }
                TillBalances[i] = new CashierTillEntry { CurrencyId = currency.Id, Amount = balance };
                return;
            }
        }
        if (balance != 0)
            TillBalances.Add(new CashierTillEntry { CurrencyId = currency.Id, Amount = balance });
    }

    // ----- ClientRpcs -----

    [ClientRpc]
    public void NotifyOccupiedClientRpc(ulong vendorNetworkObjectId)
    {
        // Hook for future visual effects / prompt refresh on every peer.
        // Phase 2b intentionally has no visual yet — gate adding any here.
    }

    [ClientRpc]
    public void NotifyReleasedClientRpc()
    {
        // Same — visual hook reserved.
    }

    [ClientRpc]
    public void OpenBuyPanelClientRpc(ulong customerNetworkObjectId, ulong cashierNetworkObjectId, ClientRpcParams p = default)
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(customerNetworkObjectId, out var customerObj)) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(cashierNetworkObjectId, out var cashierObj)) return;
        var customer = customerObj.GetComponent<Character>();
        var cashier = cashierObj.GetComponent<Cashier>();
        if (customer == null || cashier == null) return;
        if (!customer.IsOwner) return;     // only the owning client opens the UI (rule #19)
        if (PlayerUI.Instance == null)
        {
            Debug.LogWarning("[CashierNetSync] PlayerUI.Instance is null — cannot open shop buy panel.");
            return;
        }
        PlayerUI.Instance.OpenShopBuyPanel(cashier, customer);
    }

    [ClientRpc]
    public void CloseBuyPanelClientRpc(ulong customerNetworkObjectId, ClientRpcParams p = default)
    {
        PlayerUI.Instance?.CloseShopBuyPanel();
    }

    [ClientRpc]
    public void SendBusyToastClientRpc(ClientRpcParams p = default)
    {
        // Targeted toast to a single client (rule #19 — never broadcast personal-context toasts).
        MWI.UI.Notifications.UI_Toast.Show(
            "Shop vendor is busy with another customer.",
            MWI.UI.Notifications.ToastType.Warning);
    }

    [ClientRpc]
    public void ToastClientRpc(string message, MWI.UI.Notifications.ToastType type, ClientRpcParams p = default)
    {
        MWI.UI.Notifications.UI_Toast.Show(message, type);
    }

    // ----- ServerRpcs (filled in later waves) -----

    [ServerRpc(RequireOwnership = false)]
    public void RequestStartBuyServerRpc(NetworkBehaviourReference customerRef, ServerRpcParams p = default)
    {
        if (!customerRef.TryGet(out Character customer)) return;
        if (customer.OwnerClientId != p.Receive.SenderClientId)
        {
            Debug.LogWarning($"[Cashier] RequestStartBuy: sender {p.Receive.SenderClientId} does not own customer {customer.NetworkObjectId}.");
            return;
        }
        if (!_cashier.IsAvailableForCustomer)
        {
            ClientRpcParams toCaller = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { p.Receive.SenderClientId } } };
            SendBusyToastClientRpc(toCaller);
            return;
        }

        var action = new CharacterAction_BuyFromShop(
            customer, _cashier, new System.Collections.Generic.List<ItemSO>(), CharacterAction_BuyFromShop.BuyMode.Player);
        SetActiveActionServer(action);
        customer.CharacterActions.ExecuteAction(action);
    }

    /// <summary>
    /// Client → Server: unified "use this cashier" intent with server-side role routing.
    /// The CLIENT cannot read <c>CharacterJob.CurrentJob</c> for remote-owned characters
    /// (the active-jobs list is server-side only — there is no NetworkVariable replication
    /// of the job assignment yet, see wiki/projects/optimisation-backlog.md). To preserve
    /// player↔NPC parity (rule #22) on remote clients, the client just signals "I want
    /// to use this cashier somehow" and the server decides:
    /// <list type="bullet">
    ///   <item>Seat is free AND <see cref="Cashier.IsCharacterAllowedToOccupy"/> returns
    ///   true → queue <see cref="CharacterAction_OccupyFurniture"/> (the vendor path).</item>
    ///   <item>Otherwise → start the buy flow exactly like
    ///   <see cref="RequestStartBuyServerRpc"/>.</item>
    /// </list>
    /// Authored 2026-05-14 alongside the CharacterAction_OccupyFurniture refactor.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestUseCashierServerRpc(NetworkBehaviourReference customerRef, ServerRpcParams p = default)
    {
        if (!customerRef.TryGet(out Character customer)) return;
        if (customer.OwnerClientId != p.Receive.SenderClientId)
        {
            Debug.LogWarning($"[Cashier] RequestUseCashier: sender {p.Receive.SenderClientId} does not own customer {customer.NetworkObjectId}.");
            return;
        }

        var interactable = _cashier.GetComponent<InteractableObject>();
        if (interactable != null && !interactable.IsCharacterInInteractionZone(customer))
        {
            Debug.LogWarning($"[Cashier] RequestUseCashier: {customer.CharacterName} not in interaction zone of {_cashier.FurnitureName}.");
            return;
        }

        // Role routing — vendor path takes precedence over customer path when both could apply.
        if (_cashier.Occupant == null && _cashier.IsCharacterAllowedToOccupy(customer))
        {
            if (customer.CharacterActions == null)
            {
                Debug.LogWarning($"[Cashier] RequestUseCashier: customer {customer.CharacterName} has no CharacterActions.");
                return;
            }
            customer.CharacterActions.ExecuteAction(new CharacterAction_OccupyFurniture(customer, _cashier));
            return;
        }

        // Customer path — same body as RequestStartBuyServerRpc (proximity already validated
        // above; ownership already validated above).
        if (!_cashier.IsAvailableForCustomer)
        {
            ClientRpcParams toCaller = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { p.Receive.SenderClientId } } };
            // Send a more accurate toast when the root cause is "no vendor on duty" rather
            // than "vendor is busy" — IsAvailableForCustomer is false for either reason.
            if (_cashier.RequiresVendor && _cashier.Occupant == null)
            {
                ToastClientRpc("No vendor on duty.", MWI.UI.Notifications.ToastType.Warning, toCaller);
            }
            else
            {
                SendBusyToastClientRpc(toCaller);
            }
            return;
        }

        var buyAction = new CharacterAction_BuyFromShop(
            customer, _cashier, new System.Collections.Generic.List<ItemSO>(), CharacterAction_BuyFromShop.BuyMode.Player);
        SetActiveActionServer(buyAction);
        customer.CharacterActions.ExecuteAction(buyAction);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitPlayerSelectionServerRpc(BuySelectionPayload payload, ServerRpcParams p = default)
    {
        if (_activeAction == null) return;
        if (_activeAction.Mode != CharacterAction_BuyFromShop.BuyMode.Player) return;
        if (_cashier.CurrentCustomer == null || _cashier.CurrentCustomer.OwnerClientId != p.Receive.SenderClientId) return;

        var selections = new System.Collections.Generic.List<(ItemSO, int)>();
        int len = payload.ItemIds?.Length ?? 0;
        for (int i = 0; i < len; i++)
        {
            var so = ResolveItemSO(payload.ItemIds[i].ToString());
            if (so == null) continue;
            int qty = payload.Quantities[i];
            if (qty <= 0) continue;
            selections.Add((so, qty));
        }
        _activeAction.ApplyPlayerSelection(selections);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CancelPlayerTransactionServerRpc(ServerRpcParams p = default)
    {
        if (_cashier.CurrentCustomer == null || _cashier.CurrentCustomer.OwnerClientId != p.Receive.SenderClientId) return;
        _cashier.CurrentCustomer.CharacterActions?.ClearCurrentAction();
        _activeAction = null;
    }

    private static ItemSO ResolveItemSO(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var all = Resources.LoadAll<ItemSO>("Data/Item");
        return System.Array.Find(all, x => x.ItemId == itemId);
    }
}

/// <summary>
/// Replicated till entry. Sibling to (but distinct from) CurrencyBalanceEntry —
/// the latter is a save-data class; this one is a wire-format struct.
/// They will converge once the wallet system upgrades to NetworkList replication
/// (currently uses ClientRpc broadcasts) — out of scope for Phase 2b.
/// </summary>
public struct CashierTillEntry : INetworkSerializable, IEquatable<CashierTillEntry>
{
    public int CurrencyId;
    public int Amount;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrencyId);
        serializer.SerializeValue(ref Amount);
    }

    public bool Equals(CashierTillEntry other) =>
        CurrencyId == other.CurrencyId && Amount == other.Amount;
}

/// <summary>
/// Network-serializable payload for player buy submissions. Contains an
/// array of (itemId, quantity) pairs.
/// </summary>
public struct BuySelectionPayload : INetworkSerializable
{
    public Unity.Collections.FixedString64Bytes[] ItemIds;
    public int[] Quantities;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // NGO requires explicit array length serialisation for non-fixed arrays.
        int len = ItemIds?.Length ?? 0;
        serializer.SerializeValue(ref len);
        if (serializer.IsReader)
        {
            ItemIds = new Unity.Collections.FixedString64Bytes[len];
            Quantities = new int[len];
        }
        for (int i = 0; i < len; i++)
        {
            serializer.SerializeValue(ref ItemIds[i]);
            serializer.SerializeValue(ref Quantities[i]);
        }
    }
}
