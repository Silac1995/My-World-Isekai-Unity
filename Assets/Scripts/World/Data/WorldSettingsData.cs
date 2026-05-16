using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MWI.WorldSystem
{
    [CreateAssetMenu(fileName = "WorldSettingsData", menuName = "MWI/World/WorldSettingsData")]
    public class WorldSettingsData : ScriptableObject
    {
        [Header("Community Tracker: Proximity")]
        [Tooltip("Radius around an NPC to search for other NPCs to form a community chunk.")]
        public float ProximityChunkSize = 75f;

        [Header("Zone Placement")]
        [Tooltip("Minimum world-unit distance between any two IWorldZone centers. Enforced at building placement AND procedural zone spawning. 11 units = 1.67m (see CLAUDE.md rule 32). Default 150 units ≈ 22.7m.")]
        public float MapMinSeparation = 150f;

        [Header("World Offset Allocator")]
        [Tooltip("The fixed distance added per map slot on the X/Z plane.")]
        public float SlotOffsetDistance = 10000f;
        [Tooltip("Days a released slot must wait before being safely recycled.")]
        public int SlotRecycleCooldownDays = 30;
        [Tooltip("Y offset for interior maps to separate them from ground-level maps.")]
        public float InteriorYOffset = 5000f;
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

        [FormerlySerializedAs("Blueprints")]
        [Tooltip("Authored BuildingSO blueprints. One asset per building type; all lookups scan by PrefabId. Post-migration (2026-05-16) this is the single source of truth — the legacy List<BuildingRegistryEntry> field was deleted in Task 18 cleanup.")]
        public List<BuildingSO> BuildingRegistry = new List<BuildingSO>();

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

        /// <summary>
        /// Resolves a BuildingSO blueprint by its PrefabId string. Returns null when not found.
        /// Pair with GetBuildingPrefab / GetInteriorPrefab when you need the prefab reference instead.
        /// </summary>
        public BuildingSO GetBuildingBlueprint(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;
            for (int i = 0; i < BuildingRegistry.Count; i++)
            {
                var entry = BuildingRegistry[i];
                if (entry != null && entry.PrefabId == prefabId) return entry;
            }
            return null;
        }

        /// <summary>
        /// Resolves the placement prefab for a given PrefabId by looking up the BuildingSO blueprint.
        /// </summary>
        public GameObject GetBuildingPrefab(string prefabId)
            => GetBuildingBlueprint(prefabId)?.BuildingPrefab;

        /// <summary>
        /// Resolves the interior prefab for a given PrefabId by looking up the BuildingSO blueprint.
        /// </summary>
        public GameObject GetInteriorPrefab(string prefabId)
            => GetBuildingBlueprint(prefabId)?.InteriorPrefab;
    }
}
