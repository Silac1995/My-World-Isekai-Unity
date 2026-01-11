using UnityEngine;

[System.Serializable]
public class WeaponInstance : EquipmentInstance
{
    public WeaponInstance(ItemSO data) : base(data)
    {
        // Initialisation de la durabilité basée sur le SO par exemple
        if (data is WeaponSO weaponData)
        {
        }
    }
}