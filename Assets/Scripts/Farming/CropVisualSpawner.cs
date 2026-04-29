using System.Collections.Generic;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Client-side cell-stage sprite renderer for growing crops. See farming spec §8.
    /// One instance per MapController. Lives next to TerrainCellGrid + FarmGrowthSystem.
    ///
    /// CropHarvestable owns the visual past maturity; this spawner removes its sprite
    /// the moment a cell crosses DaysToMature (early-exit in <see cref="Refresh"/>).
    /// </summary>
    public class CropVisualSpawner : MonoBehaviour
    {
        [Header("Stage sprite host")]
        [Tooltip("Optional: a prefab with a SpriteRenderer at root. If null, a procedural quad is created at runtime.")]
        [SerializeField] private GameObject _stageSpritePrefab;
        [SerializeField] private Vector3 _spriteRotationEuler = new Vector3(60f, 0f, 0f); // tilt for top-down view

        private readonly Dictionary<int, GameObject> _activeVisuals = new Dictionary<int, GameObject>(64);
        private TerrainCellGrid _grid;
        private MapController _map;
        private Transform _root;

        public void Initialize(TerrainCellGrid grid, MapController map)
        {
            _grid = grid;
            _map = map;
            if (_root == null)
            {
                _root = new GameObject("CropVisualRoot").transform;
                _root.SetParent(transform);
                _root.localPosition = Vector3.zero;
            }
            Debug.Log($"[CropVisualSpawner] Initialize on {map.name} (grid={grid.Width}x{grid.Depth}). Visual root: {_root.name}.");
        }

        /// <summary>
        /// Called from MapController.SendDirtyCellsClientRpc with the list of mutated cell indices.
        /// Iterates the indices and updates each visual based on the current grid state.
        /// </summary>
        public void OnDirtyCells(int[] indices)
        {
            if (_grid == null) { Debug.LogWarning("[CropVisualSpawner] OnDirtyCells called but _grid is null. Was Initialize() invoked?"); return; }
            if (indices == null) return;
            Debug.Log($"[CropVisualSpawner] OnDirtyCells — {indices.Length} cell(s) updated.");
            for (int i = 0; i < indices.Length; i++)
                Refresh(indices[i]);
        }

        /// <summary>Re-evaluate one cell from current grid state.</summary>
        public void Refresh(int idx)
        {
            int x = idx % _grid.Width;
            int z = idx / _grid.Width;
            ref TerrainCell cell = ref _grid.GetCellRef(x, z);

            if (string.IsNullOrEmpty(cell.PlantedCropId)) { Remove(idx); return; }

            var crop = CropRegistry.Get(cell.PlantedCropId);
            if (crop == null) { Debug.LogWarning($"[CropVisualSpawner] Cell ({x},{z}) PlantedCropId='{cell.PlantedCropId}' not in CropRegistry."); Remove(idx); return; }

            // Mature → CropHarvestable owns the visual. Always remove our own sprite.
            if (cell.GrowthTimer >= crop.DaysToMature) { Remove(idx); return; }

            int stage = (int)cell.GrowthTimer;
            var sprite = crop.GetStageSprite(stage);

            Vector3 pos = _grid.GridToWorld(x, z);
            if (!_activeVisuals.TryGetValue(idx, out var go) || go == null)
            {
                go = SpawnVisualAt(pos);
                _activeVisuals[idx] = go;
                Debug.Log($"[CropVisualSpawner] Spawned stage-sprite GameObject for cell ({x},{z}) at {pos}, sprite={(sprite != null ? sprite.name : "<null — _stageSprites not assigned>")}.");
            }
            else
            {
                go.transform.position = pos;
            }
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sprite = sprite;
        }

        private GameObject SpawnVisualAt(Vector3 pos)
        {
            GameObject go;
            if (_stageSpritePrefab != null)
            {
                go = Instantiate(_stageSpritePrefab, pos, Quaternion.Euler(_spriteRotationEuler), _root);
            }
            else
            {
                // Procedural fallback so the system shows SOMETHING without a designer-set
                // prefab. A primitive cube is visible in 2D AND 3D scenes; designers replace
                // _stageSpritePrefab with their authored visual when art is ready.
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "CropStageVisual";
                go.transform.SetParent(_root, false);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(1f, 0.5f, 1f);
                // Strip the auto-added collider — visuals must not block raycasts or physics.
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            return go;
        }

        private void Remove(int idx)
        {
            if (_activeVisuals.TryGetValue(idx, out var go))
            {
                if (go != null) Destroy(go);
                _activeVisuals.Remove(idx);
            }
        }

        /// <summary>
        /// Called on map ready / late-join: rebuild all visuals from current grid state.
        /// Iterates the whole grid once — bounded by the grid size, so cheap.
        /// </summary>
        public void RebuildAll()
        {
            foreach (var kv in _activeVisuals)
                if (kv.Value != null) Destroy(kv.Value);
            _activeVisuals.Clear();

            if (_grid == null) return;
            for (int z = 0; z < _grid.Depth; z++)
            for (int x = 0; x < _grid.Width; x++)
                Refresh(z * _grid.Width + x);
        }
    }
}
