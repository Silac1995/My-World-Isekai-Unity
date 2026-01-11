using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon", menuName = "Scriptable Objects/Equipment/Weapon")]
public class WeaponSO : EquipmentSO
{
    [Header("Weapon Specifics")]
    [SerializeField] readonly private EquipmentCategory _equipmentCategory = EquipmentCategory.Weapon;
    [SerializeField] private WeaponType _weaponType;

    public WeaponType WeaponType => _weaponType;

    public override System.Type InstanceType => typeof(WeaponInstance);
    public override ItemInstance CreateInstance() => new WeaponInstance(this);
}