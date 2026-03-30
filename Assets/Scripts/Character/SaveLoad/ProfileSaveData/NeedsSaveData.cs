using System.Collections.Generic;

[System.Serializable]
public class NeedsSaveData
{
    public List<NeedSaveEntry> needs = new List<NeedSaveEntry>();
}

[System.Serializable]
public class NeedSaveEntry
{
    public string needType;
    public float value;
}
