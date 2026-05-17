using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Blueprint ScriptableObject for a single building type. Replaces the inline
    /// BuildingRegistryEntry struct on WorldSettingsData and the duplicated prefab
    /// fields on Building.cs (BuildingName / BuildingType / ConstructionRequirements /
    /// DefaultFurnitureLayout). One asset per building type, authored under
    /// Assets/Resources/Data/Buildings/.
    ///
    /// PrefabId is the cross-session identity key (matches the string written into
    /// BuildingSaveData.PrefabId). Must be preserved verbatim across the migration
    /// from BuildingRegistryEntry — renaming silently invalidates every existing save.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingSO", menuName = "MWI/World/BuildingSO", order = 100)]
    public class BuildingSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable cross-session key (e.g. 'Shop_Armor_A'). Persisted into BuildingSaveData.PrefabId. NEVER rename — breaks every save that references this building type.")]
        [SerializeField] private string _prefabId;

        [Tooltip("Designer-facing display name. Falls back to GameObject name if blank.")]
        [SerializeField] private string _buildingName;

        [SerializeField] private Sprite _icon;

        [SerializeField] private BuildingType _buildingType = BuildingType.Residential;

        [Header("Prefabs")]
        [Tooltip("The networked Building prefab spawned when this type is placed.")]
        [SerializeField] private GameObject _buildingPrefab;

        [Tooltip("Optional interior MapController prefab. Null for non-enterable buildings.")]
        [SerializeField] private GameObject _interiorPrefab;

        [Header("Community")]
        [Tooltip("Higher = community leaders auto-build this first when offline auto-build fires (MacroSimulator.SimulateCityGrowth). Single int sorted descending.")]
        [SerializeField] private int _communityPriority;

        [Header("Construction")]
        [Tooltip("Items + amounts that must be delivered to finish construction. Order is the positional index used by BuildingSaveData.DeliveredMaterials — never reorder existing entries (would corrupt in-flight construction saves). Append only.")]
        [SerializeField] private List<CraftingIngredient> _constructionRequirements = new List<CraftingIngredient>();

        [Header("Default Furniture")]
        [Tooltip("Layout spawned by the server on first construction-complete (and not on save-restore). Mirrors the legacy Building._defaultFurnitureLayout authoring surface.")]
        [SerializeField] private List<Building.DefaultFurnitureSlot> _defaultFurnitureLayout = new List<Building.DefaultFurnitureSlot>();

        [Header("Placement (Plan 2 — City Founding)")]
        [Tooltip("Footprint in BuildingGrid cells (default 1×1 = one 8-unit cell). Larger blueprints occupy a rectangle of cells; placement is rejected if any cell is occupied. Authoritative dimension; ghost preview snaps the bottom-left cell under the cursor.")]
        [SerializeField] private Vector2Int _gridFootprintCells = new Vector2Int(1, 1);

        [Tooltip("Placement-authority category. Personal = anyone with the blueprint can place via the normal ghost flow. Civic = only a community leader can place via the admin console (Plan 5).")]
        [SerializeField] private BlueprintCategory _blueprintCategory = BlueprintCategory.Personal;

        [Tooltip("Minimum community tier required for placement. Only enforced for Civic blueprints by Plan 5's admin-console authority gate.")]
        [SerializeField] private CommunityLevel _minTier = CommunityLevel.SmallGroup;

        public string PrefabId => _prefabId;
        public string BuildingName => _buildingName;
        public Sprite Icon => _icon;
        public BuildingType BuildingType => _buildingType;
        public GameObject BuildingPrefab => _buildingPrefab;
        public GameObject InteriorPrefab => _interiorPrefab;
        public int CommunityPriority => _communityPriority;
        public IReadOnlyList<CraftingIngredient> ConstructionRequirements => _constructionRequirements;
        public IReadOnlyList<Building.DefaultFurnitureSlot> DefaultFurnitureLayout => _defaultFurnitureLayout;
        public Vector2Int GridFootprintCells => _gridFootprintCells;
        public BlueprintCategory BlueprintCategory => _blueprintCategory;
        public CommunityLevel MinTier => _minTier;
    }
}
