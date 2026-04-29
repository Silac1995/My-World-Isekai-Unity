# Farmer Job + Tool Storage Primitive — Design Spec

**Date:** 2026-04-29
**Status:** Draft
**Branch:** multiplayyer
**Author:** Claude (with Kevin)

---

## 1. Problem Statement

The project has a complete farming substrate (`CropSO`, `Harvestable`/`CropHarvestable`, `FarmGrowthSystem`, `CharacterAction_PlaceCrop`, `CharacterAction_WaterCrop`) and a complete jobs/logistics cycle (`Job` + `CommercialBuilding` + `BuildingLogisticsManager` + GOAP planner + `BuildingTaskManager` blackboard). The two layers do not yet meet — there is no NPC role that closes the **plant → water → harvest → ship** loop on a designated farm field.

`JobType.Farmer = 12` already exists in the enum and is referenced by [WageSystemService.cs:124](../../Assets/Scripts/World/Jobs/Wages/WageSystemService.cs#L124), [GoapAction_DepositResources.cs:328](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs#L328), and [CommercialBuilding.cs:1403](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs#L1403) — i.e. the wage system, deposit credit calculator, and quest eligibility map all already classify Farmer as a piece-work harvester family. No Job class, building class, GOAP actions, or task types exist behind the slot.

This spec defines the **Farmer** job + **FarmingBuilding** workplace, and along the way introduces a generic **Tool Storage** primitive (`_toolStorageFurniture` on `CommercialBuilding`, two new GOAP actions, item-ownership marker, punch-out gate). The Tool Storage primitive is built generically but **only consumed by `JobFarmer` in v1** — Phase 2 will retrofit the existing harvester / transporter jobs onto the same primitive with a shift-long pickup pattern.

**Closes:**
- The "logistic cycle" loop for crops — seeds flow IN via existing `BuyOrder` chain, produce flows OUT via existing harvest deposit chain.
- Crop self-seeding via `CropSO._harvestOutputs` extension — every harvest yields produce + a small seed amount, making the loop self-sustaining at steady state.
- A new management-gameplay layer: tool storage stocking determines worker throughput. More watering cans = more parallel watering; an empty tool storage stalls work and triggers a `BuyOrder` to resupply.

**Explicitly out of scope (deferred to Phase 2):**
- Retrofitting `JobHarvester` / `JobTransporter` / `JobBlacksmith` onto the Tool Storage primitive (shift-long pickup pattern with `Job.OnShiftStart` / `OnShiftEnd` hooks).
- Bag → carry-capacity bonus model.
- Tool durability / breakage / maintenance.
- A dedicated "tool rack" furniture mesh — v1 marks any existing `StorageFurniture` as the tool storage via a designer reference.
- Crop rotation logic (per-cell or per-row scheduling).
- Plowing as a separate action (matches the v1 farming spec — plant action plows automatically).
- A new "seed supplier" building — v1 relies on either another `FarmingBuilding` with surplus, a designer-stocked `ShopBuilding` listing seeds, or `VirtualResourceSupplier` macro-sim fallback.

---

## 2. Architecture Overview

**Approach:** subclass + reuse. `FarmingBuilding` extends `HarvestingBuilding` (the existing harvester workplace) and inherits zone scanning, deposit zone, employee management, the BuildingLogisticsManager, `RegisterHarvestedItem`, and `IsResourceAtLimit`. The Farmer role adds two scan paths (plant, water) and four GOAP actions on top.

```
Layer 5 — Punch-out gate     CharacterJob.CanPunchOut() + UI toast for player workers
                             CharacterJob.OnUnassign auto-return for fired/quit workers
Layer 4 — Tool ownership     ItemInstance.OwnerBuildingId (string GUID, persisted)
                             stamped on Fetch, cleared on Return
Layer 3 — Tool storage       CommercialBuilding._toolStorageFurniture : StorageFurniture
                             generic GoapAction_FetchToolFromStorage / _ReturnToolToStorage
Layer 2 — Farmer GOAP        JobFarmer (planner + cached goals + scratch worldState)
                             + GoapAction_FetchSeed / _PlantCrop / _WaterCrop
                             + reused HarvestResources / DepositResources / IdleInBuilding
Layer 1 — Building & tasks   FarmingBuilding : HarvestingBuilding
                             + PlantCropTask, WaterCropTask
                             + override IStockProvider.GetStockTargets (seeds, can, produce)
                             + daily PlantScan / WaterScan from TimeManager.OnNewDay
```

```
                     ┌───────────────────────────────────────────┐
                     │ TimeManager.OnNewDay (server)             │
                     └─────────────┬─────────────────────────────┘
                                   ▼
            ┌──────────────────────────────────────┐
            │ FarmingBuilding.OnNewDay (overrides) │
            │  ├─ ScanHarvestingArea (inherited)   │  ← finds mature CropHarvestables
            │  ├─ PlantScan (NEW)                  │  ← finds empty cells in zone
            │  └─ WaterScan (NEW)                  │  ← finds dry planted cells
            └──────────────────────────────────────┘
                                   ▼
                    BuildingTaskManager registers:
                    HarvestResourceTask  (existing, fires when CropHarvestable matures)
                    PlantCropTask        (NEW)
                    WaterCropTask        (NEW)
                                   ▼
                    JobFarmer.PlanNextActions (per worker, 0.3s cadence)
                                   ▼
   GOAP plan (priority-ordered goals):
   1. HarvestMatureCells       → HarvestResources → DepositResources
   2. WaterDryCells            → FetchTool(Can) → WaterCrop → ReturnTool(Can)
   3. PlantEmptyCells          → FetchSeed(crop) → PlantCrop
   4. DepositResources         → DepositResources (when bag full)
   5. Idle                     → IdleInBuilding

           Logistics (existing plumbing — zero new code):
                                   ▼
   FarmingBuilding.IStockProvider.GetStockTargets:
   ├─ output: produce items (Wheat, Flower, Apple)        max-cap → throttle harvest
   ├─ input:  seed items (WheatSeed, FlowerSeed, …)       min-trigger → BuyOrder
   └─ input:  WateringCan                                 min-trigger → BuyOrder
                                   ▼
   LogisticsStockEvaluator.CheckStockTargets fires BuyOrder for any deficit
                                   ▼
   Existing supplier-lookup (any IStockProvider listing the item) routes Transporter
```

**Network model.** Server is sole authority over `BuildingTaskManager`, `BuildingLogisticsManager`, GOAP planning, `CharacterAction_PlaceCrop`, `CharacterAction_WaterCrop`, and `ItemInstance.OwnerBuildingId` mutation. Clients see effects through existing replication: `CropHarvestable` is a `NetworkObject`, `_inventory` mutations replicate via existing `CommercialBuilding` flow, and the punch-out blocked-toast for player workers is sent via a single ClientRpc to the owning client.

**Save/load.** All farming state (cell `IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `Moisture`) already serialises through `TerrainCellSaveData` (see [farming-plot-system spec](2026-04-28-farming-plot-system-design.md) §9). The Tool Storage primitive adds **one new save field**: `ItemInstance.OwnerBuildingId` (string). All other state derives from existing serialised state (storage furniture inventory, building employee list, `CharacterJob` assignments).

---

## 3. Data Model

### 3.1 `FarmingBuilding : HarvestingBuilding`

```csharp
public class FarmingBuilding : HarvestingBuilding
{
    public override BuildingType BuildingType => BuildingType.Farm;

    [Header("Farming Config")]
    [SerializeField] private List<CropSO> _cropsToGrow = new List<CropSO>();
    [SerializeField] private int _farmerCount = 2;
    [SerializeField] private string _farmerJobTitle = "Farmer";

    [Header("Tool & Seed Stock Targets")]
    [SerializeField] private int _seedMinStock = 5;
    [SerializeField] private int _seedMaxStock = 20;
    [SerializeField] private int _wateringCanMaxStock = 2;   // typically == _farmerCount
    [SerializeField] private WateringCanSO _wateringCanItem; // designer reference; null = no watering loop
}
```

**Auto-derivation rules** (no extra designer config beyond `_cropsToGrow`):

| Stock target type | Source | Min | Max |
|---|---|---|---|
| Produce item (e.g. Wheat) | each `CropSO.HarvestOutputs[*].Item` that is the **first** (primary) output | 0 | inherited `maxQuantity` from `_wantedResources` (or default 50) |
| Seed item (e.g. WheatSeed) | each `CropSO.HarvestOutputs[*].Item` whose ItemSO is a `SeedSO` AND `seed.CropToPlant == thisCrop` | `_seedMinStock` | `_seedMaxStock` |
| WateringCan | `_wateringCanItem` | 1 | `_wateringCanMaxStock` |

Designer-friendly v1: a Wheat farm authoring is just `_cropsToGrow = [Crop_Wheat]`, set `_farmerCount`, drop a `StorageFurniture` in the building and reference it as `_toolStorageFurniture`, drop a deposit zone, drop a harvesting-area zone covering the field, done.

### 3.2 `JobFarmer : Job`

Mirrors `JobHarvester` shape exactly:

```csharp
[System.Serializable]
public class JobFarmer : Job
{
    public override JobCategory Category => JobCategory.Harvester;
    public override JobType Type => JobType.Farmer;
    public override float ExecuteIntervalSeconds => 0.3f;
    public override List<ScheduleEntry> GetWorkSchedule() =>
        new List<ScheduleEntry> { new ScheduleEntry(6, 18, ScheduleActivity.Work, 10) };

    // GOAP plumbing — same pattern as JobHarvester (cached goals, scratch worldState dict,
    // per-plan fresh action instances, _scratchValidActions pre-filter on IsValid).
    private GoapGoal _cachedHarvestGoal;
    private GoapGoal _cachedWaterGoal;
    private GoapGoal _cachedPlantGoal;
    private GoapGoal _cachedDepositGoal;
    private GoapGoal _cachedIdleGoal;
    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(12);
    private List<GoapAction> _availableActions;
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;
}
```

Goal selection (priority order, planner picks the highest with an achievable plan):

| Priority | Goal name | Desired state | Triggers when |
|---|---|---|---|
| 1 (top) | `HarvestMatureCells` | `hasDepositedResources=true` | `HarvestResourceTask` available AND has free space OR carrying mature produce |
| 2 | `WaterDryCells` | `hasWateredCell=true` | `WaterCropTask` available AND `WateringCan` available in tool storage OR in hand |
| 3 | `PlantEmptyCells` | `hasPlantedCrop=true` | `PlantCropTask` available AND a matching `SeedSO` exists in storage |
| 4 | `DepositResources` | `hasDepositedResources=true` | Carrying produce/seeds AND inventory full OR end-of-shift |
| 5 (bottom) | `Idle` | `isIdling=true` | None of the above |

### 3.3 `PlantCropTask` and `WaterCropTask` (BuildingTask subclasses)

```csharp
public class PlantCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }
    public CropSO Crop { get; }
    public override IInteractableObject Target => null; // cell-targeted, not interactable
    public PlantCropTask(int x, int z, CropSO crop) { CellX = x; CellZ = z; Crop = crop; }
}

