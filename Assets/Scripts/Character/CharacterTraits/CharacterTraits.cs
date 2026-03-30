using UnityEngine;

public class CharacterTraits : CharacterSystem, ICharacterSaveData<TraitsSaveData>
{
    [Header("Behavioral Profile")]
    [Tooltip("ScriptableObject defining this character's behavioral biases.")]
    public CharacterBehavioralTraitsSO behavioralTraitsProfile;

    /// <summary>
    /// Retrieves the numerical intensity of aggressivity (0.0 to 1.0). Returns 0 if no profile is assigned.
    /// </summary>
    public float GetAggressivity()
    {
        return behavioralTraitsProfile != null ? behavioralTraitsProfile.aggressivity : 0f;
    }

    /// <summary>
    /// Retrieves the numerical intensity of sociability (0.0 to 1.0). Returns 0.5 (neutral) if no profile is assigned.
    /// </summary>
    public float GetSociability()
    {
        return behavioralTraitsProfile != null ? behavioralTraitsProfile.sociability : 0.5f;
    }

    /// <summary>
    /// Retrieves the numerical intensity of loyalty (0.0 to 1.0). Returns 0 if no profile is assigned.
    /// </summary>
    public float GetLoyalty()
    {
        return behavioralTraitsProfile != null ? behavioralTraitsProfile.loyalty : 0f;
    }

    /// <summary>
    /// Checks if the character has the ability to found a community.
    /// </summary>
    public bool CanCreateCommunity()
    {
        return behavioralTraitsProfile != null ? behavioralTraitsProfile.canCreateCommunity : false;
    }

    // --- ICharacterSaveData IMPLEMENTATION ---

    public string SaveKey => "CharacterTraits";
    public int LoadPriority => 40;

    public TraitsSaveData Serialize()
    {
        return new TraitsSaveData
        {
            behavioralTraitsProfileId = behavioralTraitsProfile != null ? behavioralTraitsProfile.name : ""
        };
    }

    public void Deserialize(TraitsSaveData data)
    {
        if (data == null) return;

        if (!string.IsNullOrEmpty(data.behavioralTraitsProfileId))
        {
            var allProfiles = Resources.LoadAll<CharacterBehavioralTraitsSO>("Data/Behavioural Traits");
            CharacterBehavioralTraitsSO matched = null;

            foreach (var profile in allProfiles)
            {
                if (profile.name == data.behavioralTraitsProfileId)
                {
                    matched = profile;
                    break;
                }
            }

            if (matched != null)
            {
                behavioralTraitsProfile = matched;
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[CharacterTraits]</color> Could not find behavioral traits profile '{data.behavioralTraitsProfileId}' in Resources. Profile will remain null.");
            }
        }
        else
        {
            behavioralTraitsProfile = null;
        }
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
