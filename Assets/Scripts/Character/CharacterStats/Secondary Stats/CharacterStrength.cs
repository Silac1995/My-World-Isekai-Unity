[System.Serializable]
public class CharacterStrength : CharacterSecondaryStats
{
    public CharacterStrength(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Strength";
    }
}