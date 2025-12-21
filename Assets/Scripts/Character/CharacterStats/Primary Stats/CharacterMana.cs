
using UnityEngine;

[System.Serializable]
public class CharacterMana : CharacterPrimaryStats
{
    public CharacterMana(CharacterStats characterStats, float baseValue = 1) : base(characterStats, baseValue)
    {
    }
}