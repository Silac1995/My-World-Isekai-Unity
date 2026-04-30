using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// STUB — Plan 3 Task 9 will implement the real GOAP-driven JobFarmer.
/// This stub exists so <see cref="FarmingBuilding"/> compiles standalone (Plan 3 Task 4).
///
/// Mirrors <see cref="JobHarvester"/>'s constructor + base contract so
/// <c>FarmingBuilding.InitializeJobs</c> can instantiate it identically. The Execute body
/// is intentionally empty: a Farmer assigned to this stub will sit idle at their building
/// (the BT/Work scheduler still drives clock-in/clock-out via the schedule, but no GOAP
/// plan is produced). Replace wholesale in Task 9.
/// </summary>
[System.Serializable]
public class JobFarmer : Job
{
    [SerializeField] private string _jobTitle = "Farmer";
    [SerializeField] private JobType _jobType = JobType.Farmer;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => _jobType;

    public JobFarmer(string jobTitle = "Farmer", JobType jobType = JobType.Farmer)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    public override void Execute()
    {
        // STUB. Plan 3 Task 9 implements the real GOAP planning loop
        // (PlantCrop / WaterCrop / Harvest / Deposit / FetchTool / ReturnTool).
    }

    /// <summary>
    /// Default farmer schedule: 6h–18h Work. Task 9 may refine by season / crop demand.
    /// </summary>
    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 18, ScheduleActivity.Work, 10)
        };
    }
}
