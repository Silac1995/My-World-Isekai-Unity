using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Per-<see cref="CommunityLevel"/> requirements asset for tier-up.
    /// <see cref="Community.TryPromoteLevel"/> reads the next level's requirements,
    /// validates population + buildings + treasury, and either promotes or returns a
    /// "what's missing" reason for UI display.
    ///
    /// One asset per CommunityLevel value lives in
    /// <c>Assets/Resources/Data/CommunityTiers/</c>; <see cref="CommunityTierRegistry.Get"/>
    /// is the runtime entry point (lazy-init from Resources on first call so
    /// joining clients that skip <c>GameLauncher.LaunchSequence</c> still work).
    ///
    /// Plan 4c Task 2.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Community/Tier Requirements", fileName = "TierRequirements_")]
    public class CommunityTierRequirementsSO : ScriptableObject
    {
        [Header("Tier identity (authoritative)")]
        [SerializeField, Tooltip("Stable string id for save data + cross-references. Defaults to the asset name on first OnValidate; designers can override to anything unique.")]
        private string _tierId;

        [SerializeField, Tooltip("Sort order in the tier ladder. Lower = earlier (SmallGroup=0). Tier-up logic reads Registry.GetByOrder(currentOrder+1) — adding a new tier between Town and City is just inserting an SO at the right order index. Negative values are allowed for pre-tiers.")]
        private int _order = 0;

        [SerializeField, Tooltip("UI display name (e.g. \"Small Group\", \"Camp\", \"Province\"). Empty falls back to the asset name.")]
        private string _displayName;

        [Header("Tier identity (legacy display hint)")]
        [SerializeField, Tooltip("Optional CommunityLevel enum mapping for back-compat with code paths that still query the enum. New tiers can leave this at SmallGroup — the gameplay path no longer keys off it.")]
        private CommunityLevel _level = CommunityLevel.SmallGroup;

        [Header("Tier-up requirements")]
        [SerializeField, Tooltip("Minimum citizen count (community.members.Count).")]
        private int _minPopulation = 1;

        [SerializeField, Range(0f, 1f), Tooltip("Minimum fraction of citizens whose mood is positive. v1 stub: returns true until a CharacterMood / NeedHappiness system ships. Leave at 0 to skip.")]
        private float _minHappyPopulationFraction = 0f;

        [SerializeField, Tooltip("Required completed buildings. Duplicates count: [House, House, Farm] = 2 houses + 1 farm.")]
        private List<BuildingSO> _requiredBuildings = new List<BuildingSO>();

        [SerializeField, Tooltip("Minimum gold in the AB treasury.")]
        private int _minTreasury = 0;

        [Header("Tier rewards")]
        [SerializeField, Tooltip("Civic blueprints unlocked in the admin console at this tier.")]
        private List<BuildingSO> _unlockedBlueprints = new List<BuildingSO>();

        /// <summary>Stable string id (legacy fall back: asset name when the field is empty).</summary>
        public string TierId => string.IsNullOrEmpty(_tierId) ? name : _tierId;
        /// <summary>Sort order in the tier ladder. Tier-up reads <c>Registry.GetByOrder(currentOrder + 1)</c>.</summary>
        public int Order => _order;
        /// <summary>UI display name (falls back to asset name when blank).</summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;

        /// <summary>Legacy enum mapping. New tiers ignore this — gameplay paths use TierId / Order.</summary>
        public CommunityLevel Level => _level;
        public int MinPopulation => _minPopulation;
        public float MinHappyPopulationFraction => _minHappyPopulationFraction;
        public IReadOnlyList<BuildingSO> RequiredBuildings => _requiredBuildings;
        public int MinTreasury => _minTreasury;
        public IReadOnlyList<BuildingSO> UnlockedBlueprints => _unlockedBlueprints;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Default _tierId to asset name on first save, but never overwrite a manual edit.
            if (string.IsNullOrEmpty(_tierId)) _tierId = name;
        }
#endif
    }
}
