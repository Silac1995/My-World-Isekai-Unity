using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;
using MWI.Interactables;
using MWI.Terrain;
using MWI.Time;
using MWI.WorldSystem;

/// <summary>
/// Commercial building specialisation for crop farms. Extends <see cref="HarvestingBuilding"/> with:
/// <list type="bullet">
/// <item>Farmer-specific job type (<see cref="JobFarmer"/>) + farmer-specific tasks
///   (<c>PlantCropTask</c> + <c>WaterCropTask</c>) registered via daily scans (Task 6+).</item>
/// <item>Multi-zone farm field (<c>List&lt;Zone&gt; _farmingAreaZones</c>) — designer can author
///   multiple non-contiguous fields per building (e.g. north field for wheat, south for flowers).
///   Differs from the inherited single <c>_harvestingAreaZone</c> which is left null on
///   FarmingBuilding prefabs.</item>
/// <item>Auto-derived <see cref="IStockProvider"/> stock targets (Task 5): seed inputs +
///   watering can input. Output produce is throttled by the inherited <c>_wantedResources</c>
///   path, NOT by a stock target (StockTargets only model "refill UP TO MinStock", which is
///   an input-restock semantic — outputs would loop the building into ordering its own produce).</item>
/// </list>
///
/// Consumes Plan 1 (Tool Storage primitive — watering can fetch/return), Plan 2 (Help Wanted
/// + IsHiring gates), and Plan 2.5 (ManagementFurniture + NeedJob OnNewDay throttle).
/// </summary>
public class FarmingBuilding : HarvestingBuilding, IStockProvider
{
    public override BuildingType BuildingType => BuildingType.Farm;

    [Header("Farming Config")]
    [Tooltip("Crops this farm grows. Each crop's _harvestOutputs are auto-derived as " +
             "the building's seed input stock targets (Task 5 wires the override). Produce " +
             "outputs are throttled via the inherited HarvestingBuilding._wantedResources path.")]
    [SerializeField] private List<CropSO> _cropsToGrow = new List<CropSO>();

    [Tooltip("Number of Farmer positions this building creates on InitializeJobs.")]
    [SerializeField] private int _farmerCount = 2;

    [SerializeField] private string _farmerJobTitle = "Farmer";

    [Header("Farming Areas (multi-zone)")]
    [Tooltip("Designer-authored zones defining the farm's plant/water area. PlantScan + " +
             "WaterScan (Task 6) iterate the union of all cells inside these zones. Empty " +
             "list = no farming activity (the building still works as a regular " +
             "HarvestingBuilding via the inherited _harvestingAreaZone path, which is " +
             "typically left null on FarmingBuilding prefabs).")]
    [SerializeField] private List<Zone> _farmingAreaZones = new List<Zone>();

    [Header("Tool / Seed Stock Targets")]
    [Tooltip("Authoring-only floor for seed stock — surfaced for designer documentation. " +
             "The actual StockTarget passed to LogisticsStockEvaluator uses _seedMaxStock " +
             "as MinStock (StockTarget has a single-field 'refill UP TO this' semantic, not " +
             "a separate min/max). Reorder fires when virtual stock < _seedMaxStock.")]
    [SerializeField] private int _seedMinStock = 5;

    [Tooltip("Refill target for seed stock — the StockTarget.MinStock value passed to the " +
             "logistics evaluator. When virtual seed stock dips below this, a BuyOrder fires " +
             "via the existing logistics chain.")]
    [SerializeField] private int _seedMaxStock = 20;

    [Tooltip("Reference to the WateringCan ItemSO (typically a MiscSO). Drives the input " +
             "stock target for the building's tool storage. Null = no watering loop " +
             "(WaterScan still runs; the cans-needed slot just won't auto-restock).")]
    [SerializeField] private ItemSO _wateringCanItem;

    [Tooltip("Refill target for watering cans — used as StockTarget.MinStock. Same single-" +
             "field semantic as _seedMaxStock.")]
    [SerializeField] private int _wateringCanMaxStock = 2;

    // ── Public accessors ────────────────────────────────────────────

