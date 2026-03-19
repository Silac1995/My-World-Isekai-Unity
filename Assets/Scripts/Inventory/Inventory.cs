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
    /// Vérifie si l'inventaire a de la place pour au moins un des items de la liste donnée.
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
    /// Vérifie si l'inventaire contient au moins un des items de la liste donnée.
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
            // Appelle la logique que nous avons crée pour le sac
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

    /// <summary>
    /// Retire un item de l'inventaire et le fait spawn physiquement dans le monde à la position donnée.
    /// </summary>
    public bool DropItem(ItemInstance item, Vector3 dropPosition, Character characterForVisualUpdate = null)
    {
        if (RemoveItem(item, characterForVisualUpdate))
        {
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            WorldItem.SpawnWorldItem(item, dropPosition + offset);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Retire un item au hasard de l'inventaire et le fait spawn dans le monde. (ex: sac troué)
    /// </summary>
    public ItemInstance DropRandomItem(Vector3 dropPosition, Character characterForVisualUpdate = null)
    {
        List<ItemSlot> filledSlots = _itemSlots.FindAll(s => !s.IsEmpty());
        if (filledSlots.Count == 0) return null;

        ItemSlot randomSlot = filledSlots[Random.Range(0, filledSlots.Count)];
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