# Farmer Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Farmer — `FarmingBuilding` + `JobFarmer` + plant/water tasks — closing the plant→water→harvest→ship loop on top of all four predecessor systems (Plan 1 Tool Storage, Plan 2 Help Wanted, Plan 2.5 Management Furniture, plus the existing farming substrate). The `FarmingBuilding` extends `HarvestingBuilding` and adds three things: a designer-authored **`List<Zone>` of farming areas** (multi-zone, per 2026-04-30 design refinement), daily plant + water scans that register `PlantCropTask` / `WaterCropTask` on the building's `BuildingTaskManager`, and an auto-derived `IStockProvider.GetStockTargets` (seeds + watering can as inputs, produce as output). `JobFarmer` mirrors `JobHarvester`'s GOAP shape and uses Plan 1's Tool Storage primitive for the watering can (per-task fetch/return pattern). Crop self-seeding is a designer-only edit to the existing `Crop_*.asset` files (zero new code — `RegisterHarvestedItem` already handles).

**Architecture:** `FarmingBuilding : HarvestingBuilding` overrides `OnEnable`/`OnDisable` to subscribe `PlantScan` + `WaterScan` to `TimeManager.OnNewDay` alongside the inherited `ScanHarvestingArea`. The multi-zone list (`_farmingAreaZones`) is a separate authoring field — the inherited `_harvestingAreaZone` is left null on FarmingBuilding prefabs (the scan still no-ops cleanly). PlantScan iterates every cell in the union of `_farmingAreaZones`, registers a `PlantCropTask(cellX, cellZ, CropSO)` for empty cells where the crop's seed is in stock + the building's quota for that crop's produce isn't hit. WaterScan registers `WaterCropTask(cellX, cellZ)` for planted cells with `cell.TimeSinceLastWatered ≥ 1f` AND `cell.Moisture < crop.MinMoistureForGrowth` AND `cell.GrowthTimer < crop.DaysToMature` (mature/perennial crops don't water). `JobFarmer` shape mirrors `JobHarvester` exactly (cached goals, scratch worldState, fresh GOAP action instances per plan) — only the action library + goal priority differ. Goals: `HarvestMatureCells` > `WaterDryCells` > `PlantEmptyCells` > `DepositResources` > `Idle`.

**Tech Stack:** Unity 6, NGO, C#. No new EditMode tests (consistent with Plans 1-2.5).

**Source spec:** [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../specs/2026-04-29-farmer-job-and-tool-storage-design.md). Multi-zone refinement per 2026-04-30 conversation.

**Phase scope:** Plan 3 of 4 (terminal Farmer plan in the rollout). Plans 1+2+2.5 already shipped:
- Plan 1: `_toolStorageFurniture` + `OwnerBuildingId` marker + `FetchToolFromStorage` / `ReturnToolToStorage` GOAP actions + `CanPunchOut` gate.
- Plan 2: `DisplayTextFurniture` + `_isHiring` NetworkVariable + hiring API + `IsHiring` gates on `AskForJob` / `FindAvailableJob` + `UI_DisplayTextReader`.
- Plan 2.5: `ManagementFurniture` (owner's hiring desk) + sign-becomes-informative-only + NPC `NeedJob` `OnNewDay` throttle.

After this plan ships, the full Farmer loop is testable end-to-end: a player can build a `FarmingBuilding`, drop a `ManagementFurniture` + `_helpWantedFurniture` + `_toolStorageFurniture` (with a `WateringCan` inside) + draw two `Zone`s as the farm fields. NPCs auto-discover the vacant Farmer position via `NeedJob` (Plan 2.5's OnNewDay scan), walk to the boss, get hired, and start the plant/water/harvest loop autonomously.

---

## Files affected

**Created:**
- `Assets/Scripts/World/Buildings/Tasks/PlantCropTask.cs`
- `Assets/Scripts/World/Buildings/Tasks/WaterCropTask.cs`
- `Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs`
- `Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs`
- `.agent/skills/job-farmer/SKILL.md`
- `wiki/systems/job-farmer.md`
- `docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md`

**Modified:**
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — extend `DoesJobTypeAcceptQuest` to map `PlantCropTask` and `WaterCropTask` → `JobType.Farmer`.
- `Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset` — designer edit: add the matching `SeedSO` to `_harvestOutputs`.
- `Assets/Resources/Data/Farming/Crops/Crop_Flower.asset` — same.
- `Assets/Resources/Data/Farming/Crops/Crop_AppleTree.asset` — same.
- `wiki/systems/farming.md` — change-log entry for the JobFarmer integration.
- `wiki/systems/jobs-and-logistics.md` — change-log entry.

---

## Task 1: PlantCropTask + WaterCropTask

**Files:**
- Create: `Assets/Scripts/World/Buildings/Tasks/PlantCropTask.cs`
- Create: `Assets/Scripts/World/Buildings/Tasks/WaterCropTask.cs`

Two new `BuildingTask` subclasses. Cell-targeted (CellX, CellZ stored as fields). `Target` is null because these tasks point at terrain cells, not InteractableObjects — Plan 2's `IQuest` unification handles this case via `ZoneTarget` or null.

### Step 0: Read existing BuildingTask hierarchy

Read `Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs` (the abstract base) + at least one concrete subclass (`HarvestResourceTask.cs` or `DestroyHarvestableTask.cs`) to understand the canonical shape. Note:
- The constructor signature pattern.
- Whether `Target` is abstract or virtual.
- How `IsValid` is invoked + what it checks.
- The `IQuest` interface members (Issuer, OriginWorldId, OriginMapId, etc.).

### Step 1: Create PlantCropTask.cs

```csharp
using MWI.Farming;

/// <summary>
/// BuildingTask: plant <see cref="Crop"/> at the given terrain cell. Registered by
/// FarmingBuilding.PlantScan once per OnNewDay for empty cells inside the farm zones.
/// Claimed + executed by JobFarmer's GoapAction_PlantCrop chain (FetchSeed → PlantCrop).
///
/// Auto-publishes as IQuest via existing CommercialBuilding quest aggregator (Hybrid C
/// unification, 2026-04-23). Quest eligibility maps to JobType.Farmer via
/// CommercialBuilding.DoesJobTypeAcceptQuest extension.
/// </summary>
public class PlantCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }
    public CropSO Crop { get; }

    /// <summary>Target is null — task is cell-targeted, not InteractableObject-targeted.
    /// Quest world-marker layer falls back to building-position rendering.</summary>
    public override IInteractableObject Target => null;

    public PlantCropTask(int cellX, int cellZ, CropSO crop)
    {
        CellX = cellX;
        CellZ = cellZ;
        Crop = crop;
    }
}
```

(Adapt to actual `BuildingTask` API — verify `Target` property name, whether constructor needs an `Issuer` parameter, etc.)

### Step 2: Create WaterCropTask.cs

```csharp
/// <summary>
/// BuildingTask: water the planted crop at the given terrain cell. Registered by
/// FarmingBuilding.WaterScan once per OnNewDay for cells whose
/// TimeSinceLastWatered ≥ 1f AND Moisture &lt; crop.MinMoistureForGrowth AND
/// GrowthTimer &lt; crop.DaysToMature (mature/perennial crops don't water).
///
/// Claimed by JobFarmer's GoapAction_WaterCrop chain (FetchToolFromStorage(WateringCan)
/// → WaterCrop → ReturnToolToStorage). The WateringCan is fetched from the building's
/// _toolStorageFurniture (Plan 1 primitive).
/// </summary>
public class WaterCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }

    public override IInteractableObject Target => null;

    public WaterCropTask(int cellX, int cellZ)
    {
        CellX = cellX;
        CellZ = cellZ;
    }
}
```

### Step 3: Compile + commit

```bash
git add Assets/Scripts/World/Buildings/Tasks/PlantCropTask.cs Assets/Scripts/World/Buildings/Tasks/WaterCropTask.cs
git commit -m "feat(tasks): PlantCropTask + WaterCropTask — farmer task primitives

Cell-targeted BuildingTasks for the farming loop. PlantCropTask carries
the destination cell + the CropSO to plant. WaterCropTask carries the
cell to water. Both have Target=null (cell-targeted, not interactable);
quest world-marker layer falls back to building-position rendering.

Auto-publish as IQuest via existing CommercialBuilding aggregator. Quest
eligibility for JobType.Farmer wired in Task 3.

Part of: farmer-integration plan, Task 1/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Quest eligibility extension

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Extend `DoesJobTypeAcceptQuest` (around line 1390) so the existing on-shift auto-claim flow accepts `PlantCropTask` and `WaterCropTask` for `JobType.Farmer`.

### Step 1: Find the method

`grep -n "DoesJobTypeAcceptQuest" Assets/Scripts/World/Buildings/CommercialBuilding.cs`. Read the existing body. Current shape:

```csharp
    private static bool DoesJobTypeAcceptQuest(MWI.WorldSystem.JobType jobType, MWI.Quests.IQuest quest)
    {
        if (quest is HarvestResourceTask || quest is DestroyHarvestableTask)
        {
            return jobType == MWI.WorldSystem.JobType.Woodcutter
                || jobType == MWI.WorldSystem.JobType.Miner
                || jobType == MWI.WorldSystem.JobType.Forager
                || jobType == MWI.WorldSystem.JobType.Farmer;
        }
        if (quest is PickupLooseItemTask) ...
        ...
    }
```

### Step 2: Add the new task-type branches

Insert before the `if (quest is PickupLooseItemTask)` branch:

```csharp
        if (quest is PlantCropTask || quest is WaterCropTask)
        {
            return jobType == MWI.WorldSystem.JobType.Farmer;
        }
```

(Order: place near other harvester-family branches for readability.)

### Step 3: Commit

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(quests): PlantCropTask + WaterCropTask accept JobType.Farmer

DoesJobTypeAcceptQuest extension: the new farmer task types are claimable
by Farmer-type workers. Existing harvest/destroy task branches already
include Farmer (per spec §3.3); this adds the two new task families.

Part of: farmer-integration plan, Task 2/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Crop self-seeding asset config

**Files (designer-only `.asset` edits — Unity Editor):**
- Modify: `Assets/Resources/Data/Farming/Crops/Crop_Wheat.asset`
- Modify: `Assets/Resources/Data/Farming/Crops/Crop_Flower.asset`
- Modify: `Assets/Resources/Data/Farming/Crops/Crop_AppleTree.asset`

Each crop's `_harvestOutputs` list gets a second entry: the matching `SeedSO`. RegisterHarvestedItem already loops through outputs and adds wanted items to inventory — this gives every harvest a small seed yield, making the farm self-sustaining.

### Step 1: Verify the SeedSO assets exist

`Glob Assets/Resources/Data/Farming/Seeds/**/*.asset` (or wherever SeedSO assets live; check via `grep -rn "CreateAssetMenu.*Seed" Assets/Scripts/Farming/`). If `WheatSeed` / `FlowerSeed` / `AppleSeed` don't exist as authored SeedSO assets, **stop and ask** — the designer needs to author them first. The Plan can't proceed without them.

If they exist, note their paths.

### Step 2: Add seed to each crop's `_harvestOutputs`

For each `Crop_*.asset`:
- Open in Unity Editor (or edit the YAML directly via `script-execute` if you prefer).
- Locate the `_harvestOutputs` list field on the CropSO.
- Add a second `HarvestableOutputEntry` row: `Item = <matching SeedSO>`, `Count = 1`.

Example for Crop_Wheat (after edit, the list contains):
```
- Item: <ref to ItemSO_Wheat>
  Count: 3
- Item: <ref to SeedSO_WheatSeed>
  Count: 1
```

### Step 3: Verify

In the Editor, open each crop's CropSO inspector. `_harvestOutputs` should show 2 entries (or more if it had bonus outputs already).

### Step 4: Commit

```bash
git add Assets/Resources/Data/Farming/Crops/
git commit -m "content(farming): crops self-seed via _harvestOutputs

Designer edits to the 3 existing CropSO assets (Wheat, Flower, AppleTree).
Each now lists its matching SeedSO as a secondary HarvestableOutputEntry
with Count=1. Zero new code — RegisterHarvestedItem already loops the
outputs list and adds matching items to building inventory.

Effect: a 10-cell wheat farm averaging 3 harvests per cell per season
yields 30 wheat + 10 seeds; replanting consumes 10 seeds; surplus 0.
Designer can rebalance by tweaking Count to 2+ if seeds should
accumulate, or 0 if hiring should rely entirely on BuyOrders.

BuyOrder fallback (Task 5's IStockProvider override) covers the case
where seed stock dips below the configured min — orders flow to any
neighbouring FarmingBuilding with surplus or a designer-stocked Shop.

Part of: farmer-integration plan, Task 3/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: FarmingBuilding scaffolding (class + multi-zone field + accessors)

**Files:**
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs`

Minimal skeleton — class declaration, designer fields (incl. `List<Zone> _farmingAreaZones`), accessors, `BuildingType` override, `InitializeJobs` override. Daily scans land in Task 6.

### Step 0: Read HarvestingBuilding.cs to know what to inherit

`Read Assets/Scripts/World/Buildings/CommercialBuildings/HarvestingBuilding.cs` end-to-end. Key inherited members FarmingBuilding will use:
- `_wantedResources: List<HarvestingResourceEntry>` (output stock targets).
- `WantedResources` accessor.
- `DepositZone`, `_depositZone`.
- `RegisterHarvestedItem(ItemInstance)` — used implicitly by deposit flow.
- `GetWantedItems()` / `GetAcceptedItems()` / `IsResourceAtLimit(...)`.
- `OnEnable` / `OnDisable` virtual — for the daily-scan subscription.
- `InitializeJobs` virtual — for the job creation pattern.

### Step 1: Create the class skeleton

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;
using MWI.Time;
using MWI.WorldSystem;

/// <summary>
/// Commercial building specialisation for crop farms. Extends HarvestingBuilding with:
/// - Farmer-specific job + task types (JobFarmer + PlantCropTask + WaterCropTask).
/// - Multi-zone farm field (List&lt;Zone&gt; _farmingAreaZones) — designer can author multiple
///   non-contiguous fields per building (north field for wheat, south for flowers).
/// - Auto-derived IStockProvider stock targets: produce items (output throttle), seed items
///   (input restock trigger), watering can (input).
/// - Daily PlantScan + WaterScan that register PlantCropTask / WaterCropTask on the
///   inherited BuildingTaskManager.
///
/// Consumes Plan 1 (Tool Storage primitive — watering can fetch/return), Plan 2 (Help Wanted
/// + IsHiring gates), and Plan 2.5 (ManagementFurniture + NeedJob OnNewDay throttle).
/// </summary>
public class FarmingBuilding : HarvestingBuilding
{
    public override BuildingType BuildingType => BuildingType.Farm;   // verify enum name

    [Header("Farming Config")]
    [Tooltip("Crops this farm grows. Each crop's _harvestOutputs are auto-derived as " +
             "the building's wanted produce + seed stock targets (Task 5 wires the override).")]
    [SerializeField] private List<CropSO> _cropsToGrow = new List<CropSO>();

    [Tooltip("Number of Farmer positions this building creates on InitializeJobs.")]
    [SerializeField] private int _farmerCount = 2;

    [SerializeField] private string _farmerJobTitle = "Farmer";

    [Header("Farming Areas (multi-zone)")]
    [Tooltip("Designer-authored zones defining the farm's plant/water area. PlantScan + " +
             "WaterScan iterate the union of all cells inside these zones. Empty list = " +
             "no farming activity (the building still works as a regular HarvestingBuilding).")]
    [SerializeField] private List<Zone> _farmingAreaZones = new List<Zone>();

    [Header("Tool / Seed Stock Targets")]
    [Tooltip("Min stock per seed type — when the building's seed inventory dips below this, " +
             "a BuyOrder fires via the existing logistics chain.")]
    [SerializeField] private int _seedMinStock = 5;

    [SerializeField] private int _seedMaxStock = 20;

    [Tooltip("Reference to the WateringCan ItemSO (typically a MiscSO). Drives the input " +
             "stock target for the building's tool storage. Null = no watering loop " +
             "(WaterScan still runs; the cans-needed slot just won't auto-restock).")]
    [SerializeField] private ItemSO _wateringCanItem;

    [SerializeField] private int _wateringCanMaxStock = 2;

    public IReadOnlyList<CropSO> CropsToGrow => _cropsToGrow;
    public int FarmerCount => _farmerCount;
    public IReadOnlyList<Zone> FarmingAreaZones => _farmingAreaZones;
    public ItemSO WateringCanItem => _wateringCanItem;
    public int SeedMinStock => _seedMinStock;
    public int SeedMaxStock => _seedMaxStock;

    protected override void InitializeJobs()
    {
        for (int i = 0; i < _farmerCount; i++)
        {
            _jobs.Add(new JobFarmer(_farmerJobTitle, JobType.Farmer));
        }

        _jobs.Add(new JobLogisticsManager("Logistics Manager"));

        Debug.Log($"<color=green>[FarmingBuilding]</color> {buildingName} initialised with {_farmerCount} farmer(s) + 1 Logistics Manager.");
    }
}
```

**Verify before saving:**
- `BuildingType.Farm` enum value exists. If not, add it (single-line edit to BuildingType.cs).
- `JobType.Farmer = 12` already exists (confirmed pre-Plan-1).
- `JobLogisticsManager` constructor signature — match HarvestingBuilding's existing usage.
- The `_jobs` field on `CommercialBuilding` is `protected` — accessible from subclasses.

### Step 2: Compile + commit

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs
git commit -m "feat(building): FarmingBuilding scaffolding — fields + InitializeJobs

New CommercialBuilding subclass extending HarvestingBuilding. Designer
fields: _cropsToGrow (List<CropSO>), _farmerCount, _farmingAreaZones
(multi-zone — per 2026-04-30 design refinement), _wateringCanItem,
_seedMinStock / _seedMaxStock / _wateringCanMaxStock.

InitializeJobs creates _farmerCount × JobFarmer + 1 JobLogisticsManager
(matches HarvestingBuilding pattern).

Daily scans + IStockProvider override land in Tasks 5+6.

Part of: farmer-integration plan, Task 4/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: FarmingBuilding IStockProvider override + GetHelpWantedDisplayText flavor

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs`

Override `IStockProvider.GetStockTargets()` to auto-derive seed + produce + watering can stock targets from `_cropsToGrow`. Also override `GetHelpWantedDisplayText` for farm-specific flavor.

### Step 1: Verify HarvestingBuilding implements IStockProvider

`grep -n "IStockProvider" Assets/Scripts/World/Buildings/CommercialBuildings/HarvestingBuilding.cs`. If yes, FarmingBuilding inherits the interface contract — just override the method. If no, you'll need to add `IStockProvider` to FarmingBuilding's class declaration.

(Also check `Assets/Scripts/World/Buildings/IStockProvider.cs` for the exact contract: `IEnumerable<StockTarget> GetStockTargets()` or similar.)

### Step 2: Add the override

```csharp
    /// <summary>
    /// Auto-derived from _cropsToGrow:
    /// - For each crop, every non-Seed HarvestableOutputEntry becomes an OUTPUT target
    ///   (Min=0, Max=inherited from _wantedResources entry — the wanted-resources list is
    ///   the canonical produce-cap source, this method just surfaces it).
    /// - For each crop, every Seed HarvestableOutputEntry becomes an INPUT target
    ///   (Min=_seedMinStock, Max=_seedMaxStock).
    /// - If _wateringCanItem is non-null: an INPUT target for the can (Min=1, Max=_wateringCanMaxStock).
    ///
    /// Existing LogisticsStockEvaluator picks up these targets and fires BuyOrders for any
    /// deficit. Seeds + watering can flow IN via the existing logistics chain (TransporterJob
    /// delivers from supplier buildings); produce flows OUT via the existing harvest deposit
    /// path (no change).
    /// </summary>
    public override IEnumerable<StockTarget> GetStockTargets()    // adapt return type to actual contract
    {
        // Walk crops and emit per-output stock targets.
        for (int i = 0; i < _cropsToGrow.Count; i++)
        {
            var crop = _cropsToGrow[i];
            if (crop == null) continue;

            for (int j = 0; j < crop.HarvestOutputs.Count; j++)
            {
                var entry = crop.HarvestOutputs[j];
                if (entry.Item == null) continue;

                if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == crop)
                {
                    // INPUT target — seed for the crop we grow.
                    yield return new StockTarget(seedSO, _seedMinStock, _seedMaxStock);
                }
                else
                {
                    // OUTPUT target — produce. Use the inherited _wantedResources entry's
                    // maxQuantity if one exists; otherwise default to 50.
                    int max = LookupWantedMax(entry.Item) ?? 50;
                    yield return new StockTarget(entry.Item, 0, max);
                }
            }
        }

        // Watering can input target.
        if (_wateringCanItem != null)
        {
            yield return new StockTarget(_wateringCanItem, 1, _wateringCanMaxStock);
        }
    }

    private int? LookupWantedMax(ItemSO item)
    {
        for (int i = 0; i < _wantedResources.Count; i++)
        {
            if (_wantedResources[i].targetItem == item)
                return _wantedResources[i].maxQuantity;
        }
        return null;
    }

    /// <summary>Farm-specific Help Wanted text (override for flavor). Default falls back to
    /// the base CommercialBuilding implementation if list is empty.</summary>
    protected override string GetHelpWantedDisplayText()
    {
        var vacant = GetVacantJobs();
        if (vacant.Count == 0) return GetClosedHiringDisplayText();

        // Reuse base format. Optional: add a farm-flavored opener.
        return base.GetHelpWantedDisplayText();
    }
```

(Adapt `StockTarget` struct + `GetStockTargets` return type to the actual `IStockProvider.cs` contract. The existing `ShopBuilding` and `CraftingBuilding` provide reference implementations.)

### Step 3: Commit

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs
git commit -m "feat(building): FarmingBuilding IStockProvider auto-derive

Walks _cropsToGrow and emits stock targets per HarvestableOutputEntry:
- Seed entries (item is SeedSO whose CropToPlant matches the crop) →
  INPUT target with _seedMinStock / _seedMaxStock.
- Non-seed entries (produce: Wheat, Apple, Sap, etc.) → OUTPUT target;
  Max sourced from inherited _wantedResources or defaults to 50.
- _wateringCanItem (if set) → INPUT target with min=1, max=_wateringCanMaxStock.

Existing LogisticsStockEvaluator fires BuyOrders for deficits. Seeds +
watering can flow IN via TransporterJob; produce flows OUT via existing
deposit path. Zero new logistics code beyond the auto-derivation.

GetHelpWantedDisplayText override is a no-op stub for now; future flavor
text can land here.

Part of: farmer-integration plan, Task 5/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: FarmingBuilding daily scans (PlantScan + WaterScan + multi-zone walk)

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs`

Subscribe `PlantScan` + `WaterScan` to `TimeManager.OnNewDay` alongside the inherited `ScanHarvestingArea`. Each iterates every cell inside the union of `_farmingAreaZones`.

### Step 0: Understand TerrainCellGrid

Read `Assets/Scripts/World/MapSystem/TerrainCellGrid.cs` (or wherever the cell grid lives — `grep -rn "class TerrainCellGrid"`). Understand:
- How to convert a Zone's BoxCollider bounds → cell index range.
- The `TerrainCell.IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `Moisture` fields.
- The `GetCellRef(int x, int z)` accessor.
- How `MapController.GetMapAtPosition(Vector3)` resolves the owning map.

The plan/spec uses `_map.GetComponent<TerrainCellGrid>()` — verify this is canonical. Reference: `CharacterAction_PlaceCrop.cs`.

### Step 1: Override OnEnable + OnDisable to add subscriptions

```csharp
    protected override void OnEnable()
    {
        base.OnEnable();   // subscribes ScanHarvestingArea
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += PlantScan;
            TimeManager.Instance.OnNewDay += WaterScan;
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= PlantScan;
            TimeManager.Instance.OnNewDay -= WaterScan;
        }
    }
```

### Step 2: Add PlantScan

```csharp
    /// <summary>
    /// Server-only. For each cell in the union of _farmingAreaZones that is empty
    /// (no PlantedCropId, not plowed-only-not-planted) AND has its corresponding seed in
    /// stock AND the crop's produce quota isn't hit: register a PlantCropTask on the
    /// building's BuildingTaskManager. Quota-driven crop selection — picks the crop most
    /// under its produce target.
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

                // Pick the crop most under quota. Skip if no seed in stock.
                var crop = SelectCropForCell();
                if (crop == null) return;
                if (!HasSeedInInventory(crop)) return;

                // Already a PlantCropTask for this cell? Skip duplicates.
                if (HasExistingPlantTaskForCell(cellX, cellZ)) return;

                TaskManager.RegisterTask(new PlantCropTask(cellX, cellZ, crop));
            });
        }
    }

    private CropSO SelectCropForCell()
    {
        // Quota-driven: returns the crop whose primary produce stock is most under target.
        // Tie-broken by ascending CropSO.Id (the public string property on HarvestableSO,
        // also used as the persistence key in TerrainCell.PlantedCropId).
        CropSO best = null;
        int bestDeficit = -1;
        for (int i = 0; i < _cropsToGrow.Count; i++)
        {
            var crop = _cropsToGrow[i];
            if (crop == null) continue;

            // Find this crop's primary (non-seed) output.
            ItemSO primaryProduce = null;
            for (int j = 0; j < crop.HarvestOutputs.Count; j++)
            {
                var entry = crop.HarvestOutputs[j];
                if (entry.Item != null && !(entry.Item is SeedSO))
                {
                    primaryProduce = entry.Item;
                    break;
                }
            }
            if (primaryProduce == null) continue;

            int max = LookupWantedMax(primaryProduce) ?? 50;
            int current = GetItemCount(primaryProduce);
            int deficit = max - current;
            if (deficit <= 0) continue;   // already at target

            if (deficit > bestDeficit
                || (deficit == bestDeficit && best != null && string.Compare(crop.Id, best.Id) < 0))
            {
                best = crop;
                bestDeficit = deficit;
            }
        }
        return best;
    }

    private bool HasSeedInInventory(CropSO crop)
    {
        // Walk crop.HarvestOutputs, find the SeedSO whose CropToPlant matches, check building inventory.
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
        // Walk active PlantCropTasks; skip if cell already targeted.
        for (int i = 0; i < TaskManager.AvailableTasks.Count; i++)
        {
            if (TaskManager.AvailableTasks[i] is PlantCropTask pct && pct.CellX == cellX && pct.CellZ == cellZ)
                return true;
        }
        return false;
    }
