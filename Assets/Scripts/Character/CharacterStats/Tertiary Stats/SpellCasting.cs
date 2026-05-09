[System.Serializable]
public class SpellCasting : CharacterTertiaryStats
{
    public SpellCasting(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "SpellCasting";
    }
}
