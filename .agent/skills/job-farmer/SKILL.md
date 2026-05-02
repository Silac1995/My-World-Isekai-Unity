# JobFarmer + FarmingBuilding System

The Farmer closes the plant→water→harvest→ship loop on top of all four predecessor systems (Plan 1 Tool Storage, Plan 2 Help Wanted, Plan 2.5 Management Furniture, plus the existing farming substrate). `FarmingBuilding` extends `HarvestingBuilding` with a multi-zone farm field, daily plant + water scans, and auto-derived `IStockProvider` stock targets. `JobFarmer` mirrors `JobHarvester`'s GOAP shape with farmer-specific actions and uses the Tool Storage primitive for the watering can (per-task fetch/return).

## Public API

### `FarmingBuilding : HarvestingBuilding, IStockProvider`
```csharp
IReadOnlyList<CropSO> CropsToGrow { get; }       // designer authoring (List<CropSO>)
int FarmerCount { get; }
IReadOnlyList<Zone> FarmingAreaZones { get; }    // multi-zone — union of cells
ItemSO WateringCanItem { get; }                  // designer reference; null = no water loop
int SeedMaxStock { get; }                        // StockTarget.MinStock — single-field 'refill UP TO this' semantic
int WateringCanMaxStock { get; }

// Daily scan entry points (subscribe to TimeManager.OnNewDay):
public void PlantScan();      // registers PlantCropTask for empty cells
public void WaterScan();      // registers WaterCropTask for dry planted cells

// IStockProvider override — auto-derives input targets (seeds + can) from _cropsToGrow.
public override IEnumerable<StockTarget> GetStockTargets();

// Helpers:
public bool HasAnySeedForUnclaimedPlantTask();   // drives JobFarmer worldState
```

### `FarmingBuilding` (additions, 2026-05-02)
```csharp
public void RefreshScansThrottled();
// Server-only, 1 Hz per building. Re-runs PlantScan + WaterScan mid-shift.
// Called from JobFarmer.PlanNextActions so seeds/cells changing during a shift
// (player drop, NPC logistics inbound, debug spawn) become visible to GOAP within ~1s.

public bool HasAnySeedForActionablePlantTask(Character worker);
// Walks AvailableTasks AND InProgressTasks[claimed-by-worker]. Drives JobFarmer's
// hasMatchingSeedInStorage worldState flag.

private void AutoRegisterCropProduceAsWantedResources();
// Called from FarmingBuilding.Start BEFORE base.Start runs ScanHarvestingArea.
// For each crop in _cropsToGrow, registers the first non-SeedSO HarvestableOutputEntry.Item
// as a WantedResource at default cap 50.
```

### `JobFarmer : Job`
```csharp
public override JobCategory Category => JobCategory.Harvester;
public override JobType Type => JobType.Farmer;
public override float ExecuteIntervalSeconds => 0.3f;   // heavy-planning cadence

// GOAP planner action library (fresh instance per plan):
//   HarvestResources, PickupLooseItem, DepositResources,
//   FetchSeed, PlantCrop,
//   [if WateringCan + ToolStorage] FetchToolFromStorage(can), WaterCrop, ReturnToolToStorage(can),
//   IdleInBuilding

// worldState keys (built per-tick into _scratchWorldState):
//   hasUnfilledHarvestTask  — mirrors GoapAction_HarvestResources.IsValid (blacklist + yield-match)
//   hasUnfilledWaterTask    — HasAvailableOrClaimedTask<WaterCropTask>(worker)
//   hasUnfilledPlantTask    — HasAvailableOrClaimedTask<PlantCropTask>(worker)
//   hasSeedInHand, hasCanInHand, hasMatchingSeedInStorage, hasResources
//   hasWateringCanAvailable
//   looseItemExists         — live: HasAvailableOrClaimedTask<PickupLooseItemTask>(worker)
//   hasHarvestZone          — mirror of hasUnfilledHarvestTask (precondition gate; missing-key=false)
//   hasToolInHand_<canKey>, toolNeededForTask_<canKey>,
//   taskCompleteForTool_<canKey>, toolReturned_<canKey>
//
// Goal cascade (priority high→low):
//   1. seed-in-hand + plant work     → PlantGoal     (free the hand first)
//   2. can-in-hand                    → WaterGoal    (use what you started)
//   3. hasUnfilledHarvestTask
//      OR looseItemExists
//      OR hasResources                → HarvestGoal  (single goal hasDepositedResources=true;
//                                                    planner picks Harvest→Pickup→Deposit
//                                                    OR Pickup→Deposit OR Deposit alone)
//   4. hasUnfilledWaterTask + hasWateringCanAvailable → WaterGoal
//   5. hasUnfilledPlantTask + hasMatchingSeedInStorage → PlantGoal
//   6. else → IdleGoal

public override List<ScheduleEntry> GetWorkSchedule();   // 6h-18h
```

