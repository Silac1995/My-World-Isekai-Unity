using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;

/// <summary>
/// Entrée de configuration pour une ressource voulue par un GatheringBuilding.
/// Chaque entrée définit quel item le building veut récolter et en quelle quantité max.
/// </summary>
[System.Serializable]
public class GatheringResourceEntry
{
    public ItemSO targetItem;
    public int minAmount = -1;      // -1 = illimité
}

/// <summary>
/// Building commercial pour les lieux de récolte (Mine, Camp de bûcheron, Ferme...).
/// Configure les ressources voulues, le nombre de gatherers, et la zone de dépôt.
/// Les employés explorent pour trouver une zone de GatherableObject, puis récoltent.
/// </summary>
public class GatheringBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.GatheringSite;

    [Header("Gathering Config")]
    [SerializeField] private List<GatheringResourceEntry> _wantedResources = new List<GatheringResourceEntry>();
    [SerializeField] private int _gathererCount = 2;
    [SerializeField] private string _gathererJobTitle = "Gatherer";

    [Header("Zones")]
    [SerializeField] private Zone _depositZone;
    [SerializeField, Tooltip("Optionnel: Zone à scanner automatiquement chaque jour pour trouver des ressources. Au lieu de laisser les workers explorer.")]
    private Zone _gatheringAreaZone;

    // Runtime : zone de récolte découverte par les employés (ou assignée via le scan)
    private Zone _gatherableZone;

    // Runtime : liste des employés (Characters assignés)
    private List<Character> _employees = new List<Character>();

    // Runtime : list of scanned gatherable objects we are tracking for respawn events
    private List<GatherableObject> _trackedGatherables = new List<GatherableObject>();

    // === Accesseurs publics ===

    public IReadOnlyList<GatheringResourceEntry> WantedResources => _wantedResources;
    public Zone DepositZone => _depositZone;
    public Zone GatherableZone => _gatherableZone;
    public IReadOnlyList<Character> Employees => _employees;
    public bool HasGatherableZone => _gatherableZone != null;
    public int GathererCount => _gathererCount;

    // === Initialisation ===

    protected virtual void OnEnable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += ScanGatheringArea;
        }
    }

    protected virtual void OnDisable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= ScanGatheringArea;
        }
    }

    protected override void Start()
    {
        base.Start();
        // Premier scan au démarrage
        ScanGatheringArea();
    }

    protected override void InitializeJobs()
    {
        for (int i = 0; i < _gathererCount; i++)
        {
            _jobs.Add(new JobGatherer(_gathererJobTitle));
        }

        _jobs.Add(new JobLogisticsManager("Logistics Manager"));

        Debug.Log($"<color=green>[GatheringBuilding]</color> {buildingName} initialisé avec {_gathererCount} {_gathererJobTitle}(s) et 1 Manager.");
    }

    // === Gestion de la zone de récolte ===

    /// <summary>
    /// Scanne la zone de récolte assignée (_gatheringAreaZone) pour voir s'il reste des ressources voulues.
    /// Appelé automatiquement au début de chaque jour (OnNewDay).
    /// </summary>
    public void ScanGatheringArea()
    {
        if (_gatheringAreaZone == null) return;
        ScanAndRegisterZone(_gatheringAreaZone);
    }

    /// <summary>
    /// Scanne une zone de récolte spécifique pour voir s'il reste des ressources voulues
    /// et enregistre toutes les tâches dans le TaskManager.
    /// </summary>
    public void ScanAndRegisterZone(Zone zone)
    {
        if (zone == null) return;

        BoxCollider boxCol = zone.GetComponent<BoxCollider>();
        if (boxCol == null) return;

        // Préparer les infos pour l'OverlapBox
        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

        // Récupérer les items désirés
        var wantedItems = GetWantedItems();
        if (wantedItems.Count == 0)
        {
            // Le building ne veut plus rien (tout est plein)
            if (_gatherableZone != null) ClearGatherableZone();
            return;
        }

        bool foundValidResource = false;

        Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var col in colliders)
        {
            GatherableObject gatherable = col.GetComponent<GatherableObject>() ?? col.GetComponentInParent<GatherableObject>();
            if (gatherable != null && gatherable.CanGather())
            {
                // Vérifier si ce gatherable donne un item voulu
                if (gatherable.HasAnyOutput(wantedItems))
                {
                    foundValidResource = true;
                    break;
                }
            }
        }

        if (foundValidResource)
        {
            SetGatherableZone(zone);
            
            // Cleanup old subscriptions
            ClearTrackedGatherables();

            // Register tasks for all valid gatherables found
            TaskManager?.ClearAvailableTasksOfType<GatherResourceTask>();
            foreach (var col in colliders)
            {
                GatherableObject gatherable = col.GetComponent<GatherableObject>() ?? col.GetComponentInParent<GatherableObject>();
                if (gatherable != null && gatherable.HasAnyOutput(wantedItems))
                {
                    // Track for respawn regardless of current state
                    if (!_trackedGatherables.Contains(gatherable))
                    {
                        gatherable.OnRespawned += HandleGatherableRespawned;
                        _trackedGatherables.Add(gatherable);
                    }

                    if (gatherable.CanGather())
                    {
                        TaskManager?.RegisterTask(new GatherResourceTask(gatherable));
                    }
                }
            }
        }
        else
        {
            ClearGatherableZone();
            TaskManager?.ClearAvailableTasksOfType<GatherResourceTask>();
            Debug.Log($"<color=orange>[GatheringBuilding]</color> {buildingName} : Scan de {zone.zoneName} n'a rien trouvé. Retour à l'exploration.");
        }
    }

    private void HandleGatherableRespawned(GatherableObject gatherable)
    {
        if (gatherable != null && TaskManager != null)
        {
            // Only register if we still want this item type
            var wantedItems = GetWantedItems();
            if (wantedItems.Count > 0 && gatherable.HasAnyOutput(wantedItems))
            {
                Debug.Log($"<color=green>[GatheringBuilding]</color> {buildingName} noticed {gatherable.name} respawned! Re-registering task.");
                TaskManager.RegisterTask(new GatherResourceTask(gatherable));
            }
        }
    }

    private void ClearTrackedGatherables()
    {
        foreach (var gatherable in _trackedGatherables)
        {
            if (gatherable != null)
            {
                gatherable.OnRespawned -= HandleGatherableRespawned;
            }
        }
        _trackedGatherables.Clear();
    }

    /// <summary>
    /// Appelé par un employé explorateur quand il a trouvé une zone contenant
    /// des GatherableObject avec les items voulus.
    /// </summary>
    public void SetGatherableZone(Zone zone)
    {
        if (zone == null) return;

        _gatherableZone = zone;
        Debug.Log($"<color=green>[GatheringBuilding]</color> {buildingName} : zone de récolte trouvée → {zone.zoneName}.");
    }

    /// <summary>
    /// Réinitialise la zone de récolte (ex: si la zone est épuisée).
    /// Les employés devront explorer à nouveau.
    /// </summary>
    public void ClearGatherableZone()
    {
        if (_gatherableZone != null)
        {
            Debug.Log($"<color=orange>[GatheringBuilding]</color> {buildingName} : zone de récolte {_gatherableZone.zoneName} épuisée ou invalide.");
        }
        _gatherableZone = null;
        ClearTrackedGatherables();
    }

    // === Gestion des ressources voulues ===

    /// <summary>
    /// Retourne la liste de tous les items acceptés par le building, même si la limite est atteinte.
    /// Utile pour forcer le dépôt des ressources en surplus avant de changer d'activité.
    /// </summary>
    public List<ItemSO> GetAcceptedItems()
    {
        var accepted = new List<ItemSO>();
        foreach (var entry in _wantedResources)
        {
            if (entry.targetItem != null)
            {
                accepted.Add(entry.targetItem);
            }
        }
        return accepted;
    }

    /// <summary>
    /// Retourne la liste des ItemSO encore sous la limite (pas encore assez récoltés).
    /// </summary>
    public List<ItemSO> GetWantedItems()
    {
        var wanted = new List<ItemSO>();
        foreach (var entry in _wantedResources)
        {
            if (entry.targetItem != null && !IsResourceAtLimit(entry))
            {
                wanted.Add(entry.targetItem);
            }
        }
        return wanted;
    }

    /// <summary>
    /// Vérifie si le building veut encore des ressources (au moins un item sous la limite combinée: minAmount + pending orders).
    /// </summary>
    public bool NeedsResources()
    {
        return _wantedResources.Any(r => r.targetItem != null && !IsResourceAtLimit(r));
    }

    /// <summary>
    /// Evalue dynamiquement si un item a atteint son quota.
    /// Quota = minAmount (stock de base du bâtiment) + demandes d'expédition (BuyOrders actives dans le LogisticsManager).
    /// </summary>
    private bool IsResourceAtLimit(GatheringResourceEntry entry)
    {
        if (entry.minAmount <= 0) return false; // illimité

        int activeOrdersDemand = 0;
        int reservedStock = 0;

        if (LogisticsManager != null)
        {
            // On calcule la demande basée sur ce qui n'a pas encore été dispatché. 
            // Si c'est dispatché, c'est en transite, et ça a déjà fait baisser le freeStock physiquement sans que ce soit entièrement livré,
            // on ne doit plus le compter comme demande pour ne pas sur-récolter.
            activeOrdersDemand = LogisticsManager.ActiveOrders
                                .Where(o => o.ItemToTransport == entry.targetItem)
                                .Sum(o => o.Quantity - o.DispatchedQuantity);
                                
            reservedStock = LogisticsManager.GetReservedItemCount(entry.targetItem);
        }

        int totalRequiredAmount = entry.minAmount + activeOrdersDemand;
        
        // REFACTOR: Use the free inventory count to prevent stalls caused by reserved items,
        // and prevent over-gathering while items are in transit.
        int freeStock = GetItemCount(entry.targetItem) - reservedStock;
        
        return freeStock >= totalRequiredAmount;
    }

    /// <summary>
    /// Vérifie si TOUTES les ressources demandées ont atteint leur limite maximale.
    /// Utilisé pour ordonner aux employés de se reposer.
    /// </summary>
    public bool AreAllRequestedResourcesGathered()
    {
        return !NeedsResources();
    }

    /// <summary>
    /// Enregistre un item récolté. Incrémente le compteur de la ressource correspondante.
    /// Retourne true si l'item était bien voulu.
    /// </summary>
    public bool RegisterGatheredItem(ItemSO item)
    {
        if (item == null) return false;

        foreach (var entry in _wantedResources)
        {
            if (entry.targetItem == item)
            {
                int actualStock = GetItemCount(item);
                Debug.Log($"<color=cyan>[GatheringBuilding]</color> {buildingName} : {item.ItemName} récolté ({actualStock} en stock physique réel).");
                return true;
            }
        }

        return false;
    }

    // === Gestion des employés ===

    /// <summary>
    /// Ajoute un employé à la liste. Appelé automatiquement lors de l'assignation d'un job.
    /// </summary>
    public void AddEmployee(Character employee)
    {
        if (employee != null && !_employees.Contains(employee))
        {
            _employees.Add(employee);
            Debug.Log($"<color=yellow>[GatheringBuilding]</color> {employee.CharacterName} rejoint {buildingName} comme employé.");
        }
    }

    /// <summary>
    /// Retire un employé de la liste.
    /// </summary>
    public void RemoveEmployee(Character employee)
    {
        if (employee != null && _employees.Remove(employee))
        {
            Debug.Log($"<color=yellow>[GatheringBuilding]</color> {employee.CharacterName} quitte {buildingName}.");
        }
    }

    // === Methodes Globales Fournisseur ===

    public override bool ProducesItem(ItemSO item)
    {
        // Un batiment de recolte produit tout ce qui est dans ses wanted resources.
        return _wantedResources.Any(r => r.targetItem == item);
    }
}
