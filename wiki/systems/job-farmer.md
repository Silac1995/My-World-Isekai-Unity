---
type: system
title: "JobFarmer + FarmingBuilding"
tags: [jobs, farming, gameplay-loop, tier-1-child]
created: 2026-04-30
updated: 2026-04-30
sources: []
related:
  - "[[jobs-and-logistics]]"
  - "[[farming]]"
  - "[[tool-storage]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
  - "[[building-task-manager]]"
  - "[[commercial-building]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - harvestable-resource-node-specialist
owner_code_path: "Assets/Scripts/World/Buildings/CommercialBuildings/"
depends_on:
  - "[[jobs-and-logistics]]"
  - "[[farming]]"
  - "[[tool-storage]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
  - "[[building-task-manager]]"
depended_on_by: []
---

# JobFarmer + FarmingBuilding

## Summary

The Farmer is the first GOAP-driven worker that closes the **plant→water→harvest→ship** loop on a designated farm field. `FarmingBuilding : HarvestingBuilding` adds three things to its parent: a designer-authored multi-zone farm field (`List<Zone>`), daily PlantScan + WaterScan that register cell-targeted tasks (`PlantCropTask` / `WaterCropTask`), and an auto-derived `IStockProvider` for seed/produce/can stock targets. `JobFarmer` mirrors `JobHarvester`'s GOAP planner shape exactly — cached goals, scratch worldState dict, fresh action instances per plan, 0.3s execute cadence — but uses farmer-specific actions: `GoapAction_FetchSeed`, `GoapAction_PlantCrop`, `GoapAction_WaterCrop` plus the reused tool-storage actions for the watering can.

## Purpose

Plans 1, 2, and 2.5 shipped reusable infrastructure (Tool Storage primitive, Help Wanted + Hiring, Management Furniture); Plan 3 (this) is the first consumer. The Farmer demonstrates the full vision of the rollout: a player or NPC can drop a `FarmingBuilding` prefab + draw zones, and the building autonomously runs the farming economy via the existing logistics chain. Crop self-seeding (via designer edits to `CropSO._harvestOutputs`) makes the loop self-sustaining at steady state; BuyOrders fire only when stocks crash. Per-task watering can pickup via the Plan 1 ToolStorage primitive enforces tool stocking as gameplay (no can = no watering = drought losses).

## Responsibilities

- Designating multi-zone farm fields via `_farmingAreaZones: List<Zone>`.
- Daily PlantScan registering `PlantCropTask` for empty cells (with seed available + quota not hit).
- Daily WaterScan registering `WaterCropTask` for dry planted cells (skipping mature, rained-on, or already-wet cells).
- Quota-driven crop selection — picks the crop whose primary produce is most under target.
- GOAP-driven plant/water/harvest cycle on a per-farmer basis.
- Per-task watering can fetch/return via Plan 1's ToolStorage primitive.
- Auto-derived `IStockProvider` input targets for seeds + watering can.
- Crop self-seeding via designer-only edits to `_harvestOutputs` (zero new code).

## Non-responsibilities

- **Does not** own the farming substrate (CropSO, CharacterAction_PlaceCrop, FarmGrowthSystem) — see [[farming]].
- **Does not** own the watering can lifecycle plumbing — see [[tool-storage]].
- **Does not** own the hiring path — see [[help-wanted-and-hiring]] + Plan 2.5's NeedJob throttle.
- **Does not** model plowing as a separate action (CharacterAction_PlaceCrop plows automatically).
- **Does not** model seasons, drought-death, or crop rotation in v1 (Phase 2 follow-ups).

## Key classes / files

| File | Role |
|---|---|
| [Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs) | Building subclass + scans + IStockProvider override |
| [Assets/Scripts/World/Buildings/Tasks/PlantCropTask.cs](../../Assets/Scripts/World/Buildings/Tasks/PlantCropTask.cs) | Cell-targeted plant task |
| [Assets/Scripts/World/Buildings/Tasks/WaterCropTask.cs](../../Assets/Scripts/World/Buildings/Tasks/WaterCropTask.cs) | Cell-targeted water task |
| [Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs) | GOAP-driven Job |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs) | Seed pickup from building inventory |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs) | Wraps CharacterAction_PlaceCrop |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs) | Wraps CharacterAction_WaterCrop |
| `Crop_Wheat.asset` / `Crop_Flower.asset` / `Crop_AppleTree.asset` | `_harvestOutputs` extended with matching SeedSO entries |

## Public API / entry points

See [[job-farmer|SKILL.md]] for full method signatures + integration hooks.

## Data flow