```

### Step 3: Add WaterScan

```csharp
    /// <summary>
    /// Server-only. For each planted cell in the farm zones that meets the dry-cell predicate:
    /// - cell.GrowthTimer &lt; crop.DaysToMature (mature/perennial crops don't water).
    /// - cell.Moisture &lt; crop.MinMoistureForGrowth (already wet enough → skip).
    /// - cell.TimeSinceLastWatered ≥ 1f (rain today resets to 0; wait at least one day).
    /// Register a WaterCropTask. The Farmer's GOAP plan picks it up + chains
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
                if (cell.GrowthTimer >= crop.DaysToMature) return;       // mature — don't water
                if (cell.TimeSinceLastWatered < 1f) return;              // freshly watered (or rain)
                if (cell.Moisture >= crop.MinMoistureForGrowth) return;  // wet enough

                if (HasExistingWaterTaskForCell(cellX, cellZ)) return;

                TaskManager.RegisterTask(new WaterCropTask(cellX, cellZ));
            });
        }
    }

    private bool HasExistingWaterTaskForCell(int cellX, int cellZ)
    {
        if (TaskManager == null) return false;
        for (int i = 0; i < TaskManager.AvailableTasks.Count; i++)
        {
            if (TaskManager.AvailableTasks[i] is WaterCropTask wct && wct.CellX == cellX && wct.CellZ == cellZ)
                return true;
        }
        return false;
    }
