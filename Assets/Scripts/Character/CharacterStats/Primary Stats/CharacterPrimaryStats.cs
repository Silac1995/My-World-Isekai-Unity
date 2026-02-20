using UnityEngine;

[System.Serializable]
public abstract class CharacterPrimaryStats : CharacterBaseStats
{
    [SerializeField] protected float currentAmount; // Valeur actuelle de la stat (ex : PV restants)

    public float MaxValue => CurrentValue;

    public float CurrentAmount
    {
        get => currentAmount;
        set => currentAmount = value;
    }

    protected CharacterPrimaryStats(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        Reset();
        currentAmount = CurrentValue;
    }

    public new void SetBaseValue(float value)
    {
        base.SetBaseValue(value);
        currentAmount = CurrentValue;
    }

    public void LoseCurrentAmount(float value)
    {
        currentAmount = Mathf.Max(0f, currentAmount - value);
    }

    public void GainCurrentAmount(float value)
    {
        currentAmount = Mathf.Min(CurrentValue, currentAmount + value);
    }

    public bool IsFull()
    {
        return currentAmount >= CurrentValue;
    }

    public bool IsEmpty()
    {
        return currentAmount <= 0f;
    }
}
