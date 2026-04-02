using UnityEngine;
using Unity.Netcode;

namespace MWI.Terrain
{
    /// <summary>
    /// Manages wild vegetation growth on fertile cells that are NOT plowed.
    /// Server-only. Uses catch-up loops for Giga Speed compliance.
    /// </summary>
    public class VegetationGrowthSystem : MonoBehaviour
    {
        [Header("Tick Settings")]
        [SerializeField] private float _tickIntervalGameHours = 1f;
        [SerializeField] private float _minimumMoistureForGrowth = 0.2f;
        [SerializeField] private float _droughtDeathHours = 48f;

        [Header("Growth Stage Thresholds (game-hours)")]
        [SerializeField] private float _sproutTime = 6f;
        [SerializeField] private float _bushTime = 24f;
        [SerializeField] private float _saplingTime = 72f;
        [SerializeField] private float _treeTime = 168f;

        [Header("Growth Stage Prefabs")]
        [SerializeField] private GameObject _sproutPrefab;
        [SerializeField] private GameObject _bushPrefab;
        [SerializeField] private GameObject _saplingPrefab;
        [SerializeField] private GameObject _treePrefab;

        private TerrainCellGrid _grid;
        private float _timeSinceLastTick;

        public void Initialize(TerrainCellGrid grid)
        {
            _grid = grid;
        }

        private void Update()
        {
            if (_grid == null) return;
            if (!NetworkManager.Singleton.IsServer) return;

            _timeSinceLastTick += Time.deltaTime;

            float tickInterval = _tickIntervalGameHours * 3600f;
            while (_timeSinceLastTick >= tickInterval)
            {
                _timeSinceLastTick -= tickInterval;
                ProcessGrowthTick(_tickIntervalGameHours);
            }
        }

        private void ProcessGrowthTick(float hoursElapsed)
        {
            for (int z = 0; z < _grid.Depth; z++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                    var terrainType = cell.GetCurrentType();
                    if (terrainType == null || !terrainType.CanGrowVegetation) continue;
                    if (cell.IsPlowed) continue;

                    if (cell.Moisture >= _minimumMoistureForGrowth)
                    {
                        cell.GrowthTimer += hoursElapsed;
                        cell.TimeSinceLastWatered = 0f;
                    }
                    else
                    {
                        cell.TimeSinceLastWatered += hoursElapsed;
                    }

                    // Drought death
                    if (cell.TimeSinceLastWatered > _droughtDeathHours && cell.GrowthTimer > 0f)
                    {
                        cell.GrowthTimer = 0f;
                        // TODO: Despawn visual prefab at this cell when prefab management is implemented
                    }

                    // TODO: Spawn/update visual prefabs based on GetGrowthStage(cell.GrowthTimer)
                    // and spawn Harvestable objects at Bush/Tree stages
                }
            }
        }

        /// <summary>
        /// Returns the growth stage index based on accumulated growth time.
        /// 0=Empty, 1=Sprout, 2=Bush, 3=Sapling, 4=Tree
        /// </summary>
        public int GetGrowthStage(float growthTimer)
        {
            if (growthTimer >= _treeTime) return 4;
            if (growthTimer >= _saplingTime) return 3;
            if (growthTimer >= _bushTime) return 2;
            if (growthTimer >= _sproutTime) return 1;
            return 0;
        }
    }
}
