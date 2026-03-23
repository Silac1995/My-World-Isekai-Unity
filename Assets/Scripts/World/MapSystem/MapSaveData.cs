using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// V1 of Serialized Map State for Hibernation.
    /// Stores everything needed to recreate an NPC when a map wakes up.
    /// </summary>
    [Serializable]
    public class MapSaveData
    {
        public string MapId;
        public double LastHibernationTime; // Absolute time in Days (CurrentDay + CurrentTime01)
        public List<HibernatedNPCData> HibernatedNPCs = new List<HibernatedNPCData>();
    }

    [Serializable]
    public class HibernatedNPCData
    {
        public string CharacterId;
        public string PrefabName; // Quick way to know what to spawn

        // V1 Simple Data: Position and Basic State
        public Vector3 Position;
        public Quaternion Rotation;

        // --- TIER 1 SCHEDULE ANCHORS ---
        public bool HasSchedule;
        
        // Locations
        public string HomeMapId;
        public Vector3 HomePosition;
        
        public string WorkMapId;
        public Vector3 WorkPosition;

        public string FreeTimeMapId;
        public Vector3 FreeTimePosition;

        // Routine Hours
        public int SleepHourStarts; // Default e.g. 22
        public int WorkHourStarts;  // Default e.g. 8
        public int FreeTimeStarts;  // Default e.g. 18
        
        // TODO for V2: Insert Inventory list, GOAP state snippet, Need levels, etc.
    }
}