### Tasks
```csharp
class PlantCropTask : BuildingTask
{
    int CellX, CellZ;
    CropSO Crop;
}

class WaterCropTask : BuildingTask
{
    int CellX, CellZ;
}
```

Both auto-publish as `IQuest` via the existing `CommercialBuilding` quest aggregator (Hybrid C unification, 2026-04-23). Quest eligibility maps to `JobType.Farmer` via the `DoesJobTypeAcceptQuest` extension.

### GOAP actions
```csharp
new GoapAction_FetchSeed(farmingBuilding)          // walks to seed storage, takes 1, equips
new GoapAction_PlantCrop(farmingBuilding)          // walks to cell, queues CharacterAction_PlaceCrop
new GoapAction_WaterCrop(farmingBuilding)          // walks to cell (with can in hand), queues CharacterAction_WaterCrop
// + reused from earlier plans:
new GoapAction_HarvestResources(farmingBuilding)   // existing
new GoapAction_DepositResources(farmingBuilding)   // existing
new GoapAction_FetchToolFromStorage(farmingBuilding, wateringCanItem)   // Plan 1
new GoapAction_ReturnToolToStorage(farmingBuilding, wateringCanItem)    // Plan 1
new GoapAction_IdleInBuilding(farmingBuilding)     // existing
```

## Integration points

- **Daily scans** subscribe to `MWI.Time.TimeManager.OnNewDay`. PlantScan iterates union of `_farmingAreaZones`, registers PlantCropTask per empty cell. WaterScan registers WaterCropTask for cells where `GrowthTimer < DaysToMature` AND `Moisture < MinMoistureForGrowth` AND `TimeSinceLastWatered ≥ 1f`.
- **Crop self-seeding** is a designer-only edit to existing `CropSO` `_harvestOutputs` lists — each crop's matching `SeedSO` is added as a secondary entry. `RegisterHarvestedItem` (inherited from `HarvestingBuilding`) auto-adds matching items to `_inventory`. Zero new code.
- **Tool storage primitive** (Plan 1) handles the watering can lifecycle: `_toolStorageFurniture` reference on `CommercialBuilding`, `OwnerBuildingId` stamp on fetch, AddItem origin-clear hook on return, `CharacterJob.CanPunchOut` gate at shift end.
- **Hiring path** (Plans 2+2.5) handles farmer recruitment: NPC `NeedJob` (OnNewDay throttle) discovers the farm via `BuildingManager.FindAvailableJob`, walks to Owner, applies via `InteractionAskForJob`, gets hired. `WorkerStartingShift` auto-claims published farming quests.
- **`MapController.GetMapAtPosition`** + **`TerrainCellGrid.WorldToGrid` / `GridToWorld`** are the canonical APIs for cell coordinate math (used in `EnumerateCellsInZone` + GOAP action movement).

## Events

None — the system is fully event-driven via existing infrastructure:
- `TimeManager.OnNewDay` drives daily scans.
- `Harvestable.OnStateChanged` drives mature-crop harvest task registration (existing path).
- Cell mutations replicate via `MapController.NotifyDirtyCells` ClientRpc (existing).

## Dependencies

- [[jobs-and-logistics]] — Job/Workflow plumbing.
- [[farming]] — CropSO, CharacterAction_PlaceCrop, CharacterAction_WaterCrop, FarmGrowthSystem.
- [[tool-storage]] — Plan 1 watering can fetch/return primitive.
- [[help-wanted-and-hiring]] — Plan 2 hiring API + Plan 2.5 NeedJob throttle.
- [[character-job]] — punch-out gate, schedule integration.
- [[building-task-manager]] — task registration + claim API.

## Softlock-guard pattern (six actions share it)

`GoapAction_FetchSeed`, `GoapAction_FetchToolFromStorage`, `GoapAction_ReturnToolToStorage`, `GoapAction_PlantCrop`, `GoapAction_WaterCrop`, and `GoapAction_HarvestResources` share an "arrived-but-stuck" softlock guard:

```csharp
if (distXZ > WorkRadius)            // strict accept band — 2.5u for cell actions, 2u for clock
{
    var movement = worker.CharacterMovement;
    bool arrived = movement == null
        || !movement.HasPath
        || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
    if (!(arrived && distXZ <= OuterBand))   // 4u cells, 3u storage
    {
        if (!_isMoving) { worker.CharacterMovement?.SetDestination(target); _isMoving = true; }
        return;
    }
    // Fell through: agent settled within outer band — accept.
}
```

