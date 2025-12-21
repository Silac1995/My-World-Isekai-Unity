[System.Serializable]
public class ManaRegenRate : CharacterTertiaryStats
{
    public ManaRegenRate(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Mana Regeneration Rate";
    }
}