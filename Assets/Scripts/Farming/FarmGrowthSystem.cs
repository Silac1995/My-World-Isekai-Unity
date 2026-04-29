using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Server-only daily tick for farming cells. One instance per active MapController.
    /// See farming spec §4 + §9.2 + the 2026-04-29 single-GameObject-per-crop rework.
    ///
    /// In the post-rework model, the CropHarvestable spawn happens at plant-time
    /// (CharacterAction_PlaceCrop calls <see cref="SpawnCropHarvestableAt"/>), NOT at
    /// maturity. The daily tick advances <see cref="CropHarvestable.AdvanceStage"/> on
    /// existing instances; nothing new is spawned mid-growth.
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
            TrySubscribeToOnNewDay();
        }

        // Subscribe to TimeManager.OnNewDay. Safe to call multiple times — re-attempts each
        // frame from Update if TimeManager wasn't ready at Start time (script-execution-order
        // race between TimeManager.Awake and FarmGrowthSystem.Start).
        private void TrySubscribeToOnNewDay()
        {
            if (_subscribed) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (MWI.Time.TimeManager.Instance == null) return;
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
            _subscribed = true;
            Debug.Log($"[FarmGrowthSystem] Subscribed to TimeManager.OnNewDay (current Day={MWI.Time.TimeManager.Instance.CurrentDay}).");
        }

        private void Update()
        {
            // Idempotent until the first successful subscribe.
            if (!_subscribed) TrySubscribeToOnNewDay();
        }

        private void OnDestroy()
        {
            if (_subscribed && MWI.Time.TimeManager.Instance != null)
                MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
            _subscribed = false;
        }

        // Self-init for scene-authored MapControllers whose WakeUp never fires.
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

        // ────────────────────── Server: spawn at plant-time ──────────────────────

        /// <summary>
        /// Server-only. Spawns the CropHarvestable for a freshly-planted cell. Called by
        /// CharacterAction_PlaceCrop.OnApplyEffect. Idempotent — no-op if a harvestable is
        /// already registered for the cell.
        /// </summary>
        public CropHarvestable SpawnCropHarvestableAt(int x, int z, CropSO crop, int startStage, bool startDepleted)
        {
            if (_grid == null || crop == null || crop.HarvestablePrefab == null) return null;
            int idx = LinearIndex(x, z);
            if (_activeHarvestables.TryGetValue(idx, out var existing) && existing != null) return existing;

            var pos = _grid.GridToWorld(x, z);
            var go = Instantiate(crop.HarvestablePrefab, pos, Quaternion.identity);
            var h = go.GetComponent<CropHarvestable>();
            if (h == null)
            {
                Debug.LogError($"[FarmGrowthSystem] HarvestablePrefab on {crop.name} has no CropHarvestable component.");
                Destroy(go);
                return null;
            }
            // Spawn over the network FIRST so OnNetworkSpawn wires up the NetVar callbacks
            // before InitializeFromCell sets the values.
            if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
            {
                netObj.Spawn(true);
                // Parent under the MapController. NGO requires both parent and child to be
                // NetworkObjects — MapController qualifies (it inherits NetworkBehaviour).
                // TrySetParent runs server-only and replicates the parent-relationship to all
                // peers so the harvestable nests under the correct map in every Hierarchy.
                var mapNetObj = _map.GetComponent<NetworkObject>();
                if (mapNetObj != null && !netObj.TrySetParent(mapNetObj, worldPositionStays: true))
                    Debug.LogWarning($"[FarmGrowthSystem] TrySetParent failed for crop at ({x},{z}) under map '{_map.name}'.");
            }
            h.InitializeFromCell(_grid, _map, x, z, crop, startStage, startDepleted);
            RegisterHarvestable(x, z, h);
            return h;
        }

        // ────────────────────── Server: daily tick ──────────────────────

        private void HandleNewDay()
        {
            if (_grid == null || _map == null) return;
            _dirtyIndices.Clear();

            int grew = 0, stalled = 0, justMatured = 0, refilled = 0, refilling = 0, orphan = 0;

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
                        justMatured++;
                        if (_activeHarvestables.TryGetValue(idx, out var hm) && hm != null)
                            hm.AdvanceStage();
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.Grew:
                        grew++;
                        if (_activeHarvestables.TryGetValue(idx, out var h) && h != null)
                            h.AdvanceStage();
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.JustRefilled:
                        refilled++;
                        if (_activeHarvestables.TryGetValue(idx, out var hr) && hr != null)
                            hr.Refill();
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.Refilling:
                        refilling++;
                        _dirtyIndices.Add(idx);
                        break;
                    case FarmGrowthPipeline.Outcome.Stalled:
                        stalled++;
                        break;
                    case FarmGrowthPipeline.Outcome.OrphanCrop:
                        orphan++;
                        break;
                }
            }

            int total = grew + stalled + justMatured + refilled + refilling + orphan;
            Debug.Log($"[FarmGrowthSystem] HandleNewDay (Day={MWI.Time.TimeManager.Instance?.CurrentDay}): {total} planted cells — Grew={grew}, JustMatured={justMatured}, Stalled={stalled}, JustRefilled={refilled}, Refilling={refilling}, Orphan={orphan}.");

            if (_dirtyIndices.Count > 0)
                _map.NotifyDirtyCells(_dirtyIndices.ToArray());
        }

        // ────────────────────── Server: post-wake reconstruction ──────────────────────

        /// <summary>
        /// Reconstructs CropHarvestables from cell state on map wake / save-load. Spawns one
        /// for every cell with PlantedCropId set, regardless of growth stage.
        /// </summary>
        public void PostWakeSweep()
        {
            if (_grid == null) return;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
            {
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                if (!cell.IsPlowed || string.IsNullOrEmpty(cell.PlantedCropId)) continue;
                int idx = LinearIndex(x, z);
                if (_activeHarvestables.ContainsKey(idx)) continue;

                var crop = CropRegistry.Get(cell.PlantedCropId);
                if (crop == null) continue;

                int startStage = Mathf.Clamp((int)cell.GrowthTimer, 0, crop.DaysToMature);
                bool startDepleted = crop.IsPerennial && cell.TimeSinceLastWatered >= 0f;
                SpawnCropHarvestableAt(x, z, crop, startStage, startDepleted);
            }
        }

        private int LinearIndex(int x, int z) => z * _grid.Width + x;
    }
}
