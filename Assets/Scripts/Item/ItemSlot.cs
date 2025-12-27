using UnityEngine;

public class ItemSlot
{
    private ItemInstance itemInstance;

    // Propriété pour lire ou modifier l'instance de l'item dans le slot
    public ItemInstance ItemInstance
    {
        get => itemInstance;
        set => itemInstance = value;
    }

    // Une petite méthode utilitaire souvent pratique pour les slots
    public bool IsEmpty => itemInstance == null;

    public void ClearSlot()
    {
        itemInstance = null;
    }
}