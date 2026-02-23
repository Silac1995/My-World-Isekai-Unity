using UnityEngine;

[System.Serializable]
public class CharacterHealth : CharacterPrimaryStats
{
    public CharacterHealth(CharacterStats characterStats, CharacterBaseStats linkedStat = null, float multiplier = 1f, float baseOffset = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset)
    {
        statName = "Health";
    }
}
