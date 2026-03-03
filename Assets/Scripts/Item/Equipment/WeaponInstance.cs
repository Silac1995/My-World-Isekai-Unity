using UnityEngine;

[System.Serializable]
public abstract class WeaponInstance : EquipmentInstance
{
    [SerializeField] protected float _durability = 100f;
    [SerializeField] protected float _maxDurability = 100f;

    protected WeaponInstance(ItemSO data) : base(data)
    {
        if (data is WeaponSO weaponData)
        {
            _maxDurability = weaponData.MaxDurability;
            _durability = _maxDurability;
        }
    }

    public float Durability => _durability;
    public float MaxDurability => _maxDurability;
    public bool IsBroken => _durability <= 0f;

    public void DegradeDurability(float amount)
    {
        _durability = Mathf.Max(0f, _durability - amount);
    }

    public void Repair(float amount)
    {
        _durability = Mathf.Min(_maxDurability, _durability + amount);
    }
}