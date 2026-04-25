using UnityEngine;

/// <summary>
/// Permissive slot that accepts any non-null ItemInstance, regardless of subtype.
/// Use it in storage furniture (or any other slotted container) when you want a
/// generic catch-all column that fits weapons, wearables, misc, consumables, etc.
/// </summary>
[System.Serializable]
public class AnySlot : ItemSlot
{
    public override bool CanAcceptItem(ItemInstance item)
    {
        return item != null;
    }
}
