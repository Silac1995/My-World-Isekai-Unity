
using UnityEngine;

[System.Serializable]
public class CharacterMana : CharacterPrimaryStats
{
    public CharacterMana(CharacterStats characterStats, CharacterBaseStats linkedStat = null, float multiplier = 1f, float baseOffset = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset)
    {
    }
}