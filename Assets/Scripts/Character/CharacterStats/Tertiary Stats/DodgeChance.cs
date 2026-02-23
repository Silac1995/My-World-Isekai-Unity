[System.Serializable]
public class DodgeChance : CharacterTertiaryStats
{
    public DodgeChance(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Dodge Chance";
    }
}