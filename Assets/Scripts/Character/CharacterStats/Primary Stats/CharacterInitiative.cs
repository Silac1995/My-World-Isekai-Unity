[System.Serializable]
public class CharacterInitiative : CharacterPrimaryStats
{
    public CharacterInitiative(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Initiative";
    }

    public bool IsReady()
    {
        return IsFull();
    }

    public void ResetInitiative()
    {
        currentAmount = 0f;
    }
}