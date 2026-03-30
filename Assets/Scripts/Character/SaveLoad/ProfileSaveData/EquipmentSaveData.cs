using System.Collections.Generic;

/// <summary>
/// Save data for CharacterEquipment.
/// Mirrors the NetworkEquipmentSyncData pattern: each equipped item is stored
/// as a slotId + itemId (SO name) + jsonData (serialized instance state).
/// Bag inventory items are stored as separate entries with slotId = 2+.
/// </summary>
[System.Serializable]
public class EquipmentSaveData
{
    /// <summary>
    /// All equipped items: weapon (slot 0), bag (slot 1), wearable layers (100+/200+/300+).
    /// </summary>
    public List<EquipmentSlotSaveEntry> equippedItems = new List<EquipmentSlotSaveEntry>();

    /// <summary>
    /// Items inside the bag's inventory, saved separately because JsonUtility
    /// cannot handle polymorphic ItemInstance serialization inside Inventory.
    /// Each entry stores: slotIndex (position in the bag inventory), itemId, jsonData.
    /// </summary>
    public List<InventorySlotSaveEntry> bagInventoryItems = new List<InventorySlotSaveEntry>();
}

/// <summary>
/// A single equipped item entry, using the same slot ID scheme as NetworkEquipmentSyncData:
/// 0 = Weapon, 1 = Bag, 100+ = Underwear layer, 200+ = Clothing layer, 300+ = Armor layer.
/// </summary>
[System.Serializable]
public class EquipmentSlotSaveEntry
{
    public int slotId;
    public string itemId;
    public string jsonData;
}

/// <summary>
/// A single item inside the bag's inventory.
/// slotIndex is the position within the Inventory.ItemSlots list.
/// </summary>
[System.Serializable]
public class InventorySlotSaveEntry
{
    public int slotIndex;
    public string itemId;
    public string jsonData;
}
