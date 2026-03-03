using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bâtiment de type Bar.
/// Nécessite un Barman (prépare les boissons) et des Serveurs (servent les clients).
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

        Debug.Log($"<color=magenta>[Bar]</color> {buildingName} initialisé avec 1 Barman et {_serverCount} Serveurs.");
    }

    /// <summary>
    /// Récupère le barman de ce bar.
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
    /// Récupère tous les serveurs de ce bar.
    /// </summary>
    public IEnumerable<JobServer> GetServers()
    {
        return GetJobsOfType<JobServer>();
    }
}