```

### Step 4: Add EnumerateCellsInZone helper

```csharp
    /// <summary>
    /// Walks every cell whose world position falls inside the zone's BoxCollider bounds.
    /// Calls the callback with (map, grid, cellX, cellZ). Resolves the owning MapController
    /// once per zone via the zone's transform position.
    /// </summary>
    private void EnumerateCellsInZone(Zone zone, System.Action<MapController, MWI.Terrain.TerrainCellGrid, int, int> callback)
    {
        if (zone == null || callback == null) return;

        var box = zone.GetComponent<BoxCollider>();
        if (box == null) return;

        var map = MapController.GetMapAtPosition(zone.transform.position);
        if (map == null) return;

        var grid = map.GetComponent<MWI.Terrain.TerrainCellGrid>();
        if (grid == null || grid.Width == 0) return;

        Vector3 worldCenter = box.transform.TransformPoint(box.center);
        Vector3 worldHalf = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
        Vector3 worldMin = worldCenter - worldHalf;
        Vector3 worldMax = worldCenter + worldHalf;

        if (!grid.WorldToGrid(worldMin, out int minX, out int minZ)) return;
        if (!grid.WorldToGrid(worldMax, out int maxX, out int maxZ)) return;

        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minZ > maxZ) (minZ, maxZ) = (maxZ, minZ);

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            callback(map, grid, x, z);
        }
    }
