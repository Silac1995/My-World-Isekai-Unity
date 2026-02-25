using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bâtiment de type Forge.
/// Nécessite un Forgeron (craft les objets) et optionnellement des Apprentis.
/// Le boss est typiquement le forgeron principal.
/// </summary>
public class ForgeBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Workshop;

    [Header("Forge Config")]
    [SerializeField] private int _apprenticeCount = 1;

    public int ApprenticeCount => _apprenticeCount;

    protected override void InitializeJobs()
    {
        _jobs.Add(new JobBlacksmith());
        for (int i = 0; i < _apprenticeCount; i++)
        {
            _jobs.Add(new JobBlacksmithApprentice());
        }

        Debug.Log($"<color=orange>[Forge]</color> {buildingName} initialisée avec 1 Forgeron et {_apprenticeCount} Apprenti(s).");
    }

    /// <summary>
    /// Récupère le job de forgeron de ce building.
    /// </summary>
    public JobBlacksmith GetBlacksmith()
    {
        foreach (var job in _jobs)
        {
            if (job is JobBlacksmith smith) return smith;
        }
        return null;
    }

    /// <summary>
    /// Récupère tous les apprentis de cette forge.
    /// </summary>
    public List<JobBlacksmithApprentice> GetApprentices()
    {
        return GetJobsOfType<JobBlacksmithApprentice>();
    }
}
