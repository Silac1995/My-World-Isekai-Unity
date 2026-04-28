using System.Collections.Generic;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Static O(1) lookup from CropSO.Id → CropSO. Mirrors TerrainTypeRegistry. See spec §3.2.
    ///
    /// Initialise() is called once from GameLauncher.LaunchSequence after scene load.
    /// Clear() is called from SaveManager.ResetForNewSession.
    ///
    /// MUST be initialised before any MapController.WakeUp() or save-restore that reads cells
    /// with PlantedCropId set — see spec §9.3.
    /// </summary>
    public static class CropRegistry
    {
        private static readonly Dictionary<string, CropSO> _byId = new Dictionary<string, CropSO>();
        private static bool _initialised;

        public static bool IsInitialised => _initialised;

        public static void Initialize()
        {
            if (_initialised) return;
            var crops = Resources.LoadAll<CropSO>("Data/Farming/Crops");
            for (int i = 0; i < crops.Length; i++)
                Register(crops[i]);
            _initialised = true;
            Debug.Log($"[CropRegistry] Initialised with {_byId.Count} crop(s).");
        }

        public static void Clear()
        {
            _byId.Clear();
            _initialised = false;
        }

        public static CropSO Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var crop) ? crop : null;
        }

        private static void Register(CropSO crop)
        {
            if (crop == null || string.IsNullOrEmpty(crop.Id)) return;
            if (_byId.ContainsKey(crop.Id))
            {
                Debug.LogError($"[CropRegistry] Duplicate Id '{crop.Id}' on {crop.name}; overwriting.");
            }
            _byId[crop.Id] = crop;
        }

#if UNITY_EDITOR
        public static void InitializeForTests(IEnumerable<CropSO> crops)
        {
            Clear();
            foreach (var c in crops) Register(c);
            _initialised = true;
        }
#endif
    }
}
