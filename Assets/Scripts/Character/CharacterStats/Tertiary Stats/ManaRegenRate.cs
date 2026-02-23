[System.Serializable]
public class ManaRegenRate : CharacterTertiaryStats
{
    public ManaRegenRate(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Mana Regeneration Rate";
    }
}