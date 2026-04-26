using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Forge-type building.
/// Requires a Blacksmith (crafts the items) and optionally Apprentices.
/// The boss is typically the main blacksmith, or a LogisticsManager.
/// </summary>
public class ForgeBuilding : CraftingBuilding
{
    public override BuildingType BuildingType => BuildingType.Workshop;

    [Header("Forge Config")]
    [SerializeField] private int _apprenticeCount = 1;
    
    [Header("Crafting Requirements")]
    [SerializeField] private SkillSO _smithingSkill;
    [SerializeField] private SkillTier _minimumBlacksmithTier = SkillTier.Intermediate;
    [SerializeField] private SkillTier _minimumApprenticeTier = SkillTier.Novice;

    public int ApprenticeCount => _apprenticeCount;

    protected override void InitializeJobs()
    {
        _jobs.Add(new JobLogisticsManager("Forge Master")); // The boss/manager who takes orders
        _jobs.Add(new JobBlacksmith(_smithingSkill, _minimumBlacksmithTier));
        
        for (int i = 0; i < _apprenticeCount; i++)
        {
            _jobs.Add(new JobBlacksmithApprentice(_smithingSkill, _minimumApprenticeTier));
        }

        Debug.Log($"<color=orange>[Forge]</color> {buildingName} initialized with 1 Manager, 1 Blacksmith and {_apprenticeCount} Apprentice(s).");
    }

    /// <summary>
    /// Returns the blacksmith job of this building.
    /// </summary>
    public JobBlacksmith GetBlacksmith()
    {
        foreach (var job in _jobs)
        {
            if (job is JobBlacksmith smith) return smith;
        }
        return null;
    }

    /// <summary>
    /// Returns all apprentices of this forge.
    /// </summary>
    public IEnumerable<JobBlacksmithApprentice> GetApprentices()
    {
        return GetJobsOfType<JobBlacksmithApprentice>();
    }

    /// <summary>
    /// Finds an available anvil in the forge.
    /// </summary>
    public CraftingStation FindAvailableAnvil()
    {
        foreach (var station in GetFurnitureOfType<CraftingStation>())
        {
            if (station.SupportsType(CraftingStationType.Anvil) && !station.IsOccupied)
                return station;
        }
        return null;
    }

    /// <summary>
    /// Finds an available furnace in the forge.
    /// </summary>
    public CraftingStation FindAvailableFurnace()
    {
        foreach (var station in GetFurnitureOfType<CraftingStation>())
        {
            if (station.SupportsType(CraftingStationType.Furnace) && !station.IsOccupied)
                return station;
        }
        return null;
    }
}
