using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    [Serializable]
    public struct BuildingRegistryEntry
    {
        [Tooltip("The ID used to reference this building across save files and blueprints.")]
        public string PrefabId;
        public string BuildingName;
        public Sprite Icon;
        public GameObject BuildingPrefab;
        [Tooltip("Prefab for the building interior map. Contains MapController, floor, walls, exit door, NavMeshSurface. Leave null for non-enterable buildings.")]
        public GameObject InteriorPrefab;
        [Tooltip("Higher number = higher priority for community leaders to build this first when missing.")]
        public int CommunityPriority;
    }

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

        [System.Obsolete("Use Blueprints (List<BuildingSO>) — this field will be removed after Task 18 migration cleanup.")]
        [Tooltip("DEPRECATED — migrated to Blueprints. Kept until Task 18 cleanup so existing scenes/saves don't lose data during the transition.")]
        public List<BuildingRegistryEntry> BuildingRegistry = new List<BuildingRegistryEntry>();

        [Tooltip("BuildingSO blueprints. After the 2026-05-16 migration this is the source of truth; the legacy BuildingRegistry list above is kept until the Task 18 cleanup so existing scenes/saves don't lose data during the transition.")]
        public List<BuildingSO> Blueprints = new List<BuildingSO>();

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
        /// Resolves a BuildingSO blueprint by its PrefabId string. Scans the new Blueprints
        /// list; returns null when not found. Pair with GetBuildingPrefab / GetInteriorPrefab
        /// when you need the prefab reference instead.
        /// </summary>
        public BuildingSO GetBuildingBlueprint(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;
            for (int i = 0; i < Blueprints.Count; i++)
            {
                var entry = Blueprints[i];
                if (entry != null && entry.PrefabId == prefabId) return entry;
            }
            return null;
        }

        /// <summary>
        /// Resolves the placement prefab for a given PrefabId. Prefers the new Blueprints
        /// list; falls back to the legacy BuildingRegistry during the migration window
        /// (Task 11 → Task 18). Remove the legacy fall-through with the field deletion.
        /// </summary>
        public GameObject GetBuildingPrefab(string prefabId)
        {
            var blueprint = GetBuildingBlueprint(prefabId);
            if (blueprint != null) return blueprint.BuildingPrefab;

            // Legacy fall-through: only fires during the migration window.
#pragma warning disable CS0618
            foreach (var entry in BuildingRegistry)
            {
                if (entry.PrefabId == prefabId) return entry.BuildingPrefab;
            }
#pragma warning restore CS0618
            return null;
        }

        /// <summary>
        /// Resolves the interior prefab for a given PrefabId. Prefers the new Blueprints
        /// list; falls back to the legacy BuildingRegistry during the migration window.
        /// </summary>
        public GameObject GetInteriorPrefab(string prefabId)
        {
            var blueprint = GetBuildingBlueprint(prefabId);
            if (blueprint != null) return blueprint.InteriorPrefab;
#pragma warning disable CS0618
            foreach (var entry in BuildingRegistry)
            {
                if (entry.PrefabId == prefabId) return entry.InteriorPrefab;
            }
#pragma warning restore CS0618
            return null;
        }
    }
}
