[System.Serializable]
public class CharacterIntelligence : CharacterSecondaryStats
{
    public CharacterIntelligence(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Intelligence";
    }
}