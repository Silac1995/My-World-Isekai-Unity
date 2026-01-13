using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Inventory
{
    [SerializeField] private StorageWearableInstance _storageWearableInstance;
    [SerializeField] private List<ItemSlot> _itemSlots;

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

    /// <summary>
    /// Ajoute l'objet en passant le Character pour les mises à jour visuelles.
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
                Debug.Log($"[Inventory] Misc ajouté : {item.CustomizedName}");
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

                // On utilise le paramètre character pour mettre à jour le visuel
                UpdateWeaponVisuals(character);

                Debug.Log($"[Inventory] Arme ajoutée : {item.CustomizedName}");
                return true;
            }
        }
        return false;
    }

    private void UpdateWeaponVisuals(Character character)
    {
        if (character != null && character.CharacterEquipment != null)
        {
            // Appelle la logique que nous avons créée pour le sac
            character.CharacterEquipment.UpdateWeaponVisualOnBag();
        }
    }

    /// <summary>
    /// Retire un item et notifie le Character (utile si on retire une arme par exemple).
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

                Debug.Log($"[Inventory] {item.CustomizedName} retiré.");
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
        }
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index >= 0 && index < _itemSlots.Count) return _itemSlots[index];
        return null;
    }
}