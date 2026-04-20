using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    [SerializeField] protected Character _owner; // Individual owner
    [SerializeField] protected Community _ownerCommunity; // Collective owner
    [SerializeField] protected Zone _storageZone;

    protected List<Job> _jobs = new List<Job>();
    protected List<Character> _activeWorkersOnShift = new List<Character>();
    protected List<ItemInstance> _inventory = new List<ItemInstance>();

    protected BuildingTaskManager _taskManager;
    protected BuildingLogisticsManager _logisticsManager;

    public Character Owner => _owner;
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

    public void SetOwner(Character newOwner, Job ownerJob = null)
    {
        if (!IsServer) return;

        // Remove from old community
        if (_ownerCommunity != null && _ownerCommunity.ownedBuildings.Contains(this))
        {
            _ownerCommunity.ownedBuildings.Remove(this);
        }

        _owner = newOwner;
        
        // Add to new community if applicable
        if (_owner != null && _owner.CharacterCommunity != null && _owner.CharacterCommunity.CurrentCommunity != null)
        {
            _ownerCommunity = _owner.CharacterCommunity.CurrentCommunity;
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
    /// Le building a-t-il un owner/boss (individuel) ?
    /// </summary>
    public bool HasOwner => _owner != null && _owner.IsAlive();

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
