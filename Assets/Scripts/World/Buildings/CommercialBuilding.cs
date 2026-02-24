using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Classe abstraite pour les bâtiments commerciaux.
/// Chaque type de building commercial (Bar, Shop...) hérite de cette classe
/// et override InitializeJobs() pour définir ses postes de travail.
/// </summary>
public abstract class CommercialBuilding : Building
{
    [Header("Commercial")]
    [SerializeField] protected Character _owner;

    protected List<Job> _jobs = new List<Job>();

    public Character Owner => _owner;
    public IReadOnlyList<Job> Jobs => _jobs;

    /// <summary>
    /// Le building est opérationnel si tous les jobs sont occupés par un worker.
    /// </summary>
    public bool IsOperational => _jobs.Count > 0 && _jobs.TrueForAll(j => j.IsAssigned);

    protected override void Awake()
    {
        base.Awake();
        InitializeJobs();
    }

    /// <summary>
    /// Chaque sous-classe crée ses jobs spécifiques ici.
    /// Ex: BarBuilding crée un JobBarman + des JobServer.
    /// </summary>
    protected abstract void InitializeJobs();

    /// <summary>
    /// Assigne un worker à un job spécifique dans ce building.
    /// </summary>
    public bool AssignWorker(Character worker, Job job)
    {
        if (worker == null || job == null) return false;
        if (!_jobs.Contains(job)) return false;
        if (job.IsAssigned) return false;

        job.Assign(worker, this);
        return true;
    }

    /// <summary>
    /// Retire un worker d'un job.
    /// </summary>
    public void RemoveWorker(Job job)
    {
        if (job == null || !_jobs.Contains(job)) return;
        job.Unassign();
    }

    /// <summary>
    /// Trouve le premier job disponible (non occupé) d'un type donné.
    /// </summary>
    public T FindAvailableJob<T>() where T : Job
    {
        foreach (var job in _jobs)
        {
            if (job is T typedJob && !typedJob.IsAssigned)
                return typedJob;
        }
        return null;
    }

    /// <summary>
    /// Récupère tous les jobs d'un type donné.
    /// </summary>
    public List<T> GetJobsOfType<T>() where T : Job
    {
        List<T> result = new List<T>();
        foreach (var job in _jobs)
        {
            if (job is T typedJob)
                result.Add(typedJob);
        }
        return result;
    }

    /// <summary>
    /// Fait travailler tous les employés assignés.
    /// Appelé régulièrement (par le BuildingManager ou par Update).
    /// </summary>
    public void UpdateWorkCycle()
    {
        foreach (var job in _jobs)
        {
            if (job.CanExecute())
            {
                job.Execute();
            }
        }
    }

    public void SetOwner(Character newOwner)
    {
        _owner = newOwner;
        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} est propriétaire de {buildingName}.");
    }
}
