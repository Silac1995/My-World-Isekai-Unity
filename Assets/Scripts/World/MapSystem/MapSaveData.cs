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
    public class HibernatedNeedData
    {
        public string NeedType;
        public float Value;
    }

    [Serializable]
    public class HibernatedNPCData
    {
        public string CharacterId;
        public string PrefabName; // Quick way to know what to spawn
        public uint PrefabHash; // Proper NGO GUID matching

        // Party
        public string PartyId;

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
        
        // V2 Job State
        public bool HasHarvesterJob;
        public JobType SavedJobType;
        
        // V2 Needs
        public List<HibernatedNeedData> SavedNeeds = new List<HibernatedNeedData>();
        
        // Character Knowledge
        public List<string> UnlockedBuildingIds = new List<string>();

        // Character Identity & Visuals (required for proper respawn)
        public string RaceId;           // NetworkRaceId — needed for visual preset + name generation
        public string CharacterName;    // NetworkCharacterName — display name
        public int VisualSeed;          // NetworkVisualSeed — deterministic visual variation
    }
}
