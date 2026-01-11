using UnityEngine;

[System.Serializable]
public class ItemSlot
{
    [SerializeField] private ItemInstance _itemInstance;

    // Propriété pour lire ou modifier l'instance de l'item dans le slot
    public ItemInstance ItemInstance
    {
        get => _itemInstance;
        set => _itemInstance = value;
    }

    // Une petite méthode utilitaire souvent pratique pour les slots
    public bool IsEmpty => _itemInstance == null;

    public void ClearSlot()
    {
        _itemInstance = null;
    }
}