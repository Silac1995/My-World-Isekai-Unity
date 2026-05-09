using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct StatRestoreEntry
{
    public StatType stat;
    [Tooltip("Amount to restore. Negative values drain the stat.")]
    public float value;
    [Tooltip("If true, value is a fraction of the stat's max (0-1 range).")]
    public bool isPercentage;
}

public static class StatRestoreProcessor
{
    public static void ApplyRestores(IReadOnlyList<StatRestoreEntry> restores, Character target)
    {
        if (restores == null || target == null) return;

        var stats = target.Stats;
        foreach (var restore in restores)
        {
            var stat = stats.GetBaseStat(restore.stat);
            if (stat == null) continue;

            if (stat is CharacterPrimaryStats primaryStat)
            {
                if (restore.isPercentage)
                {
                    if (restore.value >= 0f)
                        primaryStat.IncreaseCurrentAmountPercent(restore.value);
                    else
                        primaryStat.DecreaseCurrentAmountPercent(Mathf.Abs(restore.value));
                }
                else
                {
                    if (restore.value >= 0f)
                        primaryStat.IncreaseCurrentAmount(restore.value);
                    else
                        primaryStat.DecreaseCurrentAmount(Mathf.Abs(restore.value));
                }
            }
        }
    }
}
