using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon", menuName = "Scriptable Objects/Equipment/Weapon")]
public class WeaponSO : EquipmentSO
{
    [Header("Weapon Specifics")]
    [SerializeField] readonly private EquipmentCategory _equipmentCategory = EquipmentCategory.Weapon;
    [SerializeField] private WeaponType _weaponType;
    [SerializeField] private WeaponCategory _weaponCategory = WeaponCategory.Melee;
    [SerializeField] private DamageType _damageType = DamageType.Blunt;

    [Header("Durability")]
    [SerializeField] private float _maxDurability = 100f;

    [Header("Melee Specifics")]
    [SerializeField] private float _maxSharpness = 1f;

    [Header("Magazine Specifics (Ranged Only)")]
    [SerializeField] private int _magazineSize = 6;

    public WeaponType WeaponType => _weaponType;
    public WeaponCategory WeaponCategory => _weaponCategory;
    public DamageType DamageType => _damageType;
    public bool IsRanged => _weaponCategory == WeaponCategory.Ranged;
    public float MaxDurability => _maxDurability;
    public float MaxSharpness => _maxSharpness;
    public int MagazineSize => _magazineSize;

    public override System.Type InstanceType
    {
        get
        {
            if (_weaponCategory == WeaponCategory.Ranged)
            {
                // Les armes charging (arc) n'ont pas de magazineSize
                return _magazineSize > 0
                    ? typeof(MagazineWeaponInstance) 
                    : typeof(ChargingWeaponInstance);
            }
            return typeof(MeleeWeaponInstance);
        }
    }

    public override ItemInstance CreateInstance()
    {
        if (_weaponCategory == WeaponCategory.Ranged)
        {
            return _magazineSize > 0
                ? new MagazineWeaponInstance(this) 
                : new ChargingWeaponInstance(this);
        }
        return new MeleeWeaponInstance(this);
    }
}