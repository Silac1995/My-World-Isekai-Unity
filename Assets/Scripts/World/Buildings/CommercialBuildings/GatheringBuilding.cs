using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Entrée de configuration pour une ressource voulue par un GatheringBuilding.
/// Chaque entrée définit quel item le building veut récolter et en quelle quantité max.
/// </summary>
[System.Serializable]
public class GatheringResourceEntry
{
    public ItemSO targetItem;
    public int maxAmount = -1;      // -1 = illimité
    [HideInInspector] public int currentAmount = 0;

    public bool IsAtLimit => maxAmount > 0 && currentAmount >= maxAmount;
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

    // Runtime : zone de récolte découverte par les employés
    private Zone _gatherableZone;

    // Runtime : liste des employés (Characters assignés)
    private List<Character> _employees = new List<Character>();

    // === Accesseurs publics ===

    public IReadOnlyList<GatheringResourceEntry> WantedResources => _wantedResources;
    public Zone DepositZone => _depositZone;
    public Zone GatherableZone => _gatherableZone;
    public IReadOnlyList<Character> Employees => _employees;
    public bool HasGatherableZone => _gatherableZone != null;
    public int GathererCount => _gathererCount;

    // === Initialisation ===

    protected override void InitializeJobs()
    {
        for (int i = 0; i < _gathererCount; i++)
        {
            _jobs.Add(new JobGatherer(_gathererJobTitle));
        }

        Debug.Log($"<color=green>[GatheringBuilding]</color> {buildingName} initialisé avec {_gathererCount} {_gathererJobTitle}(s).");
    }

    // === Gestion de la zone de récolte ===

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
            if (entry.targetItem != null && !entry.IsAtLimit)
            {
                wanted.Add(entry.targetItem);
            }
        }
        return wanted;
    }

    /// <summary>
    /// Vérifie si le building veut encore des ressources (au moins un item sous la limite).
    /// </summary>
    public bool NeedsResources()
    {
        return _wantedResources.Any(r => r.targetItem != null && !r.IsAtLimit);
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
                entry.currentAmount++;
                Debug.Log($"<color=cyan>[GatheringBuilding]</color> {buildingName} : {item.ItemName} récolté ({entry.currentAmount}/{(entry.maxAmount > 0 ? entry.maxAmount.ToString() : "∞")}).");
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
}
