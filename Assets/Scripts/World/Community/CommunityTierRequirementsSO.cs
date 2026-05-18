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
        [Header("Tier identity")]
        [SerializeField, Tooltip("Which CommunityLevel this asset describes. One asset per level.")]
        private CommunityLevel _level = CommunityLevel.SmallGroup;

        [Header("Tier-up requirements")]
        [SerializeField, Tooltip("Minimum citizen count (community.members.Count).")]
        private int _minPopulation = 1;

        [SerializeField, Tooltip("Required completed buildings. Duplicates count: [House, House, Farm] = 2 houses + 1 farm.")]
        private List<BuildingSO> _requiredBuildings = new List<BuildingSO>();

        [SerializeField, Tooltip("Minimum gold in the AB treasury.")]
        private int _minTreasury = 0;

        [Header("Tier rewards")]
        [SerializeField, Tooltip("Civic blueprints unlocked in the admin console at this tier.")]
        private List<BuildingSO> _unlockedBlueprints = new List<BuildingSO>();

        public CommunityLevel Level => _level;
        public int MinPopulation => _minPopulation;
        public IReadOnlyList<BuildingSO> RequiredBuildings => _requiredBuildings;
        public int MinTreasury => _minTreasury;
        public IReadOnlyList<BuildingSO> UnlockedBlueprints => _unlockedBlueprints;
    }
}
