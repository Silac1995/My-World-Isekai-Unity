using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton qui gère tous les bâtiments du monde.
/// Permet de chercher des logements disponibles, des postes de travail, etc.
/// </summary>
public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance { get; private set; }

    [Header("All Buildings")]
    public List<Building> allBuildings = new List<Building>();

    /// <summary>
    /// Fires after any Building completes registration. Subscribers (e.g. CharacterJob)
    /// use this to re-bind to a workplace whose BuildingId was loaded from save data
    /// before the live Building instance existed.
    /// </summary>
    public static event System.Action<Building> OnBuildingRegistered;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enregistre un bâtiment dans la liste globale.
    /// </summary>
    public void RegisterBuilding(Building building)
    {
        if (building != null && !allBuildings.Contains(building))
        {
            allBuildings.Add(building);
            Debug.Log($"<color=cyan>[Building Manager]</color> Bâtiment enregistré : {building.BuildingName} ({building.BuildingType})");
            OnBuildingRegistered?.Invoke(building);
        }
    }

    /// <summary>
    /// Finds a registered building by its unique BuildingId (GUID).
    /// </summary>
    public Building FindBuildingById(string buildingId)
    {
        if (string.IsNullOrEmpty(buildingId)) return null;
        return allBuildings.Find(b => b.BuildingId == buildingId);
    }

    /// <summary>
    /// Retire un bâtiment de la liste globale.
    /// </summary>
    public void UnregisterBuilding(Building building)
    {
        if (building != null && allBuildings.Contains(building))
        {
            allBuildings.Remove(building);
        }
    }

    /// <summary>
    /// Trouve un logement résidentiel avec de la place disponible (celui qui a le moins de résidents).
    /// </summary>
    public ResidentialBuilding FindAvailableHousing()
    {
        ResidentialBuilding bestBuilding = null;
        int minResidents = int.MaxValue;

        foreach (var building in allBuildings)
        {
            if (building is ResidentialBuilding residential)
            {
                int count = residential.Residents.Count();
                if (count < minResidents)
                {
                    minResidents = count;
                    bestBuilding = residential;
                }
            }
        }
        return bestBuilding;
    }

    /// <summary>
    /// Trouve un bâtiment commercial sans propriétaire.
    /// </summary>
    public CommercialBuilding FindUnownedCommercialBuilding()
    {
        foreach (var building in allBuildings)
        {
            if (building is CommercialBuilding commercial && !commercial.HasOwner)
            {
                return commercial;
            }
        }
        return null;
    }

    /// <summary>
    /// Trouve un job disponible d'un type spécifique dans tous les buildings commerciaux.
    /// Retourne le building et le job trouvé, ou null si aucun n'est disponible.
    /// </summary>
    public (CommercialBuilding building, T job) FindAvailableJob<T>(bool requireBoss = false) where T : Job
    {
        // Iterate from a random start index so callers don't all flock to the same boss first,
        // while avoiding the allocation and O(B log B) cost of `allBuildings.OrderBy(Random.value)`.
        int count = allBuildings.Count;
        if (count == 0) return (null, null);

        int start = UnityEngine.Random.Range(0, count);
        for (int offset = 0; offset < count; offset++)
        {
            var building = allBuildings[(start + offset) % count];
            if (building is CommercialBuilding commercial)
            {
                if (requireBoss && !commercial.HasOwner) continue;

                T availableJob = commercial.FindAvailableJob<T>();
                if (availableJob != null)
                {
                    return (commercial, availableJob);
                }
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Assigne un résident à un bâtiment résidentiel.
    /// </summary>
    public bool AssignResident(Character character, ResidentialBuilding building)
    {
        if (character == null || building == null) return false;
        return building.AddResident(character);
    }

    /// <summary>
    /// Assigne un worker à un job dans un building commercial.
    /// </summary>
    public bool AssignWorker(Character worker, CommercialBuilding building, Job job)
    {
        if (worker == null || building == null || job == null) return false;
        return building.AssignWorker(worker, job);
    }
}