```

(Verify `MWI.Terrain.TerrainCellGrid` namespace + `WorldToGrid` signature against the actual file. The crop placement manager — `CropPlacementManager.cs` — uses these APIs already.)

### Step 5: Commit

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs
git commit -m "feat(building): FarmingBuilding daily PlantScan + WaterScan

Subscribes both to TimeManager.OnNewDay alongside inherited
ScanHarvestingArea. Each iterates the union of cells in
_farmingAreaZones (multi-zone, per 2026-04-30 design refinement).

PlantScan: empty cell + matching seed in stock + produce quota not hit
→ register PlantCropTask. Quota-driven crop selection picks the crop
whose primary produce is most under target (tie-broken by Id ascending).

WaterScan: planted cell + GrowthTimer < DaysToMature + Moisture below
MinMoistureForGrowth + TimeSinceLastWatered ≥ 1f → register WaterCropTask.
Mature/perennial crops skip; rain-watered cells (TerrainWeatherProcessor
resets to 0 on rain) skip on the same day. Free drought insurance.

EnumerateCellsInZone helper resolves owning map + grid once per zone via
MapController.GetMapAtPosition, walks BoxCollider bounds → cell range.

Part of: farmer-integration plan, Task 6/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: GoapAction_FetchSeed

**Files:**
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs`

Specialised fetch — finds the SeedSO matching a claimed `PlantCropTask`, walks to the building's storage furniture (regular storage, NOT the tool storage — seeds are consumables), takes 1 SeedSO, equips in hand. Companion to `GoapAction_PlantCrop` (Task 8).

### Step 1: Read GoapAction_FetchToolFromStorage as template

`GoapAction_FetchToolFromStorage.cs` (Plan 1 Task 3) is the closest precedent. Differences:
- FetchSeed targets the building's **seed storage** (which storage? — probably the same as tool storage OR the building's primary storage. Read FurnitureManager / find the canonical "general storage" accessor on `CommercialBuilding`.)
- FetchSeed does NOT stamp `OwnerBuildingId` (seeds are consumables, not tools — no return path).
- FetchSeed's IsValid checks `hasUnfilledPlantTask` AND `hasMatchingSeedInStorage`.

### Step 2: Determine seed storage location

The Farmer's seeds get deposited via the existing harvest deposit cycle (`RegisterHarvestedItem` → `_inventory`). They live in the building's general inventory, accessed by the existing `FindStorageFurnitureForItem` helper (or similar).

Read `Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeFromSourceFurniture.cs` for the canonical "find a storage furniture containing this item, walk there, take it" pattern. FetchSeed will mirror this almost exactly — it's effectively a parameterised TakeFromSourceFurniture for SeedSO.

### Step 3: Create the file

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;

