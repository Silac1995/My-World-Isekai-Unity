[System.Serializable]
public class PhysicalPower : CharacterTertiaryStats
{
    public PhysicalPower(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Physical Power";
    }
}