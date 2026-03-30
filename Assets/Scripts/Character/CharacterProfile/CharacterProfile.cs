using UnityEngine;

public class CharacterProfile : CharacterSystem, ICharacterSaveData<ProfileSaveData>
{
    [SerializeField] private CharacterPersonalitySO _personality;

    public Character Character => _character;
    public CharacterPersonalitySO Personality => _personality;

    // --- ICharacterSaveData IMPLEMENTATION ---
    public string SaveKey => "CharacterProfile";
    public int LoadPriority => 0;

    public ProfileSaveData Serialize()
    {
        var bio = _character.CharacterBio;

        return new ProfileSaveData
        {
            raceId = _character.Race != null ? _character.Race.RaceName : "",
            gender = bio != null ? (bio.IsMale ? 0 : 1) : 0,
            age = bio != null ? bio.Age : 0,
            weight = bio != null ? bio.Weight : 0f,
            height = bio != null ? bio.Height : 0f,
            characterName = _character.CharacterName ?? "",
            visualSeed = _character.NetworkVisualSeed.Value,
            archetypeId = _character.Archetype != null ? _character.Archetype.ArchetypeName : "",
            personalityId = _personality != null ? _personality.PersonalityName : ""
        };
    }

    public void Deserialize(ProfileSaveData data)
    {
        if (data == null) return;

        // Restore character name
        _character.CharacterName = data.characterName;

        // Restore race via Resources lookup
        if (!string.IsNullOrEmpty(data.raceId))
        {
            var allRaces = Resources.LoadAll<RaceSO>("");
            foreach (var race in allRaces)
            {
                if (race.RaceName == data.raceId)
                {
                    _character.InitializeRace(race);
                    break;
                }
            }
        }

        // Restore archetype via Resources lookup
        if (!string.IsNullOrEmpty(data.archetypeId))
        {
            var allArchetypes = Resources.LoadAll<CharacterArchetype>("");
            foreach (var archetype in allArchetypes)
            {
                if (archetype.ArchetypeName == data.archetypeId)
                {
                    // Archetype is a serialized field on Character; set via reflection or
                    // a dedicated setter if one exists. For now, log if mismatch.
                    if (_character.Archetype == null || _character.Archetype.ArchetypeName != data.archetypeId)
                    {
                        Debug.LogWarning($"[CharacterProfile] Archetype mismatch on deserialize: " +
                            $"expected '{data.archetypeId}', current is '{_character.Archetype?.ArchetypeName ?? "null"}'. " +
                            $"Archetype must be set on the prefab or via a dedicated initializer.");
                    }
                    break;
                }
            }
        }

        // Restore personality via Resources lookup
        if (!string.IsNullOrEmpty(data.personalityId))
        {
            var allPersonalities = Resources.LoadAll<CharacterPersonalitySO>("");
            foreach (var personality in allPersonalities)
            {
                if (personality.PersonalityName == data.personalityId)
                {
                    SetPersonality(personality);
                    break;
                }
            }
        }
        else
        {
            _personality = null;
        }

        // Restore bio (gender + age) — bio is reconstructed via Character's existing initialization
        // Gender is stored as int: 0 = Male, 1 = Female
        // Bio reconstruction happens through Character's existing flow, but we can
        // ensure the gender type matches if the bio already exists.
        var bio = _character.CharacterBio;
        if (bio != null)
        {
            if (data.gender == 0)
                bio.SetGenderToMale();
            else
                bio.SetGenderToFemale();
        }
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    // --- END ICharacterSaveData ---

    public void Initialize(Character character)
    {
        _character = character;
    }

    public void SetPersonality(CharacterPersonalitySO personality)
    {
        _personality = personality;
    }

    /// <summary>
    /// Checks base compatibility with another profile.
    /// </summary>
    /// <returns>1 if compatible, -1 if incompatible, 0 if neutral.</returns>
    public int GetCompatibilityWith(CharacterProfile other)
    {
        if (_personality == null || other == null || other.Personality == null) return 0;

        if (_personality.IsCompatibleWith(other.Personality)) return 1;
        if (_personality.IsIncompatibleWith(other.Personality)) return -1;

        return 0;
    }
}
