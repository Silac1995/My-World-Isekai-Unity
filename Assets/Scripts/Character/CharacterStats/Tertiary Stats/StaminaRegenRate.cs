[System.Serializable]
public class StaminaRegenRate : CharacterTertiaryStats
{
    public StaminaRegenRate(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Stamina Regeneration Rate";
    }
}