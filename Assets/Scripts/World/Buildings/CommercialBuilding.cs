using System.Collections.Generic;
using System.Linq;
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
    protected List<Character> _activeWorkersOnShift = new List<Character>();

    public Character Owner => _owner;
    public IReadOnlyList<Job> Jobs => _jobs;
    public IReadOnlyList<Character> ActiveWorkersOnShift => _activeWorkersOnShift;

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
    public IEnumerable<T> GetJobsOfType<T>() where T : Job
    {
        return _jobs.OfType<T>();
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

    public void SetOwner(Character newOwner, Job ownerJob = null)
    {
        _owner = newOwner;
        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} est propriétaire de {buildingName}.");

        if (ownerJob == null)
        {
            // Y a-t-il DÉJÀ quelqu'un qui est assigné (occupé) à un JobLogisticsManager dans ce building ?
            bool hasActiveLogisticsManager = _jobs.OfType<JobLogisticsManager>().Any(j => j.IsAssigned);

            if (!hasActiveLogisticsManager)
            {
                // S'il n'y a personne pour faire la logistique, le boss DOIT prendre ce poste
                ownerJob = _jobs.OfType<JobLogisticsManager>().FirstOrDefault();
            }

            // Si vraiment il y a déjà un logisticien (ou si le bâtiment n'en a pas du tout), on prend un autre poste libre
            if (ownerJob == null)
            {
                ownerJob = GetAvailableJobs().FirstOrDefault();
            }
        }

        // Le boss peut aussi prendre un job dans son building
        if (ownerJob != null && newOwner != null)
        {
            var charJob = newOwner.CharacterJob;
            if (charJob != null)
            {
                charJob.TakeJob(ownerJob, this);
            }
        }
    }

    /// <summary>
    /// Le building a-t-il un owner/boss ?
    /// </summary>
    public bool HasOwner => _owner != null && _owner.IsAlive();

    /// <summary>
    /// Un personnage demande un job au boss de ce building.
    /// Retourne true si l'embauche est acceptée.
    /// </summary>
    public bool AskForJob(Character applicant, Job job)
    {
        if (applicant == null || job == null) return false;

        // Il faut un boss pour embaucher
        if (!HasOwner)
        {
            Debug.Log($"<color=red>[Building]</color> {buildingName} n'a pas de boss. Personne ne peut embaucher.");
            return false;
        }

        // Le job doit exister dans ce building
        if (!_jobs.Contains(job))
        {
            Debug.Log($"<color=red>[Building]</color> Le poste {job.JobTitle} n'existe pas dans {buildingName}.");
            return false;
        }

        // Le job doit être libre
        if (job.IsAssigned)
        {
            Debug.Log($"<color=orange>[Building]</color> Le poste {job.JobTitle} à {buildingName} est déjà pris.");
            return false;
        }

        // Si le job est un métier d'artisanat, on vérifie les prérequis de compétences
        if (job is JobCrafter crafterJob)
        {
            if (!crafterJob.CheckRequirements(applicant))
            {
                Debug.Log($"<color=orange>[Building]</color> {applicant.CharacterName} n'a pas les compétences requises pour le poste de {job.JobTitle}.");
                return false;
            }
        }

        // Embauche approuvée. On retourne true pour que le CharacterJob.TakeJob()
        // puisse s'occuper de synchroniser l'assignation des deux côtés (Employé et Bâtiment).
        return true;
    }

    /// <summary>
    /// Retourne tous les jobs non-assignés dans ce building.
    /// </summary>
    public IEnumerable<Job> GetAvailableJobs()
    {
        return _jobs.Where(j => !j.IsAssigned);
    }

    /// <summary>
    /// Appelé par un employé lorsqu'il arrive physiquement sur son lieu de travail
    /// et commence (Punch In).
    /// </summary>
    public virtual void WorkerStartingShift(Character worker)
    {
        if (worker != null && !_activeWorkersOnShift.Contains(worker))
        {
            _activeWorkersOnShift.Add(worker);
            Debug.Log($"<color=green>[Building]</color> {worker.CharacterName} a pointé (Punch In) à {buildingName}.");
        }
    }

    /// <summary>
    /// Appelé par un employé lorsqu'il quitte son comportement de travail
    /// (fin de journée, événement spécial) (Punch Out).
    /// </summary>
    public virtual void WorkerEndingShift(Character worker)
    {
        if (worker != null && _activeWorkersOnShift.Contains(worker))
        {
            _activeWorkersOnShift.Remove(worker);
            Debug.Log($"<color=orange>[Building]</color> {worker.CharacterName} a dépointé (Punch Out) de {buildingName}.");
        }
    }
}
