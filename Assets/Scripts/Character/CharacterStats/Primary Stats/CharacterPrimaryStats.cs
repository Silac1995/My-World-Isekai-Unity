using UnityEngine;

[System.Serializable]
public abstract class CharacterPrimaryStats : CharacterBaseStats
{
    [SerializeField] protected float currentAmount; // Valeur actuelle de la stat (ex : PV restants)

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
        
        // Initialiser currentAmount à 100% du maxAVANT de recalculer quoi que ce soit
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
    /// Met à jour le multiplicateur et l'offset, utilisés pour modifier les bases raciales de ce personnage.
    /// </summary>
    public void UpdateScaling(float multiplier, float baseOffset)
    {
        _multiplier = multiplier;
        _baseOffset = baseOffset;
        UpdateFromLinkedStat();
    }

    /// <summary>
    /// Recalcule la valeur maximale (BaseValue) de cette stat primaire en fonction de la stat secondaire liée et du multiplicateur.
    /// La préservation du pourcentage est désormais gérée globalement par `RecalculateCurrentValue`.
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
        // On retient l'ancien max et notre pourcentage actuel
        float oldMax = CurrentValue;
        float percentage = oldMax > 0.001f ? currentAmount / oldMax : 1f;

        // Le parent s'occupe de changer le CurrentValue
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
