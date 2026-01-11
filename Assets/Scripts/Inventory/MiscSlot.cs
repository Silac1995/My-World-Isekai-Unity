using UnityEngine;
[System.Serializable]
public class MiscSlot : ItemSlot
{
    public override bool CanAcceptItem(ItemInstance item)
    {
        // Un slot Misc accepte les objets Divers et les Consommables (grâce à ton héritage)
        return item is MiscInstance;
    }
}