public class WaterCropTask : BuildingTask
{
    public int CellX { get; }
    public int CellZ { get; }
    public WaterCropTask(int x, int z) { CellX = x; CellZ = z; }
}
```

**Quest eligibility (existing `CommercialBuilding.DoesJobTypeAcceptQuest`).** v1 adds:
- `PlantCropTask` → `JobType.Farmer`
- `WaterCropTask` → `JobType.Farmer`

`HarvestResourceTask` already accepts `JobType.Farmer` as of [CommercialBuilding.cs:1403](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs#L1403).

### 3.4 Tool Storage primitive — `CommercialBuilding` extension

```csharp
public abstract class CommercialBuilding : Building, IInteractableSpawner, …
{
    [Header("Tool Storage")]
    [SerializeField] private StorageFurniture _toolStorageFurniture; // designer reference; null = no tool storage

    public StorageFurniture ToolStorage => _toolStorageFurniture;
    public bool HasToolStorage => _toolStorageFurniture != null;

    /// <summary>
    /// Returns true if the worker currently carries any item stamped with this building's
    /// BuildingId on its OwnerBuildingId field (i.e. tools fetched from the tool storage that
    /// haven't been returned). Server-authoritative.
    /// </summary>
    public bool WorkerCarriesUnreturnedTools(Character worker, out List<ItemInstance> unreturned);
}
```

The `_toolStorageFurniture` is a plain `StorageFurniture` reference — no new furniture type. Designer drops any chest/cupboard/barrel inside the building and assigns it to the `_toolStorageFurniture` slot. Multiple buildings can share the *visual style* but each must reference its own physical storage instance.

### 3.5 `ItemInstance.OwnerBuildingId` — tool ownership marker

```csharp
public class ItemInstance
{
    // … existing fields …