```
TimeManager.OnNewDay (server)
        │
        ├─ FarmingBuilding.PlantScan
        │     ├─ for each cell in union(_farmingAreaZones):
        │     │     ├─ if empty + has seed + quota not hit:
        │     │     │     └─ TaskManager.RegisterTask(PlantCropTask(x, z, crop))
        └─ FarmingBuilding.WaterScan
              └─ for each cell:
                    ├─ if planted + GrowthTimer < DaysToMature + dry:
                    │     └─ TaskManager.RegisterTask(WaterCropTask(x, z))

CommercialBuilding.OnQuestPublished fires (existing) → CharacterQuestLog of on-shift workers
auto-claims via WorkerStartingShift (Plan 2 quest unification).

JobFarmer.Execute (per-tick @ 0.3 Hz)
        │
        ├─ if _currentAction != null: tick / replan
        └─ else: PlanNextActions(farm)
              │
              ├─ Build worldState (hasUnfilledHarvestTask, hasSeedInHand, hasCanInHand,
              │   hasMatchingSeedInStorage, hasResources, hasToolInHand_<canKey>, ...)
              ├─ Pick highest-priority achievable goal:
              │     1. HarvestMatureCells (if mature crops exist)
              │     2. DepositResources (if bag full / produce in hand)
              │     3. WaterDryCells (if dry cells + can available)
              │     4. PlantEmptyCells (if empty cells + seed available)
              │     5. Idle
              ├─ Build action library (fresh instances per plan)
              ├─ Pre-filter via IsValid
              └─ GoapPlanner.Plan → Queue<GoapAction>

GoapAction execution:
        WaterDryCells goal:
              FetchToolFromStorage(WateringCan) [stamps OwnerBuildingId]
              → walks to dry cell
              → CharacterAction_WaterCrop (cell.Moisture = canSO.MoistureSetTo)
              → ReturnToolToStorage(WateringCan) [clears OwnerBuildingId]

        PlantEmptyCells goal:
              FetchSeed (walks to building storage, takes 1 SeedSO, equips)
              → walks to empty cell
              → CharacterAction_PlaceCrop (cell.PlantedCropId = crop.Id, spawns CropHarvestable)

        HarvestMatureCells goal:
              GoapAction_HarvestResources (existing — picks closest claimable HarvestResourceTask)
              → GoapAction_DepositResources (drops at deposit zone; building.RegisterHarvestedItem
                adds matching items to _inventory, INCLUDING the seed via Plan 3 Task 3 self-seed)

CharacterSchedule transitions Work → next:
        └─ CharacterJob.CanPunchOut (Plan 1) — if farmer still carries WateringCan,
           gate fires, NPC replans ReturnToolToStorage, retries punch-out.

Logistics fallback:
        FarmingBuilding.IStockProvider.GetStockTargets emits seed + can input targets.
        LogisticsStockEvaluator detects deficits, fires BuyOrders, JobLogisticsManager places
        them at supplier buildings, JobTransporter delivers. Self-seeding usually keeps the
        farm sustaining; BuyOrder is the cold-start / catastrophe fallback.
```

## Dependencies

### Upstream
- [[jobs-and-logistics]] — Job/Workflow plumbing, BuildingTaskManager, BuildingLogisticsManager.
- [[farming]] — CropSO, SeedSO, WateringCanSO, CharacterAction_PlaceCrop, CharacterAction_WaterCrop, FarmGrowthSystem.
- [[tool-storage]] — Plan 1 watering can fetch/return primitive (`GoapAction_FetchToolFromStorage`, `_ReturnToolToStorage`, `OwnerBuildingId` stamp, `CanPunchOut` gate).
- [[help-wanted-and-hiring]] — Plan 2 hiring API + Plan 2.5 NeedJob OnNewDay throttle (NPC discovers vacant Farmer position).
- [[character-job]] — `CanPunchOut` gate, schedule integration, `QuitJob` auto-return.
- [[building-task-manager]] — `ClaimBestTask<T>`, `UnclaimTask`, `CompleteTask`, `AvailableTasks`.
- [[commercial-building]] — `_toolStorageFurniture`, `_helpWantedFurniture`, `_managementFurniture` references; `_isHiring` NetworkVariable; quest aggregator.

### Downstream
None in v1. Phase 2 retrofitting (Woodcutter / Miner / Forager / Transporter onto Tool Storage shift-long pattern) will reference this as the proven first consumer of the per-task pickup pattern.

## State & persistence

- `_farmingAreaZones`, `_cropsToGrow`, `_wateringCanItem`, stock-target ints — designer-authored, scene-serialised.
- Cell state (PlantedCropId, GrowthTimer, TimeSinceLastWatered, Moisture) — TerrainCell fields, persisted via existing `TerrainCellSaveData` (no new save fields).
- BuildingTask instances — re-derived on each `OnNewDay` scan; not persisted across save/load (acceptable — tasks are per-day work units).
- WateringCan `ItemInstance.OwnerBuildingId` — Plan 1 persistence (rides existing `JsonUtility` round-trip on inventory + storage).

