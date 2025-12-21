using UnityEngine;

[System.Serializable]
public class CharacterArmor : CharacterPrimaryStats
{
    public CharacterArmor(CharacterStats characterStats, float baseValue = 1f)
        : base(characterStats, baseValue)
    {
        statName = "Armor";
    }
}
