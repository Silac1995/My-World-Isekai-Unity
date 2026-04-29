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

        // Last NetVar values seen by the polling fallback in Update. Tracks change so we
        // only re-run ApplyVisual when something actually flipped, not every frame.
        private int _lastSeenStage = -1;
        private bool _lastSeenDepleted;
        private string _lastSeenCropId = "";

        private void Awake()
        {
            _netSync = GetComponent<CropHarvestableNetSync>();
            _baseScale = transform.localScale;
        }

        /// <summary>
        /// Polls the three replicated NetVars every frame and triggers ApplyVisual on any
        /// change. Defensive fallback to the OnValueChanged subscriptions in
        /// CropHarvestableNetSync — those callbacks reliably fire on the host (server-side)
        /// but have shown intermittent behaviour on remote clients (initial-sync replicates
        /// correctly, post-spawn CurrentStage / IsDepleted updates sometimes don't trigger
        /// the registered callback). This poll is cheap (three NetVar reads + three compares
        /// + a string compare per frame per active crop) and idempotent — host pays it too,
        /// but ApplyVisual is a no-op when the visual is already correct because the
        /// detection short-circuits via _lastSeen* equality.
        /// </summary>
        private void Update()
        {
            if (_netSync == null) return;

            int currStage = _netSync.CurrentStage.Value;
            bool currDepleted = _netSync.IsDepleted.Value;
            string currCropId = _netSync.CropIdNet.Value.ToString();

            if (currStage == _lastSeenStage && currDepleted == _lastSeenDepleted && currCropId == _lastSeenCropId)
                return;

            _lastSeenStage = currStage;
            _lastSeenDepleted = currDepleted;
            _lastSeenCropId = currCropId;
            ApplyVisual();
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
            SetHarvestOutputsRuntime(CastEntryList(crop.HarvestOutputs));
            SetMaxHarvestCountRuntime(1);
            SetIsDepletableRuntime(true);
            SetRespawnDelayDaysRuntime(0);
            SetDestructionFieldsRuntime(CastEntryList(crop.DestructionOutputs), crop.DestructionDuration);
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

        /// <summary>
        /// Replaces the base `_harvestOutputs`-based check with one that reads only
        /// network-replicated state (`_netSync` NetVars + `CropRegistry` lookup), so the
        /// owning client of a player character can decide whether to fire a harvest action.
        /// `_harvestOutputs` and `_crop` are populated by `InitializeFromCell` on the server
        /// only — non-host clients see them empty/null and must not be consulted here.
        /// </summary>
        public override bool CanHarvest()
        {
            if (!IsMature()) return false;
            if (_netSync != null && _netSync.IsDepleted.Value) return false;
            var crop = ResolveCropFromNet();
            if (crop == null) return false;
            return crop.HarvestOutputs != null && crop.HarvestOutputs.Count > 0;
        }

        private bool IsMature()
        {
            if (_netSync == null) return false;
            var crop = ResolveCropFromNet();
            if (crop == null) return false;
            return _netSync.CurrentStage.Value >= crop.DaysToMature;
        }

        /// <summary>
        /// Override so non-host clients can see the destruction setting — base reads
        /// `_allowDestruction` which is a server-only runtime mutation. Resolved from the
        /// replicated CropSO instead.
        /// </summary>
        public override bool AllowDestruction
        {
            get
            {
                var crop = ResolveCropFromNet();
                return crop != null && crop.AllowDestruction;
            }
        }

        /// <summary>
        /// Override so non-host clients see the destruction-tool requirement — base reads
        /// `_requiredDestructionTool` which is a server-only runtime mutation.
        /// </summary>
        public override ItemSO RequiredDestructionTool
        {
            get
            {
                var crop = ResolveCropFromNet();
                return crop != null ? crop.RequiredDestructionTool as ItemSO : null;
            }
        }

        /// <summary>
        /// Resolves the CropSO from the replicated NetVar so client-side code paths work
        /// even before/without `InitializeFromCell` running locally. Caches into _crop on
        /// success so subsequent calls are O(1).
        /// </summary>
        private CropSO ResolveCropFromNet()
        {
            if (_crop != null) return _crop;
            if (_netSync == null) return null;
            string id = _netSync.CropIdNet.Value.ToString();
            if (string.IsNullOrEmpty(id)) return null;
            _crop = CropRegistry.Get(id);
            return _crop;
        }

        /// <summary>
        /// Client-readable replacement for the base method. Reads outputs/destruction lists
        /// from the registry-resolved CropSO instead of the server-only `_harvestOutputs` /
        /// `_destructionOutputs` runtime fields. Allows the Hold-E menu to render correct
        /// rows on every peer, not just the server/host.
        /// </summary>
        public override System.Collections.Generic.IList<HarvestInteractionOption> GetInteractionOptions(Character actor)
        {
            var list = new System.Collections.Generic.List<HarvestInteractionOption>(2);
            var crop = ResolveCropFromNet();
            if (crop == null) return list;

            var held = Harvestable.GetHeldItemSO(actor);

            // ── Yield row ──
            ItemSO firstYield = null;
            if (crop.HarvestOutputs != null)
            {
                for (int i = 0; i < crop.HarvestOutputs.Count; i++)
                {
                    var e = crop.HarvestOutputs[i];
                    if (e.Item is ItemSO it && it != null && e.Count > 0)
                    {
                        firstYield = it;
                        break;
                    }
                }
            }
            if (firstYield != null)
            {
                bool yieldOk = CanHarvest()
                    && (RequiredHarvestTool == null || held == RequiredHarvestTool);
                string yieldReason = null;
                if (!yieldOk)
                {
                    if (!IsMature()) yieldReason = "Not yet ripe";
                    else if (_netSync != null && _netSync.IsDepleted.Value) yieldReason = "Already harvested";
                    else if (RequiredHarvestTool != null) yieldReason = $"Requires {RequiredHarvestTool.ItemName}";
                    else yieldReason = "Cannot harvest";
                }
                list.Add(new HarvestInteractionOption(
                    label: $"Pick {firstYield.ItemName}",
                    icon: firstYield.Icon,
                    outputPreview: BuildOutputPreviewFromCropSO(crop.HarvestOutputs),
                    isAvailable: yieldOk,
                    unavailableReason: yieldReason,
                    actionFactory: ch => new CharacterHarvestAction(ch, this)));
            }

            // ── Destruction row ──
            if (crop.AllowDestruction)
            {
                ItemSO firstDestroy = null;
                if (crop.DestructionOutputs != null)
                {
                    for (int i = 0; i < crop.DestructionOutputs.Count; i++)
                    {
                        var e = crop.DestructionOutputs[i];
                        if (e.Item is ItemSO it && it != null && e.Count > 0) { firstDestroy = it; break; }
                    }
                }
                if (firstDestroy != null)
                {
                    var requiredTool = crop.RequiredDestructionTool as ItemSO;
                    bool destroyOk = requiredTool == null || held == requiredTool;
                    string destroyReason = destroyOk ? null
                        : (requiredTool != null ? $"Requires {requiredTool.ItemName}" : "Cannot destroy");
                    list.Add(new HarvestInteractionOption(
                        label: "Destroy",
                        icon: firstDestroy.Icon,
                        outputPreview: BuildOutputPreviewFromCropSO(crop.DestructionOutputs),
                        isAvailable: destroyOk,
                        unavailableReason: destroyReason,
                        actionFactory: ch => new CharacterAction_DestroyHarvestable(ch, this)));
                }
            }

            return list;
        }

        private static string BuildOutputPreviewFromCropSO(System.Collections.Generic.IReadOnlyList<CropHarvestOutput> entries)
        {
            if (entries == null || entries.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            bool first = true;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!(e.Item is ItemSO it) || it == null || e.Count <= 0) continue;
                if (!first) sb.Append(", ");
                sb.Append(e.Count).Append("× ").Append(it.ItemName);
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Crops own their own refill cycle: perennials regrow via FarmGrowthSystem +
        /// FarmGrowthPipeline (PHASE C, gated on RegrowDays AND moisture); one-shots get
        /// despawned in OnDepleted. The base Harvestable's auto-respawn-after-N-days path
        /// must NOT fire — otherwise it would un-deplete the crop on the next OnNewDay
        /// independent of the FarmGrowthPipeline state, letting the player harvest a
        /// perennial every single day regardless of regrow timing.
        /// </summary>
        protected override void ScheduleRespawnAfterDeplete() { }

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

        private static List<HarvestOutputEntry> CastEntryList(IReadOnlyList<CropHarvestOutput> entries)
        {
            var list = new List<HarvestOutputEntry>(entries != null ? entries.Count : 0);
            if (entries == null) return list;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Item is ItemSO item && item != null && entries[i].Count > 0)
                    list.Add(new HarvestOutputEntry(item, entries[i].Count));
            }
            return list;
        }
    }
}
