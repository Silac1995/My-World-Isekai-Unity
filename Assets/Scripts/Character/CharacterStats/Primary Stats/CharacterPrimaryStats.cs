using UnityEngine;

[System.Serializable]
public abstract class CharacterPrimaryStats : CharacterBaseStats
{
    [SerializeField] protected float currentAmount; // Current value of the stat (e.g.: remaining HP)

    protected CharacterBaseStats _linkedStat;
    protected float _multiplier;
    protected float _baseOffset;

    public event System.Action<float, float> OnAmountChanged; // oldAmount, newAmount

    public float MaxValue => CurrentValue;

    public float CurrentAmount
    {
        get => currentAmount;
        set
        {
            if (Mathf.Abs(currentAmount - value) > 0.001f)
            {
                float prev = currentAmount;
                currentAmount = value;
                OnAmountChanged?.Invoke(prev, currentAmount);
            }
        }
    }

    protected CharacterPrimaryStats(CharacterStats characterStats, CharacterBaseStats linkedStat = null, float multiplier = 1f, float baseOffset = 0f)
        : base(characterStats, baseOffset)
    {
        _linkedStat = linkedStat;
        _multiplier = multiplier;
        _baseOffset = baseOffset;
        
        // Initialize currentAmount to 100% of max BEFORE recalculating anything
        currentAmount = CurrentValue;

        if (_linkedStat != null)
        {
            UpdateFromLinkedStat();
        }
        else
        {
            base.SetBaseValue(baseOffset);
        }
    }

    /// <summary>
    /// Updates the multiplier and the offset, used to modify the racial bases of this character.
    /// </summary>
    public void UpdateScaling(float multiplier, float baseOffset)
    {
        _multiplier = multiplier;
        _baseOffset = baseOffset;
        UpdateFromLinkedStat();
    }

    /// <summary>
    /// Recalculates the maximum value (BaseValue) of this primary stat based on the linked secondary stat and the multiplier.
    /// The preservation of the percentage is now handled globally by `RecalculateCurrentValue`.
    /// </summary>
    public void UpdateFromLinkedStat()
    {
        if (_linkedStat != null)
        {
            float newMax = _baseOffset + (_linkedStat.CurrentValue * _multiplier);
            base.SetBaseValue(Mathf.Max(newMax, 0f));
        }
        else
        {
            // Even without a linked stat, we must update the base value using the offset
            base.SetBaseValue(Mathf.Max(_baseOffset, 0f));
        }
    }

    protected override void RecalculateCurrentValue()
    {
        // We remember the old max and our current percentage
        float oldMax = CurrentValue;
        float percentage = oldMax > 0.001f ? currentAmount / oldMax : 1f;

        // The parent takes care of changing the CurrentValue
        base.RecalculateCurrentValue();

        float newMax = CurrentValue;

        // Use the property to trigger the event (OnAmountChanged)
        CurrentAmount = newMax * percentage;
    }

    public void DecreaseCurrentAmount(float value)
    {
        CurrentAmount = Mathf.Max(0f, CurrentAmount - value);
    }

    /// <summary>
    /// Reduces CurrentAmount by a percentage of MaxValue (0.0 to 1.0).
    /// </summary>
    public void DecreaseCurrentAmountPercent(float percentage)
    {
        DecreaseCurrentAmount(MaxValue * Mathf.Clamp01(percentage));
    }

    public void IncreaseCurrentAmount(float value)
    {
        CurrentAmount = Mathf.Min(MaxValue, CurrentAmount + value);
    }

    /// <summary>
    /// Increases CurrentAmount by a percentage of MaxValue (0.0 to 1.0).
    /// </summary>
    public void IncreaseCurrentAmountPercent(float percentage)
    {
        IncreaseCurrentAmount(MaxValue * Mathf.Clamp01(percentage));
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
