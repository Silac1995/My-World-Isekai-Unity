using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Lazy static registry of <see cref="CommunityTierRequirementsSO"/> assets, indexed
    /// by <see cref="CommunityTierRequirementsSO.TierId"/> + <see cref="CommunityTierRequirementsSO.Order"/>
    /// (and the legacy <see cref="CommunityLevel"/> enum for back-compat). Loaded from
    /// <c>Resources/Data/CommunityTiers/</c> on first call.
    ///
    /// <para>Lazy-init in the accessor (NOT in a <c>[RuntimeInitializeOnLoadMethod]</c>) per
    /// the joining-clients-skip-GameLauncher pattern — a late-joiner that never runs
    /// <c>GameLauncher.LaunchSequence</c> still gets a working registry on first call.</para>
    ///
    /// <para>Adding a new tier (Plan 4c follow-up, 2026-05-18): drop a new SO into
    /// <c>Resources/Data/CommunityTiers/</c>, set its <c>_order</c> field to slot it into
    /// the ladder (e.g. 7 = post-Empire, or 3.5 by re-numbering neighbours). No code change
    /// needed — <see cref="Community.TryPromoteLevel"/> walks the ladder via
    /// <see cref="GetNext"/>.</para>
    /// </summary>
    public static class CommunityTierRegistry
    {
        private static Dictionary<string, CommunityTierRequirementsSO> _byId;
        private static Dictionary<CommunityLevel, CommunityTierRequirementsSO> _byLevel;
        private static List<CommunityTierRequirementsSO> _orderedAscending;

        /// <summary>Legacy: requirements for the given enum level. Returns null when the
        /// enum value has no matching SO. New code prefers <see cref="GetById"/> or
        /// <see cref="GetByOrder"/> so designer-authored tiers (off-enum) resolve too.</summary>
        public static CommunityTierRequirementsSO Get(CommunityLevel level)
        {
            EnsureInit();
            return _byLevel.TryGetValue(level, out var so) ? so : null;
        }

        /// <summary>Legacy: requirements for the tier immediately above <paramref name="current"/>
        /// in the enum order. Kept as a thin wrapper around <see cref="GetNext"/> for any
        /// caller still threading a <see cref="CommunityLevel"/> through; new code should
        /// pass the SO directly to <see cref="GetNext"/>.</summary>
        public static CommunityTierRequirementsSO GetForNextLevelFrom(CommunityLevel current)
        {
            var currentTier = Get(current);
            return GetNext(currentTier);
        }

        /// <summary>Authoritative lookup by stable string id (<see cref="CommunityTierRequirementsSO.TierId"/>).</summary>
        public static CommunityTierRequirementsSO GetById(string tierId)
        {
            if (string.IsNullOrEmpty(tierId)) return null;
            EnsureInit();
            return _byId.TryGetValue(tierId, out var so) ? so : null;
        }

        /// <summary>Lookup by sort order. Returns null when no tier has the exact order.
        /// Use <see cref="GetNext"/> for tier-up — it walks to the strict-next order even
        /// when the ladder has gaps.</summary>
        public static CommunityTierRequirementsSO GetByOrder(int order)
        {
            EnsureInit();
            for (int i = 0; i < _orderedAscending.Count; i++)
            {
                if (_orderedAscending[i].Order == order) return _orderedAscending[i];
            }
            return null;
        }

        /// <summary>The tier immediately above <paramref name="current"/> in the ascending
        /// order ladder. Returns null when <paramref name="current"/> is at max (top of ladder).
        /// When <paramref name="current"/> is null, returns the first (lowest-order) tier.</summary>
        public static CommunityTierRequirementsSO GetNext(CommunityTierRequirementsSO current)
        {
            EnsureInit();
            if (_orderedAscending.Count == 0) return null;
            if (current == null) return _orderedAscending[0];
            for (int i = 0; i < _orderedAscending.Count; i++)
            {
                if (_orderedAscending[i] != current) continue;
                return i + 1 < _orderedAscending.Count ? _orderedAscending[i + 1] : null;
            }
            // Fallback for callers passing an SO that isn't in this registry instance — find
            // the first tier with strictly greater Order.
            for (int i = 0; i < _orderedAscending.Count; i++)
            {
                if (_orderedAscending[i].Order > current.Order) return _orderedAscending[i];
            }
            return null;
        }

        /// <summary>The tier immediately below <paramref name="current"/>. Mirrors <see cref="GetNext"/>
        /// for symmetric dev-mode tier shifts. Returns null at the bottom of the ladder.</summary>
        public static CommunityTierRequirementsSO GetPrevious(CommunityTierRequirementsSO current)
        {
            EnsureInit();
            if (_orderedAscending.Count == 0 || current == null) return null;
            for (int i = 0; i < _orderedAscending.Count; i++)
            {
                if (_orderedAscending[i] != current) continue;
                return i - 1 >= 0 ? _orderedAscending[i - 1] : null;
            }
            for (int i = _orderedAscending.Count - 1; i >= 0; i--)
            {
                if (_orderedAscending[i].Order < current.Order) return _orderedAscending[i];
            }
            return null;
        }

        /// <summary>Read-only ascending-order view of every registered tier. Useful for UI
        /// listings (tier ladder display) and dev tooling.</summary>
        public static IReadOnlyList<CommunityTierRequirementsSO> AllAscending
        {
            get { EnsureInit(); return _orderedAscending; }
        }

        private static void EnsureInit()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, CommunityTierRequirementsSO>();
            _byLevel = new Dictionary<CommunityLevel, CommunityTierRequirementsSO>();
            _orderedAscending = new List<CommunityTierRequirementsSO>();

            var all = Resources.LoadAll<CommunityTierRequirementsSO>("Data/CommunityTiers");
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                var so = all[i];
                if (so == null) continue;
                _orderedAscending.Add(so);
                if (!string.IsNullOrEmpty(so.TierId) && !_byId.ContainsKey(so.TierId))
                    _byId[so.TierId] = so;
                // Legacy enum index — first-write-wins, so designers can have multiple SOs
                // share an enum value (e.g. two flavours of "Camp") without breaking the
                // lookup; the authoritative path is GetById/GetByOrder.
                if (!_byLevel.ContainsKey(so.Level)) _byLevel[so.Level] = so;
            }
            _orderedAscending.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>Editor / test hook — drops the cache so the next call re-scans.</summary>
        public static void ResetForTests()
        {
            _byId = null;
            _byLevel = null;
            _orderedAscending = null;
        }
    }
}
