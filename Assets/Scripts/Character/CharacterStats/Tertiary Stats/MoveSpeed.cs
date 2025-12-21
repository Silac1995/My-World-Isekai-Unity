[System.Serializable]
public class MoveSpeed : CharacterTertiaryStats
{
    public MoveSpeed(CharacterStats characterStats, float baseValue = 10f)
        : base(characterStats, baseValue)
    {
        statName = "Move Speed";
    }
}