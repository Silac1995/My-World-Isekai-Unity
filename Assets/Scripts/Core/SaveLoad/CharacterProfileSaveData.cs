// Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs
using System.Collections.Generic;

/// <summary>
/// Portable character profile — the "cartridge" that travels across worlds.
/// Saved to Profiles/{characterGuid}.json via SaveFileHandler.
/// Serialized via Newtonsoft.Json (Dictionary not Unity-Inspector-serializable).
/// </summary>
[System.Serializable]
public class CharacterProfileSaveData
{
    public int profileVersion = 1;

    // Identity
    public string characterGuid;
    public string originWorldGuid;
    public string characterName;
    public string archetypeId;

    public string timestamp;

    // All subsystem states, keyed by ICharacterSaveData.SaveKey
    public Dictionary<string, string> componentStates = new Dictionary<string, string>();

    // Party NPC members (fully serialized, players excluded)
    public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();

    // Worlds this character has visited, with last position per world
    public List<WorldAssociation> worldAssociations = new List<WorldAssociation>();
}
