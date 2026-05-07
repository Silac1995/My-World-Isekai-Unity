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

    public NetworkVariable<ulong> CurrentCustomerNetworkObjectId = new(
        0,
        readPermission: NetworkVariableReadPermission.Everyone,
        writePermission: NetworkVariableWritePermission.Server);

    public NetworkList<CashierTillEntry> TillBalances;

    public NetworkVariable<NetworkObjectReference> LinkedBuildingRef = new(
        default,
        readPermission: NetworkVariableReadPermission.Everyone,
        writePermission: NetworkVariableWritePermission.Server);

    protected void Awake()
    {
        _cashier = GetComponent<Cashier>();
        TillBalances = new NetworkList<CashierTillEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    // ----- Server-side helpers -----

    public void SetCurrentCustomerServer(ulong networkObjectId)
    {
        if (!IsServer) return;
        CurrentCustomerNetworkObjectId.Value = networkObjectId;
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
        // Wire to UI_ShopBuyPanel in Wave 8 (Task 23). For now, no-op so the
        // pre-Wave-8 manual smoke test doesn't NRE.
    }

    [ClientRpc]
    public void CloseBuyPanelClientRpc(ulong customerNetworkObjectId, ClientRpcParams p = default)
    {
        // Wire to UI_ShopBuyPanel in Wave 8.
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
        // Implemented in Task 21 (after CharacterAction_BuyFromShop exists).
        Debug.LogWarning($"[Cashier] RequestStartBuyServerRpc: not yet implemented (filled in Wave 6).");
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitPlayerSelectionServerRpc(BuySelectionPayload payload, ServerRpcParams p = default)
    {
        // Implemented in Task 21.
        Debug.LogWarning($"[Cashier] SubmitPlayerSelectionServerRpc: not yet implemented (filled in Wave 6).");
    }

    [ServerRpc(RequireOwnership = false)]
    public void CancelPlayerTransactionServerRpc(ServerRpcParams p = default)
    {
        // Implemented in Task 21.
        Debug.LogWarning($"[Cashier] CancelPlayerTransactionServerRpc: not yet implemented (filled in Wave 6).");
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
