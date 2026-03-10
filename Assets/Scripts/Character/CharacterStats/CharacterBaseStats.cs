using System.Collections.Generic;
using UnityEngine;

public abstract class CharacterBaseStats
{
    protected CharacterStats characterStats;
    protected string statName;

    [SerializeField] protected float baseValue; //Raw base value
    [SerializeField] protected float currentValue; //Value with modifiers etc

    private List<StatModifier> modifiers = new List<StatModifier>();
    public event System.Action<float, float> OnValueChanged; // oldValue, newValue

    public CharacterBaseStats(CharacterStats characterStats, float baseValue = 1f)
    {
        this.characterStats = characterStats;
        this.baseValue = baseValue;
        this.currentValue = baseValue;
    }

    public string StatName => statName;
    public float BaseValue => baseValue;
    public float CurrentValue => currentValue;

    public void SetBaseValue(float value)
    {
        baseValue = value;
        RecalculateCurrentValue();
    }

    public void ApplyModifier(StatModifier modifier)
    {
        if (modifier == null) return;
        modifiers.Add(modifier);
        RecalculateCurrentValue();
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        if (modifier == null) return false;

        if (modifiers.Remove(modifier))
        {
            RecalculateCurrentValue();
            return true;
        }
        else
        {
            Debug.LogWarning($"Modifier introuvable dans la liste des modificateurs pour {statName}");
            return false;
        }
    }

    /// <summary>
    /// Retire tous les modificateurs provenant d'une source spécifique.
    /// </summary>
    public bool RemoveAllModifiersFromSource(object source)
    {
        if (modifiers == null) return false;

        int removedCount = modifiers.RemoveAll(mod => mod.Source == source);
        if (removedCount > 0)
        {
            RecalculateCurrentValue();
            return true;
        }
        return false;
    }

    public void IncreaseBaseValue(float value)
    {
        baseValue += value;
        RecalculateCurrentValue();
    }

    public void DecreaseBaseValue(float value)
    {
        baseValue -= value;
        RecalculateCurrentValue();
    }

    public void ModifyBaseValue(float value)
    {
        baseValue = value;
        RecalculateCurrentValue();
    }

    public void Reset()
    {
        float previousValue = currentValue;
        if (modifiers != null) modifiers.Clear();
        currentValue = baseValue;

        if (Mathf.Abs(previousValue - currentValue) > 0.001f)
        {
            OnValueChanged?.Invoke(previousValue, currentValue);
        }
    }

    protected virtual void RecalculateCurrentValue()
    {
        if (modifiers == null) modifiers = new List<StatModifier>();

        float totalModifiers = 0f;
        foreach (var mod in modifiers)
        {
            totalModifiers += mod.Value;
        }

        float previousValue = currentValue;
        currentValue = baseValue + totalModifiers;

        if (Mathf.Abs(previousValue - currentValue) > 0.001f)
        {
            OnValueChanged?.Invoke(previousValue, currentValue);
        }
    }
}
