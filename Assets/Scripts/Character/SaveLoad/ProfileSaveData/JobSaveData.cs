using System.Collections.Generic;

[System.Serializable]
public class JobSaveData
{
    public List<JobAssignmentSaveEntry> jobs = new List<JobAssignmentSaveEntry>();
}

[System.Serializable]
public class JobAssignmentSaveEntry
{
    public string jobType;
    public string workplaceBuildingId;
}
