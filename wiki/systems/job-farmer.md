---
type: system
title: "JobFarmer + FarmingBuilding"
tags: [jobs, farming, gameplay-loop, tier-1-child]
created: 2026-04-30
updated: 2026-05-02
sources: []
related:
  - "[[jobs-and-logistics]]"
  - "[[farming]]"
  - "[[tool-storage]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
  - "[[building-task-manager]]"
  - "[[commercial-building]]"
  - "[[chain-action-isvalid-pre-filter]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
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
- Daily WaterScan registering `WaterCropTask` for dry planted cells (skipping mature, rained-on, or already-wet cells; **`TimeSinceLastWatered = -1f` is treated as "never watered, eligible"**).
- **Reactive mid-shift re-scan** via `RefreshScansThrottled()` (1 Hz per building) called from `JobFarmer.PlanNextActions`. Catches inventory + cell mutations between `OnNewDay` ticks (player drops a seed in a chest, logistics inbound delivers seeds, crop gets planted by another worker, etc.).
- **Auto-registered crop produce as wanted resources** via `AutoRegisterCropProduceAsWantedResources()` (called from `Start` before `base.Start`). Walks `_cropsToGrow` and registers each crop's first non-`SeedSO` `HarvestableOutputEntry.Item` as a `WantedResource` at default cap 50. Without this, the very first `ScanHarvestingArea` finds `_wantedResources` empty and never registers `HarvestResourceTask`s.
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
              ├─ farm.RefreshScansThrottled()   // 1 Hz reactive PlantScan+WaterScan
              ├─ Build worldState — keys (all consumed by GOAP preconditions/effects):
              │     hasUnfilledHarvestTask  // mirrors GoapAction_HarvestResources.IsValid:
              │                             //   walks Available + InProgress[claimed-by-me]
              │                             //   excludes blacklisted instance IDs
              │                             //   filters by farm.GetWantedItems() yield-match
              │     hasUnfilledWaterTask    // any actionable WaterCropTask (claimed-or-available)
              │     hasUnfilledPlantTask    // any actionable PlantCropTask (claimed-or-available)
              │     hasSeedInHand
              │     hasCanInHand
              │     hasMatchingSeedInStorage // gated by HasAnySeedForActionablePlantTask
              │     hasResources            // bag has any item OR carrying non-seed-non-can
              │     hasWateringCanAvailable
              │     looseItemExists         // live: actionable PickupLooseItemTask exists
              │     hasHarvestZone          // mirror of hasUnfilledHarvestTask (HarvestResources
              │                             //   precondition; missing-key=false would gate it out)
              │     hasToolInHand_<canKey>, toolNeededForTask_<canKey>,
              │     taskCompleteForTool_<canKey>, toolReturned_<canKey>
              │
              ├─ Pick highest-priority achievable goal — REWRITTEN cascade
              │   (priority order matters — see "Goal cascade" section below):
              │     1. seed-in-hand + plant work     → PlantGoal      (free the hand first)
              │     2. can-in-hand                    → WaterGoal     (use what you started)
              │     3. hasUnfilledHarvestTask
              │        OR looseItemExists
              │        OR hasResources                → HarvestGoal   (single funnel — all paths
              │                                                       end in hasDepositedResources=true:
              │                                                       Harvest→PickupLoose→Deposit
              │                                                       OR PickupLoose→Deposit
              │                                                       OR Deposit alone)
              │     4. hasUnfilledWaterTask
              │        + hasWateringCanAvailable      → WaterGoal     (start a water cycle)
              │     5. hasUnfilledPlantTask
              │        + hasMatchingSeedInStorage     → PlantGoal     (start a plant cycle)
              │     6. (else)                         → IdleGoal
              │
              ├─ Build action library (fresh instances per plan):
              │     HarvestResources, PickupLooseItem, DepositResources,
              │     FetchSeed, PlantCrop,
              │     [if WateringCan + ToolStorage]
              │         FetchToolFromStorage(can), WaterCrop, ReturnToolToStorage(can),
              │     IdleInBuilding
              ├─ Pre-filter via IsValid into _scratchValidActions
              └─ GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal)

