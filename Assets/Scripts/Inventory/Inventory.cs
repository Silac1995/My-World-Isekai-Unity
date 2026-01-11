using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Inventory
{
    // On pointe vers la classe de base du stockage
    [SerializeField] private StorageWearableInstance _owner;
    [SerializeField] private int _capacity;
    [SerializeField] private List<ItemSlot> _itemSlots;

    public StorageWearableInstance Owner => _owner;
    public int Capacity => _capacity;
    public List<ItemSlot> ItemSlots => _itemSlots;

    public Inventory(StorageWearableInstance owner, int capacity)
    {
        _owner = owner;
        InitializeItemSlots(capacity);
    }

    public void InitializeItemSlots(int capacity)
    {
        _capacity = capacity;
        _itemSlots = new List<ItemSlot>(_capacity);

        for (int i = 0; i < _capacity; i++)
        {
            _itemSlots.Add(new ItemSlot());
        }
    }

    // --- NOUVELLES MÉTHODES ---

    /// <summary>
    /// Vérifie s'il reste au moins un slot vide dans l'inventaire.
    /// </summary>
    public bool HasFreeSpace()
    {
        foreach (var slot in _itemSlots)
        {
            if (slot.IsEmpty) return true;
        }
        return false;
    }

    /// <summary>
    /// Ajoute un objet dans le premier slot disponible.
    /// </summary>
    public bool AddItem(ItemInstance item)
    {
        foreach (var slot in _itemSlots)
        {
            if (slot.IsEmpty)
            {
                slot.ItemInstance = item;
                return true;
            }
        }
        Debug.LogWarning("[Inventory] Impossible d'ajouter l'item : Inventaire plein.");
        return false;
    }

    /// <summary>
    /// Force l'ajout d'un objet dans un slot précis (utile pour le drag & drop).
    /// </summary>
    public void AddItemToSlot(ItemInstance item, ItemSlot targetSlot)
    {
        if (_itemSlots.Contains(targetSlot))
        {
            targetSlot.ItemInstance = item;
        }
    }

    /// <summary>
    /// Retire un objet d'un slot spécifique.
    /// </summary>
    public void RemoveItemFromSlot(ItemSlot slot)
    {
        if (_itemSlots.Contains(slot))
        {
            slot.ClearSlot();
        }
    }

    /// <summary>
    /// Retourne l'index d'un slot donné ou -1 s'il n'existe pas dans cet inventaire.
    /// </summary>
    public int GetSlotIndex(ItemSlot slot)
    {
        return _itemSlots.IndexOf(slot);
    }

    /// <summary>
    /// Récupère un slot spécifique par son index. 
    /// Très utile pour l'UI (ex: OnClickSlot(int index)).
    /// </summary>
    public ItemSlot GetItemSlot(int index)
    {
        if (index >= 0 && index < _itemSlots.Count)
        {
            return _itemSlots[index];
        }

        Debug.LogError($"[Inventory] Index {index} hors limites ! (Capacité: {_itemSlots.Count})");
        return null;
    }

    /// <summary>
    /// Vérifie si un slot précis existe dans cet inventaire.
    /// </summary>
    public ItemSlot GetItemSlot(ItemSlot slot)
    {
        if (_itemSlots.Contains(slot))
        {
            return slot;
        }
        return null;
    }
}