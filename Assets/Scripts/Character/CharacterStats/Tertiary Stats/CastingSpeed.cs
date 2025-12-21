[System.Serializable]
public class CastingSpeed : CharacterTertiaryStats
{
    public CastingSpeed(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Casting Speed";
    }
}