using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class JobAssignment
{
    [SerializeReference] public Job AssignedJob;
    public CommercialBuilding Workplace;
    public List<ScheduleEntry> WorkScheduleEntries = new List<ScheduleEntry>();

    // Wage fields. Seeded at hire time via WageSystemService.SeedAssignmentDefaults.
    // Owner-editable at runtime via SetWage(). Persisted via JobAssignmentSaveEntry (Task 17).
    public MWI.Economy.CurrencyId Currency;
    public int PieceRate;          // coins per shift unit (piece-work jobs only)
    public int MinimumShiftWage;   // floor for piece-work jobs (additive, prorated)
    public int FixedShiftWage;     // shop / vendor / barman / server / logistics manager

    /// <summary>
    /// Mutate wage rates at runtime. Use null to leave a field unchanged.
    /// Caller is responsible for authorization (owner / community leader gate is in Task 27).
    /// Returns true if any field was changed.
    /// </summary>
    public bool SetWage(int? pieceRate = null, int? minimumShift = null, int? fixedShift = null)
    {
        bool changed = false;
        if (pieceRate.HasValue && pieceRate.Value >= 0 && pieceRate.Value != PieceRate) { PieceRate = pieceRate.Value; changed = true; }
        if (minimumShift.HasValue && minimumShift.Value >= 0 && minimumShift.Value != MinimumShiftWage) { MinimumShiftWage = minimumShift.Value; changed = true; }
        if (fixedShift.HasValue && fixedShift.Value >= 0 && fixedShift.Value != FixedShiftWage) { FixedShiftWage = fixedShift.Value; changed = true; }
        return changed;
    }
}

/// <summary>
/// Composant attaché au personnage pour gérer ses différents jobs actuels.
/// Permet d'assigner, quitter, et exécuter plusieurs jobs dans des CommercialBuilding.
/// </summary>
public class CharacterJob : CharacterSystem, ICharacterSaveData<JobSaveData>
{

    [SerializeField] private List<JobAssignment> _activeJobs = new List<JobAssignment>();
    private CommercialBuilding _ownedBuilding; // Lieu dont il est prioritaire

    /// <summary>
    /// Saved job data from deserialization, keyed on workplace BuildingId. Resolved
    /// when the matching CommercialBuilding registers with BuildingManager (event-driven —
    /// works for hibernated workplaces that haven't spawned yet).
    /// </summary>
    private List<JobAssignmentSaveEntry> _pendingJobData = new List<JobAssignmentSaveEntry>();
    private bool _waitingForBuildings = false;

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

    // --- ICharacterSaveData<JobSaveData> IMPLEMENTATION ---

    public string SaveKey => "CharacterJob";
    public int LoadPriority => 60;

    public JobSaveData Serialize()
    {
        var data = new JobSaveData();

        foreach (var assignment in _activeJobs)
        {
            if (assignment.AssignedJob == null) continue;

            var entry = new JobAssignmentSaveEntry
            {
                jobType = assignment.AssignedJob.GetType().Name,
                workplaceBuildingId = assignment.Workplace != null ? assignment.Workplace.BuildingId : "",
                // Wage fields (Task 17). Currency stored as raw int matching CurrencyId.Id.
                currencyId = assignment.Currency.Id,
                pieceRate = assignment.PieceRate,
                minimumShiftWage = assignment.MinimumShiftWage,
                fixedShiftWage = assignment.FixedShiftWage
            };
            data.jobs.Add(entry);
        }

        // Re-serialize any pending data that was not resolved this session
        foreach (var pending in _pendingJobData)
        {
            bool alreadySerialized = data.jobs.Exists(e =>
                e.jobType == pending.jobType && e.workplaceBuildingId == pending.workplaceBuildingId);
            if (!alreadySerialized)
            {
                data.jobs.Add(pending);
            }
        }

        return data;
    }

