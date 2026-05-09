using System.Collections.Generic;

[System.Serializable]
public class ScheduleSaveData
{
    public List<ScheduleEntrySaveData> entries = new List<ScheduleEntrySaveData>();
}

[System.Serializable]
public class ScheduleEntrySaveData
{
    public int activity;
    public int startHour;
    public int endHour;
    public int priority;
}