/// <summary>
/// Specialised fetch for the Farmer plan: walks to the building's storage furniture
/// containing a matching SeedSO, takes 1 instance, equips in hand. Companion to
/// GoapAction_PlantCrop (Task 8).
///
/// Differs from GoapAction_FetchToolFromStorage (Plan 1):
/// - Target storage is general building inventory (not the dedicated _toolStorageFurniture)
///   — seeds are consumables, not tools.
/// - Does NOT stamp OwnerBuildingId. Seeds get consumed by CharacterAction_PlaceCrop;
///   no return path needed.
/// - IsValid gates on (hands free) AND (an unclaimed PlantCropTask exists for a crop whose
///   seed is in the building's inventory).
///
/// Cost = 1.
/// Preconditions:
///   hasSeedInHand = false
///   hasUnfilledPlantTask = true
///   hasMatchingSeedInStorage = true
/// Effects:
///   hasSeedInHand = true
/// </summary>
public class GoapAction_FetchSeed : GoapAction
{
    private readonly FarmingBuilding _building;
    private bool _isMoving;
    private bool _isComplete;
    private CropSO _claimedCrop;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "FetchSeed";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_FetchSeed(FarmingBuilding building)
    {
        _building = building;
        _preconditions = new Dictionary<string, bool>
        {
            { "hasSeedInHand", false },
            { "hasUnfilledPlantTask", true },
            { "hasMatchingSeedInStorage", true }
        };
        _effects = new Dictionary<string, bool>
        {
            { "hasSeedInHand", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null) return false;

        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree()) return false;

        // Find an unclaimed PlantCropTask whose crop has a seed in the building's storage.
        if (_building.TaskManager == null) return false;
        for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
        {
            if (_building.TaskManager.AvailableTasks[i] is PlantCropTask pct && pct.Crop != null)
            {
                if (BuildingHasSeedFor(pct.Crop)) return true;
            }
        }
        return false;
    }

    private bool BuildingHasSeedFor(CropSO crop)
    {
        for (int j = 0; j < crop.HarvestOutputs.Count; j++)
        {
            var entry = crop.HarvestOutputs[j];
            if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == crop)
            {
                return _building.GetItemCount(seedSO) > 0;
            }
        }
        return false;
    }

    public override void Execute(Character worker)
    {
        if (worker == null || _building == null) { _isComplete = true; return; }

        // Find the first PlantCropTask we can serve.
        if (_claimedCrop == null)
        {
            for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
            {
                if (_building.TaskManager.AvailableTasks[i] is PlantCropTask pct
                    && pct.Crop != null
                    && BuildingHasSeedFor(pct.Crop))
                {
                    _claimedCrop = pct.Crop;
                    break;
                }
            }
            if (_claimedCrop == null) { _isComplete = true; return; }
        }

        // Find a storage furniture containing the matching seed.
        var seedSO = ResolveSeedFor(_claimedCrop);
        if (seedSO == null) { _isComplete = true; return; }

        var sourceFurniture = _building.FindStorageFurnitureForItem(seedSO);   // verify API name
        if (sourceFurniture == null) { _isComplete = true; return; }

        var interactable = sourceFurniture.GetComponent<InteractableObject>();
        if (interactable != null && !interactable.IsCharacterInInteractionZone(worker))
        {
            if (!_isMoving)
            {
                worker.CharacterMovement.SetDestination(sourceFurniture.GetInteractionPosition(worker.transform.position));
                _isMoving = true;
            }
            return;
        }

        // In zone — take 1 seed instance.
        var instance = TakeOneFromStorage(sourceFurniture, seedSO);
        if (instance == null) { _isComplete = true; return; }

        worker.CharacterEquipment?.CarryItemInHand(instance);

        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=cyan>[FetchSeed]</color> {worker.CharacterName} fetched {seedSO.ItemName} for crop {_claimedCrop.Id}.");

        _isComplete = true;
    }

    private static SeedSO ResolveSeedFor(CropSO crop)
    {
        for (int j = 0; j < crop.HarvestOutputs.Count; j++)
        {
            if (crop.HarvestOutputs[j].Item is SeedSO seedSO && seedSO.CropToPlant == crop)
                return seedSO;
        }
        return null;
    }

    private static ItemInstance TakeOneFromStorage(StorageFurniture storage, ItemSO target)
    {
        if (storage == null) return null;
        for (int i = 0; i < storage.ItemSlots.Count; i++)
        {
            var slot = storage.ItemSlots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == target)
            {
                var taken = slot.ItemInstance;
                storage.RemoveItem(taken);
                return taken;
            }
        }
        return null;
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        _isComplete = false;
        _claimedCrop = null;
    }
}
```

(Adapt `_building.FindStorageFurnitureForItem(seedSO)` to the actual API on `CommercialBuilding`. If it doesn't exist by that name, search via `GetComponentsInChildren<StorageFurniture>` and walk to find one containing the item — but check first because there's likely a canonical helper per `feedback_check_existing_api_first.md`.)

### Step 4: Commit

```bash
git add Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs
git commit -m "feat(ai): GoapAction_FetchSeed — farmer's seed pickup

Specialised fetch for Plan 3's Farmer. Walks to a building storage
containing a SeedSO matching an unclaimed PlantCropTask, takes 1 seed,
equips in hand. Differs from GoapAction_FetchToolFromStorage (Plan 1):
target is the building's general inventory (not _toolStorageFurniture),
no OwnerBuildingId stamp (seeds are consumables, not tools).

IsValid gates on hands-free AND at least one PlantCropTask exists whose
crop has a seed in storage. Race-loss handling: Execute claims a crop
to fetch for; if storage no longer has the seed by the time the action
runs (another farmer took it), Execute sets _isComplete=true and the
planner replans.

Part of: farmer-integration plan, Task 7/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: GoapAction_PlantCrop + GoapAction_WaterCrop

**Files:**
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs`
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs`

Two new actions, both wrap existing `CharacterAction_*` server-only mutations. Bundled because they share a similar shape (walk to cell → run CharacterAction → mark complete).

### Step 1: Create GoapAction_PlantCrop.cs

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;

/// <summary>
/// Wraps CharacterAction_PlaceCrop. Walks worker to the cell of a claimed PlantCropTask,
/// queues the action, marks complete. The CharacterAction itself consumes the held seed
/// + sets cell.PlantedCropId + spawns the CropHarvestable (existing 2026-04-29 farming
/// substrate handles the rest).
///
/// Cost = 1.
/// Preconditions:
///   hasSeedInHand = true
///   hasUnfilledPlantTask = true
/// Effects:
///   hasPlantedCrop = true
///   hasSeedInHand = false   (CharacterAction_PlaceCrop.OnApplyEffect consumes the seed)
/// </summary>
public class GoapAction_PlantCrop : GoapAction
{
    private readonly FarmingBuilding _building;
    private bool _isMoving;
    private bool _isComplete;
    private PlantCropTask _claimedTask;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "PlantCrop";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_PlantCrop(FarmingBuilding building)
    {
        _building = building;
        _preconditions = new Dictionary<string, bool>
        {
            { "hasSeedInHand", true },
            { "hasUnfilledPlantTask", true }
        };
        _effects = new Dictionary<string, bool>
        {
            { "hasPlantedCrop", true },
            { "hasSeedInHand", false }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null) return false;
        if (_building.TaskManager == null) return false;

        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying || hands.CarriedItem == null) return false;
        if (!(hands.CarriedItem.ItemSO is SeedSO heldSeed)) return false;

