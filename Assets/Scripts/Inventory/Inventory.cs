using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Inventory
{
    [SerializeField] private StorageWearableInstance _storageWearableInstance;
    [SerializeField] private List<ItemSlot> _itemSlots;

    public StorageWearableInstance Owner => _storageWearableInstance;
    public List<ItemSlot> ItemSlots => _itemSlots;
    // La capacité est maintenant déduite du nombre total de slots
    public int Capacity => _itemSlots?.Count ?? 0;

    // Nouveau constructeur : on passe le nombre de slots de chaque type
    public Inventory(StorageWearableInstance storageWearableInstance, int miscCapacity, int weaponCapacity)
    {
        _storageWearableInstance = storageWearableInstance;
        InitializeItemSlots(miscCapacity, weaponCapacity);
    }

    public void InitializeItemSlots(int miscCapacity, int weaponCapacity)
    {
        _itemSlots = new List<ItemSlot>(miscCapacity + weaponCapacity);

        // On remplit avec les slots spécifiques
        for (int i = 0; i < miscCapacity; i++)
        {
            _itemSlots.Add(new MiscSlot());
        }

        for (int i = 0; i < weaponCapacity; i++)
        {
            _itemSlots.Add(new WeaponSlot());
        }
    }

    /// <summary>
    /// Vérifie s'il reste au moins un slot vide capable d'accueillir cet item précis.
    /// </summary>
    public bool HasFreeSpaceForItem(ItemInstance item)
    {
        foreach (var slot in _itemSlots)
        {
            if (slot.IsEmpty() && slot.CanAcceptItem(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// Ajoute l'objet dans le premier slot compatible.
    /// </summary>
    public bool AddItem(ItemInstance item)
    {
        if (item == null) return false;

        // Sécurité si la liste n'a pas été initialisée
        if (_itemSlots == null) return false;

        foreach (var slot in _itemSlots)
        {
            if (slot.IsEmpty() && slot.CanAcceptItem(item))
            {
                slot.ItemInstance = item;
                Debug.Log($"[Inventory] {item.CustomizedName} ajouté au slot {slot.GetType().Name}");
                return true;
            }
        }

        Debug.LogWarning($"[Inventory] Inventaire plein ou aucun slot compatible pour {item.CustomizedName}");
        return false;
    }

    /// <summary>
    /// Retire un item spécifique de l'inventaire s'il s'y trouve.
    /// </summary>
    public bool RemoveItem(ItemInstance item)
    {
        if (item == null || _itemSlots == null) return false;

        foreach (var slot in _itemSlots)
        {
            if (slot.ItemInstance == item)
            {
                slot.ClearSlot();
                Debug.Log($"[Inventory] {item.CustomizedName} retiré de l'inventaire.");
                return true;
            }
        }

        return false;
    }

    // --- Les autres méthodes (Remove, GetIndex, etc.) restent identiques ---

    public void RemoveItemFromSlot(ItemSlot slot)
    {
        if (_itemSlots.Contains(slot)) slot.ClearSlot();
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index >= 0 && index < _itemSlots.Count) return _itemSlots[index];
        return null;
    }
}