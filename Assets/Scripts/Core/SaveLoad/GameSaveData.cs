// Assets/Scripts/Core/SaveLoad/GameSaveData.cs
using System;
using System.Collections.Generic;

[System.Serializable]
public class GameSaveData
{
    public int saveVersion = 1;
    public SaveSlotMetadata metadata = new SaveSlotMetadata();
    
    // Each world system's serialized state, keyed by ISaveable.SaveKey.
    // Example keys: "TimeManager", "BuildingManager", "DroppedItems"
    public Dictionary<string, string> worldStates = new Dictionary<string, string>();
}

[System.Serializable]
public class SaveSlotMetadata
{
    public int slotIndex;
    public string displayName;
    public string worldName;
    public string worldGuid;  // Unique GUID for this world instance, generated once at world creation
    public float totalPlaytimeSeconds;
    public string timestamp;
    public bool isEmpty = true;
}