        // Find a PlantCropTask whose crop matches the seed in hand.
        for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
        {
            if (_building.TaskManager.AvailableTasks[i] is PlantCropTask pct
                && pct.Crop != null
                && heldSeed.CropToPlant == pct.Crop)
            {
                return true;
            }
        }
        return false;
    }

    public override void Execute(Character worker)
    {
        if (worker == null || _building == null || _building.TaskManager == null)
        {
            _isComplete = true; return;
        }

        // Claim a matching task on first tick.
        if (_claimedTask == null)
        {
            var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands == null || hands.CarriedItem == null || !(hands.CarriedItem.ItemSO is SeedSO heldSeed))
            {
                _isComplete = true; return;
            }
            for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
            {
                if (_building.TaskManager.AvailableTasks[i] is PlantCropTask pct
                    && pct.Crop != null && heldSeed.CropToPlant == pct.Crop)
                {
                    if (_building.TaskManager.TryClaim(pct, worker))   // verify API name
                    {
                        _claimedTask = pct;
                        break;
                    }
                }
            }
            if (_claimedTask == null) { _isComplete = true; return; }
        }

        // Move to the cell.
        var map = MWI.WorldSystem.MapController.GetMapAtPosition(_building.transform.position);
        if (map == null) { _isComplete = true; return; }

        var grid = map.GetComponent<MWI.Terrain.TerrainCellGrid>();
        if (grid == null) { _isComplete = true; return; }

        Vector3 cellWorld = grid.GridToWorld(_claimedTask.CellX, _claimedTask.CellZ);
        if (Vector3.Distance(worker.transform.position, cellWorld) > 1.5f)
        {
            if (!_isMoving)
            {
                worker.CharacterMovement.SetDestination(cellWorld);
                _isMoving = true;
            }
            return;
        }

        // At the cell. Queue the existing CharacterAction_PlaceCrop.
        var crop = _claimedTask.Crop;
        worker.CharacterActions.ExecuteAction(
            new CharacterAction_PlaceCrop(worker, map, _claimedTask.CellX, _claimedTask.CellZ, crop));

        _building.TaskManager.MarkCompleted(_claimedTask);   // verify API
        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=green>[PlantCrop]</color> {worker.CharacterName} planted {crop.Id} at ({_claimedTask.CellX}, {_claimedTask.CellZ}).");

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        if (_claimedTask != null && !_isComplete && _building != null && _building.TaskManager != null)
            _building.TaskManager.Unclaim(_claimedTask);   // verify API

        _isMoving = false;
        _isComplete = false;
        _claimedTask = null;
    }
}
```

### Step 2: Create GoapAction_WaterCrop.cs

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Farming;

/// <summary>
/// Wraps CharacterAction_WaterCrop. Walks worker (with a WateringCan in hand — fetched via
/// GoapAction_FetchToolFromStorage(WateringCan) earlier in the plan) to the cell of a
/// claimed WaterCropTask, queues the action, marks complete.
///
/// Cost = 1.
/// Preconditions:
///   hasToolInHand_<wateringCan.name> = true   (Plan 1 ToolStorage primitive convention)
///   hasUnfilledWaterTask = true
/// Effects:
///   hasWateredCell = true
/// </summary>
public class GoapAction_WaterCrop : GoapAction
{
    private readonly FarmingBuilding _building;
    private bool _isMoving;
    private bool _isComplete;
    private WaterCropTask _claimedTask;

    private readonly Dictionary<string, bool> _preconditions;
    private readonly Dictionary<string, bool> _effects;

    public override Dictionary<string, bool> Preconditions => _preconditions;
    public override Dictionary<string, bool> Effects => _effects;
    public override string ActionName => "WaterCrop";
    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    public GoapAction_WaterCrop(FarmingBuilding building)
    {
        _building = building;
        string canKey = building != null && building.WateringCanItem != null
            ? building.WateringCanItem.name
            : "null";
        _preconditions = new Dictionary<string, bool>
        {
            { $"hasToolInHand_{canKey}", true },
            { "hasUnfilledWaterTask", true }
        };
        _effects = new Dictionary<string, bool>
        {
            { "hasWateredCell", true }
        };
    }

    public override bool IsValid(Character worker)
    {
        if (worker == null || _building == null || _building.WateringCanItem == null) return false;
        if (_building.TaskManager == null) return false;

        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying || hands.CarriedItem == null) return false;
        if (hands.CarriedItem.ItemSO != _building.WateringCanItem) return false;

        // At least one unclaimed WaterCropTask exists.
        for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
        {
            if (_building.TaskManager.AvailableTasks[i] is WaterCropTask) return true;
        }
        return false;
    }

    public override void Execute(Character worker)
    {
        if (worker == null || _building == null || _building.TaskManager == null) { _isComplete = true; return; }

        if (_claimedTask == null)
        {
            for (int i = 0; i < _building.TaskManager.AvailableTasks.Count; i++)
            {
                if (_building.TaskManager.AvailableTasks[i] is WaterCropTask wct)
                {
                    if (_building.TaskManager.TryClaim(wct, worker))
                    {
                        _claimedTask = wct;
                        break;
                    }
                }
            }
            if (_claimedTask == null) { _isComplete = true; return; }
        }

        var map = MWI.WorldSystem.MapController.GetMapAtPosition(_building.transform.position);
        if (map == null) { _isComplete = true; return; }

        var grid = map.GetComponent<MWI.Terrain.TerrainCellGrid>();
        if (grid == null) { _isComplete = true; return; }

        Vector3 cellWorld = grid.GridToWorld(_claimedTask.CellX, _claimedTask.CellZ);
        if (Vector3.Distance(worker.transform.position, cellWorld) > 1.5f)
        {
            if (!_isMoving)
            {
                worker.CharacterMovement.SetDestination(cellWorld);
                _isMoving = true;
            }
            return;
        }

        // At the cell. Queue the existing CharacterAction_WaterCrop.
        var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
        var canSO = hands != null && hands.CarriedItem != null ? hands.CarriedItem.ItemSO as WateringCanSO : null;
        float moisture = canSO != null ? canSO.MoistureSetTo : 1f;

        worker.CharacterActions.ExecuteAction(
            new CharacterAction_WaterCrop(worker, map, _claimedTask.CellX, _claimedTask.CellZ, moisture));

        _building.TaskManager.MarkCompleted(_claimedTask);
        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=cyan>[WaterCrop]</color> {worker.CharacterName} watered ({_claimedTask.CellX}, {_claimedTask.CellZ}).");

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        if (_claimedTask != null && !_isComplete && _building != null && _building.TaskManager != null)
            _building.TaskManager.Unclaim(_claimedTask);

        _isMoving = false;
        _isComplete = false;
        _claimedTask = null;
    }
}
```

### Step 3: Commit

