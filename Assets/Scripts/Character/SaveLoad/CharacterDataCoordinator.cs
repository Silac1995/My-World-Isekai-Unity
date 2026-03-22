// Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

[RequireComponent(typeof(Character))]
public class CharacterDataCoordinator : MonoBehaviour
{
    private Character _character;
    
    // Grabs all ISaveables purely on this character (Stats, Equipment, Bio)
    private ISaveable[] _characterSaveables;

    private void Awake()
    {
        _character = GetComponent<Character>();
        _characterSaveables = GetComponentsInChildren<ISaveable>(true);
    }

    /// <summary>
    /// Bundles all local ISaveable states into a CharacterProfileSaveData.
    /// This is called when saving to local disk, or to send to a Multiplayer Host.
    /// </summary>
    public CharacterProfileSaveData ExportProfile()
    {
        // Re-evaluate saveables in case dynamic components were added (e.g., equipment)
        _characterSaveables = GetComponentsInChildren<ISaveable>(true);

        var profile = new CharacterProfileSaveData
        {
            characterName = _character.CharacterName,
            timestamp = DateTime.Now.ToString("o"),
            // Defaulting profileId; in a real deployment this would be uniquely generated at character creation
            profileId = _character.CharacterName.Trim().ToLower().Replace(" ", "_")
        };

        foreach (var s in _characterSaveables)
        {
            // Serialize each specific DTO into JSON and store in the dictionary
            profile.componentStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState());
        }

        // Example logic for party integration (expand based on CharacterParty setup):
        // if (_character.IsPartyLeader()) {
        //     foreach (var member in _character.CurrentParty.Members) {
        //          if (member != _character) profile.partyMembers.Add(member.GetComponent<CharacterDataCoordinator>().ExportProfile());
        //     }
        // }

        return profile;
    }

    /// <summary>
    /// Injects a loaded CharacterProfileSaveData into this character's systems.
    /// </summary>
    public void ImportProfile(CharacterProfileSaveData data)
    {
        // Must find all saveables first to inject data efficiently
        _characterSaveables = GetComponentsInChildren<ISaveable>(true);
        _character.CharacterName = data.characterName;

        foreach (var s in _characterSaveables)
        {
            if (data.componentStates.TryGetValue(s.SaveKey, out string json))
            {
                var stateType = s.CaptureState().GetType();
                var state = JsonConvert.DeserializeObject(json, stateType);
                s.RestoreState(state);
            }
        }
        
        Debug.Log($"<color=cyan>[Profile Injection]</color> Successfully injected profile data for {data.characterName}.");
    }
    
    // --- Local Disk Helpers (For Solo Play) ---

    public async Task SaveLocalProfileAsync()
    {
        var profile = ExportProfile();
        // Prevent writing empty or invalid profile IDs
        if (string.IsNullOrEmpty(profile.profileId)) return;
        
        await SaveFileHandler.WriteProfileAsync(profile.profileId, profile);
        Debug.Log($"<color=cyan>[SaveProfile]</color> Profile {profile.profileId} saved locally.");
    }

    public async Task LoadLocalProfileAsync(string profileId)
    {
        var data = await SaveFileHandler.ReadProfileAsync(profileId);
        if (data != null)
        {
            ImportProfile(data);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[SaveProfile]</color> Profile {profileId} not found on local disk.");
        }
    }

    // --- DEBUG CONTEXT MENUS ---
    [ContextMenu("Debug: Save Local Profile")]
    private void DebugSaveProfile()
    {
        _ = SaveLocalProfileAsync();
    }

    [ContextMenu("Debug: Load Local Profile")]
    private void DebugLoadProfile()
    {
        // Infer the profile ID using the same logic as ExportProfile
        string profileId = _character.CharacterName.Trim().ToLower().Replace(" ", "_");
        _ = LoadLocalProfileAsync(profileId);
    }
}