    public IReadOnlyList<CropSO> CropsToGrow => _cropsToGrow;
    public int FarmerCount => _farmerCount;
    public IReadOnlyList<Zone> FarmingAreaZones => _farmingAreaZones;
    public ItemSO WateringCanItem => _wateringCanItem;
    public int SeedMinStock => _seedMinStock;
    public int SeedMaxStock => _seedMaxStock;
    public int WateringCanMaxStock => _wateringCanMaxStock;

    // ── Initialisation ──────────────────────────────────────────────

    protected override void InitializeJobs()
    {
        for (int i = 0; i < _farmerCount; i++)
        {
            _jobs.Add(new JobFarmer(_farmerJobTitle, MWI.WorldSystem.JobType.Farmer));
        }

        _jobs.Add(new JobLogisticsManager("Logistics Manager"));

        Debug.Log($"<color=green>[FarmingBuilding]</color> {buildingName} initialised with {_farmerCount} farmer(s) + 1 Logistics Manager.");
    }

    // ── IStockProvider ──────────────────────────────────────────────

    /// <summary>
    /// Auto-derived from <see cref="_cropsToGrow"/>:
    /// <list type="bullet">
    /// <item>For each crop, every Seed <see cref="MWI.Interactables.HarvestableOutputEntry"/>
    ///   whose <c>CropToPlant</c> matches the crop becomes an INPUT target with
    ///   <c>MinStock = _seedMaxStock</c> (StockTarget's single-field 'refill UP TO this'
    ///   semantic — see <see cref="MinStockPolicy"/>).</item>
    /// <item>If <see cref="_wateringCanItem"/> is non-null: an INPUT target for the can
    ///   with <c>MinStock = _wateringCanMaxStock</c>.</item>
    /// </list>
    /// <para>
    /// <b>Produce outputs are NOT emitted as stock targets.</b> The current
    /// <see cref="StockTarget"/> contract models a single 'refill up to MinStock'
    /// floor — appropriate for inputs that flow IN via BuyOrders, but semantically
    /// wrong for outputs the building generates itself. Output throttling for produce
    /// is already handled by the inherited <see cref="HarvestingBuilding._wantedResources"/>
    /// / <c>IsResourceAtLimit</c> path which gates <c>GetWantedItems</c> and stops
    /// harvesters when full. Emitting produce here would (a) loop the building into
    /// placing BuyOrders for items it produces itself and (b) duplicate the output-cap
    /// logic that already lives one level up.
    /// </para>
    /// <para>
    /// Existing <see cref="LogisticsStockEvaluator"/> picks up these targets and fires
    /// BuyOrders for any deficit. Seeds + watering can flow IN via the existing
    /// logistics chain (TransporterJob delivers from supplier buildings); produce
    /// flows OUT via the existing harvest-deposit path (no change).
    /// </para>
    /// </summary>
    public IEnumerable<StockTarget> GetStockTargets()
    {
        for (int i = 0; i < _cropsToGrow.Count; i++)
        {
            var crop = _cropsToGrow[i];
            if (crop == null) continue;

            var outputs = crop.HarvestOutputs;
            if (outputs == null) continue;

            for (int j = 0; j < outputs.Count; j++)
            {
                var entry = outputs[j];
                if (entry.Item == null) continue;

                // Item is typed as ScriptableObject on HarvestableOutputEntry (Pure-asmdef
                // constraint). SeedSO inherits ScriptableObject, so the type-test is direct.
                // Non-seed entries are skipped — see the method-level note about why produce
                // outputs are not emitted as stock targets.
                if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == crop)
                {
                    if (_seedMaxStock > 0)
                    {
                        yield return new StockTarget(seedSO, _seedMaxStock);
                    }
                }
            }
        }

