using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;

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
}
