using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Classe abstraite pour les bâtiments commerciaux.
/// Chaque type de building commercial (Bar, Shop...) hérite de cette classe
/// et override InitializeJobs() pour définir ses postes de travail.
/// </summary>
[RequireComponent(typeof(BuildingTaskManager))]
[RequireComponent(typeof(BuildingLogisticsManager))]
public abstract class CommercialBuilding : Building
{
    [Header("Commercial")]
    [SerializeField] protected Community _ownerCommunity; // Collective owner
    [SerializeField] protected Zone _storageZone;

    [Tooltip("Optional outbound staging zone. When authored, the building's Logistics Manager " +
             "moves reserved ItemInstances from StorageZone into this zone before an incoming " +
             "transporter arrives, and transporters path to this zone instead of the raw " +
             "WorldItem position. Leave empty to keep legacy behaviour (transporter walks " +
             "straight into the StorageZone). Prefer a small area adjacent to the building's " +
             "exterior door — authored, not networked, so no NetworkVariable cost.")]
    [SerializeField] protected Zone _pickupZone;

    protected List<Job> _jobs = new List<Job>();
    protected List<Character> _activeWorkersOnShift = new List<Character>();
    protected List<ItemInstance> _inventory = new List<ItemInstance>();

    protected BuildingTaskManager _taskManager;
    protected BuildingLogisticsManager _logisticsManager;

    // Per-active-worker punch-in time in hours (TimeManager.CurrentTime01 * 24f).
    // Used at punch-out to compute attendance ratio for wage calculation.
    private readonly System.Collections.Generic.Dictionary<Character, float> _punchInTimeByWorker
        = new System.Collections.Generic.Dictionary<Character, float>();

    public Character Owner => _ownerIds.Count > 0 ? Character.FindByUUID(_ownerIds[0].ToString()) : null;
    public Community OwnerCommunity => _ownerCommunity;
    public IReadOnlyList<Job> Jobs => _jobs;
    public IReadOnlyList<Character> ActiveWorkersOnShift => _activeWorkersOnShift;
    public Zone StorageZone => _storageZone;
    public Zone PickupZone => _pickupZone;
    public IReadOnlyList<ItemInstance> Inventory => _inventory;
    
    public BuildingTaskManager TaskManager => _taskManager;
    public BuildingLogisticsManager LogisticsManager => _logisticsManager;

    /// <summary>
    /// Le building est opérationnel si tous les jobs sont occupés par un worker et s'il a terminé sa construction.
    /// </summary>
    public bool IsOperational => !IsUnderConstruction && _jobs.Count > 0 && _jobs.TrueForAll(j => j.IsAssigned);

    protected override void Awake()
    {
        base.Awake();
        
        _taskManager = gameObject.GetComponent<BuildingTaskManager>();
        if (_taskManager == null)
        {
            _taskManager = gameObject.AddComponent<BuildingTaskManager>();
        }
        
        _logisticsManager = gameObject.GetComponent<BuildingLogisticsManager>();
        if (_logisticsManager == null)
        {
            _logisticsManager = gameObject.AddComponent<BuildingLogisticsManager>();
        }

        InitializeJobs();
        HookQuestPublishingEvents();
    }

    // =========================================================================
    // QUEST PUBLISHING (Task 16 — aggregates TaskManager + LogisticsManager events)
    // =========================================================================

    /// <summary>Fires after a new IQuest has been enqueued at this building (TaskManager or OrderBook).</summary>
    public event System.Action<MWI.Quests.IQuest> OnQuestPublished;

    /// <summary>Fires when a tracked IQuest's state changes (join/leave/progress/completion).</summary>
    public event System.Action<MWI.Quests.IQuest> OnQuestStateChanged;

    private void HookQuestPublishingEvents()
    {
        if (_taskManager != null)
        {
            _taskManager.OnTaskRegistered += HandleTaskRegistered;
        }
        if (_logisticsManager != null && _logisticsManager.OrderBook != null)
        {
            _logisticsManager.OrderBook.OnBuyOrderAdded += HandleOrderPublished;
            _logisticsManager.OrderBook.OnTransportOrderAdded += HandleOrderPublished;
            _logisticsManager.OrderBook.OnCraftingOrderAdded += HandleOrderPublished;
        }
    }

    private void HandleTaskRegistered(BuildingTask task) => PublishQuest(task);
    private void HandleOrderPublished(MWI.Quests.IQuest quest) => PublishQuest(quest);

    /// <summary>Stamp issuer + world/map, subscribe to state changes, fire OnQuestPublished.</summary>
    private void PublishQuest(MWI.Quests.IQuest quest)
    {
        if (quest == null) return;

        Character issuer = ResolveIssuer();
        if (issuer != null) AssignIssuerIfPossible(quest, issuer);
        StampOriginWorldAndMap(quest);

        quest.OnStateChanged += HandleQuestStateChanged;
        OnQuestPublished?.Invoke(quest);
    }

    private void HandleQuestStateChanged(MWI.Quests.IQuest quest) => OnQuestStateChanged?.Invoke(quest);

    private static void AssignIssuerIfPossible(MWI.Quests.IQuest quest, Character issuer)
    {
        // IQuest.Issuer is declared get-only; concrete types expose public setters.
        switch (quest)
        {
            case BuildingTask bt: bt.Issuer = issuer; break;
            case BuyOrder bo: bo.Issuer = issuer; break;
            case TransportOrder to: to.Issuer = issuer; break;
            case CraftingOrder co: co.Issuer = issuer; break;
        }
    }

