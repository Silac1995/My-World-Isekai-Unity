using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Designer-authored template for spawning a WildernessZone. Passed into
    /// WildernessZoneManager.SpawnZone(pos, def, parent) to produce a configured zone.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWildernessZoneDef", menuName = "MWI/World/WildernessZoneDef", order = 10)]
    public class WildernessZoneDef : ScriptableObject
    {
        [Tooltip("Default radius in Unity units (11 units = 1.67m). Typical: 75 units ≈ 11.4m = 1 chunk.")]
        public float DefaultRadius = 75f;

        [Tooltip("Optional biome override. Null = inherit from parent Region's DefaultBiome.")]
        public BiomeDefinition BiomeOverride;

        [Tooltip("Motion strategies applied daily by MacroSimulator. Leave empty to default to StaticMotion.")]
        public List<ScriptableZoneMotionStrategy> DefaultMotion = new List<ScriptableZoneMotionStrategy>();

        [Tooltip("Optional table seeding initial harvestable pool entries.")]
        public HarvestableSeedingTable HarvestableSeedTable;
    }
}
