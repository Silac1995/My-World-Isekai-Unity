using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Network sibling for ShopBuilding. Replicates the runtime catalog,
/// sell-shelves, and cashiers lists so clients can render the buy UI / management UI
/// from authoritative server state without RPC pingpong per query.
/// </summary>
[RequireComponent(typeof(ShopBuilding))]
public class ShopBuildingNetSync : NetworkBehaviour
{
    private ShopBuilding _shop;

    public NetworkList<ShopItemEntryNet> Catalog;
    public NetworkList<NetworkObjectReference> SellShelves;
    public NetworkList<NetworkObjectReference> Cashiers;

    protected void Awake()
    {
        _shop = GetComponent<ShopBuilding>();
        Catalog = new NetworkList<ShopItemEntryNet>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        SellShelves = new NetworkList<NetworkObjectReference>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        Cashiers = new NetworkList<NetworkObjectReference>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    // ----- Server-only push helpers -----

    public void PushCatalogEntryAddedServer(ShopItemEntry entry)
    {
        if (!IsServer || entry.Item == null) return;
        Catalog.Add(ToNet(entry));
    }

    public void PushCatalogEntryRemovedServer(string itemId)
    {
        if (!IsServer) return;
        for (int i = Catalog.Count - 1; i >= 0; i--)
        {
            if (Catalog[i].itemId == itemId) { Catalog.RemoveAt(i); return; }
        }
    }

    public void PushCatalogEntryEditedServer(ShopItemEntry entry)
    {
        if (!IsServer || entry.Item == null) return;
        for (int i = 0; i < Catalog.Count; i++)
        {
            if (Catalog[i].itemId == entry.Item.ItemId) { Catalog[i] = ToNet(entry); return; }
        }
    }

    public void PushSellShelfAddedServer(NetworkObjectReference shelfRef)
    {
        if (!IsServer) return;
        SellShelves.Add(shelfRef);
    }

    public void PushSellShelfRemovedServer(NetworkObjectReference shelfRef)
    {
        if (!IsServer) return;
        for (int i = SellShelves.Count - 1; i >= 0; i--)
        {
            if (SellShelves[i].NetworkObjectId == shelfRef.NetworkObjectId) { SellShelves.RemoveAt(i); return; }
        }
    }

    public void PushCashierAddedServer(ulong cashierNetworkObjectId)
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(cashierNetworkObjectId, out var obj))
            Cashiers.Add(new NetworkObjectReference(obj));
    }

    public void PushCashierRemovedServer(ulong cashierNetworkObjectId)
    {
        if (!IsServer) return;
        for (int i = Cashiers.Count - 1; i >= 0; i--)
        {
            if (Cashiers[i].NetworkObjectId == cashierNetworkObjectId) { Cashiers.RemoveAt(i); return; }
        }
    }

    [ClientRpc]
    public void SendUnauthorizedToastClientRpc(ClientRpcParams p = default)
    {
        MWI.UI.Notifications.UI_Toast.Show(
            "Only the shop owner can do that.",
            MWI.UI.Notifications.ToastType.Warning);
    }

    private static ShopItemEntryNet ToNet(ShopItemEntry e) => new()
    {
        itemId = new FixedString64Bytes(e.Item.ItemId),
        maxStock = e.MaxStock,
        priceOverride = e.PriceOverride
    };
}

public struct ShopItemEntryNet : INetworkSerializable, System.IEquatable<ShopItemEntryNet>
{
    public FixedString64Bytes itemId;
    public int maxStock;
    public int priceOverride;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref maxStock);
        serializer.SerializeValue(ref priceOverride);
    }

    public bool Equals(ShopItemEntryNet other) =>
        itemId == other.itemId && maxStock == other.maxStock && priceOverride == other.priceOverride;
}
