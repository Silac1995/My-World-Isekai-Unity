using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Lazy static registry of <see cref="CommunityTierRequirementsSO"/> assets, keyed by
    /// <see cref="CommunityLevel"/>. Loaded from <c>Resources/Data/CommunityTiers/</c> on
    /// first <see cref="Get"/> call.
    ///
    /// Lazy-init in <see cref="Get"/> (NOT in a <c>[RuntimeInitializeOnLoadMethod]</c>) per
    /// the joining-clients-skip-GameLauncher pattern — see
    /// <c>memory/feedback_lazy_static_registry_pattern.md</c>. A late-joiner that never
    /// runs <c>GameLauncher.LaunchSequence</c> still gets a working registry the first
    /// time anything calls <see cref="Get"/>.
    ///
    /// Plan 4c Task 2.
    /// </summary>
    public static class CommunityTierRegistry
    {
        private static Dictionary<CommunityLevel, CommunityTierRequirementsSO> _byLevel;

        /// <summary>
        /// Returns the requirements asset for the given level, or null if no asset is
        /// authored for that level. Defensive null-return so callers can early-exit
        /// rather than throw on bootstrap holes.
        /// </summary>
        public static CommunityTierRequirementsSO Get(CommunityLevel level)
        {
            if (_byLevel == null) LazyInit();
            return _byLevel.TryGetValue(level, out var so) ? so : null;
        }

        /// <summary>Convenience: requirements for promoting FROM <paramref name="current"/> TO the next level.</summary>
        public static CommunityTierRequirementsSO GetForNextLevelFrom(CommunityLevel current)
        {
            int nextInt = (int)current + 1;
            // Defensive: cap at Empire (last tier). Returns null when at max already.
            if (!System.Enum.IsDefined(typeof(CommunityLevel), nextInt)) return null;
            return Get((CommunityLevel)nextInt);
        }

        private static void LazyInit()
        {
            _byLevel = new Dictionary<CommunityLevel, CommunityTierRequirementsSO>();
            var all = Resources.LoadAll<CommunityTierRequirementsSO>("Data/CommunityTiers");
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                var so = all[i];
                if (so == null) continue;
                _byLevel[so.Level] = so;
            }
        }

        /// <summary>Editor / test hook — drops the cache so the next <see cref="Get"/> call re-scans.</summary>
        public static void ResetForTests()
        {
            _byLevel = null;
        }
    }
}
