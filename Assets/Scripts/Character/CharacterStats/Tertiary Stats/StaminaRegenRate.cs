[System.Serializable]
public class StaminaRegenRate : CharacterTertiaryStats
{
    public StaminaRegenRate(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Stamina Regeneration Rate";
    }
}