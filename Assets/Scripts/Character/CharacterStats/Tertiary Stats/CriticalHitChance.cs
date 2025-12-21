[System.Serializable]
public class CriticalHitChance : CharacterTertiaryStats
{
    public CriticalHitChance(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Critical Hit Chance";
    }
}