GoapAction execution:
        HarvestGoal (3 sub-paths — planner picks cheapest chain that satisfies
                     hasDepositedResources=true given current worldState):
              Path A — fresh harvest:  HarvestResources → PickupLooseItem → DepositResources
              Path B — orphan pickup:  PickupLooseItem → DepositResources
              Path C — bag drain:      DepositResources alone

        WaterGoal:
              [hands free]   FetchToolFromStorage(WateringCan) [stamps OwnerBuildingId]
              → walks to dry cell
              → CharacterAction_WaterCrop (cell.Moisture = canSO.MoistureSetTo)
              → ReturnToolToStorage(WateringCan) [clears OwnerBuildingId]
              [hands hold the can ⇒ planner skips the Fetch step and goes straight to WaterCrop or
               just ReturnToolToStorage if no water work remains]

        PlantGoal:
              [hands free]   FetchSeed (walks to building storage, takes 1 SeedSO, equips)
              → walks to empty cell
              → CharacterAction_PlaceCrop (cell.PlantedCropId = crop.Id, spawns CropHarvestable)
              [hands hold a seed ⇒ planner skips Fetch and goes straight to PlantCrop]

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

## Goal cascade — why the order matters

The cascade is a strict priority list, not a "best of" pick. The order encodes three rules:

