using System.Collections.Generic;
using UnityEngine;

public abstract class CharacterBaseStats
{
    protected CharacterStats characterStats;
    protected string statName;

    [SerializeField] protected float baseValue; //Raw base value
    [SerializeField] protected float currentValue; //Value with modifiers etc

    private List<float> modifiers = new List<float>();

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

    public void ApplyModifier(float modValue)
    {
        modifiers.Add(modValue);
        RecalculateCurrentValue();
    }

    public void RemoveModifier(float modValue)
    {
        if (modifiers.Remove(modValue))
        {
            RecalculateCurrentValue();
        }
        else
        {
            Debug.LogWarning($"Modifier {modValue} introuvable dans la liste des modificateurs pour {statName}");
        }
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
        modifiers.Clear();
        currentValue = baseValue;
    }

    private void RecalculateCurrentValue()
    {
        float totalModifiers = 0f;
        foreach (var mod in modifiers)
        {
            totalModifiers += mod;
        }
        currentValue = baseValue + totalModifiers;
    }
}