```bash
git add Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs
git commit -m "feat(ai): GoapAction_PlantCrop + GoapAction_WaterCrop

Wrap existing CharacterAction_PlaceCrop / _WaterCrop. Walk worker to the
cell of a claimed PlantCropTask / WaterCropTask, queue the CharacterAction,
mark complete.

PlantCrop's preconditions match GoapAction_FetchSeed's effects
(hasSeedInHand=true → planner chains FetchSeed → PlantCrop).
WaterCrop reuses Plan 1's tool-storage key shape (hasToolInHand_<canName>=true)
so the planner can chain GoapAction_FetchToolFromStorage(WateringCan) →
WaterCrop → GoapAction_ReturnToolToStorage(WateringCan).

Race-loss handling on both: TryClaim guards, Exit unclaims if not
completed.

Part of: farmer-integration plan, Task 8/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: JobFarmer

**Files:**
- Create: `Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs`

GOAP-driven Job mirroring `JobHarvester`'s shape. Five goals (priority order: Harvest > Water > Plant > Deposit > Idle). Action library = the new actions (Fetch/Plant/Water + tool fetch/return) + reused actions (HarvestResources, DepositResources, IdleInBuilding). Cached goals + scratch worldState dict (zero per-tick allocations after warm-up).

### Step 1: Read JobHarvester.cs as the canonical template

`Read Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs` end-to-end. Mirror its shape exactly:
- `_jobTitle`, `_jobType`, `Category`, `Type` boilerplate.
- `ExecuteIntervalSeconds = 0.3f` (heavy planning).
- `_currentAction`, `_currentPlan`, `_availableActions`, `_scratchValidActions`, `_scratchWorldState`, cached goals.
- `Execute()` shape: tick action → if invalid: Exit + replan. If complete: Exit + replan. Else: Execute.
- `PlanNextActions()` shape: build worldState dict, fresh action instances each plan, pre-filter via IsValid, GoapPlanner.Plan.
- `CanExecute`, `HasWorkToDo`, `GetWorkSchedule`, `Assign`, `Unassign` overrides.

### Step 2: Create the file

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Farming;
using MWI.WorldSystem;

/// <summary>
/// JobFarmer: plants, waters, and harvests crops in a FarmingBuilding. GOAP-driven,
/// mirrors JobHarvester's shape exactly (cached goals, scratch worldState, fresh action
/// instances per plan, ExecuteIntervalSeconds=0.3f).
///
/// Goal priority (highest first):
///   HarvestMatureCells   → HarvestResources → DepositResources
///   WaterDryCells        → FetchTool(WateringCan) → WaterCrop → ReturnTool(WateringCan)
///   PlantEmptyCells      → FetchSeed → PlantCrop
///   DepositResources     → DepositResources (when bag full)
///   Idle                 → IdleInBuilding
///
/// Schedule: 6h–18h (slightly later than Harvester's 6–16h so farmers can water in evening).
/// </summary>
[System.Serializable]
public class JobFarmer : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => _jobType;
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP plumbing.
    private GoapGoal _currentGoal;
    private List<GoapAction> _availableActions;
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(12);
    private GoapGoal _cachedHarvestGoal;
    private GoapGoal _cachedWaterGoal;
    private GoapGoal _cachedPlantGoal;
    private GoapGoal _cachedDepositGoal;
    private GoapGoal _cachedIdleGoal;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _currentGoal != null ? _currentGoal.GoalName : "No Goal";

    public JobFarmer(string jobTitle = "Farmer", JobType jobType = JobType.Farmer)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    public override void Execute()
    {
        if (_workplace == null || !(_workplace is FarmingBuilding farm)) return;

        if (_currentAction != null)
        {
            if (!_currentAction.IsValid(_worker))
            {
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }
            _currentAction.Execute(_worker);
            if (_currentAction.IsComplete)
            {
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
            }
            return;
        }

        PlanNextActions(farm);
    }

    private void PlanNextActions(FarmingBuilding farm)
    {
        // Build worldState. Re-query each tick — the building's task manager + worker inventory
        // are the source of truth.
        bool hasUnfilledHarvestTask = HasAvailableTask<HarvestResourceTask>(farm);
        bool hasUnfilledWaterTask = HasAvailableTask<WaterCropTask>(farm);
        bool hasUnfilledPlantTask = HasAvailableTask<PlantCropTask>(farm);

        var hands = _worker.CharacterVisual?.BodyPartsController?.HandsController;
        var inventory = _worker.CharacterEquipment?.GetInventory();

        bool hasSeedInHand = hands != null && hands.IsCarrying && hands.CarriedItem != null
                             && hands.CarriedItem.ItemSO is SeedSO;

        bool hasCanInHand = hands != null && hands.IsCarrying && hands.CarriedItem != null
                            && farm.WateringCanItem != null
                            && hands.CarriedItem.ItemSO == farm.WateringCanItem;

        bool hasMatchingSeedInStorage = farm.HasAnySeedForUnclaimedPlantTask();   // helper added below
        bool hasWateringCanAvailable = farm.WateringCanItem != null
                                        && farm.HasToolStorage
                                        && farm.ToolStorage != null
                                        && StorageHasItem(farm.ToolStorage, farm.WateringCanItem);

        bool hasResourcesToDeposit = false;
        if (inventory != null)
        {
            for (int i = 0; i < inventory.ItemSlots.Count; i++)
            {
                var slot = inventory.ItemSlots[i];
                if (slot != null && !slot.IsEmpty()) { hasResourcesToDeposit = true; break; }
            }
        }
        if (!hasResourcesToDeposit && hands != null && hands.IsCarrying && !hasSeedInHand && !hasCanInHand)
        {
            hasResourcesToDeposit = true;
        }

        _scratchWorldState.Clear();
        _scratchWorldState["hasUnfilledHarvestTask"] = hasUnfilledHarvestTask;
        _scratchWorldState["hasUnfilledWaterTask"] = hasUnfilledWaterTask;
        _scratchWorldState["hasUnfilledPlantTask"] = hasUnfilledPlantTask;
        _scratchWorldState["hasSeedInHand"] = hasSeedInHand;
        _scratchWorldState["hasMatchingSeedInStorage"] = hasMatchingSeedInStorage;
        _scratchWorldState["hasResources"] = hasResourcesToDeposit;
        _scratchWorldState["hasDepositedResources"] = false;
        _scratchWorldState["isIdling"] = false;

        // Tool key uses the WateringCan ItemSO's name (Plan 1 convention).
        string canKey = farm.WateringCanItem != null ? farm.WateringCanItem.name : "null";
        _scratchWorldState[$"hasToolInHand_{canKey}"] = hasCanInHand;
        _scratchWorldState[$"toolNeededForTask_{canKey}"] = hasUnfilledWaterTask;
        _scratchWorldState[$"taskCompleteForTool_{canKey}"] = hasCanInHand && !hasUnfilledWaterTask;

        // Build action library — fresh instances per plan (action instances are stateful).
        if (_availableActions == null) _availableActions = new List<GoapAction>(10);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_HarvestResources(farm));
        _availableActions.Add(new GoapAction_DepositResources(farm));
        _availableActions.Add(new GoapAction_FetchSeed(farm));
        _availableActions.Add(new GoapAction_PlantCrop(farm));
        if (farm.WateringCanItem != null && farm.HasToolStorage)
        {
            _availableActions.Add(new GoapAction_FetchToolFromStorage(farm, farm.WateringCanItem));
            _availableActions.Add(new GoapAction_WaterCrop(farm));
            _availableActions.Add(new GoapAction_ReturnToolToStorage(farm, farm.WateringCanItem));
        }
        _availableActions.Add(new GoapAction_IdleInBuilding(farm));

        // Cache goals (DesiredState dicts are constant).
        if (_cachedHarvestGoal == null)
            _cachedHarvestGoal = new GoapGoal("HarvestMatureCells", new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 5);
        if (_cachedWaterGoal == null)
            _cachedWaterGoal = new GoapGoal("WaterDryCells", new Dictionary<string, bool> { { "toolReturned_" + canKey, true } }, priority: 4);
        if (_cachedPlantGoal == null)
            _cachedPlantGoal = new GoapGoal("PlantEmptyCells", new Dictionary<string, bool> { { "hasPlantedCrop", true } }, priority: 3);
        if (_cachedDepositGoal == null)
            _cachedDepositGoal = new GoapGoal("DepositResources", new Dictionary<string, bool> { { "hasDepositedResources", true } }, priority: 2);
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);

        // Pick the highest-priority goal whose plan is achievable.
        GoapGoal targetGoal = _cachedIdleGoal;
        if (hasUnfilledHarvestTask || hasResourcesToDeposit) targetGoal = _cachedHarvestGoal;
        else if (hasUnfilledWaterTask && hasWateringCanAvailable) targetGoal = _cachedWaterGoal;
        else if (hasUnfilledPlantTask && hasMatchingSeedInStorage) targetGoal = _cachedPlantGoal;

        _currentGoal = targetGoal;

        // Pre-filter actions by IsValid.
        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            if (_availableActions[i].IsValid(_worker)) _scratchValidActions.Add(_availableActions[i]);
        }

        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
        }
    }

    private static bool HasAvailableTask<T>(FarmingBuilding farm) where T : BuildingTask
    {
        if (farm == null || farm.TaskManager == null) return false;
        for (int i = 0; i < farm.TaskManager.AvailableTasks.Count; i++)
        {
            if (farm.TaskManager.AvailableTasks[i] is T) return true;
        }
        return false;
    }

    private static bool StorageHasItem(StorageFurniture storage, ItemSO item)
    {
        if (storage == null) return false;
        for (int i = 0; i < storage.ItemSlots.Count; i++)
        {
            var slot = storage.ItemSlots[i];
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == item) return true;
        }
        return false;
    }

    public override bool CanExecute() => base.CanExecute() && _workplace is FarmingBuilding;

    public override bool HasWorkToDo()
    {
        if (_workplace is not FarmingBuilding farm) return false;
        // Work to do if any task type has open work.
        return HasAvailableTask<HarvestResourceTask>(farm)
            || HasAvailableTask<WaterCropTask>(farm)
            || HasAvailableTask<PlantCropTask>(farm);
    }

    public override List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(6, 18, ScheduleActivity.Work, 10)
        };
    }

    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);
        if (workplace is FarmingBuilding farm) farm.AddEmployee(worker);
    }

    public override void Unassign()
    {
        if (_workplace is FarmingBuilding farm && _worker != null) farm.RemoveEmployee(_worker);

        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
    }
}
```

(Add a small helper to FarmingBuilding to support `HasAnySeedForUnclaimedPlantTask` — walks unclaimed PlantCropTasks, returns true if any has a seed in storage.)

### Step 3: Add the helper to FarmingBuilding

```csharp
    /// <summary>True if any unclaimed PlantCropTask has its corresponding seed in this
    /// building's inventory. Used by JobFarmer's worldState to gate the Plant goal.</summary>
    public bool HasAnySeedForUnclaimedPlantTask()
    {
        if (TaskManager == null) return false;
        for (int i = 0; i < TaskManager.AvailableTasks.Count; i++)
        {
            if (TaskManager.AvailableTasks[i] is PlantCropTask pct && pct.Crop != null)
            {
                for (int j = 0; j < pct.Crop.HarvestOutputs.Count; j++)
                {
                    var entry = pct.Crop.HarvestOutputs[j];
                    if (entry.Item is SeedSO seedSO && seedSO.CropToPlant == pct.Crop)
                    {
                        if (GetItemCount(seedSO) > 0) return true;
                    }
                }
            }
        }
        return false;
    }
```

