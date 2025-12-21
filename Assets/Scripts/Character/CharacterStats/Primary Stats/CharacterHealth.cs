using UnityEngine;

[System.Serializable]
public class CharacterHealth : CharacterPrimaryStats
{
    public CharacterHealth(CharacterStats characterStats, float baseValue = 1f)
        : base(characterStats, baseValue)
    {
        statName = "Health";
    }
}
