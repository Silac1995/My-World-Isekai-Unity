using UnityEngine;

[System.Serializable]
public class CharacterHealth : CharacterPrimaryStats
{
    public CharacterHealth(CharacterStats characterStats, CharacterBaseStats linkedStat = null, float multiplier = 1f, float baseOffset = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset)
    {
        statName = "Health";
    }

    /// <summary>
    /// Restores Health, capping at MaxValue.
    /// </summary>
    public void Heal(float amount)
    {
        IncreaseCurrentAmount(amount);
    }

    /// <summary>
    /// Restores Health by a percentage of MaxValue (0.0 to 1.0), capping at MaxValue.
    /// </summary>
    public void HealPercent(float percentage)
    {
        IncreaseCurrentAmountPercent(percentage);
    }
}
