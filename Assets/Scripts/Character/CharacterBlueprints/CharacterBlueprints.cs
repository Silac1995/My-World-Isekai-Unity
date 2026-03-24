using System.Collections.Generic;
using UnityEngine;
namespace MWI.CharacterSystem
{
    /// <summary>
    /// Component added to Character prefabs to store which building IDs they know how to construct.
    /// This represents their specific "crafting knowledge" for communities.
    /// </summary>
    public class CharacterBlueprints : MonoBehaviour
    {
        [Header("Starting Knowledge")]
        [Tooltip("List of BuildingPrefab IDs this character already knows how to build by default.")]
        [SerializeField] private List<string> _unlockedBuildingIds = new List<string>();

        /// <summary>
        /// Read-only access to the currently known blueprint IDs.
        /// </summary>
        public IReadOnlyList<string> UnlockedBuildingIds => _unlockedBuildingIds;

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
                // TODO: Fire an event here if UI needs to update (e.g., "New Blueprint Unlocked!")
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
}
