[System.Serializable]
public class GameSaveData
{
    public int              saveVersion = 1;   // Bump when schema changes
    public SaveSlotMetadata metadata    = new SaveSlotMetadata();

    // Each system's serialized state, keyed by ISaveable.SaveKey
    // Use Newtonsoft JSON for Dictionary support; see serializer note in SKILL.md
    public Dictionary<string, string> systemStates = new Dictionary<string, string>();
}

[System.Serializable]
public class SaveSlotMetadata
{
    public int    slotIndex;
    public string displayName;
    public string sceneName;
    public float  totalPlaytimeSeconds;
    public string timestamp;          // DateTime.Now.ToString("o")
    public bool   isEmpty = true;
}
