[System.Serializable]
public class CharacterDexterity : CharacterSecondaryStats
{
    public CharacterDexterity(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Dexterity";
    }
}