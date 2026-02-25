using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Meuble de stockage (coffre, étagère, tonneau, armoire...).
/// Peut contenir des items et être verrouillé.
/// </summary>
public class StorageFurniture : Furniture
{
    [Header("Storage")]
    [SerializeField] private int _capacity = 10;
    [SerializeField] private bool _isLocked = false;

    public int Capacity => _capacity;
    public bool IsLocked => _isLocked;

    // TODO: Remplacer par un vrai système d'inventaire quand il existera
    private List<ItemInstance> _storedItems = new List<ItemInstance>();
    public IReadOnlyList<ItemInstance> StoredItems => _storedItems;
    public int ItemCount => _storedItems.Count;
    public bool IsFull => _storedItems.Count >= _capacity;

    public void Lock() => _isLocked = true;
    public void Unlock() => _isLocked = false;

    /// <summary>
    /// Ajoute un item dans le stockage.
    /// </summary>
    public bool AddItem(ItemInstance item)
    {
        if (item == null || IsFull || _isLocked) return false;
        _storedItems.Add(item);
        return true;
    }

    /// <summary>
    /// Retire un item du stockage.
    /// </summary>
    public bool RemoveItem(ItemInstance item)
    {
        if (item == null || _isLocked) return false;
        return _storedItems.Remove(item);
    }
}
