using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Serializable snapshot of a WildernessZone for save/load.
    /// Holds enough data to respawn the zone with its harvestable + future wildlife contents.
    /// </summary>
    [Serializable]
    public class WildernessZoneSaveData
    {
        public string ZoneId;
        public Vector3 Center;
        public float Radius;
        /// <summary>Resources path to a BiomeDefinition override, or null to inherit from the parent Region.</summary>
        public string BiomeOverrideAssetPath;
        public List<ResourcePoolEntry> Harvestables = new List<ResourcePoolEntry>();
        /// <summary>Wildlife records (empty in Phase 1; populated once animal ecology ships).</summary>
        public List<HibernatedNPCData> Wildlife = new List<HibernatedNPCData>();
        /// <summary>Resources paths to ScriptableZoneMotionStrategy assets driving this zone.</summary>
        public List<string> MotionStrategyAssetPaths = new List<string>();
        public bool IsDynamicallySpawned;
    }
}
