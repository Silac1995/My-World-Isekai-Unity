using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class JobAssignment
{
    [SerializeReference] public Job AssignedJob;
    public CommercialBuilding Workplace;
    public List<ScheduleEntry> WorkScheduleEntries = new List<ScheduleEntry>();
}

/// <summary>
/// Composant attaché au personnage pour gérer ses différents jobs actuels.
/// Permet d'assigner, quitter, et exécuter plusieurs jobs dans des CommercialBuilding.
/// </summary>
public class CharacterJob : CharacterSystem
{

    [SerializeField] private List<JobAssignment> _activeJobs = new List<JobAssignment>();
    private CommercialBuilding _ownedBuilding; // Lieu dont il est prioritaire

    public Character Character => _character;
    public IReadOnlyList<JobAssignment> ActiveJobs => _activeJobs;
    public bool HasJob => _activeJobs.Count > 0;
    
    // Rétrocompatibilité : Retourne le job actif à l'heure actuelle, ou le premier par défaut
    public Job CurrentJob
    {
        get
        {
            if (_activeJobs.Count == 0) return null;
            int h = MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentHour : 12;
            foreach (var j in _activeJobs)
            {
                if (j.WorkScheduleEntries.Any(e => h >= e.startHour && h < e.endHour))
                    return j.AssignedJob;
            }
            return _activeJobs[0].AssignedJob;
        }
    }

    public CommercialBuilding Workplace
    {
        get
        {
            if (_activeJobs.Count == 0) return null;
            int h = MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentHour : 12;
            foreach (var j in _activeJobs)
            {
                if (j.WorkScheduleEntries.Any(e => h >= e.startHour && h < e.endHour))
                    return j.Workplace;
            }
            return _activeJobs[0].Workplace;
        }
    }

    public bool IsWorking => HasJob && _activeJobs.Any(j => j.AssignedJob.IsAssigned);

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Assigne un job au personnage dans un building commercial s'il n'y a pas de chevauchement d'horaires.
    /// </summary>
    public bool TakeJob(Job job, CommercialBuilding building)
    {
        if (job == null || building == null) return false;

        var newEntries = job.GetWorkSchedule();
        if (DoesScheduleOverlap(newEntries))
        {
            Debug.LogWarning($"<color=orange>[CharacterJob]</color> {_character.CharacterName} ne peut pas prendre le poste de {job.JobTitle} car les horaires se chevauchent.");
            return false;
        }

        // Assigner le nouveau job coté building
        if (building.AssignWorker(_character, job))
        {
            var assignment = new JobAssignment
            {
                AssignedJob = job,
                Workplace = building,
                WorkScheduleEntries = newEntries
            };

            _activeJobs.Add(assignment);

            // Injecter les horaires de travail dans le schedule du joueur
            InjectWorkSchedule(newEntries);

            Debug.Log($"<color=yellow>[CharacterJob]</color> {_character.CharacterName} a pris le poste de {job.JobTitle} à {building.BuildingName}.");
            return true;
        }

        return false;
    }

