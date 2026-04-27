/// <summary>
/// Save data for HandsController — persists the item the character is currently
/// carrying in-hand (food, log, stone, key, …). Distinct from the equipped weapon,
/// which lives in EquipmentSaveData slot 0.
/// </summary>
[System.Serializable]
public class HandsSaveData
{
    /// <summary>ItemSO.ItemId of the carried item, or empty if hands are free.</summary>
    public string carriedItemId;

    /// <summary>JsonUtility-serialized ItemInstance state for the carried item.</summary>
    public string carriedItemJson;
}