**Zero new save schema** — all state surfaces (cell, building inventory, character schedule, character job, item instance) reuse pre-Plan-3 persistence machinery.

## Network rules

All mutations are server-only:
- `BuildingTaskManager.RegisterTask` runs server-side (PlantScan/WaterScan early-return on `!IsServer`).
- `CharacterAction_PlaceCrop` / `_WaterCrop` are server-only cell mutations.
- GOAP planning runs on the server (workers are server-spawned NPCs).

Replication paths (all existing):
- Cell deltas → `MapController.NotifyDirtyCells` ClientRpc.
- `CropHarvestable` spawn → existing NetworkObject spawn.
- Building inventory changes → existing CommercialBuilding replication.
- `OwnerBuildingId` on the watering can → existing `JsonUtility` round-trip via inventory sync.
- Quest snapshots → existing `CharacterQuestLog` NetworkList + ClientRpc push.

Per rule #19, validated scenarios:
- Host↔Client: Host runs farm, client sees cells + crops + farmer behavior + sign updates.
- Client↔Client: not applicable (the building is server-authoritative; clients are just viewers).
- Host/Client↔NPC: NPC behavior runs server-side; clients see resulting actions via existing AI sync.

## Known gotchas / edge cases

- **Multi-zone bounds-walk per-cell cost.** O(cells × crops) per OnNewDay. v1 zones (25-100 cells) are fine; Phase 2 should hoist crop selection out of the cell loop for >10k cell farms.
- **Crop_Flower's primary produce currently references the wheat ItemSO** — pre-existing authoring data, untouched by Plan 3 Task 3. Designer fix in a follow-up.
- **`FetchSeed` does NOT claim the PlantCropTask** — keeps fetch cheap. Two farmers could fetch seeds for the same task; the second's PlantCrop fails to claim and the seed stays in their hand. The orphan seed gets used at a different matching PlantCropTask OR returns via deposit cycle. Acceptable for v1.
- **Watering can per-task pattern** (vs shift-long) means the can lives in tool storage between waterings. With many waterings per day, the farmer makes a lot of round trips. Phase 2 may add a "shift-long" toggle on the building.
- **Quota-driven crop selection** picks the crop most under target. Tie-broken by `CropSO.Id` ascending. Override `SelectCropForCell` in a subclass for round-robin or rotation.
- **Mid-day staleness on rain** — if rain hits mid-day, in-progress WaterCropTasks become unnecessary but stay on the manager until claimed-and-IsValid-fails. Wasted walks, no incorrect behavior. Phase 2 could prune on rain event.
- **Pre-existing `Cinematics` compile errors** appeared in working state during Plan 3 implementation but are unrelated to this work — they're from a parallel-session cinematic-system effort.

## Open questions / TODO

- **Phase 2: Plowing as a separate action.** v1 plant-action plows automatically. Phase 2 split could add agricultural realism + tool gating (Hoe required for plowing).
- **Phase 2: Seasons.** Per-CropSO seasonal mask; PlantScan filters out off-season crops.
- **Phase 2: Crop rotation logic.** Designer-authored sequence per cell or per row.
- **Phase 2: Wither / drought death.** Currently dry crops just stop growing.
- **Phase 2: Multi-tool support per farm** (Hoe, Sickle, etc.).
- **Phase 2: Persist `IsHiring` + custom sign text** in `BuildingSaveData` (carried over from Plan 2's deferred items).

## Change log

- 2026-04-30 — Initial implementation. 10 tasks committed across [first SHA] → [Task 10 SHA]. Plans 1+2+2.5 already shipped; Plan 3 is the terminal Farmer rollout. — claude
- 2026-04-30 — Plan 3 final-review fixes: `BuildingTaskManager` now accepts null-Target (cell-targeted) tasks via the new `BuildingTask.GetTaskWorldPosition()` virtual override on `PlantCropTask` + `WaterCropTask`. Critical bug: every plant/water task was being silently dropped on `RegisterTask` early-return. Also: `EnumerateCellsInZone` resolves owning map via `transform.position` (the building's own anchor); dead `_seedMinStock` field removed (StockTarget single-field semantic only uses `_seedMaxStock`); smoketest yield-count examples updated to match `Crop_Wheat.asset` `_harvestOutputs[0].Count = 1`. — claude

## Sources

- [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md)
- [docs/superpowers/plans/2026-04-30-farmer-integration.md](../../docs/superpowers/plans/2026-04-30-farmer-integration.md)
- [docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md](../../docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md)
- [.agent/skills/job-farmer/SKILL.md](../../.agent/skills/job-farmer/SKILL.md)
- 2026-04-29 / 2026-04-30 conversation with [[kevin]]
