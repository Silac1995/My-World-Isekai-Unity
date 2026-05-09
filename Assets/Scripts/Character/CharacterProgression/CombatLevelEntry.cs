using System;

[Serializable]
public class CombatLevelEntry
{
    public int LevelIndex;

    // Stats bonuses chosen for this level
    public float BonusStrength;
    public float BonusAgility;
    public float BonusDexterity;
    public float BonusIntelligence;
    public float BonusEndurance;
    public float BonusCharisma;

    public void AddBonus(StatType statType, float amount)
    {
        switch (statType)
        {
            case StatType.Strength:     BonusStrength += amount;     break;
            case StatType.Agility:      BonusAgility += amount;      break;
            case StatType.Dexterity:    BonusDexterity += amount;    break;
            case StatType.Intelligence: BonusIntelligence += amount; break;
            case StatType.Endurance:    BonusEndurance += amount;    break;
            case StatType.Charisma:     BonusCharisma += amount;     break;
        }
    }
}