    private bool DoesScheduleOverlap(List<ScheduleEntry> newEntries)
    {
        foreach (var newEntry in newEntries)
        {
            foreach (var activeJob in _activeJobs)
            {
                foreach (var existingEntry in activeJob.WorkScheduleEntries)
                {
                    // L'heure de début de l'un est avant la fin de l'autre
                    if (newEntry.startHour < existingEntry.endHour && existingEntry.startHour < newEntry.endHour)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Le personnage quitte un job spécifique.
    /// </summary>
    public void QuitJob(Job job)
    {
        var assignment = _activeJobs.FirstOrDefault(j => j.AssignedJob == job);
        if (assignment == null) return;

        Debug.Log($"<color=yellow>[CharacterJob]</color> {_character.CharacterName} quitte son poste de {job.JobTitle} à {assignment.Workplace?.BuildingName}.");

        RemoveWorkSchedule(assignment.WorkScheduleEntries);

        if (assignment.Workplace != null)
        {
            assignment.Workplace.RemoveWorker(job);
        }

        _activeJobs.Remove(assignment);
    }

    /// <summary>
    /// Rétrocompatibilité : quitte tous les jobs d'un coup.
    /// </summary>
    public void QuitJob()
    {
        var jobsToQuit = _activeJobs.Select(j => j.AssignedJob).ToList();
        foreach (var j in jobsToQuit)
        {
            QuitJob(j);
        }
    }

    private void InjectWorkSchedule(List<ScheduleEntry> entries)
    {
        var schedule = _character.CharacterSchedule;
        if (schedule != null && entries != null)
        {
            schedule.InjectJobSchedule(entries);
        }
    }

    private void RemoveWorkSchedule(List<ScheduleEntry> entries)
    {
        var schedule = _character.CharacterSchedule;
        if (schedule != null && entries != null)
        {
            schedule.RemoveJobSchedule(entries);
        }
    }

    /// <summary>
    /// Exécute le travail actuel correspondant à l'heure du jour.
    /// </summary>
    public void Work()
    {
        var job = CurrentJob;
        if (job != null && job.CanExecute())
        {
            job.Execute();
        }
    }

    /// <summary>
    /// Vérifie si le personnage travaille dans un building spécifique.
    /// </summary>
    public bool WorksAt(CommercialBuilding building)
    {
        return _activeJobs.Any(j => j.Workplace == building);
    }

    /// <summary>
    /// Vérifie si le personnage a un job d'un type spécifique.
    /// </summary>
    public bool HasJobOfType<T>() where T : Job
    {
        return _activeJobs.Any(j => j.AssignedJob is T);
    }

    /// <summary>
    /// Le personnage est-il propriétaire de son lieu de travail ?
    /// </summary>
    public bool IsOwner => _ownedBuilding != null && _ownedBuilding.Owner == _character;

    /// <summary>
    /// Demande un job en passant par le building.
    /// </summary>
    public bool AskForJob(CommercialBuilding building, Job job)
    {
        if (building == null || job == null) return false;

        // Demande au building (qui check le boss)
        if (building.AskForJob(_character, job))
        {
            // Le building a accepté, du coup on est techniquement embauché
            // Mais AskForJob de CommercialBuilding appelle souvent `AssignWorker` 
            // qui lui-même rappelle `TakeJob`. Si ça se boucle mal, ça peut foirer.
            // On s'assure juste d'appeler notre TakeJob interne :
            return TakeJob(job, building);
        }

        return false;
    }

    /// <summary>
    /// Forcefully assigns a job, dissolving any existing overlapping jobs the character might have.
    /// Used by Community Leaders to impose work.
    /// </summary>
    public bool ForceAssignJob(Job job, CommercialBuilding building)
    {
        if (job == null || building == null) return false;

        var newEntries = job.GetWorkSchedule();
        
        // Find all overlapping jobs
        List<Job> jobsToQuit = new List<Job>();
        foreach (var newEntry in newEntries)
        {
            foreach (var activeJob in _activeJobs)
            {
                foreach (var existingEntry in activeJob.WorkScheduleEntries)
                {
                    if (newEntry.startHour < existingEntry.endHour && existingEntry.startHour < newEntry.endHour)
                    {
                        if (!jobsToQuit.Contains(activeJob.AssignedJob)) 
                            jobsToQuit.Add(activeJob.AssignedJob);
                    }
                }
            }
        }

        // Quit the overlapping jobs (which clears the schedule)
        foreach (var overlappingJob in jobsToQuit)
        {
            Debug.LogWarning($"<color=orange>[CharacterJob]</color> {_character.CharacterName} is forced to quit {overlappingJob.JobTitle} to make room for an imposed job.");
            QuitJob(overlappingJob);
        }

        // Now take the job normally, it will pass DoesScheduleOverlap
        return TakeJob(job, building);
    }

    /// <summary>
    /// Devient propriétaire. Optionnellement, prend le 1er job dans la foulée.
    /// </summary>
    public bool BecomeOwner(CommercialBuilding building, Job optionalJob = null)
    {
        if (building == null) return false;

        // Devenir propriétaire (SetOwner assigne le Owner du building)
        building.SetOwner(_character, optionalJob);
        _ownedBuilding = building;

        if (optionalJob != null)
        {
            TakeJob(optionalJob, building);
        }

        Debug.Log($"<color=green>[CharacterJob]</color> {_character.CharacterName} est devenu propriétaire de {building.BuildingName}.");
        return true;
    }
}
