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

    // Party NPC members (fully serialized, players excluded).
    // [NonSerialized] keeps Unity's reflection-based serializer from walking the self-
    // referencing CharacterProfileSaveData → partyMembers → CharacterProfileSaveData
    // type cycle. Without it, Unity hits its depth-10 limit while scanning anything
    // that reaches this type through a [SerializeField] / [Serializable] chain (e.g.
    // WildernessZone._wildlife → HibernatedNPCData.ProfileData). In the editor it
    // logs a warning and continues; in standalone Mono builds the same recursion
    // overflows the native stack during scene load and crashes the player.
    // Newtonsoft.Json (used by SaveFileHandler with default settings, where
    // IgnoreSerializableAttribute = true) ignores [NonSerialized] and still writes
    // and reads this field via the JSON profile — party persistence is preserved.
    [System.NonSerialized]
    public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();

    // Worlds this character has visited, with last position per world
    public List<WorldAssociation> worldAssociations = new List<WorldAssociation>();
}
