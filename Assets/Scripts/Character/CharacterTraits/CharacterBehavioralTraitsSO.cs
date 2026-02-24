using UnityEngine;

[CreateAssetMenu(fileName = "NewBehavioralTraits", menuName = "MWI/Character/Behavioral Traits")]
public class CharacterBehavioralTraitsSO : ScriptableObject
{
    [Header("Behavioral Statistics")]
    [Range(0f, 1f)]
    [Tooltip("How likely the character is to attack enemies unprovoked (0 = Passive, 1 = Very Aggressive).")]
    public float aggressivity = 0f;

    [Range(0f, 1f)]
    [Tooltip("How likely the character is to initiate social interactions with others (0 = Loner, 1 = Very Social).")]
    public float sociability = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("How likely the character is to help acquaintances (not just friends) in combat.")]
    public float loyalty = 0.5f;

    [Header("Abilities")]
    [Tooltip("Can this character found a new community?")]
    public bool canCreateCommunity = false;
}
