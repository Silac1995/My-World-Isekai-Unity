using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Inventory
{
    [SerializeField] private StorageWearableInstance _storageWearableInstance;
    [SerializeField] private List<ItemSlot> _itemSlots;

    public event Action OnInventoryChanged;

    public StorageWearableInstance Owner => _storageWearableInstance;
    public List<ItemSlot> ItemSlots => _itemSlots;
    public int Capacity => _itemSlots?.Count ?? 0;

    public Inventory(StorageWearableInstance storageWearableInstance, int miscCapacity, int weaponCapacity)
    {
        _storageWearableInstance = storageWearableInstance;
        InitializeItemSlots(miscCapacity, weaponCapacity);
    }

    public void InitializeItemSlots(int miscCapacity, int weaponCapacity)
    {
        _itemSlots = new List<ItemSlot>(miscCapacity + weaponCapacity);

        for (int i = 0; i < miscCapacity; i++)
        {
            _itemSlots.Add(new MiscSlot());
        }

        for (int i = 0; i < weaponCapacity; i++)
        {
            _itemSlots.Add(new WeaponSlot());
        }
    }

    public bool HasFreeSpaceForItem(ItemInstance item)
    {
        foreach (var slot in _itemSlots)
        {
            if (slot.IsEmpty() && slot.CanAcceptItem(item)) return true;
        }
        return false;
    }

    public bool HasFreeSpaceForMisc()
    {
        foreach (var slot in _itemSlots)
        {
            if (slot is MiscSlot && slot.IsEmpty()) return true;
        }
        return false;
    }

    public bool HasFreeSpaceForWeapon()
    {
        foreach (var slot in _itemSlots)
        {
            if (slot is WeaponSlot && slot.IsEmpty()) return true;
        }
        return false;
    }

    public bool HasFreeSpaceForWearable()
    {
        foreach (var slot in _itemSlots)
        {
            if (slot is MiscSlot && slot.IsEmpty()) return true;
        }
        return false;
    }

    public bool HasFreeSpaceForItemSO(ItemSO itemSO)
    {
        if (itemSO == null) return false;

        if (itemSO is WeaponSO) return HasFreeSpaceForWeapon();
        if (itemSO is WearableSO) return HasFreeSpaceForWearable();
        
        return HasFreeSpaceForMisc();
    }

    /// <summary>
    /// Checks whether the inventory has room for at least one of the items in the given list.
    /// </summary>
    public bool HasFreeSpaceForAnyItemSO(List<ItemSO> itemSOs)
    {
        if (itemSOs == null || itemSOs.Count == 0 || _itemSlots == null) return false;

        foreach (var item in itemSOs)
        {
            if (HasFreeSpaceForItemSO(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether the inventory contains at least one of the items in the given list.
    /// </summary>
    public bool HasAnyItemSO(List<ItemSO> itemSOs)
    {
        if (itemSOs == null || itemSOs.Count == 0 || _itemSlots == null) return false;

        foreach (var slot in _itemSlots)
        {
            if (!slot.IsEmpty() && itemSOs.Contains(slot.ItemInstance.ItemSO))
            {
                return true;
            }
        }
        return false;
    }

    public bool HasNewItems()
    {
        if (_itemSlots == null) return false;
        foreach (var slot in _itemSlots)
        {
            if (!slot.IsEmpty() && slot.ItemInstance.IsNewlyAdded)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds the item, passing the Character for visual updates.
    /// </summary>
    public bool AddItem(ItemInstance item, Character character)
    {
        if (item == null || _itemSlots == null) return false;

        if (item.ItemSO is WeaponSO)
        {
            return AddWeaponItem(item, character);
        }
        else
        {
            return AddMiscItem(item, character);
        }
    }

    public bool AddMiscItem(ItemInstance item, Character character)
    {
        foreach (var slot in _itemSlots)
        {
            if (slot is MiscSlot && slot.IsEmpty() && slot.CanAcceptItem(item))
            {
                slot.ItemInstance = item;
                item.IsNewlyAdded = true;
                Debug.Log($"[Inventory] Misc added: {item.CustomizedName}");
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public bool AddWeaponItem(ItemInstance item, Character character)
    {
        foreach (var slot in _itemSlots)
        {
            if (slot is WeaponSlot && slot.IsEmpty() && slot.CanAcceptItem(item))
            {
                slot.ItemInstance = item;
                item.IsNewlyAdded = true;

                // Use the character parameter to update the visual
                UpdateWeaponVisuals(character);

                Debug.Log($"[Inventory] Weapon added: {item.CustomizedName}");
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    private void UpdateWeaponVisuals(Character character)
    {
        if (character != null && character.CharacterEquipment != null)
        {
            // Calls the logic we created for the bag
            character.CharacterEquipment.UpdateWeaponVisualOnBag();
        }
    }

    /// <summary>
    /// Removes an item and notifies the Character (useful when removing a weapon, for example).
    /// </summary>
    public bool RemoveItem(ItemInstance item, Character character)
    {
        if (item == null || _itemSlots == null) return false;

        foreach (var slot in _itemSlots)
        {
            if (slot.ItemInstance == item)
            {
                bool isWeapon = item.ItemSO is WeaponSO;
                slot.ClearSlot();

                if (isWeapon)
                    UpdateWeaponVisuals(character);

                Debug.Log($"[Inventory] {item.CustomizedName} removed.");
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void RemoveItemFromSlot(ItemSlot slot, Character character)
    {
        if (_itemSlots.Contains(slot))
        {
            bool wasWeapon = slot.ItemInstance?.ItemSO is WeaponSO;
            slot.ClearSlot();

            if (wasWeapon)
                UpdateWeaponVisuals(character);

            OnInventoryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Removes an item from the inventory and physically spawns it in the world at the given position.
    /// </summary>
    public bool DropItem(ItemInstance item, Vector3 dropPosition, Character characterForVisualUpdate = null)
    {
        if (RemoveItem(item, characterForVisualUpdate))
        {
            Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0, UnityEngine.Random.Range(-0.3f, 0.3f));
            WorldItem.SpawnWorldItem(item, dropPosition + offset);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a random item from the inventory and spawns it in the world (e.g. torn bag).
    /// </summary>
    public ItemInstance DropRandomItem(Vector3 dropPosition, Character characterForVisualUpdate = null)
    {
        List<ItemSlot> filledSlots = _itemSlots.FindAll(s => !s.IsEmpty());
        if (filledSlots.Count == 0) return null;

        ItemSlot randomSlot = filledSlots[UnityEngine.Random.Range(0, filledSlots.Count)];
        ItemInstance itemToDrop = randomSlot.ItemInstance;

        if (DropItem(itemToDrop, dropPosition, characterForVisualUpdate))
        {
            return itemToDrop;
        }

        return null;
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index >= 0 && index < _itemSlots.Count) return _itemSlots[index];
        return null;
    }
}