    /// <summary>
    /// GUID of the CommercialBuilding whose tool storage owns this item. Set by
    /// GoapAction_FetchToolFromStorage on pickup, cleared by GoapAction_ReturnToolToStorage on
    /// return. Persisted across save/load. Used by CharacterJob.CanPunchOut to gate shift end.
    /// Empty / null = item is not owned by any tool storage (player-bought tools, dropped loot,
    /// etc.).
    /// </summary>
    public string OwnerBuildingId { get; set; }
}
```

**Persistence:** added to `ItemInstanceSaveData` as a single `string OwnerBuildingId` field. Existing `ItemInstance.ToSaveData` / `FromSaveData` patterns extended in lockstep. Items in storage furniture inherit the field at save and replay it at load — no rebuild required.

**Lookup safety:** the marker is a soft reference (string GUID). If the owning building no longer exists at punch-out time (destroyed, demolished), the gate auto-passes — see §5.4.

---

## 4. New GOAP Actions

All new actions follow the existing pattern: subclass `GoapAction` (or `GoapAction_ExecuteCharacterAction` where a `CharacterAction` is wrapped), declare `Preconditions` and `Effects` dicts, override `IsValid(worker)`, `OnEnter`, `Execute`, `Exit`. Cost values are tuned so the planner picks correct chains.

### 4.1 `GoapAction_FetchSeed`

```
Cost: 1
Preconditions:
  hasSeedInHand = false
  hasEmptyCellWithSeedAvailable = true
Effects:
  hasSeedInHand = true
IsValid:
  - building has at least 1 SeedSO matching any unfilled PlantCropTask in storage
