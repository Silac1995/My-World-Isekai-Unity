[System.Serializable]
public class Accuracy : CharacterTertiaryStats
{
    public Accuracy(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "Accuracy";
    }
}