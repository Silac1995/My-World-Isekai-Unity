[System.Serializable]
public class CastingSpeed : CharacterTertiaryStats
{
    public CastingSpeed(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Casting Speed";
    }
}