using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton that manages all buildings in the world.
/// Allows searching for available housing, workplaces, etc.
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
    /// Registers a building in the global list.
    /// </summary>
    public void RegisterBuilding(Building building)
    {
        if (building != null && !allBuildings.Contains(building))
        {
            allBuildings.Add(building);
            Debug.Log($"<color=cyan>[Building Manager]</color> Building registered: {building.BuildingName} ({building.BuildingType})");
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
    /// Removes a building from the global list.
    /// </summary>
    public void UnregisterBuilding(Building building)
    {
        if (building != null && allBuildings.Contains(building))
        {
            allBuildings.Remove(building);
        }
    }

    /// <summary>
    /// Finds a residential housing with available space (the one with the fewest residents).
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
    /// Finds a commercial building without an owner.
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
    /// Finds an available job of a specific type across all commercial buildings.
    /// Returns the building and the matching job, or null if none is available.
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
    /// Assigns a resident to a residential building.
    /// </summary>
    public bool AssignResident(Character character, ResidentialBuilding building)
    {
        if (character == null || building == null) return false;
        return building.AddResident(character);
    }

    /// <summary>
    /// Assigns a worker to a job in a commercial building.
    /// </summary>
    public bool AssignWorker(Character worker, CommercialBuilding building, Job job)
    {
        if (worker == null || building == null || job == null) return false;
        return building.AssignWorker(worker, job);
    }
}
