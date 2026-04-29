using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only daily tick for farming cells. One instance per active MapController.
    /// See farming spec §4 + §9.2.
    ///
    /// Sits next to TerrainCellGrid + VegetationGrowthSystem on the MapController GameObject.
    /// MapController.WakeUp() calls Initialize() then PostWakeSweep() after restoring cells.
    /// </summary>
    public class FarmGrowthSystem : MonoBehaviour
    {
        private TerrainCellGrid _grid;
        private MapController _map;
        private readonly Dictionary<int, CropHarvestable> _activeHarvestables = new Dictionary<int, CropHarvestable>(64);
        private readonly List<int> _dirtyIndices = new List<int>(64);
        private bool _subscribed;

        public void Initialize(TerrainCellGrid grid, MapController map)
        {
            _grid = grid;
            _map = map;
            // Subscribe server-only — hibernated maps don't tick (MacroSimulator handles offline catch-up).
            if (!_subscribed && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && MWI.Time.TimeManager.Instance != null)
            {
                MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
                _subscribed = true;
            }
        }

        // Self-init for scene-authored MapControllers whose WakeUp never fires. Mirrors the
        // pattern in CropVisualSpawner. PostWakeSweep is safe to run twice (idempotent —
        // re-spawning happens only for cells whose harvestable isn't already registered).
        private void Start()
        {
            if (_grid != null) return;
            var map = GetComponent<MapController>();
            if (map == null) return;
            var grid = map.GetComponent<TerrainCellGrid>();
            if (grid == null) return;
            if (grid.Width == 0)
            {
                var box = map.GetComponent<BoxCollider>();
                if (box != null) grid.Initialize(box.bounds);
            }
            if (grid.Width > 0)
            {
                Initialize(grid, map);
                PostWakeSweep();
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && MWI.Time.TimeManager.Instance != null)
                MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
            _subscribed = false;
        }

        public void RegisterHarvestable(int x, int z, CropHarvestable h)
        {
            if (_grid == null) return;
            _activeHarvestables[LinearIndex(x, z)] = h;
        }

        public void UnregisterHarvestable(int x, int z)
        {
            if (_grid == null) return;
            _activeHarvestables.Remove(LinearIndex(x, z));
        }

        private void HandleNewDay()
        {
            if (_grid == null || _map == null) return;
            _dirtyIndices.Clear();

            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) continue;

                var outcome = FarmGrowthPipeline.AdvanceOneDay(ref cell);
                int idx = LinearIndex(x, z);

                switch (outcome)
                {
                    case FarmGrowthPipeline.Outcome.JustMatured:
                        SpawnCropHarvestable(x, z, CropRegistry.Get(cell.PlantedCropId), startDepleted: false);
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.JustRefilled:
                        if (_activeHarvestables.TryGetValue(idx, out var h)) h.Refill();
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.Grew:
                    case FarmGrowthPipeline.Outcome.Refilling:
                        _dirtyIndices.Add(idx);
                        break;
                }
            }

            if (_dirtyIndices.Count > 0)
                _map.NotifyDirtyCells(_dirtyIndices.ToArray());
        }

        /// <summary>
        /// Reconstructs harvestables from cell state. Called once after MapController.WakeUp()
        /// (covers both hibernation-wake AND save-load — same code path, see spec §9.2).
        /// </summary>
        public void PostWakeSweep()
        {
            if (_grid == null) return;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) continue;
                var crop = CropRegistry.Get(cell.PlantedCropId);
                if (crop == null) continue;
                if (cell.GrowthTimer < crop.DaysToMature) continue;

                bool startDepleted = crop.IsPerennial && cell.TimeSinceLastWatered >= 0f;
                SpawnCropHarvestable(x, z, crop, startDepleted);
            }
        }

        private void SpawnCropHarvestable(int x, int z, CropSO crop, bool startDepleted)
        {
            if (crop == null || crop.HarvestablePrefab == null)
            {
                Debug.LogError($"[FarmGrowthSystem] Cannot spawn harvestable at ({x},{z}) — crop or prefab is null.");
                return;
            }
            var pos = _grid.GridToWorld(x, z);
            var go = Instantiate(crop.HarvestablePrefab, pos, Quaternion.identity);
            var h = go.GetComponent<CropHarvestable>();
            if (h == null)
            {
                Debug.LogError($"[FarmGrowthSystem] HarvestablePrefab on {crop.name} has no CropHarvestable component.");
                Destroy(go);
                return;
            }
            // Spawn over the network FIRST so OnNetworkSpawn runs and IsDepleted's value-changed callback wires up
            // BEFORE InitializeFromCell sets the value.
            if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
                netObj.Spawn(true);
            h.InitializeFromCell(_grid, _map, x, z, crop, startDepleted);
            RegisterHarvestable(x, z, h);
        }

        private int LinearIndex(int x, int z) => z * _grid.Width + x;
    }
}
