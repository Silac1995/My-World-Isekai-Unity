using UnityEngine;

[System.Serializable]
public class StatsModifier
{
    [SerializeField]
    private StatType statType;
    [SerializeField]
    private float value; // Peut être négatif ou positif.

    public StatType StatType
    {
        get => statType;
        set => statType = value;
    }

    public float Value { get => value; set => this.value = value; }

    public bool MatchesStatType(System.Type statClass)
    {
        return statType switch
        {
            StatType.Health => statClass == typeof(CharacterHealth),
            StatType.Mana => statClass == typeof(CharacterMana),
            StatType.Stamina => statClass == typeof(CharacterStamina),
            StatType.Initiative => statClass == typeof(CharacterInitiative),

            StatType.Strength => statClass == typeof(CharacterStrength),
            StatType.Endurance => statClass == typeof(CharacterEndurance),
            StatType.Agility => statClass == typeof(CharacterAgility),
            StatType.Dexterity => statClass == typeof(CharacterDexterity),
            StatType.Intelligence => statClass == typeof(CharacterIntelligence),

            StatType.PhysicalPower => statClass == typeof(PhysicalPower),
            StatType.Speed => statClass == typeof(Speed),
            StatType.Dodge => statClass == typeof(DodgeChance),
            StatType.Accuracy => statClass == typeof(Accuracy),
            StatType.CastingSpeed => statClass == typeof(CastingSpeed),
            StatType.MagicalPower => statClass == typeof(MagicalPower),
            StatType.ManaRegen => statClass == typeof(ManaRegenRate),
            StatType.StaminaRegen => statClass == typeof(StaminaRegenRate),
            StatType.CriticalChance => statClass == typeof(CriticalHitChance),

            _ => false
        };
    }
}
