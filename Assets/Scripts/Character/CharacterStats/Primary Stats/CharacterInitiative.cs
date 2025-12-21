[System.Serializable]
public class CharacterInitiative : CharacterPrimaryStats
{
    public CharacterInitiative(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Initiative";
    }
}