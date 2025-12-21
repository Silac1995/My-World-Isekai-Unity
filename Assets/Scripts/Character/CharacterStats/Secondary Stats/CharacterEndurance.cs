using UnityEngine;

[System.Serializable]
public class CharacterEndurance : CharacterSecondaryStats
{
    public CharacterEndurance(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        statName = "Endurance";
    }
}