using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bar-type building.
/// Requires a Barman (prepares the drinks) and Servers (serve the customers).
/// </summary>
public class BarBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Bar;

    [Header("Bar Config")]
    [SerializeField] private int _serverCount = 2;

    public int ServerCount => _serverCount;

    protected override void InitializeJobs()
    {
        _jobs.Add(new JobBarman());
        for (int i = 0; i < _serverCount; i++)
        {
            _jobs.Add(new JobServer());
        }

        Debug.Log($"<color=magenta>[Bar]</color> {buildingName} initialized with 1 Barman and {_serverCount} Servers.");
    }

    /// <summary>
    /// Returns the barman of this bar.
    /// </summary>
    public JobBarman GetBarman()
    {
        foreach (var job in _jobs)
        {
            if (job is JobBarman barman) return barman;
        }
        return null;
    }

    /// <summary>
    /// Returns all servers of this bar.
    /// </summary>
    public IEnumerable<JobServer> GetServers()
    {
        return GetJobsOfType<JobServer>();
    }
}
