[System.Serializable]
public class MagicalPower : CharacterTertiaryStats
{
    public MagicalPower(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Magical Power";
    }
}