### Step 4: Commit

```bash
git add Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs
git commit -m "feat(jobs): JobFarmer — GOAP-driven plant/water/harvest cycle

Mirrors JobHarvester shape: cached goals, scratch worldState dict,
fresh action instances per plan, ExecuteIntervalSeconds=0.3f.

5 goals (priority high→low):
  HarvestMatureCells → HarvestResources → DepositResources
  WaterDryCells     → FetchTool(WateringCan) → WaterCrop → ReturnTool
  PlantEmptyCells   → FetchSeed → PlantCrop
  DepositResources  → DepositResources (bag full)
  Idle              → IdleInBuilding

Watering can chain reuses Plan 1's GoapAction_FetchToolFromStorage /
ReturnToolToStorage with the building's _toolStorageFurniture as source.
Per-task pattern: fetch can on demand, return after each WaterCrop.

Schedule 6h-18h (slightly later than Harvester's 6-16h to allow evening
watering passes).

Part of: farmer-integration plan, Task 9/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Smoketest + Documentation

**Files:**
- Create: `docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md`
- Create: `.agent/skills/job-farmer/SKILL.md`
- Create: `wiki/systems/job-farmer.md`
- Modify: `wiki/systems/farming.md` (change-log)
- Modify: `wiki/systems/jobs-and-logistics.md` (change-log)

### Step 1: Smoketest

Path: `docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md`

Cover:
- Setup: build a `FarmingBuilding` with `_cropsToGrow=[Crop_Wheat]`, drop a deposit zone, drop 1+ farming-area zones (multi-zone test), drop a `StorageFurniture` as `_toolStorageFurniture` with a `WateringCan` inside, drop another `StorageFurniture` as the seed source with WheatSeed pre-stocked, drop a `ManagementFurniture` + `DisplayTextFurniture` for hiring discovery.
- **Smoke A**: Daily PlantScan registers PlantCropTask for empty cells in zones.
- **Smoke B**: Quota-driven crop selection — when 2 crops are configured, picks the most under-target.
- **Smoke C**: Multi-zone — empty cell in either zone triggers a PlantCropTask.
- **Smoke D**: WaterScan skips on rainy day (`TimeSinceLastWatered<1`).
- **Smoke E**: WaterScan respects mature/perennial crops (no task for ready CropHarvestables).
- **Smoke F**: Full plant→water→harvest cycle on a real farmer NPC.
- **Smoke G**: Per-task watering can pickup — fetch from tool storage, return after each watering.
- **Smoke H**: Punch-out gate fires if farmer ends shift carrying the can (Plan 1 gate active).
- **Smoke I**: Crop self-seeding — wheat harvest yields seed; surplus accumulates.
- **Smoke J**: BuyOrder fallback — empty seed stock + nearby supplier triggers Transporter delivery.
- **Smoke K**: Hiring path end-to-end — NPC `NeedJob` (Plan 2.5 OnNewDay), walks to Owner, applied via `InteractionAskForJob`, hired, plants.
- **Smoke L**: Multi-peer replication — Host runs farm; client sees all task state, sign updates, NPC behaviour replicate cleanly.

### Step 2: SKILL.md

Path: `.agent/skills/job-farmer/SKILL.md`

Cover: public API of `FarmingBuilding`, `JobFarmer`, `PlantCropTask` / `WaterCropTask`, the new GOAP actions, integration points with Plans 1+2+2.5, gotchas (multi-zone bounds-walk has a per-cell cost — large zones may need profiling; quota-driven crop selection vs round-robin trade-off; rain-resets-TimeSinceLastWatered interaction).

### Step 3: Wiki page

Path: `wiki/systems/job-farmer.md`

Full system page with all 10 required sections (Purpose, Responsibilities, Non-responsibilities, Key classes / files, Public API, Data flow, Dependencies, State & persistence, Known gotchas / edge cases, Open questions / TODO, Change log). Frontmatter complete (`type: system`, `primary_agent: building-furniture-specialist`, `secondary_agents: [npc-ai-specialist, harvestable-resource-node-specialist]`, `owner_code_path: Assets/Scripts/World/Buildings/CommercialBuildings/`, `depends_on: [jobs-and-logistics, farming, tool-storage, help-wanted-and-hiring, character-job, building-task-manager]`).

### Step 4: Cross-references

- `wiki/systems/farming.md`: change-log entry: `- 2026-04-30 — JobFarmer + FarmingBuilding integration shipped (Plan 3 of farmer rollout). FarmingBuilding extends HarvestingBuilding with multi-zone field designation, daily PlantScan + WaterScan, per-task WateringCan pickup via Plan 1 ToolStorage primitive. See [[job-farmer]]. — claude`
- `wiki/systems/jobs-and-logistics.md`: change-log entry: `- 2026-04-30 — JobFarmer added to JobCategory.Harvester family. PlantCropTask + WaterCropTask quest-eligibility wired to JobType.Farmer. See [[job-farmer]]. — claude`

### Step 5: Commit

```bash
git add docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md .agent/skills/job-farmer/ wiki/systems/job-farmer.md wiki/systems/farming.md wiki/systems/jobs-and-logistics.md
git commit -m "docs(farmer): smoketest + SKILL.md + wiki page + cross-refs (Task 10/10)

Captures the Farmer integration end-to-end: 12-scenario smoketest
(plant/water/harvest cycle, multi-zone, per-task watering can,
crop self-seeding, BuyOrder fallback, hiring path, multi-peer),
SKILL.md with public API of FarmingBuilding + JobFarmer + the new
GOAP actions, full wiki system page with frontmatter + 10 required
sections.

Cross-references: farming.md and jobs-and-logistics.md change-logs.

Plan 3 (Farmer integration) is now complete: all 10 tasks shipped
across commits [first SHA] → [this]. Combined with Plans 1, 2, 2.5,
the 4-plan Farmer rollout is end-to-end testable.

Part of: farmer-integration plan, Task 10/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review Checklist

**1. Spec coverage** — every section §3.1, §3.2, §3.3, §3.6, §4.1-§4.6, §6, §7, §8 of the source spec is covered by at least one task.

**2. Placeholder scan** — no TODOs / "implement appropriate handling" / etc. (Implementation tasks may need to verify exact API names — those are flagged as adapt-as-needed, not as TBD.)

**3. Type consistency** — Preconditions/Effects keys consistent across `GoapAction_FetchSeed` / `_PlantCrop` / `_WaterCrop`. Tool-storage key shape (`hasToolInHand_<name>` etc.) reused exactly per Plan 1's convention.

**4. Multi-zone refinement** — `_farmingAreaZones: List<Zone>` baked into Task 4; PlantScan + WaterScan walk all zones in Task 6.

**5. Predecessor reuse** — Plan 1 (Tool Storage) used for watering can fetch/return. Plan 2 (Help Wanted, IsHiring gates) used implicitly via inherited `IsHiring` checks. Plan 2.5 (NeedJob throttle) covers the NPC hiring discovery path. No reinvention.

---

## Acceptance Criteria

- [ ] All 10 tasks committed.
- [ ] Plan 3 smoketest (Task 10) marked Pass on a `FarmingBuilding` test scene.
- [ ] Wiki + SKILL.md updates land.
- [ ] No regressions in existing job/building/character-action/farming tests.
- [ ] **End-to-end Farmer loop validated:** an NPC autonomously walks the full plant→water→harvest→deposit cycle on a real farm, with the WateringCan correctly fetched + returned per task, and the punch-out gate firing at shift end if the can is still held.

After this plan ships, the **4-plan Farmer rollout is complete**:
- Plan 1 (Tool Storage primitive) — generic infrastructure.
- Plan 2 (Help Wanted + Owner Hiring) — discovery + access control.
- Plan 2.5 (Management Furniture + Application Flow) — UX refinement + perf.
- Plan 3 (Farmer integration) — first consumer of all three.

Future Phase 2 work (deferred per spec §16): retrofit Woodcutter/Miner/Forager/Transporter onto Tool Storage with shift-long pickup pattern; Bag → carry-capacity bonus; tool durability; NPC owner GOAP for hiring; multi-vacancy Apply sub-menu; persistence of `IsHiring` + `_displayText` in `BuildingSaveData`.
