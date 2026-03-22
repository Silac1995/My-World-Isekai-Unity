using UnityEngine;

public class CharacterProfile : CharacterSystem
{
    [SerializeField] private Character _character;
    [SerializeField] private CharacterPersonalitySO _personality;

    public Character Character => _character;
    public CharacterPersonalitySO Personality => _personality;

    public void Initialize(Character character)
    {
        _character = character;
    }

    public void SetPersonality(CharacterPersonalitySO personality)
    {
        _personality = personality;
    }

    /// <summary>
    /// Vérifie la compatibilité de base avec un autre profil.
    /// </summary>
    /// <returns>1 si compatible, -1 si incompatible, 0 si neutre.</returns>
    public int GetCompatibilityWith(CharacterProfile other)
    {
        if (_personality == null || other == null || other.Personality == null) return 0;

        if (_personality.IsCompatibleWith(other.Personality)) return 1;
        if (_personality.IsIncompatibleWith(other.Personality)) return -1;

        return 0;
    }
}
