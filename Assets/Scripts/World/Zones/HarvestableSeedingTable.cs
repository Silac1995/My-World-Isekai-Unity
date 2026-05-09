using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Simple data container mapping harvestable ResourceIds to their initial pool sizes
    /// when a new wilderness zone is spawned. Used by WildernessZoneDef.
    /// </summary>
    [Serializable]
    public class HarvestableSeedingTable
    {
        [Serializable]
        public struct Entry
        {
            public string ResourceId;
            public float InitialAmount;
            public float MaxAmount;
        }

        public List<Entry> Entries = new List<Entry>();

        /// <summary>Produces a list of ResourcePoolEntry instances suitable for WildernessZone._harvestables.</summary>
        public List<ResourcePoolEntry> BuildInitialPool(int currentDay)
        {
            var pool = new List<ResourcePoolEntry>();
            if (Entries == null) return pool;

            foreach (var entry in Entries)
            {
                if (string.IsNullOrEmpty(entry.ResourceId)) continue;
                pool.Add(new ResourcePoolEntry
                {
                    ResourceId = entry.ResourceId,
                    CurrentAmount = entry.InitialAmount,
                    MaxAmount = entry.MaxAmount,
                    LastHarvestedDay = currentDay,
                });
            }
            return pool;
        }
    }
}
