using System;
using UnityEngine;

public enum ComparisonType
{
    AboveOrEqual,
    BelowOrEqual
}

[Serializable]
public struct StatusEffectSuspendCondition
{
    [Tooltip("Which stat to monitor for the suspend condition.")]
    public StatType statType;

    [Tooltip("The threshold value to compare against.")]
    public float threshold;

    [Tooltip("If true, threshold is a fraction (0-1) of the stat's max. Only valid for Primary stats.")]
    public bool isPercentage;

    [Tooltip("When this comparison is true, the effect suspends.")]
    public ComparisonType comparison;

    public bool Evaluate(CharacterStats stats)
    {
        var stat = stats.GetBaseStat(statType);
        if (stat == null) return false;

        float currentValue;

        if (stat is CharacterPrimaryStats primaryStat)
        {
            if (isPercentage)
                currentValue = primaryStat.MaxValue > 0f
                    ? primaryStat.CurrentAmount / primaryStat.MaxValue
                    : 0f;
            else
                currentValue = primaryStat.CurrentAmount;
        }
        else
        {
            currentValue = stat.CurrentValue;
        }

        return comparison == ComparisonType.AboveOrEqual
            ? currentValue >= threshold
            : currentValue <= threshold;
    }
}
