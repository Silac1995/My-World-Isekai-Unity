using System;

/// <summary>
/// Runtime record of a character's work history at a single building for a single JobType.
/// BuildingDisplayName is denormalized at first-work-time so the history survives
/// the building being destroyed/abandoned.
/// </summary>
[Serializable]
public class WorkPlaceRecord
{
    public string BuildingId;
    public string BuildingDisplayName;
    public int UnitsWorked;
    public int ShiftsCompleted;
    public int FirstWorkedDay;
    public int LastWorkedDay;
}
