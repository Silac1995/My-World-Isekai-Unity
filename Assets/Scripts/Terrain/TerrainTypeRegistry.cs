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
            if (_types != null) return;
            _types = Resources.LoadAll<TerrainType>("Data/Terrain/TerrainTypes")
                .ToDictionary(t => t.TypeId);
            Debug.Log($"[TerrainTypeRegistry] Initialized with {_types.Count} terrain types.");
        }

        public static TerrainType Get(string typeId)
        {
            // Lazy auto-initialise on first access. Joining clients (especially the 2nd+
            // peer) hit this from CharacterTerrainEffects.Update during the brief window
            // between NGO spawning the replicated host Character and our explicit
            // GameSessionManager.HandleClientConnected → TerrainTypeRegistry.Initialize()
            // call. Without lazy init, that window produces a per-frame "Not initialized"
            // error spam loop. Initialize is idempotent (early-return if _types != null),
            // so paying it once on first Get() is cheap and order-independent.
            if (_types == null) Initialize();
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
