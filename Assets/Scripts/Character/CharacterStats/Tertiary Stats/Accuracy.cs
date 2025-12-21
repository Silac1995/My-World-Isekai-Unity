[System.Serializable]
public class Accuracy : CharacterTertiaryStats
{
    public Accuracy(CharacterStats characterStats, float baseValue = 0f)
        : base(characterStats, baseValue)
    {
        statName = "Accuracy";
    }
}