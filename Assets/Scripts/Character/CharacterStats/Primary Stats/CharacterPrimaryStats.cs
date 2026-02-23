using UnityEngine;

[System.Serializable]
public abstract class CharacterPrimaryStats : CharacterBaseStats
{
    [SerializeField] protected float currentAmount; // Valeur actuelle de la stat (ex : PV restants)

    protected CharacterBaseStats _linkedStat;
    protected float _multiplier;
    protected float _baseOffset;

    public float MaxValue => CurrentValue;

    public float CurrentAmount
    {
        get => currentAmount;
        set => currentAmount = value;
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

        // On conserve exactement le même pourcentage par rapport au nouveau max (ex: rester à 43% d'HP)
        currentAmount = newMax * percentage;
    }

    public void DecreaseCurrentAmount(float value)
    {
        currentAmount = Mathf.Max(0f, currentAmount - value);
    }

    public void IncreaseCurrentAmount(float value)
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
