using System.Collections.Generic;

[System.Serializable]
public class CombatSaveData
{
    public List<CombatStyleSaveEntry> knownStyles = new List<CombatStyleSaveEntry>();
    public string preferredStyleId;
}

[System.Serializable]
public class CombatStyleSaveEntry
{
    public string styleId;
    public int level;
    public float experience;
}
