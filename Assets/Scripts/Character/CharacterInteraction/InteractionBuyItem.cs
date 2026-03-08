using UnityEngine;

/// <summary>
/// Interaction déclenchée par un client envers un Vendeur,
/// ou du Vendeur envers le client (quand appelé de la file).
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
        return _shop != null && _shop.Inventory.Count > 0;
    }

    public void Execute(Character source, Character target)
    {
        Debug.Log($"<color=yellow>[InteractionBuyItem]</color> Début de transaction entre {source.CharacterName} et {target.CharacterName}.");

        // Vendre le premier item de l'inventaire
        if (_shop.Inventory.Count > 0)
        {
            var itemToSell = _shop.Inventory[0].ItemSO;
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
