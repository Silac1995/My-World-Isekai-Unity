using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewPersonality", menuName = "MWI/Character/Personality")]
public class CharacterPersonalitySO : ScriptableObject
{
    [Header("Identity")]
    public string PersonalityName;
    [TextArea] public string Description;

    [Header("Compatibility")]
    [Tooltip("Personalities this personality gets along with.")]
    public List<CharacterPersonalitySO> CompatiblePersonalities = new List<CharacterPersonalitySO>();

    [Tooltip("Personalities this personality dislikes.")]
    public List<CharacterPersonalitySO> IncompatiblePersonalities = new List<CharacterPersonalitySO>();

    public bool IsCompatibleWith(CharacterPersonalitySO other)
    {
        if (other == null) return false;
        return CompatiblePersonalities.Contains(other);
    }

    public bool IsIncompatibleWith(CharacterPersonalitySO other)
    {
        if (other == null) return false;
        return IncompatiblePersonalities.Contains(other);
    }
}
