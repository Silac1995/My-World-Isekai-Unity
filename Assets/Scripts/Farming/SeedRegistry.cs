using System.Collections.Generic;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Static O(1) lookup from CropSO → SeedSO. Mirrors CropRegistry. Walks every
    /// SeedSO asset under Resources/Data/Item/Seed once at boot and indexes by
    /// <see cref="SeedSO.CropToPlant"/>.
    ///
    /// Why a registry instead of walking <c>crop.HarvestOutputs</c> at every call site?
    /// The previous discovery pattern coupled seed-discovery to harvest-yield semantics:
    /// every CropSO had to list its matching SeedSO in <c>_harvestOutputs</c>, which is
    /// wrong for crops where the seed comes from a different action than harvest. The
    /// canonical case is tree crops — you pick apples, you don't pick saplings; saplings
    /// drop when the tree is felled (destruction outputs). Forcing the seed into
    /// HarvestOutputs to satisfy the discovery loop produced spurious sapling drops on
    /// every apple harvest.
    ///
    /// The registry decouples discovery from yield: the SeedSO's <c>_cropToPlant</c>
    /// back-link is the single source of truth. Designers author harvest and destruction
    /// outputs purely for their gameplay meaning; planting discovery uses the registry.
    ///
    /// Late-joiner protocol matches CropRegistry/TerrainTypeRegistry: eager init from
    /// <c>GameLauncher.LaunchSequence</c> + <c>GameSessionManager.HandleClientConnected</c>,
    /// plus lazy init in <see cref="GetSeedFor"/> for any callsite that runs before either.
    /// </summary>
    public static class SeedRegistry
    {
        private static readonly Dictionary<CropSO, SeedSO> _byCrop = new Dictionary<CropSO, SeedSO>();
        private static bool _initialised;

        public static bool IsInitialised => _initialised;

        public static void Initialize()
        {
            if (_initialised) return;
            var seeds = Resources.LoadAll<SeedSO>("Data/Item/Seed");
            for (int i = 0; i < seeds.Length; i++)
                Register(seeds[i]);
            _initialised = true;
            Debug.Log($"[SeedRegistry] Initialised with {_byCrop.Count} seed(s).");
        }

        public static void Clear()
        {
            _byCrop.Clear();
            _initialised = false;
        }

        /// <summary>
        /// Returns the <see cref="SeedSO"/> whose <c>CropToPlant</c> field equals <paramref name="crop"/>,
        /// or null if no seed asset points at this crop. Lazy auto-init mirrors
        /// <see cref="CropRegistry.Get"/>: late-joining clients can reach this before
        /// <c>GameSessionManager.HandleClientConnected</c> finishes — paying init on first
        /// lookup is safe and order-independent.
        /// </summary>
        public static SeedSO GetSeedFor(CropSO crop)
        {
            if (!_initialised) Initialize();
            if (crop == null) return null;
            return _byCrop.TryGetValue(crop, out var seed) ? seed : null;
        }

        private static void Register(SeedSO seed)
        {
            if (seed == null) return;
            var crop = seed.CropToPlant;
            if (crop == null)
            {
                Debug.LogWarning($"[SeedRegistry] Seed '{seed.name}' has no CropToPlant — skipping. Designer fix: assign the matching CropSO on the SeedSO asset.");
                return;
            }
            if (_byCrop.TryGetValue(crop, out var existing))
            {
                Debug.LogError($"[SeedRegistry] Crop '{crop.Id}' is referenced by two seeds: '{existing.name}' and '{seed.name}'. First-loaded wins. Resolve by making each CropSO ↔ SeedSO link unique.");
                return;
            }
            _byCrop[crop] = seed;
        }

#if UNITY_EDITOR
        public static void InitializeForTests(IEnumerable<SeedSO> seeds)
        {
            Clear();
            foreach (var s in seeds) Register(s);
            _initialised = true;
        }
#endif
    }
}
