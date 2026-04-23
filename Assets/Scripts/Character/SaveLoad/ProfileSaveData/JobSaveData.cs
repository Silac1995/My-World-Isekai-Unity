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

    // Wage fields (Task 17). Currency stored as raw int matching CurrencyId.Id.
    // Backward-compatible: missing fields in old saves deserialize to 0 / CurrencyId.Default
    // and get re-seeded by WageSystemService.SeedAssignmentDefaults at hire time (Task 18).
    public int currencyId;
    public int pieceRate;
    public int minimumShiftWage;
    public int fixedShiftWage;
}