Body:
  - claim the matching PlantCropTask (so two farmers don't fetch for the same cell)
  - walk to the building's seed source storage (uses existing FindStorageFurnitureForItem helper)
  - take 1 SeedSO matching the claimed task's CropSO
  - equip in HandsController
```

### 4.2 `GoapAction_PlantCrop` (wraps `CharacterAction_PlaceCrop`)

```
Cost: 1
Preconditions:
  hasSeedInHand = true
  hasClaimedPlantTask = true
Effects:
  hasPlantedCrop = true
  hasSeedInHand = false   ← seed consumed by CharacterAction_PlaceCrop.OnApplyEffect
IsValid:
  - claimed PlantCropTask still valid (cell still empty)
  - SeedSO in hand matches the claimed task's CropSO
Body:
  - walk to GridToWorld(cell)
  - queue CharacterAction_PlaceCrop(actor, map, cellX, cellZ, crop) on _worker.CharacterActions
  - on completion: release task claim, mark plan complete
```

### 4.3 `GoapAction_FetchToolFromStorage` (generic, ItemSO-parameterized)

```
Cost: 1
Constructor: GoapAction_FetchToolFromStorage(CommercialBuilding building, ItemSO toolItem)
Preconditions:
  hasToolInHand[toolItem] = false
  toolNeededForTask[toolItem] = true
Effects:
  hasToolInHand[toolItem] = true
IsValid:
  - building.ToolStorage != null
  - building.ToolStorage.Inventory.Any(i => i.ItemSO == toolItem)
Body:
  - walk to building.ToolStorage (via InteractableObject.IsCharacterInInteractionZone gate)
  - take 1 ItemInstance matching toolItem from ToolStorage
  - stamp instance.OwnerBuildingId = building.BuildingId
  - equip in HandsController (or place in inventory if hands occupied — handled by CharacterEquipment.Receive)
```

### 4.4 `GoapAction_WaterCrop` (wraps `CharacterAction_WaterCrop`)

```
Cost: 1
Preconditions:
  hasToolInHand[WateringCanSO] = true
  hasClaimedWaterTask = true
Effects:
  hasWateredCell = true
IsValid:
  - claimed WaterCropTask still valid (cell still dry, still planted, not mature)
  - WateringCan in hand
Body:
  - walk to GridToWorld(cell)
  - queue CharacterAction_WaterCrop(actor, map, cellX, cellZ, can.MoistureSetTo)
  - on completion: release task claim, mark plan complete
```

### 4.5 `GoapAction_ReturnToolToStorage` (generic, ItemSO-parameterized)

```
Cost: 1
Constructor: GoapAction_ReturnToolToStorage(CommercialBuilding building, ItemSO toolItem)
Preconditions:
  hasToolInHand[toolItem] = true
  taskCompleteForTool[toolItem] = true
Effects:
  hasToolInHand[toolItem] = false
  toolReturned[toolItem] = true
IsValid:
  - worker carries an ItemInstance matching toolItem with OwnerBuildingId == building.BuildingId
  - building.ToolStorage != null AND has free slot
Body:
  - walk to building.ToolStorage
  - place the instance in ToolStorage (via existing StorageFurniture.AddItem)
  - clear instance.OwnerBuildingId before placing
  - clear from HandsController if it was in hand
```

### 4.6 GOAP plan composition examples

**Plan: water a dry cell**
```
FetchToolFromStorage(WateringCan)
  → WaterCrop(cellX, cellZ)
  → ReturnToolToStorage(WateringCan)
  → [replan: more WaterCropTasks? loop. else: next priority.]
```

**Plan: plant an empty cell**
```
FetchSeed(WheatSeed)
  → PlantCrop(cellX, cellZ, Crop_Wheat)
  → [replan: more PlantCropTasks? loop. else: next priority.]
```

**Plan: harvest a mature cell**
```
HarvestResources (existing — picks closest claimable HarvestResourceTask)
  → DepositResources (existing — drops in deposit zone)
  → [replan]
```

**Plan: end of shift with tool still held (edge case)**
```
ReturnAllTools (forced replan when CharacterJob.CanPunchOut returns false)
  → ReturnToolToStorage(WateringCan)   ← per held tool
  → [retry punch-out]
```

---

## 5. Punch-Out Gate

### 5.1 `CharacterJob.CanPunchOut() → (bool, string reason)`

Server-authoritative. Called automatically when `CharacterSchedule` transitions out of a `Work` slot AND when `CharacterJob.QuitJob` / `Unassign` runs.

```csharp
public (bool canPunchOut, string reasonIfBlocked) CanPunchOut()
{
    if (_workplace == null) return (true, null);
    if (!_workplace.WorkerCarriesUnreturnedTools(_worker, out var unreturned))
        return (true, null);

    string toolNames = string.Join(", ", unreturned.Select(t => t.ItemSO.ItemName));
    return (false, $"Return tools to the tool storage before punching out: {toolNames}.");
}
```

### 5.2 Player worker path

- `CharacterSchedule` calls `CanPunchOut()` on schedule transition. If blocked:
  - The `Work` slot persists past its scheduled end (worker keeps showing as on-shift).
  - A `ClientRpc` to the owning client raises a UI toast with the reason text. The toast is rate-limited (one per 30s real-time) to avoid spam if the player ignores it.
  - Re-checked on every `CharacterAction` completion + every schedule tick. As soon as tools are returned, the schedule transition completes naturally.
- The player retains agency: they can manually walk to the tool storage and drop the tool in via `Interact` on the storage furniture (existing player flow). The `OwnerBuildingId` clears on the same drop event as the NPC path because both call into the same `StorageFurniture.AddItem(instance)` → which detects an `OwnerBuildingId` matching the owning building and clears it.

### 5.3 NPC worker path

- Same `CanPunchOut()` gate. If blocked:
  - `JobFarmer.PlanNextActions` injects a forced `ReturnAllTools` goal (priority +∞ for one tick) and replans.
  - The plan walks the worker to the tool storage and returns each held tool one-by-one.
  - Once `CanPunchOut() == true`, the schedule transitions out of `Work` normally.

### 5.4 Edge cases

| Situation | Behavior |
|---|---|
| Tool storage destroyed mid-shift | `WorkerCarriesUnreturnedTools` matches by `OwnerBuildingId` against the *building*, not the *furniture*. If `_toolStorageFurniture` was destroyed but the building still exists, the tool is still flagged but cannot be returned — fallback: gate auto-passes after one retry attempt + a `Debug.LogWarning`. The tool then stays in the worker's inventory with `OwnerBuildingId` cleared (treated as "salvaged"). |
| Tool storage full when returning | Gate stays blocked. Player toast: "Tool storage is full — make space." NPC replans with `IdleInBuilding` until space appears (typically when another worker fetches a tool, or a `BuyOrder` cleanup pulls excess). |
| Worker carries tool into another map (portal) | `OwnerBuildingId` persists on the item but the gate only checks against `_workplace.BuildingId` of the *current* job. Crossing back to the original map and trying to punch out re-blocks correctly. Phase 2 may add a "stolen tool" reputation hook. |
| Worker fired (`CharacterJob.Unassign` from leadership) | `Unassign` runs an auto-`ReturnAllTools` GOAP plan synchronously if reachable. If unreachable (server shutdown, building destroyed), tools clear `OwnerBuildingId` and stay in worker inventory. |
| Worker dies mid-shift | `Character.OnIncapacitated` triggers `CharacterJob.Unassign` → same auto-return path. If the corpse despawns before return, tools drop to the ground as normal `WorldItem` with `OwnerBuildingId` cleared. |
| Building demolished while worker holds tools | `OwnerBuildingId` references a now-nonexistent BuildingId. Next `CanPunchOut()` check: `_workplace == null` → gate passes. Tools stay in worker inventory with stale `OwnerBuildingId`. v1 leaves the stale value (cosmetic only); a periodic cleanup is Phase 2. |
| Worker carries tool to sleep at a different map's bed | Cosmetic problem only — no work to punch out from. Save/load preserves the marker. |

---

## 6. Logistics Cycle Integration

### 6.1 Stock targets

`FarmingBuilding` overrides `IStockProvider.GetStockTargets()` to declare both inputs and outputs in one list. The existing `LogisticsStockEvaluator.CheckStockTargets` reads this and triggers `BuyOrder`s for any deficit.

```
For each CropSO in _cropsToGrow:
  - For each (Item, Count) in CropSO.HarvestOutputs:
      - If Item is a SeedSO whose CropToPlant == thisCrop:
          → INPUT target: (Item, MinStock=_seedMinStock, MaxStock=_seedMaxStock)
      - Else (produce):
          → OUTPUT target: (Item, MinStock=0, MaxStock=_wantedResources.maxQuantity ?? 50)

If _wateringCanItem != null:
  → INPUT target: (WateringCanItem, MinStock=1, MaxStock=_wateringCanMaxStock)
```

### 6.2 Crop self-seeding (designer config, zero new code)

Each `CropSO._harvestOutputs` includes both produce and matching seed:

```yaml
# Crop_Wheat.asset
_harvestOutputs:
  - Item: { fileID: ItemSO_Wheat }
    Count: 3
  - Item: { fileID: SeedSO_WheatSeed }   # ← seeds yield from the harvest
    Count: 1
```

`HarvestingBuilding.RegisterHarvestedItem` already loops through `_wantedResources` and adds matching items to `_inventory`. Because seeds are now declared as wanted (via `IStockProvider.GetStockTargets()` input target above), the existing `RegisterHarvestedItem` code adds them to inventory unchanged — **zero new code**.

**Steady-state rate.** If a 10-cell wheat farm averages 3 harvests per cell per season, seed yield is 30 seeds. Replanting consumes 10 seeds (one per cell). The farm produces 20 surplus seeds + 90 wheat per season — `BuyOrder`s for seeds only fire if a crop failure burns through the buffer.

### 6.3 Fallback supplier lookup

Existing `LogisticsStockEvaluator.FindSupplierFor(item)` walks every `CommercialBuilding` in the map's `BuildingPlacementManager` and returns the first one whose `IStockProvider.GetStockTargets()` advertises the item (with stock to spare). For seeds this means:

| Supplier candidate | Behavior |
|---|---|
| Another `FarmingBuilding` with surplus seeds | Natural supplier — neighbouring farms trade seeds via `BuyOrder` + `Transporter` chain. No new code. |
| A `ShopBuilding` with `SeedSO` in `ItemsToSell` | Designer-stocked supplier. No new code. |
| `VirtualResourceSupplier` (macro-sim fallback) | Existing offline fulfilment path. No new code. |

If no supplier exists, the `BuyOrder` enqueues but stays unplaced (existing `RetryUnplacedOrders` cycle). The farm idles on the seed-blocked work but continues whatever else it can do (water, harvest mature cells).

### 6.4 Watering can supply

Same flow as seeds. A single `BuyOrder` for `WateringCan` fires once at building activation (if storage starts empty). After delivery, the can sits in `_toolStorageFurniture` indefinitely until destroyed/lost. v1 has no durability — a single can lasts forever.

---

## 7. Daily Scans

Both `PlantScan` and `WaterScan` run on `TimeManager.OnNewDay`, mirroring the pattern of `HarvestingBuilding.ScanHarvestingArea`. They iterate cells inside `_harvestingAreaZone` exactly once per day and register tasks on `BuildingTaskManager`.

### 7.1 `PlantScan`

```csharp
foreach cell in zone:
    if (!cell.IsPlowed && string.IsNullOrEmpty(cell.PlantedCropId))
        // V1: plant action plows automatically — empty + non-plowed is fine
        var crop = SelectCropForCell(_cropsToGrow);  // quota-driven pick
        if (crop == null) continue;                  // no seeds in stock for any crop
        if (!HasSeedInStorage(crop)) continue;       // designer-set quota for this crop hit
        _taskManager.RegisterTask(new PlantCropTask(cell.X, cell.Z, crop));
```

**Quota-driven crop selection.** `SelectCropForCell` returns the `CropSO` whose produce stock is **most under target** (max - current is largest), tie-broken by ascending `CropSO._id`. This means a farm with `[Wheat, Flower]` configured naturally biases planting toward whichever produce is currently most deficient.

### 7.2 `WaterScan`

```csharp
foreach cell in zone:
    if (string.IsNullOrEmpty(cell.PlantedCropId)) continue;
    var crop = CropRegistry.Get(cell.PlantedCropId);
    if (crop == null) continue;
    if (cell.GrowthTimer >= crop.DaysToMature) continue;        // mature/perennial — don't water
    if (cell.TimeSinceLastWatered < 1f) continue;               // freshly watered (or it rained — TerrainWeatherProcessor resets to 0 on rain)
    if (cell.Moisture >= crop.MinMoistureForGrowth) continue;   // already wet enough from rain
    _taskManager.RegisterTask(new WaterCropTask(cell.X, cell.Z));
```

**Rain interaction.** Rain feeds `cell.Moisture` and resets `TimeSinceLastWatered` via `TerrainWeatherProcessor` (existing). On a rainy day, `WaterScan` registers no tasks → farmers spend the day on plant + harvest. This is "free drought insurance" — the farmer adds work only on dry days.

### 7.3 Task lifecycle

- Tasks remain on `BuildingTaskManager` until claimed by a farmer's GOAP plan (via `ClaimBestTask<T>`). Existing pattern.
- Tasks are auto-invalidated when their target cell state no longer matches:
  - `PlantCropTask`: cell now has a `PlantedCropId` (someone else planted, or another farm spawned a wild crop).
  - `WaterCropTask`: cell `TimeSinceLastWatered < 1f` (someone else watered, or rain arrived).
- `BuildingTaskManager.PruneInvalidTasks()` runs once per day after the new day's scan to clear any stale tasks left over from the previous day.

---

## 8. Player ↔ NPC Parity

Per rule #22, every gameplay effect must route through `CharacterAction`. The Farmer system inherits this for free:

| Action | NPC path | Player path |
|---|---|---|
| Plant a seed | `GoapAction_PlantCrop` → `CharacterAction_PlaceCrop` | `CropPlacementManager.StartPlacement` → `RequestPlaceCropServerRpc` → `CharacterAction_PlaceCrop` |
| Water a cell | `GoapAction_WaterCrop` → `CharacterAction_WaterCrop` | `CropPlacementManager.StartWatering` → `RequestWaterCellServerRpc` → `CharacterAction_WaterCrop` |
| Harvest a mature crop | `GoapAction_HarvestResources` → `CharacterHarvestAction` | Tap-E on `CropHarvestable` → `CharacterHarvestAction` |
| Fetch tool from storage | `GoapAction_FetchToolFromStorage` → `StorageFurniture.RemoveItem` + stamp `OwnerBuildingId` | Player walks to storage → `Interact` → take from menu → existing `StorageFurniture.RemoveItem` (no stamp — player isn't a worker) |
| Return tool to storage | `GoapAction_ReturnToolToStorage` → clear `OwnerBuildingId` + `StorageFurniture.AddItem` | Player walks to storage → drop in → existing `StorageFurniture.AddItem` (auto-clears matching `OwnerBuildingId`) |
| Punch out | `CharacterSchedule` slot transition | `CharacterSchedule` slot transition |

**Player-as-worker case.** A player who has taken a job at a `FarmingBuilding` (via `CommercialBuilding.AskForJob`) goes through the same `CanPunchOut` gate as an NPC. This mirrors the existing employment model — no special-casing.

---

## 9. Persistence

### 9.1 Existing state (zero new code)

- `TerrainCell` fields (`IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`, `Moisture`) — covered by [farming-plot-system spec](2026-04-28-farming-plot-system-design.md) §9.
- `CommercialBuilding._inventory`, `_activeOrders`, `_placedBuyOrders`, etc. — existing save/load.
- `BuildingTaskManager` tasks — re-derived via daily scans on map wake (`PostWakeSweep` for crops, `OnNewDay` for plant/water tasks).
- `CharacterJob.JobAssignment` — existing save/load.

### 9.2 New state

| Field | Owner | Save format |
|---|---|---|
| `ItemInstance.OwnerBuildingId` | `ItemInstance` | string (BuildingId GUID), persisted via `ItemInstanceSaveData` |
| `_toolStorageFurniture` | `CommercialBuilding` (designer-set) | scene reference, no runtime mutation, no save needed |

**`ItemInstance.OwnerBuildingId` migration.** Existing save files have no field. Default value on load is `null` → all pre-existing items load as "not owned by any building" → punch-out gate passes for every prior save. No migration script needed.

### 9.3 Hibernation

When a map hibernates:
- All `CharacterJob` workers get despawned + re-serialised into `HibernatedNPCData`. Tools in their inventory persist with `OwnerBuildingId` intact.
- On map wake, NPCs re-spawn at saved positions with their inventory restored. The first `CharacterSchedule` tick re-runs `CanPunchOut`. If still on shift, the worker resumes; if shift ended during hibernation, the gate fires its return-tools replan.
- `BuildingTaskManager` tasks do NOT persist through hibernation — they re-register on the first `OnNewDay` post-wake.

### 9.4 Save-load worker resumption

A worker mid-water-task at save → load:
1. `CropHarvestable`s reconstructed via `FarmGrowthSystem.PostWakeSweep`.
2. `BuildingTaskManager` tasks re-registered by next `OnNewDay`.
3. Worker's `ItemInstance` carries `OwnerBuildingId` for the held watering can.
4. On first `JobFarmer.Execute` tick post-load, worldState shows `hasToolInHand[WateringCan]=true`. Planner picks `WaterDryCells` goal directly (skipping fetch).
5. Plan: `WaterCrop → ReturnToolToStorage`. Resumes correctly.

---

## 10. Network Architecture

Per `NETWORK_ARCHITECTURE.md` and rule #18, server is sole authority over:
- `BuildingTaskManager` task registration / claim / completion.
- `BuildingLogisticsManager` `BuyOrder` lifecycle.
- `CharacterAction_PlaceCrop` / `_WaterCrop` cell mutations.
- `ItemInstance.OwnerBuildingId` mutation.
- `CharacterJob.CanPunchOut` evaluation.
- `JobFarmer.PlanNextActions` execution.

Clients receive effects via:
- `MapController.NotifyDirtyCells` ClientRpc — terrain cell deltas (existing).
- `CropHarvestable` `NetworkObject` spawn / `NetVar` sync (existing).
- `CommercialBuilding._inventory` replication (existing — items added via `RegisterHarvestedItem`).
- `StorageFurniture` slot-state replication for the tool storage (existing — same as any other chest).
- A new minimal ClientRpc on `CommercialBuilding`: `Server_NotifyPunchOutBlocked(ulong workerClientId, string reason)` — fires only when a player worker hits a blocked punch-out. Single string payload. Owning client raises a UI toast.

Per rule #19, all relationship scenarios validated:
- **Host ↔ Client (player worker case):** host runs gate → ClientRpc to client → client raises toast.
- **Client ↔ Client (two players in same farm):** server runs gate independently for each → ClientRpc fans out only to each owning client.
- **Host/Client ↔ NPC:** server-authoritative GOAP planning runs identically; clients see resulting actions via existing replication.

---

## 11. UI

### 11.1 Punch-out blocked toast (player workers)

A new minimal UI element: `UI_ToolReturnReminderToast`. Triggered by the `Server_NotifyPunchOutBlocked` ClientRpc. Shows a single line at the top of the screen for ~3 seconds, with the reason string. Rate-limited to one toast per 30s real-time (the message auto-suppresses if the player is mid-action).

Priority decision: **no menu, no actionable buttons** — the player already has an interaction model (walk to chest, drop tool). The toast is a hint, not a workflow.

### 11.2 Tool storage furniture display

Reuses the existing `StorageVisualDisplay` renderer. The fact that a chest is the building's tool storage is invisible to the UI in v1 — the player sees a normal chest with the tools inside. (Phase 2 may add a small icon overlay or rename the inspect title from "Storage" to "Tool Storage" if `_toolStorageFurniture` matches.)

### 11.3 Dev-mode inspector hooks

Following the pattern in `BuildingInspectorView`:

- `CommercialBuildingInspectorView` adds a new "Tool Storage" line: name of the referenced furniture, its current item count, and a button "Inspect Tool Storage" that pivots to the `StorageFurnitureInspectorView`.
- `CharacterInspectorView` (Inventory tab) adds a `[Tool: <BuildingName>]` annotation on every inventory slot whose `ItemInstance.OwnerBuildingId` is non-null, with hover-tooltip showing the full BuildingId GUID.

---

## 12. Testing Plan

### 12.1 Unit tests (EditMode)

| Test | Validates |
|---|---|
| `JobFarmer_PlanWater_FetchesCanThenReturns` | Water plan correctly chains FetchTool → WaterCrop → ReturnTool |
| `JobFarmer_PlanPlant_FetchesSeedThenPlants` | Plant plan correctly chains FetchSeed → PlantCrop |
| `JobFarmer_GoalPriority_HarvestBeforeWater` | Mature cells take priority over watering |
| `JobFarmer_NoSeed_SkipsPlantTask` | Planner falls through to next goal when seed stock empty |
| `JobFarmer_NoCan_SkipsWaterTask` | Planner falls through to next goal when WateringCan absent |
| `FarmingBuilding_PlantScan_RegistersTasksForEmptyCells` | Daily scan correctly registers PlantCropTask for each empty cell |
| `FarmingBuilding_WaterScan_SkipsRainyDay` | When TimeSinceLastWatered < 1, no WaterCropTask spawns |
| `FarmingBuilding_StockTargets_AutoDerivesFromCropsToGrow` | IStockProvider.GetStockTargets includes seeds, can, produce |
| `CropSelfSeeding_HarvestPopulatesSeedStock` | Harvesting wheat (which has WheatSeed in HarvestOutputs) increments seed inventory |
| `BuyOrderFiresOnSeedDeficit` | When seed stock < min, BuyOrder enqueued |
| `CharacterJob_CanPunchOut_BlocksWithUnreturnedTools` | Worker carrying OwnerBuildingId-stamped item → returns false |
| `CharacterJob_CanPunchOut_AllowsAfterReturn` | Same item with OwnerBuildingId cleared → returns true |
| `ToolStorageDestroyed_GateAutoPasses` | When _toolStorageFurniture null → gate passes |
| `WorkerFired_AutoReturnAllTools` | Unassign triggers return plan synchronously |

### 12.2 Integration tests (PlayMode, server-only)

| Test | Validates |
|---|---|
| `FarmingBuildingFullCycle_Day1ToDay7` | Plant on day 1, water day 2-3, harvest day 5 (DaysToMature=4), seed yield repopulates stock, replant day 6 |
| `TwoFarmersOneCan_NaturalContention` | Farmer A claims can → Farmer B replans to plant; Farmer A returns can → Farmer B fetches |
| `RainyDay_FarmerSkipsWatering` | TerrainWeatherProcessor resets TimeSinceLastWatered → WaterScan registers no tasks |
| `Hibernation_ReturnsTools` | Map hibernates mid-shift, unhibernates, worker still has tools, gate still works |
| `PlayerWorker_BlockedAtPunchOut_ToastShown` | Player takes farmer job, fetches can, schedule transitions out → ClientRpc fires toast |
| `BuyOrderResupply_NoSeedSupplier_ThenSupplierBuilt` | Farm starts with empty seeds, BuyOrder enqueues unplaced; player builds shop with seeds; BuyOrder finds supplier next dispatch cycle |

### 12.3 Manual playtest checklist

- [ ] Place a `FarmingBuilding` prefab in a scene, configure `_cropsToGrow=[Crop_Wheat]`, drop a deposit zone + harvesting zone covering 4×4 tiles, drop a `StorageFurniture` and assign it as `_toolStorageFurniture`, drop a watering can in the storage.
- [ ] Hire 2 farmers via debug or community placement.
- [ ] Verify day 1: farmers fetch seeds (manually pre-stock 10 wheat seeds), walk to cells, plant.
- [ ] Set weather to drought, verify day 2: one farmer fetches the can, waters, returns; the other plants/harvests.
- [ ] Wait 4 in-game days, verify wheat matures, farmers harvest, deposit; produce + seeds in inventory.
- [ ] Verify day 5+: replanting begins automatically with self-seeded stock.
- [ ] Force-empty seed stock → verify BuyOrder fires when a `ShopBuilding` exists with seeds.
- [ ] As player, take farmer job → fetch can → run to schedule end → verify blocked + toast → return can → verify normal punch-out.

---

## 13. Implementation Order (high-level — feeds into writing-plans)

1. **Tool Storage primitive (groundwork):**
   - `CommercialBuilding._toolStorageFurniture` field + `WorkerCarriesUnreturnedTools` helper.
   - `ItemInstance.OwnerBuildingId` field + save/load extension.
   - `CharacterJob.CanPunchOut` + `Unassign` auto-return.
   - `GoapAction_FetchToolFromStorage` / `_ReturnToolToStorage` (generic).
   - Player toast (`UI_ToolReturnReminderToast`) + ClientRpc.

2. **FarmingBuilding scaffolding:**
   - `FarmingBuilding : HarvestingBuilding` with `_cropsToGrow`, `_seedMinStock`, `_wateringCanItem`.
   - Auto-derived `IStockProvider.GetStockTargets()`.
   - `PlantCropTask`, `WaterCropTask`.
   - Daily `PlantScan`, `WaterScan`.

3. **JobFarmer + farmer-specific GOAP actions:**
   - `JobFarmer` (planner shape mirrored from `JobHarvester`).
   - `GoapAction_FetchSeed` (farmer-specific because it's seed-typed, not tool-typed).
   - `GoapAction_PlantCrop`, `GoapAction_WaterCrop` (wrappers around existing CharacterActions).
   - Planner goal priority: Harvest > Water > Plant > Deposit > Idle.

4. **Crop self-seeding asset config:**
   - Update `Crop_Wheat`, `Crop_Flower`, `Crop_AppleTree` `.asset` files to include their respective `SeedSO` in `_harvestOutputs`.
   - Verify `RegisterHarvestedItem` correctly populates seed stock on harvest.

5. **Quest eligibility + logging:**
   - Extend `CommercialBuilding.DoesJobTypeAcceptQuest` to map `PlantCropTask` / `WaterCropTask` → `JobType.Farmer`.
   - `JobYieldRegistry` entries for crops (macro-sim catch-up).

6. **Dev-mode + UI:**
   - `BuildingInspectorView` "Tool Storage" line.
   - `CharacterInspectorView` `[Tool: <Building>]` annotation.
   - `UI_ToolReturnReminderToast`.

7. **Tests:**
   - EditMode unit tests (§12.1).
   - PlayMode integration tests (§12.2).

8. **Documentation:**
   - SKILL.md: `.agent/skills/job-farmer/SKILL.md` (new).
   - SKILL.md: `.agent/skills/tool-storage/SKILL.md` (new).
   - Wiki page: `wiki/systems/job-farmer.md` (new).
   - Wiki update: `wiki/systems/jobs-and-logistics.md` change-log entry.
   - Wiki update: `wiki/systems/farming.md` change-log entry.
   - Specialist-agent updates: `npc-ai-specialist`, `building-furniture-specialist`, `harvestable-resource-node-specialist` agent definitions.

---

## 14. Risks & Open Questions

| Risk | Mitigation |
|---|---|
| GOAP planner picks "FetchTool → ReturnTool" without doing the work in between (no IsValid filter for the work step) | Mirror `JobHarvester._scratchValidActions` pattern: pre-filter actions by `IsValid(worker)` before planning. The IsValid check on `WaterCrop` requires a claimable `WaterCropTask`, which the planner's worldState already gates via `hasClaimedWaterTask`. |
| Two farmers race to fetch the last watering can | `GoapAction_FetchToolFromStorage` claims the `StorageFurnitureSlot` via existing slot-reservation pattern (`StorageFurniture.ReserveSlot`). One farmer wins; the other replans. |
| Player worker stuck unable to punch out because tool storage destroyed mid-shift | Edge case §5.4: gate auto-passes after one retry + log warning. Tool stays with player as "salvaged". |
| `OwnerBuildingId` leaks across save/load when building is later renamed | `BuildingId` is the stable GUID, not the GameObject name (existing `Building.NetworkBuildingId`). Renames don't break the marker. |
| Crop self-seeding generates infinite seeds (designer balance issue) | Designer-controlled via `_seedMaxStock` cap on `IStockProvider`. Surplus seeds get shipped out via existing `BuyOrder` flow → drives revenue. Not a code risk. |

**Open questions:**

- **Q1.** Should `WateringCan` deplete water charges over time (e.g. 5 uses then refill from a well)? **Decision: No for v1** — single-use, infinite. Phase 2 can add charges + a `RefillWateringCan` action.
- **Q2.** Should `JobFarmer` also handle tilling/plowing as a separate scan? **Decision: No for v1** — `CharacterAction_PlaceCrop` plows automatically as part of planting (matches current farming spec §3.1).
- **Q3.** Should the punch-out gate also apply to NPC-only "soft" tools like food/tools the worker brought from home? **Decision: No** — gate only checks items with `OwnerBuildingId == _workplace.BuildingId`.
- **Q4.** Should multiple `FarmingBuilding`s share a single tool storage? **Decision: No for v1** — the `_toolStorageFurniture` reference is per-building. A future enhancement could add a shared-resource furniture pattern, but it adds complexity not justified in v1.

---

## 15. Phase 2 Outline (deferred)

Captured here so the design intent is preserved across PRs.

### 15.1 Shift-long pickup pattern

Add lifecycle hooks on `Job`:
```csharp
public abstract class Job
{
    public virtual List<ItemSO> RequiredToolsForShift => null;
    public virtual void OnShiftStart() { }   // fetch tools
    public virtual void OnShiftEnd() { }     // return tools (gated by CanPunchOut)
}
```

`JobLogisticsManager` (transporter), `JobHarvester` (woodcutter / miner / forager) override `RequiredToolsForShift` and inject a one-time `FetchToolFromStorage` plan at shift start. The `CanPunchOut` gate already covers the symmetrical end.

### 15.2 Bag → carry-capacity bonus

`CharacterEquipment` adds a "bonus inventory slot count" hook driven by an active Bag item. When a `Bag` ItemInstance is equipped (via the same ToolStorage primitive), `_inventory.MaxSlots` increases by `BagSO.SlotsBonus`. Returning the bag at shift end reverts.

### 15.3 Tool durability (optional)

`ItemInstance` adds `int Durability`. Each tool use decrements; at 0, the tool breaks (despawns). Triggers a `BuyOrder` for a replacement. Repair is a Phase 3 concern.

### 15.4 Retrofit list

| Job | Tool needed | Pattern |
|---|---|---|
| `JobHarvester` (Woodcutter) | Axe | shift-long |
| `JobHarvester` (Miner) | Pickaxe | shift-long |
| `JobHarvester` (Forager) | Sickle (optional, bare-hands works without) | shift-long |
| `JobTransporter` | Bag | shift-long |
| `JobBlacksmith` | Hammer (already implicit in station, possibly redundant) | TBD — may stay station-implicit |

---

## Sources

- [farming-plot-system spec (2026-04-28)](2026-04-28-farming-plot-system-design.md) — substrate this builds on.
- [worker-wages-and-performance spec (2026-04-22)](2026-04-22-worker-wages-and-performance-design.md) — Farmer is already on the wage list.
- [quest-system spec (2026-04-23)](2026-04-23-quest-system-design.md) — Farmer is already eligibility-listed for harvest tasks.
- [Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs) — reference for `JobFarmer` GOAP shape.
- [Assets/Scripts/World/Buildings/CommercialBuildings/HarvestingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/HarvestingBuilding.cs) — base class for `FarmingBuilding`.
- [Assets/Scripts/Farming/CharacterAction_PlaceCrop.cs](../../Assets/Scripts/Farming/CharacterAction_PlaceCrop.cs) + [CharacterAction_WaterCrop.cs](../../Assets/Scripts/Farming/CharacterAction_WaterCrop.cs) — reused as-is for NPC path.
- [wiki/systems/jobs-and-logistics.md](../../wiki/systems/jobs-and-logistics.md) — system architecture overview.
- [wiki/systems/farming.md](../../wiki/systems/farming.md) — farming substrate overview.
- 2026-04-29 conversation with [[kevin]] — scope, self-seeding insight, tool storage primitive, punch-out gate.
