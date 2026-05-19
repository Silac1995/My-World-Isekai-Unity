// Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs
using System.Collections.Generic;
using UnityEngine;

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

    // Speech-bubble accent colour. hasAccentColorOverride distinguishes "use the
    // archetype default" (false) from "use the per-character override" (true) so a
    // future tweak to the archetype's default is NOT silently masked by every saved
    // character. See Task 6 of the speech-bubble rework plan.
    public Color accentColorOverride;
    public bool hasAccentColorOverride;

    public string timestamp;

    // All subsystem states, keyed by ICharacterSaveData.SaveKey
    public Dictionary<string, string> componentStates = new Dictionary<string, string>();

    // Party NPC members. [NonSerialized] stops Unity's reflection serializer from
    // walking the CharacterProfileSaveData -> partyMembers -> CharacterProfileSaveData
    // type cycle (hits Unity's depth-10 limit; standalone Mono build crashes natively
    // during scene load). Newtonsoft.Json (used by SaveFileHandler with default
    // IgnoreSerializableAttribute=true) ignores [NonSerialized] and still round-trips
    // this field via the JSON profile, so party persistence is preserved.
    [System.NonSerialized]
    public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();

    // Worlds this character has visited, with last position per world
    public List<WorldAssociation> worldAssociations = new List<WorldAssociation>();
}
