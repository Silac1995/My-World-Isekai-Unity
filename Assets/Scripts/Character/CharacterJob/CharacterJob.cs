using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

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
public class CharacterJob : CharacterSystem, ICharacterSaveData<JobSaveData>, IInteractionProvider
{

    [SerializeField] private List<JobAssignment> _activeJobs = new List<JobAssignment>();

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

    /// <summary>
    /// Read-only check: is this worker allowed to punch out of their current shift right now?
    /// Returns (false, reason) if the worker still carries any item stamped with one of their
    /// active workplaces' BuildingId (unreturned tool from that building's tool storage). Called
    /// by CharacterSchedule on the transition out of a Work slot, and by QuitJob before final
    /// removal.
    ///
    /// Server-authoritative outcome: the gameplay decision (delaying schedule transition,
    /// firing UI toast, etc.) must run server-side. The check itself is idempotent and
    /// side-effect-free.
    ///
    /// Multi-workplace handling: a character may hold several JobAssignments (different shops,
    /// non-overlapping shifts). The gate scans EVERY active workplace — if any one of them owns
    /// an unreturned tool the worker is gated and the reason text lists every offending tool
    /// alongside its owning building so the player UI can disambiguate.
    /// </summary>
    public (bool canPunchOut, string reasonIfBlocked) CanPunchOut()
    {
        if (_activeJobs == null || _activeJobs.Count == 0) return (true, null);

        // Aggregate any unreturned tools across ALL active workplaces. We can't early-out on the
        // first hit because the reason text wants to list every offender for the toast.
        System.Text.StringBuilder names = null;
        for (int i = 0; i < _activeJobs.Count; i++)
        {
            var assn = _activeJobs[i];
            var workplace = assn?.Workplace;
            if (workplace == null) continue;

            if (!workplace.WorkerCarriesUnreturnedTools(_character, out var unreturned)) continue;

            for (int u = 0; u < unreturned.Count; u++)
            {
                var inst = unreturned[u];
                if (inst == null) continue;
                if (names == null) names = new System.Text.StringBuilder();
                if (names.Length > 0) names.Append(", ");
                string itemName = inst.ItemSO != null ? inst.ItemSO.ItemName : "(unknown)";
                string buildingName = !string.IsNullOrEmpty(workplace.BuildingName) ? workplace.BuildingName : "workplace";
                names.Append(itemName).Append(" (").Append(buildingName).Append(")");
            }
        }

        if (names == null) return (true, null);
        return (false, $"Return tools to the tool storage before punching out: {names}.");
    }

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

            // Seed default wage rates from WageSystemService (Task 18). Null-conditional in case
            // the service isn't in the scene yet (early-init / test scenes — Task 19 wires it up).
            // Save-restore path (TryResolvePendingJobs) splices saved wages AFTER TakeJob returns,
            // so unconditional seeding here is safe — restored wages overwrite the defaults.
            WageSystemService.Instance?.SeedAssignmentDefaults(assignment);

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

        // Auto-return any tools the worker holds that are owned by THIS specific workplace
        // BEFORE we clear the workplace reference / remove the assignment. We deliberately
        // scope to the leaving workplace only — concurrent assignments at OTHER buildings
        // keep their stamped tools, since CanPunchOut handles those independently.
        // If auto-return fails (storage full / unreachable / destroyed), the OwnerBuildingId
        // stamp is cleared manually inside TryAutoReturnTools so the worker isn't permanently
        // gated by CanPunchOut on a building they no longer have any contractual link to.
        if (assignment.Workplace != null
            && assignment.Workplace.WorkerCarriesUnreturnedTools(_character, out var unreturned))
        {
            TryAutoReturnTools(assignment.Workplace, unreturned);
        }

        RemoveWorkSchedule(assignment.WorkScheduleEntries);

        if (assignment.Workplace != null)
        {
            assignment.Workplace.RemoveWorker(job);
        }

        _activeJobs.Remove(assignment);

