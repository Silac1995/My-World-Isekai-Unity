using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    [System.Serializable]
    public class HarvestableEntry
    {
        [Tooltip("The unique ID of the resource (e.g., 'Wood', 'Stone')")]
        public string ResourceId;
        
        [Tooltip("The prefab of the Harvestable object to spawn in dynamic maps")]
        public GameObject HarvestablePrefab;
        
        [Tooltip("Probability weight for this resource to be chosen for spawning or offline harvesting")]
        public float Weight = 1f;
        
        [Tooltip("How much of this resource is yielded per offline day of harvesting")]
        public float BaseYieldQuantity = 10f;
        
        [Tooltip("How many in-game days it takes for the resource pool to regenerate once harvested")]
        public int RegenerationDays = 1;
    }

    [CreateAssetMenu(fileName = "NewBiomeDefinition", menuName = "MWI/World/Biome Definition")]
    public class BiomeDefinition : ScriptableObject
    {
        public string BiomeId;
        public string DisplayName;          // "Temperate Forest", "Rocky Highlands"
        public Sprite BiomeIcon;

        [Range(0f, 0.8f)]
        public float HarvestableDensity = 0.5f;    // Hard capped at 80% as requested

        public List<HarvestableEntry> Harvestables = new List<HarvestableEntry>();
    }
}
