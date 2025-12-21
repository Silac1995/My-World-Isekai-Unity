[System.Serializable]
public class DodgeChance : CharacterTertiaryStats
{
    public DodgeChance(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Dodge Chance";
    }
}