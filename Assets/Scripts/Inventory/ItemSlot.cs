using UnityEngine;

[System.Serializable]
public abstract class ItemSlot
{
    [SerializeField] protected ItemInstance _itemInstance;

    public ItemInstance ItemInstance
    {
        get => _itemInstance;
        set
        {
            if (value == null || CanAcceptItem(value))
                _itemInstance = value;
            else
                Debug.LogWarning($"[Slot] Tentative d'insertion invalide : {value.GetType().Name}");
        }
    }

    /// <summary>
    /// Vérifie si le slot est vide (ne contient aucune instance d'item).
    /// </summary>
    public bool IsEmpty()
    {
        return _itemInstance == null;
    }

    public void ClearSlot() => _itemInstance = null;

    // Méthode abstraite que chaque sous-classe doit définir
    public abstract bool CanAcceptItem(ItemInstance item);
}