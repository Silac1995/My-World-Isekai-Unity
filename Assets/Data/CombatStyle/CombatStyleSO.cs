using System.Collections.Generic;
using UnityEngine;

public enum SecondaryStatType { Strength, Agility, Dexterity, Intelligence, Endurance, Charisma }

[System.Serializable]
public struct StyleLevelData
{
    public int MinLevel;
    // On utilise RuntimeAnimatorController pour accepter les Overrides OU les Controllers classiques
    public RuntimeAnimatorController CombatController;
}

public abstract class CombatStyleSO : ScriptableObject
{
    [SerializeField] private string _styleName;
    [SerializeField] private float _meleeRange = 3.5f;

    [Header("Damage Settings")]
    [SerializeField] private SecondaryStatType _scalingStat = SecondaryStatType.Strength;
    [SerializeField] private float _statMultiplier = 1.0f;
    [SerializeField] private DamageType _damageType = DamageType.Blunt;
    [SerializeField] private float _baseDamage = 5.0f;
    [SerializeField] [Range(0f, 2f)] private float _physicalPowerPercentage = 0.30f;
    [SerializeField] private float _knockbackForce = 5.0f;

    [Header("Animator Levels")]
    [SerializeField] private List<StyleLevelData> _levels = new List<StyleLevelData>();

    public abstract WeaponType WeaponType { get; }
    public string StyleName => _styleName;
    public float MeleeRange => _meleeRange;

    public SecondaryStatType ScalingStat => _scalingStat;
    public float StatMultiplier => _statMultiplier;
    public DamageType DamageType => _damageType;
    public float BaseDamage => _baseDamage;
    public float PhysicalPowerPercentage => _physicalPowerPercentage;
    public float KnockbackForce => _knockbackForce;

    public RuntimeAnimatorController GetCombatController(int level)
    {
        if (_levels.Count == 0) return null;

        StyleLevelData bestMatch = _levels[0];
        foreach (var data in _levels)
        {
            if (level >= data.MinLevel) bestMatch = data;
            else break;
        }
        return bestMatch.CombatController;
    }
}
