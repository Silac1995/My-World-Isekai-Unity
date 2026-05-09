using UnityEngine;

[System.Serializable]
public class PhysicalAbilityInstance : AbilityInstance
{
    public PhysicalAbilitySO PhysicalData => (PhysicalAbilitySO)_data;

    public PhysicalAbilityInstance(PhysicalAbilitySO data, Character owner) : base(data, owner) { }

    public override bool CanUse(Character target)
    {
        if (_data == null || _owner == null) return false;
        if (!_owner.IsAlive()) return false;

        var physData = PhysicalData;

        // Check stamina
        if (_owner.Stats?.Stamina == null) return false;
        if (_owner.Stats.Stamina.CurrentAmount < physData.StaminaCost) return false;

        // Check weapon type matches
        var weapon = _owner.CharacterEquipment?.CurrentWeapon;
        WeaponType equippedType = WeaponType.Barehands;
        if (weapon != null && weapon.ItemSO is WeaponSO weaponSO)
            equippedType = weaponSO.WeaponType;

        if (equippedType != physData.RequiredWeaponType) return false;

        return true;
    }

    public float ComputeCastTime()
    {
        float combatCasting = _owner?.Stats?.CombatCasting?.CurrentValue ?? 0f;
        return PhysicalData.ComputeCastTime(combatCasting);
    }
}
