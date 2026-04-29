using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// One GameObject per crop, from plant-time through harvest/destroy. See farming spec §6
    /// and the 2026-04-29 single-GameObject-per-crop rework.
    ///
    /// CropHarvestable inherits from Harvestable (plain MonoBehaviour) so it can't host
    /// NetworkVariables directly. The synced state lives on the sibling
    /// <see cref="CropHarvestableNetSync"/>; this class reads them and applies the visual.
    /// </summary>
    [RequireComponent(typeof(CropHarvestableNetSync))]
    public class CropHarvestable : Harvestable
    {
        [Header("Crop visuals")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Sprite _readySprite;
        [SerializeField] private Sprite _depletedSprite;

        public int CellX { get; private set; }
        public int CellZ { get; private set; }
        public TerrainCellGrid Grid { get; private set; }

        private CropSO _crop;
        private MapController _map;
        private CropHarvestableNetSync _netSync;
        private FarmGrowthSystem _farmGrowthSystem;
        private Vector3 _baseScale;

        private void Awake()
        {
            _netSync = GetComponent<CropHarvestableNetSync>();
            _baseScale = transform.localScale;
        }

        // ────────────────────── Server-only setup ──────────────────────

        /// <summary>
        /// Server-only. Called once when FarmGrowthSystem spawns this harvestable at plant-time.
        /// `startStage` is clamped 0..DaysToMature. `startDepleted` is true on post-load
        /// reconstruction of a perennial whose cell encodes a refill in progress
        /// (TimeSinceLastWatered &gt;= 0f).
        /// </summary>
        public void InitializeFromCell(TerrainCellGrid grid, MapController map, int x, int z, CropSO crop, int startStage, bool startDepleted)
        {
            Grid = grid;
            _map = map;
            CellX = x;
            CellZ = z;
            _crop = crop;
            _farmGrowthSystem = map != null ? map.GetComponent<FarmGrowthSystem>() : null;
            if (_netSync == null) _netSync = GetComponent<CropHarvestableNetSync>();

            // Configure base Harvestable from CropSO content (server-side).
            SetOutputItemsRuntime(new List<ItemSO> { (ItemSO)crop.ProduceItem });
            SetMaxHarvestCountRuntime(1);
            SetIsDepletableRuntime(true);
            SetRespawnDelayDaysRuntime(0);
            SetDestructionFieldsRuntime(CastItemList(crop.DestructionOutputs), crop.DestructionOutputCount, crop.DestructionDuration);
            SetAllowDestructionForTests(crop.AllowDestruction);
            SetRequiredDestructionToolForTests((ItemSO)crop.RequiredDestructionTool);

            _netSync.CropIdNet.Value = new FixedString64Bytes(crop.Id ?? string.Empty);
            _netSync.CurrentStage.Value = Mathf.Clamp(startStage, 0, crop.DaysToMature);
            if (startDepleted)
            {
                MarkDepletedNoCallback();
                _netSync.IsDepleted.Value = true;
            }
            else
            {
                ResetHarvestState();
                _netSync.IsDepleted.Value = false;
            }
            ApplyVisual();
        }

        /// <summary>Server-only. Called by FarmGrowthSystem on each "Grew" outcome.</summary>
        public void AdvanceStage()
        {
            if (_crop == null || _netSync == null) return;
            if (_netSync.CurrentStage.Value < _crop.DaysToMature)
                _netSync.CurrentStage.Value = _netSync.CurrentStage.Value + 1;
        }

        /// <summary>Server-only. Called by FarmGrowthSystem after RegrowDays for perennials.</summary>
        public void Refill() => SetReady();

        public void SetReady()
        {
            ResetHarvestState();
            if (_netSync != null) _netSync.IsDepleted.Value = false;
        }

        public void SetDepleted()
        {
            MarkDepletedNoCallback();
            if (_netSync != null) _netSync.IsDepleted.Value = true;
        }

        // ────────────────────── Interaction ──────────────────────

        /// <summary>Override base: a still-growing crop is never harvestable.</summary>
        public override bool CanHarvest()
        {
            if (!IsMature()) return false;
            return base.CanHarvest();
        }

        private bool IsMature()
        {
            int stage = _netSync != null ? _netSync.CurrentStage.Value : 0;
            int days = _crop != null ? _crop.DaysToMature : int.MaxValue;
            return stage >= days;
        }

        protected override void OnDepleted()
        {
            if (Grid == null || _crop == null) return;
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);

            if (_crop.IsPerennial)
            {
                cell.TimeSinceLastWatered = 0f;
                if (_netSync != null) _netSync.IsDepleted.Value = true;
            }
            else
            {
                ClearCellAndUnregister(ref cell);
                var netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned) netObj.Despawn();
            }
        }

        protected override void OnDestroyed()
        {
            if (Grid == null) return;
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);
            ClearCellAndUnregister(ref cell);
        }

        private void ClearCellAndUnregister(ref TerrainCell cell)
        {
            cell.PlantedCropId = null;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;
            if (_farmGrowthSystem != null) _farmGrowthSystem.UnregisterHarvestable(CellX, CellZ);
        }

        // ────────────────────── Visual sync (called by NetSync) ──────────────────────

        /// <summary>Invoked by CropHarvestableNetSync on every NetworkVariable change.</summary>
        public void OnNetSyncChanged() => ApplyVisual();

        /// <summary>Invoked by CropHarvestableNetSync when CropIdNet first arrives on a client.</summary>
        public void OnCropIdResolved()
        {
            if (_crop != null) return;
            if (_netSync == null) return;
            string id = _netSync.CropIdNet.Value.ToString();
            if (!string.IsNullOrEmpty(id)) _crop = CropRegistry.Get(id);
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (_netSync == null) return;
            if (_crop == null)
            {
                // Try one more resolution attempt — covers the case where Awake ran before
                // OnNetworkSpawn pushed the initial CropIdNet value.
                string id = _netSync.CropIdNet.Value.ToString();
                if (!string.IsNullOrEmpty(id)) _crop = CropRegistry.Get(id);
                if (_crop == null) return;
            }

            int stage = _netSync.CurrentStage.Value;
            bool mature = stage >= _crop.DaysToMature;

            // Scale: tiny when fresh-planted, full size at maturity. Cached _baseScale survives
            // prefab variant scale overrides.
            float t = _crop.DaysToMature > 0
                ? Mathf.Clamp01((float)stage / _crop.DaysToMature)
                : 1f;
            float scaleFactor = Mathf.Lerp(0.25f, 1f, t);
            transform.localScale = _baseScale * scaleFactor;

            if (_spriteRenderer != null)
            {
                Sprite picked;
                if (mature)
                    picked = (_netSync.IsDepleted.Value && _depletedSprite != null) ? _depletedSprite : _readySprite;
                else
                    picked = _crop.GetStageSprite(stage) ?? _readySprite;
                if (picked != null) _spriteRenderer.sprite = picked;
            }
        }

        // ────────────────────── Helpers ──────────────────────

        private static List<ItemSO> CastItemList(IReadOnlyList<ScriptableObject> sos)
        {
            var list = new List<ItemSO>(sos != null ? sos.Count : 0);
            if (sos == null) return list;
            for (int i = 0; i < sos.Count; i++)
                if (sos[i] is ItemSO item) list.Add(item);
            return list;
        }
    }
}
