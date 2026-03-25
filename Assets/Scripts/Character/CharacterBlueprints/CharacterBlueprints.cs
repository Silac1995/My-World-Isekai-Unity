using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component added to Character prefabs to store which building IDs they know how to construct.
/// This represents their specific "crafting knowledge" for communities.
/// </summary>
public class CharacterBlueprints : CharacterSystem
{
    [Header("Starting Knowledge")]
    [Tooltip("List of BuildingPrefab IDs this character already knows how to build by default.")]
    [SerializeField] private List<string> _unlockedBuildingIds = new List<string>();

    [Header("Placement Settings")]
    [Tooltip("Maximum distance from the character to place a building.")]
    [SerializeField] private float _maxPlacementRange = 10f;

    /// <summary>
    /// Read-only access to the currently known blueprint IDs.
    /// </summary>
    public IReadOnlyList<string> UnlockedBuildingIds => _unlockedBuildingIds;

    /// <summary>
    /// Maximum distance from the character to place a building.
    /// </summary>
    public float MaxPlacementRange => _maxPlacementRange;

    /// <summary>
    /// Safely completely overrides the unlocked buildings list (e.g., from a save file).
    /// </summary>
    public void SetUnlockedBuildings(IEnumerable<string> loadedIds)
    {
        _unlockedBuildingIds.Clear();
        if (loadedIds != null)
        {
            _unlockedBuildingIds.AddRange(loadedIds);
        }
    }

    /// <summary>
    /// Allows characters to learn a new blueprint at runtime.
    /// </summary>
    public void UnlockBuilding(string buildingId)
    {
        if (string.IsNullOrEmpty(buildingId)) return;
        
        if (!_unlockedBuildingIds.Contains(buildingId))
        {
            _unlockedBuildingIds.Add(buildingId);
        }
    }
    
    /// <summary>
    /// Checks if the character can build this specific ID.
    /// </summary>
    public bool KnowsBlueprint(string buildingId)
    {
        return _unlockedBuildingIds.Contains(buildingId);
    }
}