    public void Deserialize(JobSaveData data)
    {
        UnsubscribeBuildingResolver();
        _pendingJobData.Clear();

        if (data == null || data.jobs == null) return;

        foreach (var entry in data.jobs)
        {
            if (entry == null || string.IsNullOrEmpty(entry.workplaceBuildingId) || string.IsNullOrEmpty(entry.jobType))
                continue;
            _pendingJobData.Add(entry);
        }

        if (_pendingJobData.Count == 0) return;

        // Try buildings already registered (e.g. workplace map is currently active).
        TryResolvePendingJobs();

        // Anything left? Subscribe and let BuildingManager.OnBuildingRegistered drive future binds.
        // Covers the case where the workplace map is hibernated and will only spawn its buildings later.
        if (_pendingJobData.Count > 0 && !_waitingForBuildings)
        {
            BuildingManager.OnBuildingRegistered += HandleBuildingRegistered;
            _waitingForBuildings = true;
            Debug.Log($"<color=cyan>[CharacterJob:Restore]</color> {_character?.CharacterName}: subscribed to OnBuildingRegistered for {_pendingJobData.Count} pending job(s).");
        }
    }

    private void HandleBuildingRegistered(Building building)
    {
        if (building == null || _pendingJobData.Count == 0) return;
        TryResolvePendingJobs();
        if (_pendingJobData.Count == 0) UnsubscribeBuildingResolver();
    }

    private void TryResolvePendingJobs()
    {
        if (_character == null || BuildingManager.Instance == null) return;

        for (int i = _pendingJobData.Count - 1; i >= 0; i--)
        {
            var entry = _pendingJobData[i];
            Building b = BuildingManager.Instance.FindBuildingById(entry.workplaceBuildingId);
            if (b is not CommercialBuilding workplace) continue;

            // Already linked (e.g. building-side resolver beat us to it)?
            bool alreadyActive = _activeJobs.Any(a =>
                a.Workplace == workplace &&
                a.AssignedJob != null &&
                a.AssignedJob.GetType().Name == entry.jobType);
            if (alreadyActive)
            {
                _pendingJobData.RemoveAt(i);
                continue;
            }

            // Find a free job slot of the saved type.
            Job freeJob = null;
            foreach (var j in workplace.Jobs)
            {
                if (j == null || j.IsAssigned) continue;
                if (j.GetType().Name == entry.jobType) { freeJob = j; break; }
            }
            if (freeJob == null)
            {
                Debug.LogWarning($"<color=orange>[CharacterJob:Restore]</color> {_character.CharacterName}: no free '{entry.jobType}' slot at '{workplace.BuildingName}'. Dropping.");
                _pendingJobData.RemoveAt(i);
                continue;
            }

            if (TakeJob(freeJob, workplace))
            {
                // Restore wage fields (Task 17). Wages are intrinsic to the assignment and
                // don't depend on the building being live, but the JobAssignment instance is
                // only created inside TakeJob — so we splice them in here, after the fact.
                var newAssignment = _activeJobs.FirstOrDefault(a => a.Workplace == workplace && a.AssignedJob == freeJob);
                if (newAssignment != null)
                {
                    newAssignment.Currency = new MWI.Economy.CurrencyId(entry.currencyId);
                    newAssignment.PieceRate = entry.pieceRate;
                    newAssignment.MinimumShiftWage = entry.minimumShiftWage;
                    newAssignment.FixedShiftWage = entry.fixedShiftWage;
                }
                Debug.Log($"<color=green>[CharacterJob:Restore]</color> {_character.CharacterName} re-bound to '{entry.jobType}' at '{workplace.BuildingName}'.");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[CharacterJob:Restore]</color> {_character.CharacterName}: TakeJob failed for '{entry.jobType}' at '{workplace.BuildingName}'.");
            }
            _pendingJobData.RemoveAt(i);
        }
    }

    private void UnsubscribeBuildingResolver()
    {
        if (!_waitingForBuildings) return;
        BuildingManager.OnBuildingRegistered -= HandleBuildingRegistered;
        _waitingForBuildings = false;
    }

    private void OnDestroy()
    {
        UnsubscribeBuildingResolver();
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    // --- OWNERSHIP ---

    /// <summary>
    /// Devient propriétaire. Optionnellement, prend le 1er job dans la foulée.
    /// Server-only: forwards to `CommercialBuilding.SetOwner` which now early-returns on
    /// non-server callers.
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
