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
}
