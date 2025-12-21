using UnityEngine;

[System.Serializable]
public class CharacterStamina : CharacterPrimaryStats
{
    public CharacterStamina(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Stamina";
    }
}