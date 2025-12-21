[System.Serializable]
public class Speed : CharacterTertiaryStats
{
    public Speed(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Speed";
    }
}