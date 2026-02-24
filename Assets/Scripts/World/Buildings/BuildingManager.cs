using System.Collections.Generic;
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
        }
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
    /// Trouve un logement résidentiel avec de la place disponible.
    /// </summary>
    public ResidentialBuilding FindAvailableHousing()
    {
        foreach (var building in allBuildings)
        {
            if (building is ResidentialBuilding residential && !residential.IsFull)
            {
                return residential;
            }
        }
        return null;
    }

    /// <summary>
    /// Trouve un job disponible d'un type spécifique dans tous les buildings commerciaux.
    /// Retourne le building et le job trouvé, ou null si aucun n'est disponible.
    /// </summary>
    public (CommercialBuilding building, T job) FindAvailableJob<T>() where T : Job
    {
        foreach (var building in allBuildings)
        {
            if (building is CommercialBuilding commercial)
            {
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
