using UnityEngine;

[System.Serializable]
public class MiscSlot : ItemSlot
{
    public override bool CanAcceptItem(ItemInstance item)
    {
        // Un slot Misc accepte tout ce qui n'est pas une arme (Wearables, Ressources, Consommables)
        // car l'inventaire n'a que deux types de slots: WeaponSlot et MiscSlot.
        return !(item is WeaponInstance);
    }
}
