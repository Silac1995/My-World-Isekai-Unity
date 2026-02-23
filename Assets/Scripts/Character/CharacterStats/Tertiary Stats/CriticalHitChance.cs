[System.Serializable]
public class CriticalHitChance : CharacterTertiaryStats
{
    public CriticalHitChance(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Critical Hit Chance";
    }
}