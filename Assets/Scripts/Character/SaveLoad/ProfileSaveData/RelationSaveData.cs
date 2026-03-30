using System.Collections.Generic;

[System.Serializable]
public class RelationSaveData
{
    public List<RelationshipSaveEntry> relationships = new List<RelationshipSaveEntry>();
}

[System.Serializable]
public class RelationshipSaveEntry
{
    public string targetCharacterId;
    public string targetWorldGuid;
    public int relationshipType;
    public int relationValue;
    public bool hasMet;
}
