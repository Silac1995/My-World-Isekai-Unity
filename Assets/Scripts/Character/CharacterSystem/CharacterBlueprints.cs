using System.Collections.Generic;
using UnityEngine;

namespace MWI.CharacterSystem
{
    /// <summary>
    /// Stores a character's knowledge of building blueprints.
    /// This allows Community Leaders to build specifically what they know,
    /// fulfilling Rule 20: Characters are independent entities whose knowledge
    /// travels with them across servers and maps.
    /// </summary>
    public class CharacterBlueprints : MonoBehaviour
    {
        [Tooltip("List of PrefabIds this character knows how to build.")]
        [SerializeField] private List<string> _unlockedBuildingIds = new List<string>();

        /// <summary>
        /// Retrieves the list of known building IDs.
        /// </summary>
        public IReadOnlyList<string> GetUnlockedBuildingIds()
        {
            return _unlockedBuildingIds;
        }

        /// <summary>
        /// Unlocks a new building blueprint for this character.
        /// </summary>
        public void UnlockBlueprint(string prefabId)
        {
            if (!_unlockedBuildingIds.Contains(prefabId))
            {
                _unlockedBuildingIds.Add(prefabId);
            }
        }
    }
}