        // Refresh the leaving workplace's Help Wanted sign if hiring is open. Tools-returned
        // → vacancy-now-open → sign-refresh. NotifyVacancyChanged is server-only-effective
        // via the IsServer guard inside HandleVacancyChanged.
        assignment.Workplace?.NotifyVacancyChanged();
    }

    /// <summary>
    /// Best-effort auto-return of unreturned tools to a specific workplace's tool storage.
    /// Called on QuitJob before the workplace reference is cleared. Each instance is removed
    /// from the worker's hand or inventory, then placed back in storage; on storage failure
    /// (null / full / locked) the OwnerBuildingId stamp is cleared and the tool is re-equipped
    /// to the worker's inventory ("salvaged" — designer can repossess via dev tools if needed).
    /// </summary>
    private void TryAutoReturnTools(CommercialBuilding workplace, List<ItemInstance> unreturned)
    {
        if (workplace == null || unreturned == null || _character == null) return;

        var storage = workplace.ToolStorage;
        var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
        var equipment = _character.CharacterEquipment;
        var inventory = equipment != null && equipment.HaveInventory() ? equipment.GetInventory() : null;

        for (int i = 0; i < unreturned.Count; i++)
        {
            var inst = unreturned[i];
            if (inst == null) continue;

            // Detach from the worker: prefer DropCarriedItem if it's the active hand item,
            // otherwise pull it out of the inventory slot. ItemInstance is reference-equal
            // across the system so direct == comparison is correct.
            bool detached = false;
            if (hands != null && hands.IsCarrying && hands.CarriedItem == inst)
            {
                hands.DropCarriedItem();
                detached = true;
            }
            else if (inventory != null)
            {
                detached = inventory.RemoveItem(inst, _character);
            }

            if (!detached)
            {
                // Couldn't find it on the worker anymore (race with another system).
                // Clear the stamp so the residual reference doesn't gate punch-out forever.
                inst.OwnerBuildingId = "";
                Debug.LogWarning($"<color=orange>[CharacterJob]</color> Auto-return: {inst.ItemSO?.ItemName} no longer found on {_character.CharacterName} during QuitJob — stamp cleared defensively.");
                continue;
            }

            // Try to place back in storage. StorageFurniture.AddItem auto-clears
            // OwnerBuildingId via the origin-match hook (Task 4) when the destination
            // is the origin building's ToolStorage.
            if (storage != null && storage.AddItem(inst))
            {
                continue; // returned successfully
            }

            // Fallback: storage destroyed / full / null. Clear the stamp manually so the
            // worker isn't permanently gated, and put the item back in their inventory
            // (treated as "salvaged"; designer can repossess via dev tools if needed).
            inst.OwnerBuildingId = "";
            equipment?.PickUpItem(inst);
            Debug.LogWarning($"[CharacterJob] Auto-return failed for {inst.ItemSO?.ItemName} at {_character.CharacterName}'s QuitJob — storage unreachable. OwnerBuildingId cleared; item kept by worker.");
        }
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
    /// The first CommercialBuilding in the world registry where this character is listed
    /// as an owner. Derived from the replicated `Room._ownerIds` NetworkList via
    /// `Room.IsOwner(Character)`, so the answer is consistent on every peer (host, remote
    /// client, NPC) — no cached field to go stale.
    /// </summary>
    public CommercialBuilding OwnedBuilding
    {
        get
        {
            if (_character == null || BuildingManager.Instance == null) return null;
            var all = BuildingManager.Instance.allBuildings;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is CommercialBuilding commercial && commercial.IsOwner(_character))
                    return commercial;
            }
            return null;
        }
    }

    /// <summary>
    /// Le personnage est-il propriétaire de son lieu de travail ?
    /// </summary>
    public bool IsOwner => OwnedBuilding != null;

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

        // Devenir propriétaire (SetOwner replicates _ownerIds via NetworkList; the
        // derived IsOwner/OwnedBuilding accessors now read from that replicated state,
        // so no cached local field is needed — avoids the dev-mode bypass bug where the
        // cache went stale when ownership was set via Building.SetOwner directly).
        building.SetOwner(_character, optionalJob);

        if (optionalJob != null)
        {
            TakeJob(optionalJob, building);
        }

        Debug.Log($"<color=green>[CharacterJob]</color> {_character.CharacterName} est devenu propriétaire de {building.BuildingName}.");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  IInteractionProvider — hold-E menu entries ("Apply for X")
    // ─────────────────────────────────────────────────────────────

    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        if (interactor == null || interactor == _character) return null;
        if (interactor.CharacterJob == null) return null; // source can't hold jobs

        var options = new List<InteractionOption>();

        // ── A. "Apply for {JobTitle}" entries — interactor wants a job, this character is the boss ──
        var building = OwnedBuilding;
        if (building != null)
        {
            // Iterate the full Jobs list once with a natural stable index; skip assigned.
            // Avoids computing index via GetAvailableJobs + IndexOf (O(N²) + IReadOnlyList.IndexOf unavailable).
            for (int idx = 0; idx < building.Jobs.Count; idx++)
            {
                var job = building.Jobs[idx];
                if (job == null || job.IsAssigned) continue;

                bool disabled = false;
                string reason = null;
                if (interactor.CharacterJob.HasJob)
                {
                    disabled = true;
                    reason = "you already have a job";
                }

                string title = string.IsNullOrEmpty(job.JobTitle) ? "Worker" : job.JobTitle;
                string label = disabled
                    ? $"Apply for {title} ({reason})"
                    : $"Apply for {title}";

                var capturedInteractor = interactor;
                var capturedBuilding = building;
                var capturedJob = job;
                var capturedIdx = idx;
                options.Add(new InteractionOption
                {
                    Name = label,
                    IsDisabled = disabled,
                    Action = () => OnJobEntryClicked(capturedInteractor, capturedBuilding, capturedJob, capturedIdx, disabled)
                });
            }
        }

        // ── B. "Manage Hiring..." entry — interactor IS the boss of some CommercialBuilding ──
        // Emitted on any character menu the local owner-player walks up to. Owner-only by gate
        // (interactor.CharacterJob.OwnedBuilding != null), and the panel itself re-validates
        // ownership server-side via TryOpenHiring/TryCloseHiring/TrySetDisplayText. Multiple
        // building ownership is rare in V1 — when it happens, only the first OwnedBuilding is
        // surfaced here (owner can iterate by approaching a different employee or via a future
        // multi-building selector).
        var interactorOwned = interactor.CharacterJob.OwnedBuilding;
        if (interactorOwned != null)
        {
            var capturedOwned = interactorOwned;
            options.Add(new InteractionOption
            {
                Name = "Manage Hiring...",
                IsDisabled = false,
                Action = () => UI_OwnerHiringPanel.Show(capturedOwned)
            });
        }

        return options.Count > 0 ? options : null;
    }

    private void OnJobEntryClicked(Character interactor, CommercialBuilding building, Job job, int stableIdx, bool disabled)
    {
        if (disabled) return;
        if (interactor == null || building == null || job == null) return;

        if (IsServer)
        {
            var invitation = new InteractionAskForJob(building, job);
            if (invitation.CanExecute(interactor, _character))
                invitation.Execute(interactor, _character);
            return;
        }

        // Remote client → route via ServerRpc on the interactor's own CharacterJob
        if (interactor.CharacterJob == null || _character.NetworkObject == null) return;
        interactor.CharacterJob.RequestJobApplicationServerRpc(_character.NetworkObject.NetworkObjectId, stableIdx);
    }

    [Rpc(SendTo.Server)]
    public void RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetId, out var ownerObj))
            return;
        var owner = ownerObj != null ? ownerObj.GetComponent<Character>() : null;
        if (owner == null || owner.CharacterJob == null || !owner.CharacterJob.IsOwner) return;

        var building = owner.CharacterJob.OwnedBuilding;
        if (building == null) return;
        if (jobStableIndex < 0 || jobStableIndex >= building.Jobs.Count) return;

        var job = building.Jobs[jobStableIndex];
        if (job == null || job.IsAssigned) return; // race: filled since client built menu

        var invitation = new InteractionAskForJob(building, job);
        if (!invitation.CanExecute(_character, owner)) return;
        invitation.Execute(_character, owner);
    }
}