    private void StampOriginWorldAndMap(MWI.Quests.IQuest quest)
    {
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        string mapId = mapController != null ? mapController.MapId : string.Empty;
        string worldId = string.Empty;  // TODO: source from active WorldAssociation when the singleton is available to buildings.
        switch (quest)
        {
            case BuildingTask bt: bt.StampOrigin(worldId, mapId); break;
            case BuyOrder bo: bo.OriginWorldId = worldId; bo.OriginMapId = mapId; break;
            case TransportOrder to: to.OriginWorldId = worldId; to.OriginMapId = mapId; break;
            case CraftingOrder co: co.OriginWorldId = worldId; co.OriginMapId = mapId; break;
        }
    }

    /// <summary>
    /// Resolve the Character who acts as the quest issuer for this building. Priority:
    /// 1. An assigned JobLogisticsManager worker.
    /// 2. The building's Owner (if set and alive).
    /// 3. null (system-issued — quest still functional).
    /// </summary>
    private Character ResolveIssuer()
    {
        var lmJob = _jobs.OfType<JobLogisticsManager>().FirstOrDefault(j => j.IsAssigned);
        if (lmJob != null && lmJob.Worker != null) return lmJob.Worker;
        if (HasOwner) return Owner;
        return null;
    }

    /// <summary>Enumerate every IQuest this building currently publishes (TaskManager + OrderBook).</summary>
    public System.Collections.Generic.IEnumerable<MWI.Quests.IQuest> GetAvailableQuests()
    {
        if (_taskManager != null)
        {
            foreach (var task in _taskManager.AvailableTasks) yield return task;
        }
        if (_logisticsManager != null && _logisticsManager.OrderBook != null)
        {
            foreach (var bo in _logisticsManager.OrderBook.PlacedBuyOrders) yield return bo;
            foreach (var to in _logisticsManager.OrderBook.PlacedTransportOrders) yield return to;
            foreach (var co in _logisticsManager.OrderBook.ActiveCraftingOrders) yield return co;
        }
    }