Without this, a `NavMeshObstacle.carve` edge from an adjacent just-planted crop pushes the worker's natural stopping point outside the strict zone — three retries → `PathingMemory.RecordFailure` → blacklist → worker pings between Idle and the action. **When adding any new "walk to X then interact" GOAP action, copy this pattern. Never blacklist on the first overshoot.**

## Chain-action `IsValid` rule

`PlantCrop`, `WaterCrop`, `ReturnToolToStorage` are chain *consumers* — the planner adds the matching Fetch action by walking preconditions. JobFarmer pre-filters `_availableActions` through `IsValid` before `GoapPlanner.Plan`, so a chain consumer that gates on hand-contents in `IsValid` gets filtered out at the start of every plan tick (worker hasn't run Fetch yet). **Rule: chain-action `IsValid` only checks invariants the planner cannot deduce from preconditions** — workplace, task availability, hands controller exists. NOT "seed in hand" / "can in hand". See [chain-action-isvalid-pre-filter](../../wiki/gotchas/chain-action-isvalid-pre-filter.md).

## worldState predicate / action `IsValid` symmetry rule

When you add a worldState key driving goal selection (e.g. `hasUnfilledHarvestTask`), the predicate computing it MUST mirror the consuming action's `IsValid` exactly — including blacklist filters, yield-match filters, and any other gate the action applies. Otherwise the cascade picks a goal whose plan can't form (the action is filtered out by its own `IsValid`), and the worker freezes on that goal forever. See [worldstate-predicate-action-isvalid-divergence](../../wiki/gotchas/worldstate-predicate-action-isvalid-divergence.md).

## Cross-actor race detection on cell-targeted tasks

Both `PlantCropTask.IsValid` and `WaterCropTask.IsValid` re-check cell state (PlantedCropId / GrowthTimer / Moisture) so a player or another NPC who plants/waters the cell while we're walking invalidates the task. **Both consumer GOAP actions ALSO re-check task.IsValid() inside Execute right before queueing the CharacterAction** — releases the claim cleanly via `task.UnclaimTask` if the cell got taken mid-walk. Mirror this two-step pattern when adding new cell-targeted task types.

## Gotchas

- **Multi-zone bounds-walk has a per-cell cost.** Large zones (>10k cells) may need profiling — current impl is O(cells × crops) per OnNewDay scan. v1 zones are typically 25-100 cells; performance is fine. Future Phase 2: hoist crop selection out of the cell loop and bucket cells per chosen crop.
- **Crop_Flower's primary produce currently references the wheat ItemSO** (pre-existing authoring — see Plan 3 Task 3 commit). Designer fix in a follow-up.
- **`FetchSeed` does NOT claim the PlantCropTask** — keeps fetch cheap, lets PlantCrop claim at the cell. Two farmers could simultaneously fetch seeds for the same task; the second's PlantCrop fails to claim and the seed stays in their hand. Acceptable for v1: the orphan seed gets used at a different PlantCropTask matching its CropSO, OR returns to storage on a deposit cycle. Not stuck.
- **Watering can mid-day staleness** — if a farmer holds the can mid-water-task and another worker grabs an OwnerBuildingId-cleared can from elsewhere, the gate could see two cans. v1 fine because there's typically only one can per farm.
- **NeedJob OnNewDay throttle** (Plan 2.5) means new farms aren't discovered by NPCs until the next day after construction — feels organic, no fix needed.
- **Quota-driven crop selection** picks the crop most under target. Tie-broken by `CropSO.Id` ascending. If designer wants round-robin or rotation logic, override `SelectCropForCell` in a subclass.
- **Mature/perennial cells skip WaterScan** — once a CropHarvestable exists at the cell, `cell.GrowthTimer >= crop.DaysToMature` and water is skipped. Re-watering after harvest (perennial regrow) is automatic via the existing FarmGrowthSystem refill cycle.

## Follow-ups (Phase 2 candidates)

- **Plowing as a separate action** — currently plant-action plows automatically. Phase 2 could split into Plow + Plant for more agricultural realism.
- **Seasons** — crops only grow in season. Requires `Season` enum + per-CropSO seasonal mask.
- **Crop rotation logic** — designer-authored sequence per cell or per row.
- **Wither / drought death** — currently dry crops just stop growing. Could add a "withers if not watered for N days" branch.
- **Multi-tool support per FarmingBuilding** (Hoe, Sickle, etc.).

## See also

- Spec: [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md)
- Plan: [docs/superpowers/plans/2026-04-30-farmer-integration.md](../../docs/superpowers/plans/2026-04-30-farmer-integration.md)
- Smoketest: [docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md](../../docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md)
- Wiki page: [wiki/systems/job-farmer.md](../../wiki/systems/job-farmer.md)
- Predecessor SKILLs: [tool-storage](../tool-storage/SKILL.md), [help-wanted-and-hiring](../help-wanted-and-hiring/SKILL.md).