1. **Use what you're carrying first.** A seed-in-hand makes `hasResources` false (seeds are *inputs*, not outputs) AND blocks every `Fetch` action via the hands-not-free precondition. The only way to free the hand is `PlantCrop`, so we MUST route to `PlantGoal` first when `hasSeedInHand && hasUnfilledPlantTask` even if there's pending harvest/pickup/deposit work. Symptom seen pre-fix: *"She picked up a sapling, then froze with goal=HarvestMatureCells because the harvest chain needed a free hand."* Same logic for can-in-hand → `WaterGoal`.
2. **Single-funnel the harvest cycle.** Priority 3 fires for `hasUnfilledHarvestTask || looseItemExists || hasResources`. All three end in the same goal (`hasDepositedResources=true`), and `GoapPlanner` is allowed to pick the cheapest chain. This unifies the fresh-harvest path (Harvest → Pickup → Deposit), the orphan-pickup path (something dropped a `WorldItem` into our zone, just Pickup → Deposit), and the bag-drain path (we're carrying produce from earlier, just Deposit alone) under one decision.
3. **Start new work last.** Water and Plant goals only fire when there's no carry-back deposit work AND no pickup work AND no fresh-harvest work. That order keeps the farmer from chaining fetch+plant while a bag of mature crops slowly rots in their inventory.

## Common pitfalls — softlock-guard pattern

Six GOAP actions in the farmer chain share an "arrived-but-stuck" softlock guard: `GoapAction_FetchSeed`, `GoapAction_FetchToolFromStorage`, `GoapAction_ReturnToolToStorage`, `GoapAction_PlantCrop`, `GoapAction_WaterCrop`, `GoapAction_HarvestResources`. The pattern handles a class of bugs where a `NavMeshObstacle.carve` edge from a neighbouring just-planted crop (or any other obstacle that pushes the agent's natural stopping point outward) leaves the worker just outside the strict `InteractionZone` / `WorkRadius`, the action repeats setting the destination, the worker doesn't move, and after 3 retries `PathingMemory.RecordFailure` blacklists the target.

Pattern:

```csharp
if (distXZ > WorkRadius)            // strict accept band, e.g. 2.5u for cell-targeted actions
{
    var movement = worker.CharacterMovement;
    bool arrived = movement == null
        || !movement.HasPath
        || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
    if (!(arrived && distXZ <= OuterBand))   // 4u for cell actions; 3u for storage; 2u for clock
    {
        if (!_isMoving) { worker.CharacterMovement?.SetDestination(target); _isMoving = true; }
        return;   // still walking — keep moving
    }
    // Fell through: agent has settled but we're inside the outer band. Accept.
}
```

The strict + outer band lets us reject a worker who's actually 8u away (still walking) while accepting one who's 3.2u away with no path (genuinely stuck on a carve edge). When adding a new "arrive at X then interact" GOAP action, copy this pattern — never accept based on `distXZ > WorkRadius` alone, never blacklist after the first overshoot.

## Common pitfalls — chain-action `IsValid` must NOT pre-filter by carry state

`PlantCrop`, `WaterCrop`, and `ReturnToolToStorage` are **chain consumers** — the planner is supposed to add `FetchSeed → PlantCrop` (or `FetchTool → WaterCrop`) by walking preconditions. JobFarmer pre-filters `_availableActions` through `IsValid` BEFORE calling `GoapPlanner.Plan` (this is the same anti-allocation pattern `JobHarvester` uses). If `PlantCrop.IsValid` requires "seed in hand", it gets filtered out at the start of every plan tick (worker hasn't run FetchSeed yet) and the planner never sees it as a candidate — the chain is unbuildable, no plan forms, the worker falls through to Idle.

**Rule**: chain-action `IsValid` only checks invariants the planner cannot deduce from preconditions:

- ✅ Workplace exists / TaskManager is non-null.
- ✅ At least one task exists in Available + InProgress[claimed-by-me] for this worker.
- ✅ Hands controller exists (for actions that touch hands).
- ❌ "Seed in hand" / "Can in hand" — that's a precondition the planner uses to chain.

Pickup/Fetch actions still correctly require hands free in `IsValid`, because there's no precondition-driven path that could free the hand mid-plan. See [[chain-action-isvalid-pre-filter]].

## Common pitfalls — worldState predicate must mirror action `IsValid`

`hasUnfilledHarvestTask` in `JobFarmer.PlanNextActions` MUST mirror `GoapAction_HarvestResources.IsValid`'s predicate (blacklist + yield-match) — otherwise the cascade picks `HarvestGoal` when no actionable harvest task exists for this worker, the planner can't form a plan (HarvestResources is filtered out by its own `IsValid`), and the worker freezes on goal=HarvestMatureCells while a perfectly-good Plant or Water task sits idle. Symptom seen: `'blacklisted=1' in HarvestResources.IsValid REJECTED dump while worldState insisted hasUnfilledHarvestTask=True`. Same rule for `hasUnfilledWaterTask` / `hasUnfilledPlantTask` and any future predicate-action pair. See [[worldstate-predicate-action-isvalid-divergence]].

## Common pitfalls — cross-actor race detection on cell-targeted tasks

`PlantCropTask.IsValid` checks `cell.PlantedCropId.IsNullOrEmpty` (cell is still empty); `WaterCropTask.IsValid` checks PlantedCropId set + crop in registry + `GrowthTimer < DaysToMature` + `Moisture < MinMoistureForGrowth`. **Both are re-checked inside `GoapAction_PlantCrop.Execute` / `GoapAction_WaterCrop.Execute` immediately before queueing the `CharacterAction`** — so if the player or another NPC plants/waters the same cell during the worker's walk, the action aborts cleanly via `task.UnclaimTask` instead of overwriting / no-oping. New cell-targeted task types should mirror this two-step check (in `Task.IsValid` AND in the consumer GOAP action).

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

- 2026-05-02 — **Farmer end-to-end rollout (cascade, IsValid corrections, softlock guards, race detection, etc.) — claude.** ~35 commits in `bb1d0b33..85e7af59` closed the full Plant → Water → Mature → Harvest → Deposit cycle on a fresh world. Headline changes affecting JobFarmer's procedure: (a) Goal cascade rewritten — see "Goal cascade — why the order matters" section above. New rule of thumb: "use what you're carrying" first (PlantGoal/WaterGoal), then drop-resources funnel (HarvestGoal collapses to a single goal `hasDepositedResources=true` with three planner sub-paths), then start new work. (b) `worldState` keys now include `hasHarvestZone` (mirrors `hasUnfilledHarvestTask`; required by `GoapAction_HarvestResources` precondition) and live `looseItemExists` (queried from `PickupLooseItemTask` to drive the orphan-pickup path). (c) `hasUnfilledHarvestTask` predicate now matches `GoapAction_HarvestResources.IsValid` exactly (blacklist + yield-match against `farm.GetWantedItems()`) — caught the divergence symptom *"NPCs do not do anything, even though their goap says HarvestMatureCells"*. (d) `HasAvailableOrClaimedTask<T>(_worker)` now walks BOTH `_availableTasks` AND `_inProgressTasks[claimed-by-me]` — auto-claim moves tasks straight to InProgress, so the old "available only" walk reported zero work right after `PlantScan`. (e) Six GOAP actions in the farmer chain share the new "arrived-but-stuck" softlock guard (FetchSeed, FetchToolFromStorage, ReturnToolToStorage, PlantCrop, WaterCrop, HarvestResources). (f) Chain-actions (`PlantCrop`, `WaterCrop`, `ReturnToolToStorage`) no longer pre-filter by hand contents in `IsValid` — that state is a *precondition* used by the planner to chain Fetch→Consume actions, not a validity gate. (g) `PlantCropTask.IsValid` + `WaterCropTask.IsValid` cross-actor race detection (cell-state check before re-queueing the action). (h) `WaterCrop` effects now include `taskCompleteForTool_{canKey}=true` so `ReturnToolToStorage`'s precondition is reachable in the chain. (i) `CommercialBuilding.WorkerStartingShift` only auto-claims quests for player workers (`worker.IsPlayer()`) — NPCs use GOAP's `ClaimBestTask` on demand. Without this, the first NPC to subscribe hoarded every newly-published task via the multicast event order, leaving subsequent farmers idle. (j) `CharacterActions.ApplyHarvestOnServer` / `ApplyDestroyOnServer` now accept `JobHarvester || JobFarmer` — pickup tasks for farmer drops were silently dropped pre-fix. (k) `BTAction_Work.HandlePunchingIn` now checks `IsWorkerOnShift` before advancing to `WorkPhase.Working` — falls back to `MovingToTimeClock` if the action was rejected mid-flight. (l) `NeedJob` adopts the POCO subscribe/unsubscribe pattern to `TimeManager.OnNewDay` so the schedule check is once-per-day, not per-tick. New gotcha pages: [[chain-action-isvalid-pre-filter]] and [[worldstate-predicate-action-isvalid-divergence]]. — claude / [[kevin]]
- 2026-04-30 — Initial implementation. 10 tasks committed across [first SHA] → [Task 10 SHA]. Plans 1+2+2.5 already shipped; Plan 3 is the terminal Farmer rollout. — claude
- 2026-04-30 — Plan 3 final-review fixes: `BuildingTaskManager` now accepts null-Target (cell-targeted) tasks via the new `BuildingTask.GetTaskWorldPosition()` virtual override on `PlantCropTask` + `WaterCropTask`. Critical bug: every plant/water task was being silently dropped on `RegisterTask` early-return. Also: `EnumerateCellsInZone` resolves owning map via `transform.position` (the building's own anchor); dead `_seedMinStock` field removed (StockTarget single-field semantic only uses `_seedMaxStock`); smoketest yield-count examples updated to match `Crop_Wheat.asset` `_harvestOutputs[0].Count = 1`. — claude

## Sources

- [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md)
- [docs/superpowers/plans/2026-04-30-farmer-integration.md](../../docs/superpowers/plans/2026-04-30-farmer-integration.md)
- [docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md](../../docs/superpowers/smoketests/2026-04-30-farmer-integration-smoketest.md)
- [.agent/skills/job-farmer/SKILL.md](../../.agent/skills/job-farmer/SKILL.md) — operational procedures (softlock-guard pattern, chain-action `IsValid` rule, cross-actor race detection).
- [Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs) — `PlanNextActions` is the canonical reference for the goal cascade + worldState build.
- [Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/FarmingBuilding.cs)
- [[chain-action-isvalid-pre-filter]] — paired GOAP gotcha.
- [[worldstate-predicate-action-isvalid-divergence]] — paired Job-side gotcha.
- 2026-04-29 / 2026-04-30 / 2026-05-02 conversation with [[kevin]]
