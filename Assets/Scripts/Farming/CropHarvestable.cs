using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;
using MWI.WorldSystem;

namespace MWI.Farming
{
    /// <summary>
    /// Spawned by FarmGrowthSystem when a crop matures. See farming spec §6.
    /// One-shot crops despawn on harvest; perennials stay standing and refill via the cell.
    ///
    /// The "ready vs depleted" sprite swap is driven by the sibling
    /// <see cref="CropHarvestableNetSync"/>'s NetworkVariable — Harvestable itself is plain
    /// MonoBehaviour so the network sync has to live on a sibling component.
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

        private void Awake()
        {
            _netSync = GetComponent<CropHarvestableNetSync>();
        }

        /// <summary>
        /// Server-only. Called once when FarmGrowthSystem spawns this harvestable.
        /// <paramref name="startDepleted"/> reflects the cell's encoded refill state
        /// (TimeSinceLastWatered &gt;= 0f). On a fresh maturity (TimeSinceLastWatered == -1f) →
        /// false. On post-wake of a depleted perennial (TimeSinceLastWatered in [0, RegrowDays))
        /// → true. This is the load-bearing line for save/load and hibernation correctness —
        /// see spec §9.
        /// </summary>
        public void InitializeFromCell(TerrainCellGrid grid, MapController map, int x, int z, CropSO crop, bool startDepleted)
        {
            Grid = grid;
            _map = map;
            CellX = x;
            CellZ = z;
            _crop = crop;
            _farmGrowthSystem = map != null ? map.GetComponent<FarmGrowthSystem>() : null;

            // Configure base Harvestable from CropSO content. CropSO lives in MWI.Farming.Pure
            // and types its item fields as ScriptableObject (the Pure asmdef can't see ItemSO),
            // so casts are necessary at the use sites.
            SetOutputItemsRuntime(new List<ItemSO> { (ItemSO)crop.ProduceItem });
            SetMaxHarvestCountRuntime(1);   // one Interact() drops the whole yield in a burst
            SetIsDepletableRuntime(true);
            SetRespawnDelayDaysRuntime(0);  // we own post-deplete state, not the base timer

            SetDestructionFieldsRuntime(CastItemList(crop.DestructionOutputs), crop.DestructionOutputCount, crop.DestructionDuration);
            SetAllowDestructionForTests(crop.AllowDestruction);
            SetRequiredDestructionToolForTests((ItemSO)crop.RequiredDestructionTool);

            if (startDepleted) SetDepleted();
            else                SetReady();
        }

        /// <summary>
        /// Server-only. Restores full yield + ready visual. Called on fresh spawn (non-depleted
        /// cell) AND on each perennial Refill() after RegrowDays.
        /// </summary>
        public void SetReady()
        {
            ResetHarvestState();
            if (_netSync != null) _netSync.IsDepleted.Value = false;
        }

        /// <summary>
        /// Server-only. Puts the harvestable in "no fruit, regrowing" state without running
        /// the deplete pipeline. Called only from InitializeFromCell on post-load / post-wake
        /// of a depleted perennial.
        /// </summary>
        public void SetDepleted()
        {
            MarkDepletedNoCallback();
            if (_netSync != null) _netSync.IsDepleted.Value = true;
        }

        /// <summary>Server-only. Called by FarmGrowthSystem after RegrowDays of conditions met. Perennial only.</summary>
        public void Refill() => SetReady();

        /// <summary>Local sprite swap. Invoked from CropHarvestableNetSync on every peer.</summary>
        public void ApplyDepletedVisual(bool depleted)
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.sprite = depleted ? _depletedSprite : _readySprite;
        }

        protected override void OnDepleted()
        {
            if (Grid == null || _crop == null) return;
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);

            if (_crop.IsPerennial)
            {
                // Stay standing. Mark cell "depleted, refilling".
                cell.TimeSinceLastWatered = 0f;
                if (_netSync != null) _netSync.IsDepleted.Value = true;
                // Do NOT despawn. FarmGrowthSystem will call Refill() after RegrowDays.
            }
            else
            {
                ClearCellAndUnregister(ref cell);
                var netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                    netObj.Despawn();
            }
        }

        protected override void OnDestroyed()
        {
            if (Grid == null) return;
            ref var cell = ref Grid.GetCellRef(CellX, CellZ);
            ClearCellAndUnregister(ref cell);
            // Base Harvestable.DestroyForOutputs despawns the NetworkObject after this returns.
        }

        private void ClearCellAndUnregister(ref TerrainCell cell)
        {
            cell.PlantedCropId = null;
            cell.GrowthTimer = 0f;
            cell.TimeSinceLastWatered = -1f;
            // IsPlowed stays true so re-planting is one step.
            if (_farmGrowthSystem != null) _farmGrowthSystem.UnregisterHarvestable(CellX, CellZ);
        }

        // Casts the Pure-asmdef ScriptableObject list to ItemSO. Skips entries that aren't
        // actually ItemSO at runtime (designer error — surfaces in CropSO.OnValidate).
        private static List<ItemSO> CastItemList(IReadOnlyList<ScriptableObject> sos)
        {
            var list = new List<ItemSO>(sos != null ? sos.Count : 0);
            if (sos == null) return list;
            for (int i = 0; i < sos.Count; i++)
            {
                if (sos[i] is ItemSO item) list.Add(item);
            }
            return list;
        }
    }
}
