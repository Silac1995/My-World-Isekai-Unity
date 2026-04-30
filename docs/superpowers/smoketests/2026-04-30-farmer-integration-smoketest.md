# Farmer Integration — Smoketest

**Date:** 2026-04-30
**Plan:** [docs/superpowers/plans/2026-04-30-farmer-integration.md](../plans/2026-04-30-farmer-integration.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

This is the **terminal smoketest** of the 4-plan Farmer rollout. Plans 1+2+2.5 already validated; this validates the FarmingBuilding + JobFarmer end-to-end loop on a real test scene. Twelve scenarios covering: scan registration, multi-zone, quota selection, rain skipping, mature crops skipping, full plant→water→harvest cycle, per-task watering can, punch-out gate, crop self-seeding, BuyOrder fallback, hiring path, multi-peer.

## Setup

Build a test scene with:
- One `FarmingBuilding` prefab (subclass of HarvestingBuilding). Configure:
  - `_cropsToGrow = [Crop_Wheat]` (single crop for first pass).
  - `_farmerCount = 2`.
  - `_seedMinStock = 5`, `_seedMaxStock = 20`.
  - `_wateringCanItem` → reference the WateringCan ItemSO (`Item_WateringCan` or similar; create authoring asset if missing).
  - `_wateringCanMaxStock = 2`.
- **Two** `Zone` GameObjects placed inside the building's footprint as children, each with a `BoxCollider` (trigger). One ~5×5 cells, one ~3×3 cells. Reference both in `_farmingAreaZones`.
- One `Zone` for `_depositZone` (inherited from HarvestingBuilding).
- Two `StorageFurniture` instances:
  1. **Tool storage** — empty initially. Reference as `_toolStorageFurniture` on `CommercialBuilding`. Drop a `WateringCan` ItemInstance inside.
  2. **Seed storage** — pre-stocked with 10× WheatSeed ItemInstances. Plain general storage; FetchSeed walks the building's transform tree to find it.
- Help Wanted infrastructure (Plans 2+2.5):
  - One `DisplayTextFurniture` placard, referenced as `_helpWantedFurniture`.
  - One `ManagementFurniture` desk, referenced as `_managementFurniture`.
  - `_initialHiringOpen = true`.
- Set the building's Owner to a hired NPC OR to the player Character.
- Save the scene.

## Smoke A — Daily PlantScan registers tasks for empty cells

- [ ] Trigger `TimeManager.AdvanceToNextDay()` (or wait for natural day flip).
- [ ] Inspect `FarmingBuilding.TaskManager.AvailableTasks`.
- [ ] **Assert**: One `PlantCropTask` per empty cell across both `_farmingAreaZones`. Total tasks = sum of cells in all zones (e.g. 25 + 9 = 34 if both zones are full coverage).
- [ ] **Assert**: Each task has `Crop = Crop_Wheat`, distinct `(CellX, CellZ)` coordinates.

## Smoke B — Quota-driven crop selection (multi-crop)

- [ ] Add `Crop_Flower` to `_cropsToGrow` (now `[Crop_Wheat, Crop_Flower]`).
- [ ] Pre-stock the building's inventory: 50 Wheat (at the wheat quota) + 0 Flower.
- [ ] Trigger OnNewDay.
- [ ] **Assert**: All new PlantCropTasks have `Crop = Crop_Flower` (the deficit-largest crop).
- [ ] Restore: remove the 50 Wheat, set Flower to its quota; trigger OnNewDay.
- [ ] **Assert**: New tasks have `Crop = Crop_Wheat`.

## Smoke C — Multi-zone behavior

- [ ] With 2 zones in `_farmingAreaZones`, verify PlantScan creates tasks for cells in BOTH zones (not just the first).
- [ ] Move the building so one zone falls outside the map's terrain grid.
- [ ] **Assert**: Tasks only registered for the in-grid zone (the out-of-bounds zone silently skips via `EnumerateCellsInZone` clamping).

## Smoke D — WaterScan skips on rainy day

- [ ] Plant some crops manually (or wait for Smoke F's planting). Wait until cells have `GrowthTimer > 0` and `Moisture < 0.3`.
- [ ] Force a rain event via `TerrainWeatherProcessor` (or however weather is triggered). This sets `cell.Moisture = 1.0` and `cell.TimeSinceLastWatered = 0` per existing rain logic.
- [ ] Trigger OnNewDay.
- [ ] **Assert**: NO `WaterCropTask` registered for the rained-on cells.
- [ ] Wait 1 in-game day without rain (`TimeSinceLastWatered` increments to 1, `Moisture` decays).
- [ ] **Assert**: WaterCropTasks now register if moisture is below `MinMoistureForGrowth`.

## Smoke E — WaterScan skips mature/perennial cells

- [ ] Manually fast-forward a planted cell's `GrowthTimer` to its `DaysToMature` (so the crop is mature, ready to harvest).
- [ ] Trigger OnNewDay.
- [ ] **Assert**: NO `WaterCropTask` for the mature cell. (The crop is now a `CropHarvestable`; Harvest tasks fire instead via existing `Harvestable.OnStateChanged` pipeline.)

## Smoke F — Full plant→water→harvest cycle (single farmer)

- [ ] Hire one NPC as Farmer (via existing path — walk to Owner, `Apply for Farmer` from hold-E menu).
- [ ] Day 1: Verify the farmer plans `PlantEmptyCells` (after `WorkerStartingShift` auto-claims tasks). NPC walks to seed storage → fetches WheatSeed → walks to a cell → queues `CharacterAction_PlaceCrop` → cell now has `PlantedCropId = "wheat"`.
- [ ] Repeat across the day: farmer plants multiple cells.
- [ ] Day 2: Verify cells progress `GrowthTimer = 1`. WaterScan registers WaterCropTasks if moisture decayed.
- [ ] Farmer plans `WaterDryCells`: walks to tool storage → fetches WateringCan → walks to dry cell → queues `CharacterAction_WaterCrop` → `cell.Moisture` jumps to 1.0, `TimeSinceLastWatered = 0` → walks back to tool storage → returns can.
- [ ] **Assert**: After watering, the WateringCan instance is back in `_toolStorageFurniture` slots, `OwnerBuildingId == ""` (cleared by the AddItem origin-clear hook from Plan 1).
- [ ] Day N (= DaysToMature + 1): Crops mature → `CropHarvestable` spawns at each cell. `Harvestable.OnStateChanged` registers `HarvestResourceTask`.
- [ ] Farmer plans `HarvestMatureCells`: walks to crop → harvests → walks to deposit zone → drops items.
- [ ] **Assert**: Building's `_inventory` now contains Wheat (e.g. 30 instances if 10 cells × 3 yield) AND WheatSeed (10 instances if Count=1 self-seed; per Plan 3 Task 3).

## Smoke G — Per-task watering can pickup + return

- [ ] Re-run Smoke F's water phase.
- [ ] **Assert**: For each WaterCropTask, the GOAP plan is exactly: `FetchToolFromStorage(WateringCan)` → `WaterCrop` → `ReturnToolToStorage(WateringCan)`. Three actions per task.
- [ ] **Assert**: Between WaterCropTasks, the WateringCan returns to `_toolStorageFurniture` and is fetched again next time. (It does NOT stay in the farmer's hand across tasks.)

## Smoke H — Punch-out gate fires if can held at shift end

- [ ] During Smoke F's water phase, force-end the farmer's shift while they're carrying the WateringCan (skip time to 18:01 or later).
- [ ] **Assert**: `CharacterJob.CanPunchOut()` returns `(false, "Return tools...")`.
- [ ] **Assert**: Player worker would see `UI_ToolReturnReminderToast` (NPC silently replans).
- [ ] **Assert**: Farmer plans `ReturnToolToStorage(WateringCan)`, walks back, returns the can.
- [ ] Verify `CanPunchOut() → (true, null)` after return; schedule transitions out of `Work`.

## Smoke I — Crop self-seeding

- [ ] After a full harvest cycle (Smoke F), inspect `FarmingBuilding._inventory`.
- [ ] **Assert**: Both Wheat and WheatSeed appear in stock. Counts depend on `_harvestOutputs[1].Count` (1 in our authored asset).
- [ ] Trigger another day's PlantScan.
- [ ] **Assert**: New PlantCropTasks register only if `_seedStock >= 1` (Smoke F's harvest replenished seed stock).
- [ ] Continue running for 2-3 more days.
- [ ] **Assert**: Seed stock fluctuates between consumed-on-plant + replenished-on-harvest. Steady state: produce > seeds (since each harvest yields 3 wheat + 1 seed, 1 plant consumes 1 seed → surplus accumulates).

## Smoke J — BuyOrder fallback when seed stock empty

- [ ] Empty all WheatSeed from the building's inventory (dev-mode delete).
- [ ] Build a second building somewhere in the map: a `ShopBuilding` (or another building) listing WheatSeed in its `IStockProvider.GetStockTargets()` output (or place ~50 WheatSeed instances in its inventory).
- [ ] Trigger OnNewDay or wait for the next logistics evaluation.
- [ ] **Assert**: A `BuyOrder` for WheatSeed appears in the FarmingBuilding's logistics manager.
- [ ] Wait for the `JobLogisticsManager` worker to walk to the supplier + place the order, then a `JobTransporter` to deliver.
- [ ] **Assert**: WheatSeed instances appear in the FarmingBuilding's storage. Next PlantScan registers tasks again.

## Smoke K — Hiring path end-to-end

- [ ] Reset: no farmers hired. `_initialHiringOpen = true`.
- [ ] Drop an unemployed NPC near the farm. They have `NeedJob` active.
- [ ] Trigger OnNewDay.
- [ ] **Assert**: Console shows `[NeedJob]` log: `OnNewDay scan → cached <FarmingBuilding>/<Farmer>`.
- [ ] Verify NPC walks to the farm's Owner, runs `InteractionAskForJob`, gets hired.
- [ ] After hire: NPC has Farmer assignment, schedule injects 6h-18h Work slot.
- [ ] On the next 6:00 schedule transition, NPC `WorkerStartingShift` auto-claims any available farming tasks.
- [ ] **Assert**: Farmer starts the plant/water/harvest loop autonomously.

## Smoke L — Multi-peer replication

Multiplayer setup: 1 Host + 1 Client.

- [ ] Host runs the FarmingBuilding scene. Client connects.
- [ ] On the Host, trigger Smoke F's planting cycle.
- [ ] On the Client, walk to the same farm. Inspect cells.
- [ ] **Assert**: `cell.PlantedCropId` replicates correctly (existing terrain-cell ClientRpc per `MapController.NotifyDirtyCells`).
- [ ] **Assert**: `CropHarvestable` instances spawn on the Client when crops mature (existing NetworkObject spawn).
- [ ] On the Client, walk to the Help Wanted sign. Read the text.
- [ ] **Assert**: Text matches Host (NetworkVariable replication via Plan 2's `DisplayTextFurnitureNetSync`).
- [ ] On the Client, attempt `TryOpenHiring` if the player owns the building remotely.
- [ ] **Assert**: ServerRpc round-trips correctly; `_isHiring` flips on Host, replicates back to Client.

## Result

When all 12 smokes pass, mark the file's Status as **Pass** and add a final summary line. Then commit:

```bash
git add docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md
git commit -m "test(farmer): smoketest pass — full plant/water/harvest cycle validated"
```

If any smoke fails, common root causes:
- **Smoke A scans don't fire** — verify `OnEnable` correctly subscribes `PlantScan` + `WaterScan` to `TimeManager.OnNewDay`. Check `MWI.Time.TimeManager.Instance != null` at scene load.
- **Smoke F farmer never plants** — verify `JobFarmer.PlanNextActions` correctly populates worldState. Toggle `NPCDebug.VerboseJobs = true` to see goal selection + plan output.
- **Smoke G can not returned** — `GoapAction_ReturnToolToStorage`'s preconditions might not match `GoapAction_WaterCrop`'s effects. Verify `taskCompleteForTool_<name>` flag flow.
- **Smoke I seeds never replenish** — verify `Crop_Wheat.asset` `_harvestOutputs` has the WheatSeed entry (Plan 3 Task 3 commit). Check `RegisterHarvestedItem` adds to inventory on deposit.
