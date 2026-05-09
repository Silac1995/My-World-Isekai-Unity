using UnityEngine;

/// <summary>
/// Interaction déclenchée par un client envers un Vendeur,
/// ou du Vendeur envers le client (quand appelé de la file).
///
/// Network note: stock checks read through <see cref="CommercialBuilding.InventoryTotalCount"/>
/// and <see cref="CommercialBuilding.GetInventoryCountsByItemSO"/>, both of which are backed by
/// a replicated NetworkList. Reading <c>_shop.Inventory.Count</c> directly would return 0 on
/// every non-host peer (server-only list) and a remote player initiating the interaction would
/// be told the shop is empty even when it isn't.
/// </summary>
public class InteractionBuyItem : ICharacterInteractionAction
{
    private ShopBuilding _shop;
    // Note: Pour une implémentation complète, le ItemSO désiré serait passé ici,
    // mais pour l'instant on vend le premier truc dispo ou on vérifie globalement.

    public InteractionBuyItem(ShopBuilding shop)
    {
        _shop = shop;
    }

    public bool CanExecute(Character source, Character target)
    {
        // source = Le vendeur, target = le client (ou inversement)
        return _shop != null && _shop.InventoryTotalCount > 0;
    }

    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=yellow>[InteractionBuyItem]</color> Début de transaction entre {source.CharacterName} et {target.CharacterName}.");

        if (_shop == null) return;

        // Pick the first ItemSO present in the replicated count view (works on both server and client).
        // The server-side _inventory list still holds the actual ItemInstance — SellItem(itemSO) below
        // resolves it server-authoritatively, so a client-initiated call only needs the SO to know what
        // to ask for. (The full transfer-item-to-buyer + transfer-coins flow remains a TODO; this fix
        // unblocks the multiplayer entry point so the call can even reach Execute.)
        var counts = _shop.GetInventoryCountsByItemSO();
        ItemSO itemToSell = null;
        foreach (var kvp in counts)
        {
            if (kvp.Value > 0) { itemToSell = kvp.Key; break; }
        }

        if (itemToSell != null)
        {
            // SellItem mutates the server-only _inventory + replicated count list.
            // Calling on a client will return null because _inventory is empty there — that's fine
            // for the current TODO state; future work will route this through a ServerRpc so a
            // client buyer can actually trigger the transaction server-side.
            var soldItem = _shop.SellItem(itemToSell);

            if (soldItem != null)
            {
                Debug.Log($"<color=green>[Shop]</color> Vente réussie de {itemToSell.ItemName} !");
                // TODO: Transférer l'item dans l'inventaire du client (target)
                // TODO: Transférer l'argent du client vers la caisse du magasin
            }
        }
        else
        {
            Debug.Log($"<color=red>[Shop]</color> Le magasin est vide, impossible de vendre quoi que ce soit.");
        }
    }
}
