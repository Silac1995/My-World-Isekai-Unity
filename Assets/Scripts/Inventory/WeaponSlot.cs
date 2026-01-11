using UnityEngine;
[System.Serializable]
public class WeaponSlot : ItemSlot
{
    public override bool CanAcceptItem(ItemInstance item)
    {
        return item is WeaponInstance;
    }
}