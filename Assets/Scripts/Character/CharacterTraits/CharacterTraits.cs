using UnityEngine;

public class CharacterTraits : MonoBehaviour
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
}
