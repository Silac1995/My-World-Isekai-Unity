[System.Serializable]
public class CharacterAgility : CharacterSecondaryStats
{
    public CharacterAgility(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Agility";
    }
}