    /// <summary>Find a tracked IQuest by id (used by save/load reconciliation on CharacterQuestLog).</summary>
    public MWI.Quests.IQuest GetQuestById(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return null;
        foreach (var q in GetAvailableQuests())
        {
            if (q.QuestId == questId) return q;
        }
        return null;
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

    public void SetOwner(Character newOwner, Job ownerJob = null, bool autoAssignJob = true)
    {
        if (!IsServer) return;

        // Remove from old community
        if (_ownerCommunity != null && _ownerCommunity.ownedBuildings.Contains(this))
        {
            _ownerCommunity.ownedBuildings.Remove(this);
        }

        // Replicate owner via _ownerIds (mirror ResidentialBuilding). Server-only write;
        // clients receive the change via NetworkList replication.
        while (_ownerIds.Count > 0) _ownerIds.RemoveAt(0);
        if (newOwner != null) AddOwner(newOwner); // Inherited from Room — adds newOwner.CharacterId to _ownerIds.

        // Add to new community if applicable
        if (newOwner != null && newOwner.CharacterCommunity != null && newOwner.CharacterCommunity.CurrentCommunity != null)
        {
            _ownerCommunity = newOwner.CharacterCommunity.CurrentCommunity;
            if (!_ownerCommunity.ownedBuildings.Contains(this))
            {
                _ownerCommunity.ownedBuildings.Add(this);
            }
        }
        else
        {
            _ownerCommunity = null;
        }

        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} est propriétaire de {buildingName}.");

        // Restore path passes autoAssignJob=false because the saved Employees list
        // already carries the boss's actual job (avoids the auto-pick stealing a slot
        // that another saved employee owns).
        if (!autoAssignJob) return;

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

    // ---------------------------------------------------------------------------------
    //  Save / Load — Owner & Employee restoration
    // ---------------------------------------------------------------------------------

    /// <summary>Owner UUIDs awaiting Character spawn (server-only).</summary>
    private readonly List<string> _pendingOwnerIds = new List<string>();
    /// <summary>Employee assignments awaiting Character spawn (server-only).</summary>
    private readonly List<EmployeeSaveEntry> _pendingEmployees = new List<EmployeeSaveEntry>();
    /// <summary>True while subscribed to <see cref="Character.OnCharacterSpawned"/>.</summary>
    private bool _waitingForCharacters = false;

    /// <summary>
    /// Server-only. Re-binds saved owner + employees back to this freshly-spawned building.
    /// Characters that aren't loaded yet are queued — a Character.OnCharacterSpawned subscription
    /// retries them until everyone is bound or the building despawns.
    ///
    /// Call once, immediately after the building is NetworkObject.Spawn()'d and its
    /// NetworkBuildingId has been overwritten with the saved GUID.
    /// </summary>
    public void RestoreFromSaveData(List<string> ownerIds, List<EmployeeSaveEntry> employees)
    {
        if (!IsServer) return;

        _pendingOwnerIds.Clear();
        _pendingEmployees.Clear();

        if (ownerIds != null)
        {
            foreach (var id in ownerIds)
            {
                if (!string.IsNullOrEmpty(id)) _pendingOwnerIds.Add(id);
            }
        }
        if (employees != null)
        {
            foreach (var e in employees)
            {
                if (e != null && !string.IsNullOrEmpty(e.CharacterId) && !string.IsNullOrEmpty(e.JobType))
                    _pendingEmployees.Add(e);
            }
        }

        if (_pendingOwnerIds.Count == 0 && _pendingEmployees.Count == 0) return;

        Debug.Log($"<color=cyan>[CommercialBuilding:Restore]</color> {buildingName}: pending owners={_pendingOwnerIds.Count}, employees={_pendingEmployees.Count}");

        // Try to resolve everything that's already loaded.
        TryResolvePending();

        // Anything left? Subscribe and let OnCharacterSpawned drive future binds.
        if ((_pendingOwnerIds.Count > 0 || _pendingEmployees.Count > 0) && !_waitingForCharacters)
        {
            Character.OnCharacterSpawned += HandleCharacterSpawnedForRestore;
            _waitingForCharacters = true;
            Debug.Log($"<color=cyan>[CommercialBuilding:Restore]</color> {buildingName}: subscribed to OnCharacterSpawned for {_pendingOwnerIds.Count} owner(s) + {_pendingEmployees.Count} employee(s).");
        }
    }

    private void HandleCharacterSpawnedForRestore(Character spawned)
    {
        if (!IsServer || spawned == null) return;
        TryResolvePending();

        if (_pendingOwnerIds.Count == 0 && _pendingEmployees.Count == 0)
        {
            UnsubscribeRestoreListener();
        }
    }

    private void TryResolvePending()
    {
        // --- Owners ---
        for (int i = _pendingOwnerIds.Count - 1; i >= 0; i--)
        {
            string id = _pendingOwnerIds[i];
            Character owner = Character.FindByUUID(id);
            if (owner == null) continue;

            // Find the owner's saved job (if any) so we can restore it directly,
            // bypassing SetOwner's auto-job-pick (which could conflict with employees).
            Job ownerJob = null;
            var ownerEmployeeEntry = _pendingEmployees.FirstOrDefault(e => e.CharacterId == id);
            if (ownerEmployeeEntry != null)
            {
                ownerJob = FindFreeJobByType(ownerEmployeeEntry.JobType);
            }

            // Apply ownership without auto-pick. If the saved data had an explicit ownerJob,
            // SetOwner's tail will route it to TakeJob.
            SetOwner(owner, ownerJob, autoAssignJob: false);

            // The TakeJob inside SetOwner only fires when ownerJob != null (and autoAssign=false),
            // so explicitly call it here when we resolved a job slot for the owner.
            if (ownerJob != null && owner.CharacterJob != null)
            {
                owner.CharacterJob.TakeJob(ownerJob, this);
                _pendingEmployees.Remove(ownerEmployeeEntry);
            }

            Debug.Log($"<color=green>[CommercialBuilding:Restore]</color> {buildingName}: bound owner '{owner.CharacterName}' (id={id}).");
            _pendingOwnerIds.RemoveAt(i);
        }

        // --- Employees ---
        for (int i = _pendingEmployees.Count - 1; i >= 0; i--)
        {
            var entry = _pendingEmployees[i];
            Character worker = Character.FindByUUID(entry.CharacterId);
            if (worker == null) continue;

            Job job = FindFreeJobByType(entry.JobType);
            if (job == null)
            {
                // Job type missing from this building's roster (data drift) —
                // log and drop so we don't keep retrying forever.
                Debug.LogWarning($"<color=orange>[CommercialBuilding:Restore]</color> {buildingName}: no free '{entry.JobType}' slot for worker '{worker.CharacterName}'. Dropping pending entry.");
                _pendingEmployees.RemoveAt(i);
                continue;
            }

            if (worker.CharacterJob != null && worker.CharacterJob.TakeJob(job, this))
            {
                Debug.Log($"<color=green>[CommercialBuilding:Restore]</color> {buildingName}: bound '{worker.CharacterName}' to {entry.JobType}.");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[CommercialBuilding:Restore]</color> {buildingName}: TakeJob failed for '{worker.CharacterName}' on '{entry.JobType}' (schedule conflict?). Dropping pending entry.");
            }
            _pendingEmployees.RemoveAt(i);
        }
    }

    private Job FindFreeJobByType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        foreach (var j in _jobs)
        {
            if (j == null || j.IsAssigned) continue;
            if (j.GetType().Name == typeName) return j;
        }
        return null;
    }

