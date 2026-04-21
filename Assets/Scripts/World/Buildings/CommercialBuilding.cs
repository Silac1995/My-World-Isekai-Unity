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

    protected List<Job> _jobs = new List<Job>();
    protected List<Character> _activeWorkersOnShift = new List<Character>();
    protected List<ItemInstance> _inventory = new List<ItemInstance>();

    protected BuildingTaskManager _taskManager;
    protected BuildingLogisticsManager _logisticsManager;

    public Character Owner => _ownerIds.Count > 0 ? Character.FindByUUID(_ownerIds[0].ToString()) : null;
    public Community OwnerCommunity => _ownerCommunity;
    public IReadOnlyList<Job> Jobs => _jobs;
    public IReadOnlyList<Character> ActiveWorkersOnShift => _activeWorkersOnShift;
    public Zone StorageZone => _storageZone;
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
        if (mapController != null && MWI.WorldSystem.CommunityTracker.Instance != null)
        {
            var comm = MWI.WorldSystem.CommunityTracker.Instance.GetCommunity(mapController.MapId);
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
    /// Appelé par un employé lorsqu'il arrive physiquement sur son lieu de travail
    /// et commence (Punch In).
    /// </summary>
    public virtual void WorkerStartingShift(Character worker)
    {
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
        }
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
    /// Effectue un audit de sécurité (Sanity Check) entre l'inventaire logique (_inventory)
    /// et les objets réellement présents physiquement au sol.
    /// Utilisé lorsqu'un employé suspecte qu'un objet logique n'existe plus physiquement (bug, tombé sous la map, volé).
    /// </summary>
    public virtual void RefreshStorageInventory()
    {
        List<WorldItem> physicalItems = GetWorldItemsInStorage();
        List<ItemInstance> ghostlyInstances = new List<ItemInstance>();

        foreach (var logicalInstance in _inventory)
        {
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
}
