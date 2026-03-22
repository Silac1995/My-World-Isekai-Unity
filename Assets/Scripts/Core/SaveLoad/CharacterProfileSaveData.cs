// Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs
using System.Collections.Generic;

/// <summary>
/// This is the core Terraria/Valheim style Save Profile.
/// It travels with the player across solo and multiplayer worlds.
/// </summary>
[System.Serializable]
public class CharacterProfileSaveData
{
    public int profileVersion = 1;

    // Basic Identify
    public string characterName;
    public string profileId; // GUID to track the profile uniquely
    public string timestamp;

    // Component States (Stats, Equipment, Inventory, Progression)
    // Keyed by ISaveable.SaveKey specific to the Character.
    public Dictionary<string, string> componentStates = new Dictionary<string, string>();

    // The player's current active party. When they join a server, their party comes with them.
    public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();
}
