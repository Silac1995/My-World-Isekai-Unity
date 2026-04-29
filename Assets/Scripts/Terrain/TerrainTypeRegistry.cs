using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWI.Terrain
{
    public static class TerrainTypeRegistry
    {
        private static Dictionary<string, TerrainType> _types;

        public static void Initialize()
        {
            _types = Resources.LoadAll<TerrainType>("Data/Terrain/TerrainTypes")
                .ToDictionary(t => t.TypeId);
            Debug.Log($"[TerrainTypeRegistry] Initialized with {_types.Count} terrain types.");
        }

        public static TerrainType Get(string typeId)
        {
            if (_types == null)
            {
                Debug.LogError("[TerrainTypeRegistry] Not initialized. Call Initialize() first.");
                return null;
            }
            if (string.IsNullOrEmpty(typeId)) return null;
            return _types.TryGetValue(typeId, out var t) ? t : null;
        }

        public static void Clear()
        {
            _types?.Clear();
            _types = null;
        }
    }
}
