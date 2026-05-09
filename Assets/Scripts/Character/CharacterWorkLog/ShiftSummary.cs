/// <summary>
/// Return value of CharacterWorkLog.FinalizeShift — consumed by the wage pipeline.
/// </summary>
public class ShiftSummary
{
    public int ShiftUnits;
    public int NewCareerUnitsForJob;
    public int NewCareerUnitsForJobAndPlace;
    public int ShiftsCompletedAtPlace;
}