        if (_wateringCanItem != null && _wateringCanMaxStock > 0)
        {
            yield return new StockTarget(_wateringCanItem, _wateringCanMaxStock);
        }
    }

    // ── Daily scans (Task 6) ────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();   // subscribes inherited ScanHarvestingArea
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += PlantScan;
            TimeManager.Instance.OnNewDay += WaterScan;
        }
    }

    protected override void OnDisable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= PlantScan;
            TimeManager.Instance.OnNewDay -= WaterScan;
        }
        base.OnDisable();
    }

    /// <summary>
    /// Server-only. For each cell in the union of <see cref="_farmingAreaZones"/> that is empty
    /// (no PlantedCropId): register a PlantCropTask on the building's BuildingTaskManager,
    /// for the crop most under its produce quota. Skips if the crop's seed isn't in storage.
    /// Idempotent: re-running the same day doesn't duplicate tasks for the same cell.
    /// </summary>
    public void PlantScan()
    {
        if (Unity.Netcode.NetworkManager.Singleton == null) return;
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;
        if (TaskManager == null) return;
        if (_farmingAreaZones == null || _farmingAreaZones.Count == 0) return;
        if (_cropsToGrow == null || _cropsToGrow.Count == 0) return;

        for (int zi = 0; zi < _farmingAreaZones.Count; zi++)
        {
            var zone = _farmingAreaZones[zi];
            if (zone == null) continue;

            EnumerateCellsInZone(zone, (map, grid, cellX, cellZ) =>
            {
                if (grid == null) return;
                ref var cell = ref grid.GetCellRef(cellX, cellZ);
                if (!string.IsNullOrEmpty(cell.PlantedCropId)) return;   // already planted

                var crop = SelectCropForCell();
                if (crop == null) return;
                if (!HasSeedInInventory(crop)) return;

                if (HasExistingPlantTaskForCell(cellX, cellZ)) return;

                TaskManager.RegisterTask(new PlantCropTask(cellX, cellZ, crop, this));
            });
        }
    }

    /// <summary>
    /// Server-only. For each planted cell in the farm zones meeting the dry-cell predicate:
    /// - cell.GrowthTimer &lt; crop.DaysToMature (mature/perennial cells skip).
    /// - cell.Moisture &lt; crop.MinMoistureForGrowth (already wet enough → skip).
    /// - cell.TimeSinceLastWatered ≥ 1f (rain today resets to 0; wait at least one day).
    /// Register a WaterCropTask. JobFarmer's plan picks it up + chains
    /// FetchToolFromStorage(WateringCan) → WaterCrop → ReturnToolToStorage.
    /// </summary>
    public void WaterScan()
    {
        if (Unity.Netcode.NetworkManager.Singleton == null) return;
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer) return;
        if (TaskManager == null) return;
        if (_farmingAreaZones == null || _farmingAreaZones.Count == 0) return;

        for (int zi = 0; zi < _farmingAreaZones.Count; zi++)
        {
            var zone = _farmingAreaZones[zi];
            if (zone == null) continue;

            EnumerateCellsInZone(zone, (map, grid, cellX, cellZ) =>
            {
                if (grid == null) return;
                ref var cell = ref grid.GetCellRef(cellX, cellZ);
                if (string.IsNullOrEmpty(cell.PlantedCropId)) return;   // not planted

                var crop = MWI.Farming.CropRegistry.Get(cell.PlantedCropId);
                if (crop == null) return;
                if (cell.GrowthTimer >= crop.DaysToMature) return;       // mature
                if (cell.TimeSinceLastWatered < 1f) return;              // freshly watered / rain
                if (cell.Moisture >= crop.MinMoistureForGrowth) return;  // wet enough

                if (HasExistingWaterTaskForCell(cellX, cellZ)) return;

                TaskManager.RegisterTask(new WaterCropTask(cellX, cellZ, this));
            });
        }
    }

    /// <summary>
    /// Quota-driven: returns the CropSO whose primary (non-Seed) produce stock is most under
    /// its target. Tie-broken by ascending CropSO.Id.
    /// </summary>
    private CropSO SelectCropForCell()
    {
        CropSO best = null;
        int bestDeficit = -1;
        for (int i = 0; i < _cropsToGrow.Count; i++)
        {
            var crop = _cropsToGrow[i];
            if (crop == null) continue;

            ItemSO primaryProduce = null;
            for (int j = 0; j < crop.HarvestOutputs.Count; j++)
            {
                var entry = crop.HarvestOutputs[j];
                // HarvestableOutputEntry.Item is typed as ScriptableObject (Pure-asmdef
                // constraint — see HarvestableOutputEntry.cs). Cast via `as ItemSO` and
                // skip seed entries (we want the primary produce, not the seed yield).
                if (entry.Item != null && !(entry.Item is SeedSO))
                {
                    primaryProduce = entry.Item as ItemSO;
                    if (primaryProduce != null) break;
                }
            }
            if (primaryProduce == null) continue;

            int max = LookupProduceMax(primaryProduce) ?? 50;
            int current = GetItemCount(primaryProduce);
            int deficit = max - current;
            if (deficit <= 0) continue;

            if (deficit > bestDeficit
                || (deficit == bestDeficit && best != null && string.Compare(crop.Id, best.Id) < 0))
            {
                best = crop;
                bestDeficit = deficit;
            }
        }
        return best;
    }

    private int? LookupProduceMax(ItemSO item)
    {
        var wanted = WantedResources;
        if (wanted == null) return null;
        for (int i = 0; i < wanted.Count; i++)
        {
            if (wanted[i].targetItem == item)
                return wanted[i].maxQuantity;
        }
        return null;
    }

    private bool HasSeedInInventory(CropSO crop)
    {
        for (int j = 0; j < crop.HarvestOutputs.Count; j++)
        {
            var entry = crop.HarvestOutputs[j];
            if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == crop)
            {
                return GetItemCount(seedSO) > 0;
            }
        }
        return false;
    }

    private bool HasExistingPlantTaskForCell(int cellX, int cellZ)
    {
        if (TaskManager == null) return false;
        var tasks = TaskManager.AvailableTasks;
        for (int i = 0; i < tasks.Count; i++)
        {
            if (tasks[i] is PlantCropTask pct && pct.CellX == cellX && pct.CellZ == cellZ)
                return true;
        }
        return false;
    }

    private bool HasExistingWaterTaskForCell(int cellX, int cellZ)
    {
        if (TaskManager == null) return false;
        var tasks = TaskManager.AvailableTasks;
        for (int i = 0; i < tasks.Count; i++)
        {
            if (tasks[i] is WaterCropTask wct && wct.CellX == cellX && wct.CellZ == cellZ)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Walks every cell whose world position falls inside the zone's BoxCollider bounds.
    /// Calls the callback with (map, grid, cellX, cellZ). Resolves the owning MapController
    /// once per zone via the zone's transform position.
    /// </summary>
    private void EnumerateCellsInZone(Zone zone, System.Action<MapController, TerrainCellGrid, int, int> callback)
    {
        if (zone == null || callback == null) return;

        var box = zone.GetComponent<BoxCollider>();
        if (box == null) return;

        var map = MapController.GetMapAtPosition(zone.transform.position);
        if (map == null) return;

        var grid = map.GetComponent<TerrainCellGrid>();
        if (grid == null || grid.Width == 0) return;

        Vector3 worldCenter = box.transform.TransformPoint(box.center);
        Vector3 worldHalf = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
        Vector3 worldMin = worldCenter - worldHalf;
        Vector3 worldMax = worldCenter + worldHalf;

        // Reverse-derive the grid origin from GridToWorld(0,0) so we can compute cell
        // indices for points that may fall outside the grid (WorldToGrid returns false +
        // zeroes the out params for out-of-bounds, which loses the clamp signal).
        // GridToWorld returns the cell CENTER, so subtract a half-cell to get the origin.
        Vector3 origin0 = grid.GridToWorld(0, 0) - new Vector3(grid.CellSize * 0.5f, 0f, grid.CellSize * 0.5f);

        int minX = Mathf.FloorToInt((worldMin.x - origin0.x) / grid.CellSize);
        int minZ = Mathf.FloorToInt((worldMin.z - origin0.z) / grid.CellSize);
        int maxX = Mathf.FloorToInt((worldMax.x - origin0.x) / grid.CellSize);
        int maxZ = Mathf.FloorToInt((worldMax.z - origin0.z) / grid.CellSize);

        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minZ > maxZ) (minZ, maxZ) = (maxZ, minZ);

        // Clamp to grid bounds.
        if (minX < 0) minX = 0;
        if (minZ < 0) minZ = 0;
        if (maxX >= grid.Width) maxX = grid.Width - 1;
        if (maxZ >= grid.Depth) maxZ = grid.Depth - 1;

        if (minX > maxX || minZ > maxZ) return; // zone fully outside grid

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            callback(map, grid, x, z);
        }
    }
}
