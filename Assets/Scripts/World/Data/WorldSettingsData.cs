using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    [Serializable]
    public struct BuildingRegistryEntry
    {
        public string PrefabId;
        public GameObject BuildingPrefab;
        [Tooltip("Higher number = higher priority for construction.")]
        public int CommunityPriority;
    }

    [CreateAssetMenu(fileName = "WorldSettingsData", menuName = "MWI/World/WorldSettingsData")]
    public class WorldSettingsData : ScriptableObject
    {
        [Header("Community Tracker: Proximity")]
        [Tooltip("Radius around an NPC to search for other NPCs to form a community chunk.")]
        public float ProximityChunkSize = 75f;
        
        [Header("Community Tracker: Settlement Promotion")]
        [Tooltip("Minimum NPCs required to promote a Roaming Camp to a Settlement.")]
        public int SettlementMinPopulation = 6;
        [Tooltip("How many in-game days the minimum population must be sustained for Settlement promotion.")]
        public int SettlementSustainedDays = 7;

        [Header("Community Tracker: City Promotion")]
        [Tooltip("Minimum NPCs required to promote a Settlement to an Established City.")]
        public int CityMinPopulation = 15;
        [Tooltip("How many in-game days the minimum population must be sustained for City promotion.")]
        public int CitySustainedDays = 30;

        [Header("Community Tracker: Reclamation")]
        [Tooltip("Minimum NPCs required to reclaim an Abandoned City.")]
        public int ReclamationMinPopulation = 2;
        [Tooltip("How many in-game days the minimum population must be sustained to reclaim an Abandoned City.")]
        public int ReclamationSustainedDays = 2;

        [Header("Community Tracker: Dissolution")]
        [Tooltip("Time in days an area must lack the minimum population before checking for dissolution or abandonment.")]
        public int DissolutionGracePeriodDays = 7;

        [Header("World Offset Allocator")]
        [Tooltip("The fixed distance added per map slot on the X/Z plane.")]
        public float SlotOffsetDistance = 10000f;
        [Tooltip("Days a released slot must wait before being safely recycled.")]
        public int SlotRecycleCooldownDays = 30;
        [Header("Community Tracker: Prefab Registry")]
        [Tooltip("Physical terrain/building prefab spawned for a Roaming Camp.")]
        public GameObject RoamingCampPrefab;
        [Tooltip("Physical terrain/building prefab spawned for a Settlement.")]
        public GameObject SettlementPrefab;
        [Tooltip("Physical terrain/building prefab spawned for an Established City.")]
        public GameObject EstablishedCityPrefab;

        [Header("Dynamic Building Registry")]
        [Tooltip("Generic scaffolding visual used when a building is UnderConstruction.")]
        public GameObject GenericScaffoldPrefab;
        
        [Tooltip("Defines offline resource production rates based on job type.")]
        public JobYieldRegistry JobYields;

        [Tooltip("List of dynamic buildings that can be constructed offline.")]
        public List<BuildingRegistryEntry> BuildingRegistry = new List<BuildingRegistryEntry>();

        public GameObject GetPrefabForTier(CommunityTier tier)
        {
            switch (tier)
            {
                case CommunityTier.RoamingCamp: return RoamingCampPrefab;
                case CommunityTier.Settlement: return SettlementPrefab;
                case CommunityTier.EstablishedCity: return EstablishedCityPrefab;
                default: return null;
            }
        }

        public GameObject GetBuildingPrefab(string prefabId)
        {
            foreach (var entry in BuildingRegistry)
            {
                if (entry.PrefabId == prefabId) return entry.BuildingPrefab;
            }
            return null;
        }
    }
}
