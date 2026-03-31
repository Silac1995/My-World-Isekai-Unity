// Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Unity.Netcode;

/// <summary>
/// Facade-level orchestrator for character profile save/load.
/// Lives on the root Character GameObject — discovers all ICharacterSaveData
/// subsystems via GetComponentsInChildren and serializes them by priority order.
/// </summary>
[RequireComponent(typeof(Character))]
public class CharacterDataCoordinator : NetworkBehaviour
{
    private Character _character;
    private const string LOG_TAG = "<color=cyan>[CharacterDataCoordinator]</color>";

    private void Awake()
    {
        _character = GetComponent<Character>();
    }

    // ── Discovery ───────────────────────────────────────────────────

    /// <summary>
    /// Discovers all ICharacterSaveData implementors on this character hierarchy.
    /// Always re-scans to capture dynamically added components.
    /// </summary>
    private ICharacterSaveData[] DiscoverSaveDataSystems()
    {
        return GetComponentsInChildren<ICharacterSaveData>(true);
    }

    // ── Export ───────────────────────────────────────────────────────

    /// <summary>
    /// Collects all ICharacterSaveData subsystems, serializes each into JSON,
    /// and bundles into a CharacterProfileSaveData. Party NPC members are
    /// recursively exported; player members are skipped.
    /// </summary>
    public CharacterProfileSaveData ExportProfile()
    {
        var systems = DiscoverSaveDataSystems();

        var profile = new CharacterProfileSaveData
        {
            characterGuid = _character.CharacterId,
            originWorldGuid = _character.OriginWorldGuid,
            characterName = _character.CharacterName,
            archetypeId = _character.Archetype != null ? _character.Archetype.ArchetypeName : "",
            timestamp = DateTime.UtcNow.ToString("o")
        };

        // Serialize each subsystem
        foreach (var system in systems)
        {
            string key = system.SaveKey;
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"{LOG_TAG} Skipping ICharacterSaveData with null/empty SaveKey on {gameObject.name}.");
                continue;
            }

