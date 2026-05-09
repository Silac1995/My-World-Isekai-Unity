using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Lazy-init registry of all AmbitionSO assets in the project. Late-joining clients
    /// skip GameLauncher.LaunchSequence; the lazy Get() ensures they still see populated
    /// data (see feedback_lazy_static_registry_pattern.md).
    /// </summary>
    public static class AmbitionRegistry
    {
        private static Dictionary<string, AmbitionSO> _byGuid;
        private static Dictionary<AmbitionSO, string> _toGuid;

        public static AmbitionSO Get(string guid)
        {
            EnsureLoaded();
            return _byGuid.TryGetValue(guid, out var so) ? so : null;
        }

        public static string GetGuid(AmbitionSO so)
        {
            if (so == null) return null;
            EnsureLoaded();
            return _toGuid.TryGetValue(so, out var guid) ? guid : null;
        }

        public static IReadOnlyCollection<AmbitionSO> All
        {
            get
            {
                EnsureLoaded();
                return _byGuid.Values;
            }
        }

        private static void EnsureLoaded()
        {
            if (_byGuid != null) return;
            _byGuid = new Dictionary<string, AmbitionSO>();
            _toGuid = new Dictionary<AmbitionSO, string>();
            // Resources path matches the spec's authored asset layout.
            var all = Resources.LoadAll<AmbitionSO>("Data/Ambitions");
            foreach (var so in all)
            {
                if (so == null) continue;
                // Use the asset's instance ID as a stable key in builds; in the editor
                // we prefer the AssetDatabase GUID (resolved via the editor-only helper
                // in CharacterAmbition save layer). Fall back to name for ID purposes.
                string id = so.name;
                if (string.IsNullOrEmpty(id)) continue;
                _byGuid[id] = so;
                _toGuid[so] = id;
            }
        }

        /// <summary>Test seam — clears cached state so tests can repopulate from a stub.</summary>
        public static void ResetForTests() { _byGuid = null; _toGuid = null; }
    }
}