    private void UnsubscribeRestoreListener()
    {
        if (!_waitingForCharacters) return;
        Character.OnCharacterSpawned -= HandleCharacterSpawnedForRestore;
        _waitingForCharacters = false;
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeRestoreListener();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Le building a-t-il un owner/boss (individuel) ?
    /// </summary>
    public bool HasOwner
    {
        get
        {
            Character o = Owner;
            return o != null && o.IsAlive();
        }
    }

    /// <summary>
    /// Checks if this building is located in a map that has a recognized Community Leader.
    /// </summary>
    public bool HasCommunityLeader()
    {
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        if (mapController != null && MWI.WorldSystem.MapRegistry.Instance != null)
        {
            var comm = MWI.WorldSystem.MapRegistry.Instance.GetCommunity(mapController.MapId);
            if (comm != null && !string.IsNullOrEmpty(comm.LeaderNpcId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Un personnage demande un job au boss de ce building (ou au leader de la communauté).
    /// Retourne true si l'embauche est acceptée.
    /// </summary>
    public bool AskForJob(Character applicant, Job job)
    {
        if (applicant == null || job == null) return false;

        // Il faut un boss pour embaucher (boss direct ou leader de communauté)
        if (!HasOwner && !HasCommunityLeader())
        {
            Debug.Log($"<color=red>[Building]</color> {buildingName} n'a pas de boss ni de leader de communauté. Personne ne peut embaucher.");
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

        // Vérifie les prérequis spécifiques du métier (ex: compétences pour un artisan)
        if (!job.CanTakeJob(applicant))
        {
            Debug.Log($"<color=orange>[Building]</color> {applicant.CharacterName} n'a pas les prérequis pour le poste de {job.JobTitle}.");
            return false;
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
    /// Authorization predicate reused by owner-side mutation entry points
    /// (currently <see cref="TrySetAssignmentWage"/>; future owner-edit UI).
    /// Mirrors the AskForJob hiring gate: a request is authorized when the
    /// requester is either the building's direct owner OR the leader of the
    /// community whose map this building lives in.
    /// </summary>
    private bool IsAuthorizedToManage(Character requester)
    {
        if (requester == null) return false;

        // Direct owner check — reuses Room.IsOwner(Character), which compares
        // against the replicated _ownerIds NetworkList.
        if (IsOwner(requester)) return true;

        // Community-leader check — mirrors HasCommunityLeader's lookup but
        // verifies the requester's CharacterId matches the recorded leader.
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        if (mapController != null && MWI.WorldSystem.MapRegistry.Instance != null)
        {
            var comm = MWI.WorldSystem.MapRegistry.Instance.GetCommunity(mapController.MapId);
            if (comm != null && !string.IsNullOrEmpty(comm.LeaderNpcId) &&
                comm.LeaderNpcId == requester.CharacterId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Server-authoritative wrapper around JobAssignment.SetWage that enforces
    /// the same owner / community-leader authorization gate used by AskForJob.
    /// Use null arguments to leave a wage field unchanged.
    /// Returns true if the change was authorized AND any field was modified.
    /// </summary>
    public bool TrySetAssignmentWage(Character requester, Character worker,
        int? pieceRate = null, int? minimumShift = null, int? fixedShift = null)
    {
        if (requester == null || worker == null)
        {
            Debug.LogError("[CommercialBuilding] TrySetAssignmentWage: null requester or worker.");
            return false;
        }

        // Server-authority guard — mirrors CharacterWallet.AddCoins (Task 7).
        // In offline/Solo (no NetworkManager listening), allow the call so the
        // single-player path keeps working.
        if (!IsServer && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("[CommercialBuilding] TrySetAssignmentWage called on non-server instance. Route through a ServerRpc.");
            return false;
        }

        if (!IsAuthorizedToManage(requester))
        {
            Debug.LogWarning($"[CommercialBuilding] TrySetAssignmentWage denied: {requester.CharacterName} is not authorized to manage {name}.");
            return false;
        }

        var charJob = worker.CharacterJob;
        if (charJob == null) return false;

        foreach (var assn in charJob.ActiveJobs)
        {
            if (assn.Workplace == this && assn.AssignedJob != null)
            {
                return assn.SetWage(pieceRate, minimumShift, fixedShift);
            }
        }

        Debug.LogWarning($"[CommercialBuilding] TrySetAssignmentWage: worker {worker.CharacterName} has no assignment at {name}.");
        return false;
    }

    /// <summary>
    /// Appelé par un employé lorsqu'il arrive physiquement sur son lieu de travail
    /// et commence (Punch In).
    /// </summary>
    public virtual void WorkerStartingShift(Character worker)
    {
        // Wage system hook: record punch-in time + notify worker's WorkLog.
        if (worker != null)
        {
            float nowHours = MWI.Time.TimeManager.Instance != null
                ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
                : 0f;
            _punchInTimeByWorker[worker] = nowHours;

            var workLog = worker.CharacterWorkLog;
            if (workLog != null)
            {
                // Find the assignment for THIS worker at THIS building so we know the JobType + scheduled end.
                var charJob = worker.CharacterJob;
                if (charJob != null)
                {
                    foreach (var assn in charJob.ActiveJobs)
                    {
                        if (assn.Workplace == this && assn.AssignedJob != null)
                        {
                            var jobType = assn.AssignedJob.Type;
                            // Find the scheduled end-of-shift in 0..1 time-of-day from this assignment's WorkScheduleEntries.
                            float scheduledEndTime01 = ComputeScheduledEndTime01(assn);
                            workLog.OnPunchIn(jobType, GetBuildingIdForWorklog(), GetBuildingDisplayNameForWorklog(), scheduledEndTime01);
                            break;
                        }
                    }
                }
            }
        }

        if (worker != null && !_activeWorkersOnShift.Contains(worker))
        {
            _activeWorkersOnShift.Add(worker);
            Debug.Log($"<color=green>[Building]</color> {worker.CharacterName} a pointé (Punch In) à {buildingName}.");

            // Déclencher la logique logistique si c'est le manager
            if (worker.CharacterJob != null)
            {
                var logisticsJob = worker.CharacterJob.ActiveJobs
                    .Select(j => j.AssignedJob)
                    .OfType<JobLogisticsManager>()
                    .FirstOrDefault(j => j.Workplace == this);

                if (logisticsJob != null)
                {
                    logisticsJob.OnWorkerPunchIn();
                }
            }

            // Quest system hook: auto-claim eligible already-published quests + subscribe for
            // future publications during this shift.
            TryAutoClaimExistingQuests(worker);
            SubscribeWorkerQuestAutoClaim(worker);
        }
    }

    // =========================================================================
    // Quest auto-claim (Task 21)
    // =========================================================================

    private readonly System.Collections.Generic.Dictionary<Character, System.Action<MWI.Quests.IQuest>> _questAutoClaimHandlers
        = new System.Collections.Generic.Dictionary<Character, System.Action<MWI.Quests.IQuest>>();

    private void TryAutoClaimExistingQuests(Character worker)
    {
        if (worker == null || worker.CharacterQuestLog == null) return;
        foreach (var quest in GetAvailableQuests())
        {
            if (IsQuestEligibleForWorker(quest, worker))
            {
                worker.CharacterQuestLog.TryClaim(quest);
            }
        }
    }

    private void SubscribeWorkerQuestAutoClaim(Character worker)
    {
        if (worker == null) return;
        if (_questAutoClaimHandlers.ContainsKey(worker)) return;

        System.Action<MWI.Quests.IQuest> handler = quest => TryAutoClaimForOnShiftWorker(quest, worker);
        _questAutoClaimHandlers[worker] = handler;
        OnQuestPublished += handler;
    }

    private void UnsubscribeWorkerQuestAutoClaim(Character worker)
    {
        if (worker == null) return;
        if (_questAutoClaimHandlers.TryGetValue(worker, out var handler))
        {
            OnQuestPublished -= handler;
            _questAutoClaimHandlers.Remove(worker);
        }
    }

    private void TryAutoClaimForOnShiftWorker(MWI.Quests.IQuest quest, Character worker)
    {
        if (worker == null || worker.CharacterQuestLog == null) return;
        if (!IsQuestEligibleForWorker(quest, worker)) return;
        worker.CharacterQuestLog.TryClaim(quest);
    }

    private bool IsQuestEligibleForWorker(MWI.Quests.IQuest quest, Character worker)
    {
        if (quest == null || worker == null) return false;
        if (quest.State != MWI.Quests.QuestState.Open) return false;

        var charJob = worker.CharacterJob;
        if (charJob == null) return false;
        foreach (var assn in charJob.ActiveJobs)
        {
            if (assn.Workplace == this && assn.AssignedJob != null)
            {
                return DoesJobTypeAcceptQuest(assn.AssignedJob.Type, quest);
            }
        }
        return false;
    }

    private static bool DoesJobTypeAcceptQuest(MWI.WorldSystem.JobType jobType, MWI.Quests.IQuest quest)
    {
        // v1 mapping — harvest tasks to harvester family, pickup/buy to logistics manager,
        // transport to transporter, craft to crafter family. Refine as new jobs land.
        if (quest is HarvestResourceTask)
        {
            return jobType == MWI.WorldSystem.JobType.Woodcutter
                || jobType == MWI.WorldSystem.JobType.Miner
                || jobType == MWI.WorldSystem.JobType.Forager
                || jobType == MWI.WorldSystem.JobType.Farmer;
        }
        if (quest is PickupLooseItemTask)
            return jobType == MWI.WorldSystem.JobType.LogisticsManager
                || jobType == MWI.WorldSystem.JobType.Transporter;
        if (quest is BuyOrder)
            return jobType == MWI.WorldSystem.JobType.LogisticsManager;
        if (quest is TransportOrder)
            return jobType == MWI.WorldSystem.JobType.Transporter;
        if (quest is CraftingOrder)
            return jobType == MWI.WorldSystem.JobType.Crafter
                || jobType == MWI.WorldSystem.JobType.Blacksmith
                || jobType == MWI.WorldSystem.JobType.BlacksmithApprentice;
        return false;
    }

    /// <summary>
    /// Retourne la position de travail d'un employé dans ce bâtiment.
    /// Par défaut, retourne un point aléatoire dans la zone du bâtiment.
    /// Les sous-classes (ex: ShopBuilding) peuvent override pour fournir un poste précis.
    /// </summary>
    public virtual Vector3 GetWorkPosition(Character worker)
    {
        // On récupère une position de base (zone de building ou centre du building)
        Vector3 basePos = GetRandomPointInBuildingZone(worker.transform.position.y);
        
        // On ajoute un léger offset basé sur l'ID du worker pour éviter que tout le monde
        // ne converge exactement sur le même point si la zone est trop petite.
        float offsetRange = 1.5f;
        float offsetX = (Mathf.Abs(worker.gameObject.GetInstanceID() % 100) / 50f - 1f) * offsetRange;
        float offsetZ = (Mathf.Abs((worker.gameObject.GetInstanceID() / 100) % 100) / 50f - 1f) * offsetRange;
        
        Vector3 offsetPos = basePos + new Vector3(offsetX, 0, offsetZ);

        // On vérifie que le point avec offset est toujours valide sur le NavMesh
        if (UnityEngine.AI.NavMesh.SamplePosition(offsetPos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        return basePos;
    }

    /// <summary>
    /// Appelé par un employé lorsqu'il quitte son comportement de travail
    /// (fin de journée, événement spécial) (Punch Out).
    /// </summary>
    public virtual void WorkerEndingShift(Character worker)
    {
        // Wage system hook: finalize WorkLog shift + compute & pay wage.
        // Mirrors WorkerStartingShift's lack of a server-authority guard — the wallet's
        // own AddCoins guard is the defense in depth (wage payment will no-op on clients).
        if (worker != null)
        {
            // Look up the assignment for this worker at this building.
            // NOTE v1 limitation: if a worker holds multiple roles at the same building,
            // only the FIRST matching assignment gets paid this shift.
            var charJob = worker.CharacterJob;
            JobAssignment matchingAssignment = null;
            if (charJob != null)
            {
                foreach (var assn in charJob.ActiveJobs)
                {
                    if (assn.Workplace == this && assn.AssignedJob != null)
                    {
                        matchingAssignment = assn;
                        break;
                    }
                }
            }

            if (matchingAssignment != null)
            {
                var workLog = worker.CharacterWorkLog;
                var jobType = matchingAssignment.AssignedJob.Type;
                var summary = workLog != null
                    ? workLog.FinalizeShift(jobType, GetBuildingIdForWorklog())
                    : new ShiftSummary();

                // Compute hours worked and scheduled length.
                float scheduledShiftHours = ComputeScheduledShiftHours(matchingAssignment);
                float hoursWorked = ComputeHoursWorked(worker);

                var paymentCurrency = matchingAssignment.Currency.Id == 0
                    ? MWI.Economy.CurrencyId.Default
                    : matchingAssignment.Currency;

                WageSystemService.Instance?.ComputeAndPayShiftWage(
                    worker, matchingAssignment, summary, scheduledShiftHours, hoursWorked, paymentCurrency);
            }

            _punchInTimeByWorker.Remove(worker);
            UnsubscribeWorkerQuestAutoClaim(worker);
        }

        if (worker != null && _activeWorkersOnShift.Contains(worker))
        {
            _activeWorkersOnShift.Remove(worker);
            Debug.Log($"<color=orange>[Building]</color> {worker.CharacterName} a dépointé (Punch Out) de {buildingName}.");

            if (worker.CharacterJob != null)
            {
                var activeJobAssignment = worker.CharacterJob.ActiveJobs.FirstOrDefault(j => j.Workplace == this);
                if (activeJobAssignment != null && activeJobAssignment.AssignedJob != null)
                {
                    activeJobAssignment.AssignedJob.OnWorkerPunchOut();
                }
            }
        }
    }

    public virtual void AddToInventory(ItemInstance item)
    {
        if (item == null) return;
        _inventory.Add(item);
        Debug.Log($"<color=green>[Building]</color> {item.ItemSO.ItemName} ajouté à l'inventaire de {buildingName}.");
    }

    public virtual ItemInstance TakeFromInventory(ItemSO itemSO)
    {
        var item = _inventory.FirstOrDefault(i => i.ItemSO == itemSO);
        if (item != null)
        {
            _inventory.Remove(item);
            return item;
        }
        return null;
    }

    public virtual bool RemoveExactItemFromInventory(ItemInstance exactItem)
    {
        if (exactItem != null && _inventory.Contains(exactItem))
        {
            _inventory.Remove(exactItem);
            return true;
        }
        return false;
    }

    public virtual int GetItemCount(ItemSO itemSO)
    {
        return _inventory.Count(i => i.ItemSO == itemSO);
    }

    /// <summary>
    /// Récupère physiquement tous les WorldItems actuellement déposés dans la StorageZone.
    /// Pratique pour que les employés (ex: GatherStorageItems) ciblent les bons objets.
    /// </summary>
    /// <returns>Une liste de WorldItems se trouvant dans les limites du BoxCollider de la StorageZone.</returns>
    public virtual List<WorldItem> GetWorldItemsInStorage()
    {
        List<WorldItem> foundItems = new List<WorldItem>();

        if (_storageZone == null) return foundItems;

        BoxCollider boxCol = _storageZone.GetComponent<BoxCollider>();
        if (boxCol == null) return foundItems;

        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

        Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);

        foreach (var col in colliders)
        {
            // Chercher le composant sur l'objet ou sur son parent
            WorldItem worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem != null && !foundItems.Contains(worldItem))
            {
                foundItems.Add(worldItem);
            }
        }

        return foundItems;
    }

    /// <summary>
    /// Counts <paramref name="itemSO"/> units that physically exist at this building but are
    /// NOT yet visible in the logical <see cref="Inventory"/>. Covers two in-flight cases:
    ///
    /// 1. Loose WorldItems sitting inside <see cref="Building.BuildingZone"/> (e.g. just
    ///    spawned at a <c>CraftingStation._outputPoint</c>, waiting for
    ///    <c>GoapAction_GatherStorageItems</c> or <c>RefreshStorageInventory</c> Pass 2).
    /// 2. Items carried by this building's own assigned workers (the Logistics Manager
    ///    picking a crafted item up to move it to storage temporarily despawns the
    ///    WorldItem, so (1) alone would miss that case).
    ///
    /// Used by the dispatcher to distinguish "crafted but mid-transit to storage" from
    /// "actually stolen" when a completed <see cref="CraftingOrder"/> exists but no physical
    /// stock is visible in <see cref="StorageZone"/> yet. Returns 0 when there's no
    /// BuildingZone collider (legacy prefabs) AND no workers hold matching items, which
    /// degrades gracefully to the pre-fix behavior.
    /// </summary>
    public virtual int CountUnabsorbedItemsInBuildingZone(ItemSO itemSO)
    {
        if (itemSO == null) return 0;

        int count = 0;
        HashSet<ItemInstance> counted = new HashSet<ItemInstance>();

        // (1) Loose WorldItems inside BuildingZone.
        if (BuildingZone is BoxCollider boxCol)
        {
            Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
            Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

            Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);

            foreach (var col in colliders)
            {
                WorldItem wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                if (wi == null || wi.ItemInstance == null) continue;
                if (wi.IsBeingCarried) continue;
                if (wi.ItemInstance.ItemSO != itemSO) continue;
                if (_inventory.Contains(wi.ItemInstance)) continue; // already visible in the dispatcher's view
                if (!counted.Add(wi.ItemInstance)) continue;
                count++;
            }
        }

        // (2) Items held by this building's own workers. Covers the window where the
        // Logistics Manager has picked a crafted WorldItem up (despawning it from the
        // scene) but hasn't yet dropped it in storage via FinishDropoff → AddToInventory.
        for (int i = 0; i < _jobs.Count; i++)
        {
            Character worker = _jobs[i]?.Worker;
            if (worker == null) continue;

            try
            {
                var equipment = worker.CharacterEquipment;
                if (equipment != null && equipment.HaveInventory())
                {
                    var slots = equipment.GetInventory()?.ItemSlots;
                    if (slots != null)
                    {
                        for (int s = 0; s < slots.Count; s++)
                        {
                            var slot = slots[s];
                            if (slot == null || slot.IsEmpty()) continue;
                            var inst = slot.ItemInstance;
                            if (inst == null || inst.ItemSO != itemSO) continue;
                            if (_inventory.Contains(inst)) continue;
                            if (!counted.Add(inst)) continue;
                            count++;
                        }
                    }
                }

                var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
                if (hands != null && hands.CarriedItem != null && hands.CarriedItem.ItemSO == itemSO)
                {
                    var carried = hands.CarriedItem;
                    if (!_inventory.Contains(carried) && counted.Add(carried)) count++;
                }
            }
            catch (System.Exception e)
            {
                // Defensive: a broken equipment component on one worker must not break the whole stock check.
                Debug.LogException(e);
                Debug.LogError($"[CommercialBuilding] {buildingName}: CountUnabsorbedItemsInBuildingZone threw while scanning worker '{worker?.CharacterName}'. Skipping that worker's contribution this tick.");
            }
        }

        return count;
    }

    /// <summary>
    /// Two-way sync between the logical inventory (<see cref="_inventory"/>) and the physical
    /// WorldItems actually sitting in the <see cref="StorageZone"/>.
    ///
    /// Pass 1 — remove ghosts: logical entries with no physical counterpart (fell through the
    /// map, despawned, stolen, etc.). Any ghost referenced by a live TransportOrder is also
    /// reported so the order can be recomputed.
    ///
    /// Pass 2 — absorb orphans: physical WorldItems inside StorageZone bounds that were never
    /// registered logically. This happens whenever an item reaches the zone via a path that
    /// doesn't call <see cref="AddToInventory"/> — harvesters dropping in a DepositZone that
    /// overlaps StorageZone, couriers dropping directly into the zone, player drops, etc.
    /// Without this, GetItemCount stays at 0 forever and the logistics stock check re-orders
    /// wood that physically exists in the yard.
    /// </summary>
    public virtual void RefreshStorageInventory()
    {
        List<WorldItem> physicalItems = GetWorldItemsInStorage();

        // Phase-A: PickupZone contents are outbound staging — still "ours" logically, but not
        // in StorageZone. Merge them into the physical set for Pass-1 presence checks so a
        // staged instance isn't flagged as a ghost. Pass-2 absorption intentionally does NOT
        // include them (they're already reserved by a TransportOrder and will leave with the
        // transporter, so re-absorbing would re-register them in _inventory needlessly —
        // _inventory.Contains(...) already protects correctly-tracked instances anyway).
        if (_pickupZone != null)
        {
            BoxCollider pickupBox = _pickupZone.GetComponent<BoxCollider>();
            if (pickupBox != null)
            {
                Vector3 center = pickupBox.transform.TransformPoint(pickupBox.center);
                Vector3 halfExtents = Vector3.Scale(pickupBox.size, pickupBox.transform.lossyScale) * 0.5f;
                try
                {
                    Collider[] pickupCols = Physics.OverlapBox(center, halfExtents, pickupBox.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
                    foreach (var col in pickupCols)
                    {
                        WorldItem wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                        if (wi != null && !physicalItems.Contains(wi)) physicalItems.Add(wi);
                    }
                }
                catch (System.Exception e)
                {
                    // Defensive (rule #31): a PhysX hiccup on PickupZone scan must not derail the whole refresh.
                    Debug.LogException(e);
                    Debug.LogError($"[CommercialBuilding] {buildingName}: Physics.OverlapBox threw while scanning PickupZone. Pass 1 proceeds with StorageZone items only.");
                }
            }
        }

        List<ItemInstance> ghostlyInstances = new List<ItemInstance>();

        // Collect instances actively reserved by live TransportOrders. These must NEVER be
        // ghosted here: with non-kinematic WorldItem physics (post-FreezeOnGround removal),
        // a settling item can be momentarily missed by Physics.OverlapBox while still being
        // perfectly valid. Ghosting a reserved instance cascades into ReportMissingReservedItem
        // and kills an in-flight transport. True missing-item detection happens where it
        // belongs — when the transporter arrives at the source and the pickup resolves.
        HashSet<ItemInstance> reservedInstances = null;
        if (LogisticsManager != null)
        {
            reservedInstances = new HashSet<ItemInstance>();
            foreach (var transport in LogisticsManager.PlacedTransportOrders)
            {
                if (transport == null || transport.ReservedItems == null) continue;
                foreach (var r in transport.ReservedItems) reservedInstances.Add(r);
            }
        }

        foreach (var logicalInstance in _inventory)
        {
            if (reservedInstances != null && reservedInstances.Contains(logicalInstance)) continue;

            bool isPhysicallyPresent = false;

            // Vérifier si un WorldItem physique correspond à cette instance logique
            foreach (var worldItem in physicalItems)
            {
                if (worldItem.ItemInstance == logicalInstance)
                {
                    isPhysicallyPresent = true;
                    break;
                }
            }

            // Si ce n'est pas au sol ET que ce n'est PAS en train d'être porté par quelqu'un, c'est un fantôme !
            if (!isPhysicallyPresent)
            {
                // Un check supplémentaire : a-t-il vraiment un porteur assigné ?
                // La propriété IsBeingCarried de WorldItem est liée au portage effectif.
                // Dans notre architecture, si ItemInstance n'a pas de propriétaire actuel mais est perdu, on le supprime.
                ghostlyInstances.Add(logicalInstance);
            }
        }

        if (ghostlyInstances.Count > 0)
        {
            Debug.LogWarning($"<color=orange>[CommercialBuilding]</color> {buildingName} : Audit détecte {ghostlyInstances.Count} objets logiques sans réalité physique ! Nettoyage...");

            foreach (var ghost in ghostlyInstances)
            {
                _inventory.Remove(ghost);

                // Si cet objet fantôme était réservé par un logisticien pour une commande (Transport/Achats), on le signale.
                if (LogisticsManager != null)
                {
                    // Trouver quelle commande avait réservé cet item fantôme
                    var brokenTransportOrder = LogisticsManager.PlacedTransportOrders.FirstOrDefault(t => t.ReservedItems.Contains(ghost));
                    if (brokenTransportOrder != null)
                    {
                        LogisticsManager.ReportMissingReservedItem(brokenTransportOrder);
                    }
                }
            }
        }

        // Pass 2 — absorb orphans: physical items in storage that aren't tracked logically.
        int absorbed = 0;
        foreach (var worldItem in physicalItems)
        {
            if (worldItem == null || worldItem.ItemInstance == null) continue;
            if (worldItem.IsBeingCarried) continue;
            if (_inventory.Contains(worldItem.ItemInstance)) continue;

            _inventory.Add(worldItem.ItemInstance);
            absorbed++;
        }

        if (absorbed > 0)
        {
            Debug.Log($"<color=green>[CommercialBuilding]</color> {buildingName} : Audit a absorbé {absorbed} objet(s) physique(s) orphelin(s) dans l'inventaire logique.");
        }
    }

    public virtual bool HasRequiredIngredients(IEnumerable<CraftingIngredient> ingredients, int multiplier = 1)
    {
        foreach (var ingredient in ingredients)
        {
            if (GetItemCount(ingredient.Item) < ingredient.Amount * multiplier)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Indique si ce bâtiment produit ou fournit l'item demandé.
    /// Les sous-classes doivent override ceci pour exposer ce qu'elles offrent.
    /// </summary>
    public virtual bool ProducesItem(ItemSO item)
    {
        return false;
    }

    /// <summary>
    /// Indique si la production de cet item nécessite de placer une commande de fabrication (CraftingOrder).
    /// Si false, on peut directement envoyer un transporteur (TransportOrder) pour récupérer le stock (ex: Harvester).
    /// </summary>
    public virtual bool RequiresCraftingFor(ItemSO item)
    {
        return false;
    }

    /// <summary>
    /// V2 Logistics hook for dynamic virtual stock generation (like HarvestingBuildings).
    /// </summary>
    public virtual bool TryFulfillOrder(BuyOrder order, int amount)
    {
        return false;
    }

    /// <summary>
    /// Compute the end-of-shift time as a 0..1 fraction of the day, by finding the
    /// latest endHour across all of this assignment's work-schedule entries.
    /// Returns 1.0 (end-of-day) if no entries are present (defensive default).
    /// </summary>
    private float ComputeScheduledEndTime01(JobAssignment assignment)
    {
        if (assignment == null || assignment.WorkScheduleEntries == null || assignment.WorkScheduleEntries.Count == 0)
            return 1f;
        int latestEndHour = 0;
        for (int i = 0; i < assignment.WorkScheduleEntries.Count; i++)
        {
            var entry = assignment.WorkScheduleEntries[i];
            if (entry.endHour > latestEndHour) latestEndHour = entry.endHour;
        }
        return Mathf.Clamp01(latestEndHour / 24f);
    }

    /// <summary>
    /// Stable per-building id used as the WorkPlaceRecord key. Backed by
    /// <see cref="Building.BuildingId"/> — a GUID assigned at network spawn time and
    /// persisted in CommunityData / save files. Renaming the GameObject at runtime no
    /// longer forks WorkPlaceRecord history.
    /// </summary>
    private string GetBuildingIdForWorklog()
    {
        return BuildingId;
    }

    /// <summary>
    /// Human-readable label snapshot-stored ONCE in
    /// <c>WorkPlaceRecord.BuildingDisplayName</c> at the worker's first punch-in.
    /// Backed by <see cref="Building.BuildingDisplayName"/>.
    /// </summary>
    private string GetBuildingDisplayNameForWorklog()
    {
        return BuildingDisplayName;
    }

    /// <summary>
    /// Sum of (endHour - startHour) across the assignment's WorkScheduleEntries.
    /// Returns 8f as a defensive default if no entries are present.
    /// </summary>
    private float ComputeScheduledShiftHours(JobAssignment assignment)
    {
        if (assignment == null || assignment.WorkScheduleEntries == null || assignment.WorkScheduleEntries.Count == 0)
            return 8f;
        int totalHours = 0;
        for (int i = 0; i < assignment.WorkScheduleEntries.Count; i++)
        {
            var entry = assignment.WorkScheduleEntries[i];
            int duration = entry.endHour - entry.startHour;
            if (duration > 0) totalHours += duration;
        }
        return Mathf.Max(0.0001f, totalHours);
    }

    /// <summary>
    /// (now - punchInTime) in hours, capped at scheduled-end-of-shift (no overtime credit
    /// for the minimum wage component — handled also by WageCalculator clamp01, but capping
    /// here keeps the hours-worked field truthful for diagnostics).
    /// </summary>
    private float ComputeHoursWorked(Character worker)
    {
        if (!_punchInTimeByWorker.TryGetValue(worker, out float punchInHours)) return 0f;
        float nowHours = MWI.Time.TimeManager.Instance != null
            ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
            : punchInHours;
        return Mathf.Max(0f, nowHours - punchInHours);
    }
}