            try
            {
                string json = system.SerializeToJson();
                profile.componentStates[key] = json;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_TAG} Failed to serialize SaveKey '{key}' on {gameObject.name}: {ex.Message}");
            }
        }

        // Recursively export party NPC members (skip players)
        ExportPartyMembers(profile);

        Debug.Log($"{LOG_TAG} Exported profile '{profile.characterName}' ({profile.characterGuid}) " +
                  $"with {profile.componentStates.Count} component(s) and {profile.partyMembers.Count} party NPC(s).");

        return profile;
    }

    /// <summary>
    /// If this character is a party leader, recursively exports all NPC party members.
    /// Player members are excluded — they save their own profiles independently.
    /// </summary>
    private void ExportPartyMembers(CharacterProfileSaveData profile)
    {
        if (!_character.TryGet<CharacterParty>(out var party)) return;
        if (!party.IsInParty || !party.IsPartyLeader) return;

        var partyData = party.PartyData;
        if (partyData == null) return;

        foreach (string memberId in partyData.MemberIds)
        {
            // Skip self
            if (memberId == _character.CharacterId) continue;

            Character memberCharacter = Character.FindByUUID(memberId);
            if (memberCharacter == null)
            {
                Debug.LogWarning($"{LOG_TAG} Party member '{memberId}' not found in scene — skipping export.");
                continue;
            }

            // Skip player-controlled characters — they manage their own profiles
            if (memberCharacter.IsPlayer())
            {
                Debug.Log($"{LOG_TAG} Skipping player member '{memberCharacter.CharacterName}' in party export.");
                continue;
            }

            var memberCoordinator = memberCharacter.GetComponent<CharacterDataCoordinator>();
            if (memberCoordinator == null)
            {
                Debug.LogWarning($"{LOG_TAG} Party NPC '{memberCharacter.CharacterName}' has no CharacterDataCoordinator — skipping.");
                continue;
            }

            profile.partyMembers.Add(memberCoordinator.ExportProfile());
        }
    }

    // ── Import ───────────────────────────────────────────────────────

    /// <summary>
    /// Restores a character from a CharacterProfileSaveData.
    /// Subsystems are deserialized in LoadPriority order (lower = first).
    /// Overrides the character's network ID to match the saved GUID.
    /// </summary>
    public void ImportProfile(CharacterProfileSaveData data)
    {
        if (data == null)
        {
            Debug.LogError($"{LOG_TAG} Cannot import null profile on {gameObject.name}.");
            return;
        }

        // Restore identity
        _character.CharacterName = data.characterName;
        _character.OriginWorldGuid = data.originWorldGuid;

        // Override the NetworkCharacterId so the character keeps its persistent GUID
        if (IsServer && !string.IsNullOrEmpty(data.characterGuid))
        {
            _character.NetworkCharacterId.Value = data.characterGuid;
        }

        // Discover and sort subsystems by LoadPriority (ascending — lower loads first)
        var systems = DiscoverSaveDataSystems()
            .OrderBy(s => s.LoadPriority)
            .ToArray();

        // Build a set of consumed keys for orphan detection
        var consumedKeys = new HashSet<string>();

        foreach (var system in systems)
        {
            string key = system.SaveKey;
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"{LOG_TAG} ICharacterSaveData with null/empty SaveKey on {gameObject.name} — skipping.");
                continue;
            }

            if (data.componentStates.TryGetValue(key, out string json))
            {
                try
                {
                    system.DeserializeFromJson(json);
                    consumedKeys.Add(key);
                    Debug.Log($"{LOG_TAG} Loaded '{key}' (priority {system.LoadPriority}) on {gameObject.name}.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_TAG} Failed to deserialize SaveKey '{key}' on {gameObject.name}: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"{LOG_TAG} No saved data found for SaveKey '{key}' on {gameObject.name} — subsystem will use defaults.");
            }
        }

        // Log orphaned keys — saved data with no matching subsystem
        foreach (var savedKey in data.componentStates.Keys)
        {
            if (!consumedKeys.Contains(savedKey))
            {
                Debug.LogWarning($"{LOG_TAG} Orphaned save key '{savedKey}' in profile '{data.characterName}' — " +
                                 "no matching ICharacterSaveData found. Data may be from a removed or renamed subsystem.");
            }
        }

        Debug.Log($"{LOG_TAG} Imported profile '{data.characterName}' ({data.characterGuid}) — " +
                  $"{consumedKeys.Count}/{data.componentStates.Count} keys restored, " +
                  $"{data.partyMembers.Count} party NPC(s) pending.");

        // NOTE: Party NPC member import is the responsibility of the spawning system
        // (e.g., CharacterSpawner or MapController) which must instantiate NPC prefabs
        // and call ImportProfile on each. The coordinator only serializes them.
    }

    // ── Local Disk Helpers ───────────────────────────────────────────

    /// <summary>
    /// Exports the current profile and writes it to disk as a local JSON file.
    /// </summary>
    public async Task SaveLocalProfileAsync()
    {
        var profile = ExportProfile();

        if (string.IsNullOrEmpty(profile.characterGuid))
        {
            Debug.LogError($"{LOG_TAG} Cannot save profile — characterGuid is null/empty on {gameObject.name}.");
            return;
        }

        // Update WorldAssociation for current world
        string currentWorldGuid = SaveManager.Instance?.CurrentWorldGuid;
        if (!string.IsNullOrEmpty(currentWorldGuid))
        {
            var association = profile.worldAssociations.Find(w => w.worldGuid == currentWorldGuid);
            if (association == null)
            {
                association = new WorldAssociation();
                profile.worldAssociations.Add(association);
            }

            association.worldGuid = currentWorldGuid;
            association.worldName = SaveManager.Instance?.CurrentWorldName ?? "";
            association.lastPlayed = System.DateTime.Now.ToString("o");

            // Get current map and position
            var tracker = _character.GetComponentInChildren<CharacterMapTracker>();
            if (tracker != null)
            {
                association.lastMapId = tracker.CurrentMapID.Value.ToString();
                association.positionX = _character.transform.position.x;
                association.positionY = _character.transform.position.y;
                association.positionZ = _character.transform.position.z;
            }
        }

        await SaveFileHandler.WriteProfileAsync(profile.characterGuid, profile);
        Debug.Log($"{LOG_TAG} Profile '{profile.characterName}' ({profile.characterGuid}) saved to disk.");
    }

    /// <summary>
    /// Reads a profile from disk and imports it into this character.
    /// </summary>
    public async Task LoadLocalProfileAsync(string characterGuid)
    {
        if (string.IsNullOrEmpty(characterGuid))
        {
            Debug.LogError($"{LOG_TAG} Cannot load profile — characterGuid is null/empty.");
            return;
        }

        var data = await SaveFileHandler.ReadProfileAsync(characterGuid);
        if (data != null)
        {
            ImportProfile(data);
        }
        else
        {
            Debug.LogWarning($"{LOG_TAG} Profile '{characterGuid}' not found on local disk.");
        }
    }

    // ── Debug Context Menus ──────────────────────────────────────────

    [ContextMenu("Debug: Save Local Profile")]
    private void DebugSaveProfile()
    {
        _ = SaveLocalProfileAsync();
    }

    [ContextMenu("Debug: Load Local Profile")]
    private void DebugLoadProfile()
    {
        string characterGuid = _character.CharacterId;
        if (string.IsNullOrEmpty(characterGuid))
        {
            Debug.LogWarning($"{LOG_TAG} Cannot debug-load — CharacterId is empty on {gameObject.name}.");
            return;
        }
        _ = LoadLocalProfileAsync(characterGuid);
    }

    [ContextMenu("Debug: Save Profile + World")]
    private void DebugSaveProfileAndWorld()
    {
        _ = SaveLocalProfileAsync();
        if (SaveManager.Instance != null)
            _ = SaveManager.Instance.SaveWorldAsync();
        else
            Debug.LogWarning($"{LOG_TAG} SaveManager.Instance is null — world not saved.");
    }

    [ContextMenu("Debug: Log All SaveKeys")]
    private void DebugLogAllSaveKeys()
    {
        var systems = DiscoverSaveDataSystems();
        Debug.Log($"{LOG_TAG} Found {systems.Length} ICharacterSaveData system(s) on {gameObject.name}:");
        foreach (var system in systems.OrderBy(s => s.LoadPriority))
        {
            Debug.Log($"  - SaveKey: '{system.SaveKey}' | Priority: {system.LoadPriority} | Type: {system.GetType().Name}");
        }
    }
}
