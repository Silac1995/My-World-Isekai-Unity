[System.Serializable]
public class PhysicalPower : CharacterTertiaryStats
{
    public PhysicalPower(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Physical Power";
    }
}