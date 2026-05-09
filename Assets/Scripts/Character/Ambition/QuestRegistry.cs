using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Lazy-init registry of QuestSO assets used by the ambition system. Same pattern
    /// as AmbitionRegistry. Existing IQuest subtypes (BuildingTask, BuyOrder, etc.)
    /// are NOT in this registry — they live in their own job-system pipeline.
    /// </summary>
    public static class QuestRegistry
    {
        private static Dictionary<string, QuestSO> _byGuid;
        private static Dictionary<QuestSO, string> _toGuid;

        public static QuestSO Get(string guid)
        {
            EnsureLoaded();
            return _byGuid.TryGetValue(guid, out var so) ? so : null;
        }

        public static string GetGuid(QuestSO so)
        {
            if (so == null) return null;
            EnsureLoaded();
            return _toGuid.TryGetValue(so, out var guid) ? guid : null;
        }

        public static IReadOnlyCollection<QuestSO> All
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
            _byGuid = new Dictionary<string, QuestSO>();
            _toGuid = new Dictionary<QuestSO, string>();
            var all = Resources.LoadAll<QuestSO>("Data/Ambitions/Quests");
            foreach (var so in all)
            {
                if (so == null) continue;
                string id = so.name;
                if (string.IsNullOrEmpty(id)) continue;
                _byGuid[id] = so;
                _toGuid[so] = id;
            }
        }

        public static void ResetForTests() { _byGuid = null; _toGuid = null; }
    }
}
