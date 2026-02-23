using UnityEngine;

[System.Serializable]
public abstract class CharacterTertiaryStats : CharacterBaseStats
{
    [SerializeField] private float minValue = 1f;

    protected CharacterBaseStats _linkedStat;
    protected float _multiplier;

    // Optionnel : un offset de base fixe, utile pour la MoveSpeed par exemple (5f + Agi*0.1)
    protected float _baseOffset;

    protected CharacterTertiaryStats(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, baseOffset)
    {
        _linkedStat = linkedStat;
        _multiplier = multiplier;
        _baseOffset = baseOffset;
        this.minValue = minValue;

        UpdateFromLinkedStat();
    }

    public float Value => currentValue;

    public void Modify(float delta)
    {
        currentValue = Mathf.Max(currentValue + delta, minValue);
    }

    /// <summary>
    /// Met à jour le multiplicateur et l'offset, utilisé pour modifier les bases raciales de ce personnage.
    /// </summary>
    public void UpdateScaling(float multiplier, float baseOffset)
    {
        _multiplier = multiplier;
        _baseOffset = baseOffset;
        UpdateFromLinkedStat();
    }

    /// <summary>
    /// Recalcule la valeur de cette stat tertiaire en fonction de la stat secondaire li??e et du multiplicateur.
    /// </summary>
    public void UpdateFromLinkedStat()
    {
        if (_linkedStat != null)
        {
            float calculatedBase = _baseOffset + (_linkedStat.CurrentValue * _multiplier);
            SetBaseValue(Mathf.Max(calculatedBase, minValue));
        }
        else
        {
            SetBaseValue(Mathf.Max(_baseOffset, minValue));
        }
    }
}
