using UnityEngine;

[System.Serializable]
public class WearableSlot : ItemSlot
{
    public override bool CanAcceptItem(ItemInstance item)
    {
        return item is WearableInstance;
    }
}
