[System.Serializable]
public class CharacterCharisma : CharacterSecondaryStats
{
    public CharacterCharisma(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Charisma";
    }
}
