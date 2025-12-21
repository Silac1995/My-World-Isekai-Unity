[System.Serializable]
public class MagicalPower : CharacterTertiaryStats
{
    public MagicalPower(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Magical Power";
    }
}