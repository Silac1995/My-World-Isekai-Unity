using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;
using MWI.WorldSystem;

/// <summary>
/// Entrée de configuration pour une ressource voulue par un HarvestingBuilding.
/// Chaque entrée définit quel item le building veut récolter et en quelle quantité max.
/// </summary>
[System.Serializable]
public class HarvestingResourceEntry
{
    public ItemSO targetItem;
    [Tooltip("La quantité maximum à stocker. Les harvesters s'arrêteront (sauf si de nouvelles BuyOrders arrivent). -1 = illimité.")]
    public int maxQuantity = 50;
}

/// <summary>
/// Building commercial pour les lieux de récolte (Mine, Camp de bûcheron, Ferme...).
/// Configure les ressources voulues, le nombre de harvesters, et la zone de dépôt.
/// Les employés explorent pour trouver une zone de Harvestable, puis récoltent.
/// </summary>
public class HarvestingBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.HarvestingSite;

    [Header("Harvesting Config")]
    [SerializeField] private List<HarvestingResourceEntry> _wantedResources = new List<HarvestingResourceEntry>();
    [SerializeField] private int _harvesterCount = 2;
    [SerializeField] private string _harvesterJobTitle = "Harvester";
    [SerializeField] private JobType _harvesterJobType = JobType.None;

    [Header("Zones")]
    [SerializeField] private Zone _depositZone;
    [SerializeField, Tooltip("Optionnel: Zone à scanner automatiquement chaque jour pour trouver des ressources. Au lieu de laisser les workers explorer.")]
    private Zone _harvestingAreaZone;

    // Runtime : zone de récolte découverte par les employés (ou assignée via le scan)
    private Zone _harvestableZone;

    // Runtime : liste des employés (Characters assignés)
    private List<Character> _employees = new List<Character>();

    // Runtime : list of scanned harvestable objects we are tracking for respawn events
    private List<Harvestable> _trackedHarvestables = new List<Harvestable>();

    // === Accesseurs publics ===

    public IReadOnlyList<HarvestingResourceEntry> WantedResources => _wantedResources;
    public Zone DepositZone => _depositZone;
    public Zone HarvestableZone => _harvestableZone;
    public IReadOnlyList<Character> Employees => _employees;
    public bool HasHarvestableZone => _harvestableZone != null;
    public int HarvesterCount => _harvesterCount;

    // === Initialisation ===

    protected virtual void OnEnable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += ScanHarvestingArea;
        }
    }

    protected virtual void OnDisable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= ScanHarvestingArea;
        }
    }

    protected override void Start()
    {
        base.Start();
        // Premier scan au démarrage
        ScanHarvestingArea();
    }

    protected override void InitializeJobs()
    {
        for (int i = 0; i < _harvesterCount; i++)
        {
            _jobs.Add(new JobHarvester(_harvesterJobTitle, _harvesterJobType));
        }

        _jobs.Add(new JobLogisticsManager("Logistics Manager"));

        Debug.Log($"<color=green>[HarvestingBuilding]</color> {buildingName} initialisé avec {_harvesterCount} {_harvesterJobTitle}(s) et 1 Manager.");
    }

    // === Gestion de la zone de récolte ===

    /// <summary>
    /// Scanne la zone de récolte assignée (_harvestingAreaZone) pour voir s'il reste des ressources voulues.
    /// Appelé automatiquement au début de chaque jour (OnNewDay).
    /// </summary>
    public void ScanHarvestingArea()
    {
        if (_harvestingAreaZone == null) return;
        ScanAndRegisterZone(_harvestingAreaZone);
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
            if (_harvestableZone != null) ClearHarvestableZone();
            return;
        }

        bool foundValidResource = false;

        Collider[] colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var col in colliders)
        {
            Harvestable harvestable = col.GetComponent<Harvestable>() ?? col.GetComponentInParent<Harvestable>();
            // Permet de considérer la zone comme valide même si la ressource est épuisée (pour écouter son respawn dynamique)
            if (harvestable != null && harvestable.HasAnyOutput(wantedItems))
            {
                foundValidResource = true;
                break;
            }
        }

        if (foundValidResource)
        {
            SetHarvestableZone(zone);
            
            // On ne clear PLUS les anciens abonnements. La liste s'accumule !
            
            foreach (var col in colliders)
            {
                Harvestable harvestable = col.GetComponent<Harvestable>() ?? col.GetComponentInParent<Harvestable>();
                if (harvestable != null && harvestable.HasAnyOutput(wantedItems))
                {
                    AddToTrackedHarvestables(harvestable);
                }
            }
        }
        else
        {
            Debug.Log($"<color=orange>[HarvestingBuilding]</color> {buildingName} : Scan de {zone.zoneName} n'a rien trouvé. Retour à l'exploration.");
        }
    }

    /// <summary>
    /// Ajoute dynamiquement un harvestable à la liste du bâtiment pour qu'il soit traqué (respawns, etc.)
    /// </summary>
    public void AddToTrackedHarvestables(Harvestable harvestable)
    {
        if (harvestable == null) return;
        
        if (!_trackedHarvestables.Contains(harvestable))
        {
            harvestable.OnRespawned += HandleHarvestableRespawned;
            _trackedHarvestables.Add(harvestable);
        }

        if (harvestable.CanHarvest() && TaskManager != null)
        {
            TaskManager.RegisterTask(new HarvestResourceTask(harvestable));
        }
    }

    private void HandleHarvestableRespawned(Harvestable harvestable)
    {
        if (harvestable != null && TaskManager != null)
        {
            // Only register if we still want this item type
            var wantedItems = GetWantedItems();
            if (wantedItems.Count > 0 && harvestable.HasAnyOutput(wantedItems))
            {
                Debug.Log($"<color=green>[HarvestingBuilding]</color> {buildingName} noticed {harvestable.name} respawned! Re-registering task.");
                TaskManager.RegisterTask(new HarvestResourceTask(harvestable));
            }
        }
    }

    private void ClearTrackedHarvestables()
    {
        foreach (var harvestable in _trackedHarvestables)
        {
            if (harvestable != null)
            {
                harvestable.OnRespawned -= HandleHarvestableRespawned;
            }
        }
        _trackedHarvestables.Clear();
    }

    /// <summary>
    /// Appelé par un employé explorateur quand il a trouvé une zone contenant
    /// des Harvestable avec les items voulus.
    /// </summary>
    public void SetHarvestableZone(Zone zone)
    {
        if (zone == null) return;

        _harvestableZone = zone;
        Debug.Log($"<color=green>[HarvestingBuilding]</color> {buildingName} : zone de récolte trouvée → {zone.zoneName}.");
    }

    /// <summary>
    /// Obsolète ou utilisé uniquement pour réinitialiser le focus UI. 
    /// On ne wipe plus la mémoire des arbres traqués pour permettre le respawn.
    /// </summary>
    public void ClearHarvestableZone()
    {
        if (_harvestableZone != null)
        {
            Debug.Log($"<color=orange>[HarvestingBuilding]</color> {buildingName} : zone de récolte {_harvestableZone.zoneName} effacée du focus principal.");
        }
        _harvestableZone = null;
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
    /// Quota = maxQuantity (stock cible du bâtiment) + demandes d'expédition (BuyOrders actives dans le LogisticsManager).
    /// </summary>
    private bool IsResourceAtLimit(HarvestingResourceEntry entry)
    {
        if (entry.maxQuantity < 0) return false; // illimité

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

        int totalRequiredAmount = entry.maxQuantity + activeOrdersDemand;
        
        // REFACTOR: Use the free inventory count to prevent stalls caused by reserved items,
        // and prevent over-harvesting while items are in transit.
        int freeStock = GetItemCount(entry.targetItem) - reservedStock;
        
        return freeStock >= totalRequiredAmount;
    }

    /// <summary>
    /// Vérifie si TOUTES les ressources demandées ont atteint leur limite maximale.
    /// Utilisé pour ordonner aux employés de se reposer.
    /// </summary>
    public bool AreAllRequestedResourcesHarvested()
    {
        return !NeedsResources();
    }

    /// <summary>
    /// Enregistre un item récolté. Incrémente le compteur de la ressource correspondante.
    /// Retourne true si l'item était bien voulu.
    /// </summary>
    public bool RegisterHarvestedItem(ItemSO item)
    {
        if (item == null) return false;

        foreach (var entry in _wantedResources)
        {
            if (entry.targetItem == item)
            {
                int actualStock = GetItemCount(item);
                Debug.Log($"<color=cyan>[HarvestingBuilding]</color> {buildingName} : {item.ItemName} récolté ({actualStock} en stock physique réel).");
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
            Debug.Log($"<color=yellow>[HarvestingBuilding]</color> {employee.CharacterName} rejoint {buildingName} comme employé.");
        }
    }

    /// <summary>
    /// Retire un employé de la liste.
    /// </summary>
    public void RemoveEmployee(Character employee)
    {
        if (employee != null && _employees.Remove(employee))
        {
            Debug.Log($"<color=yellow>[HarvestingBuilding]</color> {employee.CharacterName} quitte {buildingName}.");
        }
    }

    // === Methodes Globales Fournisseur ===

    public override bool ProducesItem(ItemSO item)
    {
        // Un batiment de recolte produit tout ce qui est dans ses wanted resources.
        return _wantedResources.Any(r => r.targetItem == item);
    }
}
