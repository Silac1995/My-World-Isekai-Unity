using System;
using System.Collections.Generic;

[Serializable]
public class WorkLogSaveData
{
    public List<WorkLogJobEntry> jobs = new List<WorkLogJobEntry>();
}

[Serializable]
public class WorkLogJobEntry
{
    // JobType serialized as enum NAME (string) to match existing JobAssignmentSaveEntry.jobType pattern.
    public string jobType;
    public List<WorkPlaceSaveEntry> workplaces = new List<WorkPlaceSaveEntry>();
}

[Serializable]
public class WorkPlaceSaveEntry
{
    public string buildingId;
    public string buildingDisplayName;
    public int unitsWorked;
    public int shiftsCompleted;
    public int firstWorkedDay;
    public int lastWorkedDay;
}
