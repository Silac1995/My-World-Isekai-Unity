using UnityEngine;

[System.Serializable]
public class CharacterStamina : CharacterPrimaryStats
{
    public CharacterStamina(CharacterStats characterStats, CharacterBaseStats linkedStat = null, float multiplier = 1f, float baseOffset = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset)
    {
        statName = "Stamina";
    }
}