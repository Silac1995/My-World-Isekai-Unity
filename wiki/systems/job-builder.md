---
type: system
title: "JobBuilder + CityHarvester (AdministrativeBuilding)"
tags: [jobs, construction, city-founding, gameplay-loop, tier-1-child]
created: 2026-05-18
updated: 2026-05-18
sources: []
related:
  - "[[jobs-and-logistics]]"
  - "[[construction]]"
  - "[[administrative-building]]"
  - "[[job-farmer]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
  - "[[building-task-manager]]"
  - "[[commercial-building]]"
  - "[[chain-action-isvalid-pre-filter]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: npc-ai-specialist
secondary_agents:
  - building-furniture-specialist
  - harvestable-resource-node-specialist
owner_code_path: "Assets/Scripts/World/Jobs/BuilderJobs/"
depends_on:
  - "[[jobs-and-logistics]]"
  - "[[administrative-building]]"
  - "[[construction]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
depended_on_by: []
---

# JobBuilder + CityHarvester (AdministrativeBuilding)

## Summary

`JobBuilder` is the GOAP-driven city-construction worker that closes the **build order → fetch material → walk to site → drop → finish construction** loop on a chartered city. It is employed exclusively at [[administrative-building|AdministrativeBuilding]] (the AB's `InitializeJobs` adds `JobBuilder × 2 + JobHarvester + JobLogisticsManager`). The matching `JobHarvester` switches at runtime to a **CityHarvester** state-machine variant when its workplace is an AB — instead of harvesting against a `HarvestingBuilding.HarvestZone`, it drains the AB's *unfulfillable-material harvest queue* (materials the logistics chain couldn't source through B2B / producer / virtual tiers) via a manual `FindTarget → MoveToTarget → Harvesting → PickupDroppedItem → MoveToABStorage → DepositItem` cycle.

`JobBuilder` mirrors [[job-farmer|JobFarmer]]'s planner shape exactly (cached goals, scratch worldState dict, `_scratchValidActions` IsValid pre-filter, fresh action instances per plan, 0.3 s execute cadence, force-replan on action completion) but uses builder-specific actions: `GoapAction_TakeMaterialFromABStorage`, `GoapAction_GoToConstructionSite`, `GoapAction_DropMaterialAtZone`, `GoapAction_FinishBuildingConstruction`.

## Purpose

Plan 4b is the **behavior layer** of the city-founding rollout. Plans 1–3 shipped the community + ambition + building-grid foundations; Plan 4a placed the AB skeleton (`AdministrativeBuilding : CommercialBuilding`, `Building.OnFinalize` hook, charter wiring, `Ambition_FoundACity` asset chain). Plan 4b ships the autonomy that lets a chartered city *actually build itself*: `BuildOrder` is the new logistics-order type the leader places via the (Plan 4c) admin console, `JobLogisticsManager.ProcessActiveBuildOrders` cascades through the existing supply chain to source materials, `JobBuilder` fetches + delivers + constructs, and `JobHarvester`'s CityHarvester branch is the final fallback when no shop / crafter / virtual supplier exists. Rule #22 parity holds throughout: the player-founder can manually finish construction via the cooperative Phase 1 loop, or hire NPCs to do the same work — same `CharacterAction_FinishConstruction` is queued either way.

## Responsibilities

- Consuming `BuildOrder` instances on `_workplace.LogisticsManager.GetFirstActiveBuildOrder()`.
- Resolving the next missing material from `BuildOrder.GetMissingMaterials()`.
- Walking to the AB's `StorageFurniture` chain, taking one matching instance, carrying in hand.
- Walking into the construction site's `Building.BuildingZone`.
- Dropping the carried item inside the zone so `CharacterAction_FinishConstruction.ConsumeFromZone` can despawn it.
- Queueing `CharacterAction_FinishConstruction` (the Phase 1 continuous action) until `Building.Finalize` fires.
- Force-replanning on every action completion so multi-trip cycles drive `BuildOrder.IsCompleted` to true.
- (CityHarvester) Reading `AdministrativeBuilding.GetUnfulfillableHarvestQueue()` to discover materials the logistics chain failed to source.
- (CityHarvester) `Physics.OverlapSphereNonAlloc` 30 u scan for a [[harvestable-resource-node|Harvestable]] yielding the wanted item.
- (CityHarvester) Driving the harvest → pickup → walk-to-AB-storage → deposit cycle via raw `CharacterAction` primitives (the existing GOAP actions take `HarvestingBuilding` in their ctors and cannot be reused).
- (CityHarvester) Calling `ab.DecrementUnfulfillableMaterial(wanted, 1)` on successful deposit so the queue drains.

## Non-responsibilities

- **Does not** place the `BuildOrder` itself — that's a (Plan 4c) admin-console UI action that lands on `BuildingLogisticsManager.AddBuildOrder`.
- **Does not** own the `BuildOrder : IQuest` data class — see [[administrative-building]] and [Assets/Scripts/World/Jobs/BuildOrder.cs](../../Assets/Scripts/World/Jobs/BuildOrder.cs).
- **Does not** own the `ProcessActiveBuildOrders` cascade or `RequestStock` supplier resolution — that's `BuildingLogisticsManager` + `LogisticsStockEvaluator`.
- **Does not** own the cooperative Phase 1 construction loop — `CharacterAction_FinishConstruction` is the shared primitive both the player and JobBuilder queue. See [[construction]].
- **Does not** persist `BuildOrder` across server restarts in v1 (acceptable — orders are placed, drained, and resolved within a play session).
- **Does not** model builder skill tiers; `Character.GetSkillLevelOrZero(SkillId.Builder)` is wired through `CharacterAction_FinishConstruction.SkillBudgetDivisor` but tuning is a follow-up.

## Key classes / files

| File | Role |
|---|---|
| [Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs](../../Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs) | GOAP-driven Job, goal cascade, worldState build |
| [Assets/Scripts/World/Jobs/BuilderJobs/UnfulfillableMaterial.cs](../../Assets/Scripts/World/Jobs/BuilderJobs/UnfulfillableMaterial.cs) | `(Item, Qty, LastEnqueuedDay)` triple backing the AB queue |
| [Assets/Scripts/World/Jobs/BuildOrder.cs](../../Assets/Scripts/World/Jobs/BuildOrder.cs) | `IQuest` data class for "construct this Building" |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeMaterialFromABStorage.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeMaterialFromABStorage.cs) | Find slot + walk + take + carry |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToConstructionSite.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToConstructionSite.cs) | Walk into `Building.BuildingZone.bounds` |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_DropMaterialAtZone.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_DropMaterialAtZone.cs) | Wraps `CharacterDropItem` inside the zone |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_FinishBuildingConstruction.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_FinishBuildingConstruction.cs) | Wraps `CharacterAction_FinishConstruction` |
| [Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs) | CityHarvester state machine in `ExecuteCityHarvesterTick(ab)` |
| [Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs) | `InitializeJobs` + unfulfillable queue API |
| [Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs) | `ProcessActiveBuildOrders` + `Add/Remove/GetFirstActiveBuildOrder` facade |
| [Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) | `RequestStock(item, qty) → bool` cascade |
| [Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs) | `_activeBuildOrders` + `OnBuildOrderAdded` event |

## Public API / entry points

See `.agent/skills/job_system/SKILL.md` "JobBuilder + CityHarvester (Plan 4b, 2026-05-18)" section and `.agent/skills/goap/SKILL.md` "JobBuilder action library (Plan 4b, 2026-05-18)" section for full method signatures + the precondition / effect dictionaries on each GOAP action.

## Data flow

```
Plan 4c admin console (or test code) places a BuildOrder
        │
        └─ AB.LogisticsManager.AddBuildOrder(new BuildOrder(target, ab, leader, day))
              ├─ LogisticsOrderBook._activeBuildOrders.Add(order)
              └─ OnBuildOrderAdded fires (consumers can react)

JobLogisticsManager.Execute (per-tick @ 0.3 s when on shift)
        │
        ├─ RetryUnplacedOrders (existing)
        ├─ ProcessActiveBuyOrders (existing)
        └─ ProcessActiveBuildOrders   ← NEW (Plan 4b Task 6)
              │
              └─ for each order in ActiveBuildOrders:
                    └─ for each (itemSO, missing) in order.GetMissingMaterials():
                          inStorage = _building.GetItemCount(itemSO)
                          inFlight  = _orderBook.SumInFlightQuantityFor(itemSO)
                          needed    = missing - inStorage - inFlight
                          if needed > 0:
                              sourced = Evaluator.RequestStock(itemSO, needed)
                                          // cascades: B2B shop scan → producer chain
                                          //           → VirtualResourceSupplier
                              if !sourced && _building is AdministrativeBuilding ab:
                                  ab.EnqueueUnfulfillableMaterial(itemSO, needed)

JobBuilder.Execute (per-tick @ 0.3 s when on shift)
        │
        ├─ if _currentAction != null: tick / replan
        └─ else: PlanNextActions(ab)
              │
              ├─ Build worldState — keys:
              │     hasActiveBuildOrder              // ab.LogisticsManager.GetFirstActiveBuildOrder() != null
              │     hasMaterialsInHand               // carried.ItemSO matches any missing material
              │     hasMatchingMaterialInABStorage   // storage walk for missing materials
              │     insideConstructionSite           // 2D X-Z bounds check on TargetBuilding.BuildingZone
              │     materialDelivered = false (effect set by DropMaterialAtZone)
              │     isIdling = false (effect set by FinishBuildingConstruction)
              │
              ├─ Pick highest-priority achievable goal:
              │     1. hasMaterialsInHand + hasActiveBuildOrder              → DeliverAndConstructGoal
              │     2. hasActiveBuildOrder + hasMatchingMaterialInABStorage  → FetchFromABStorageGoal
              │     3. (else)                                                 → null (idle, no action runs)
              │
              ├─ Build action library (fresh instances per plan):
              │     TakeMaterialFromABStorage, GoToConstructionSite,
              │     DropMaterialAtZone, FinishBuildingConstruction
              ├─ Pre-filter via IsValid into _scratchValidActions
              └─ GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal)

GoapAction execution chains:
        FetchFromABStorage (effect: hasMaterialsInHand=true):
              TakeMaterialFromABStorage (walks AB storage furniture, takes 1, carries)

        DeliverAndConstruct (effect: isIdling=true) — planner walks preconditions backwards:
              FinishBuildingConstruction
                  ← DropMaterialAtZone (effect: materialDelivered=true)
                      ← GoToConstructionSite (effect: insideConstructionSite=true)
                            [precondition: hasMaterialsInHand=true]

JobHarvester.Execute (per-tick @ 0.3 s when on shift, workplace is AB)
        │
        └─ ExecuteCityHarvesterTick(ab) — 7-state state machine:
              Idle                  ← pick PickWantedItem(ab) from queue; if none, stay idle
              FindTarget            ← Physics.OverlapSphereNonAlloc 30 u, filter by
                                       Harvestable.HasYieldOutput(wanted) + blacklist + CanHarvest
                                       (closest match wins). On miss: 2 s cooldown + reset.
              MoveToTarget          ← worker.CharacterMovement.SetDestination + rule #36 path-loss
                                       re-fire. Arrival: InteractionZone.bounds.Contains OR ≤ 2.5 u
                                       fallback distance.
              Harvesting            ← queue CharacterHarvestAction(worker, target);
                                       OnActionFinished → PickupDroppedItem.
              PickupDroppedItem     ← 2 m scan around worker.position (matches
                                       CharacterActions.ApplyHarvestOnServer's spawn anchor).
                                       queue CharacterPickUpItem; OnActionFinished →
                                       MoveToABStorage.
              MoveToABStorage       ← ab.FindStorageFurnitureForItem(carried) → walk to its
                                       interaction zone (rule #36 gate). On no-storage:
                                       logical AddToInventory + worker-side remove +
                                       DecrementUnfulfillableMaterial fallback.
              DepositItem           ← queue CharacterStoreInFurnitureAction; OnActionFinished →
                                       ab.AddToInventory + ab.DecrementUnfulfillableMaterial +
                                       ResetCityState (back to Idle).

End-to-end completion:
        FinishBuildingConstruction consumes loose WorldItems from the zone via
        CharacterAction_FinishConstruction.ConsumeFromZone. When ConstructionProgress
        reaches 1.0, Building.Finalize fires:
              ├─ _currentState.Value = Complete (replicates to clients)
              ├─ OnFinalize() virtual hook (AB grants founder citizenship; other subclasses no-op)
              └─ BuildOrder.IsCompleted → true (TargetBuilding.IsUnderConstruction flips false)

BuildOrder.RefreshState() is called by the logistics layer; on completion, LogisticsOrderBook
removes it from _activeBuildOrders. JobBuilder force-replans, finds no order, idles.
```

## Dependencies

### Upstream
- [[jobs-and-logistics]] — `Job` base, `BuildingTaskManager`, `BuildingLogisticsManager` facade, `LogisticsStockEvaluator` cascade, `LogisticsOrderBook` storage.
- [[administrative-building]] — workplace class, `InitializeJobs`, unfulfillable queue API, citizenship grant.
- [[construction]] — Phase 1 cooperative loop, `CharacterAction_FinishConstruction`, `BuildingZone` collider, `ConstructionProgress` NetworkVariable, `ContributedMaterials` dict, `Building.Finalize`.
- [[character-job]] — `Job._worker` / `_workplace` plumbing, `Assign` / `Unassign` hooks, schedule integration.
- [[help-wanted-and-hiring]] — hiring path that staffs the AB's `_jobs` slots with player or NPC workers.
- [[commercial-building]] — `GetItemCount`, `FindStorageFurnitureForItem`, `AddToInventory`, `StorageFurniture` walk, `LogisticsManager` accessor.
- [[harvestable-resource-node]] — `Harvestable.HasYieldOutput`, `CanHarvest`, `InteractionZone`, `HarvestDuration`.

### Downstream
None in v1. Plan 4c (admin console + drifter migration + tier-up) is the first downstream consumer — it places `BuildOrder` instances via the leader's UI.

## State & persistence

- `JobBuilder` / `JobHarvester` instances live on `CommercialBuilding._jobs` — Plan 4b adds `JobCategory.Builder` (appended last) and `JobType.Builder = 13` (appended last; never reorder).
- `BuildOrder._activeBuildOrders` on `LogisticsOrderBook` — **server-only `List` not persisted across server restarts in v1**. Orders are placed, drained, and resolved within a play session. Acceptable for v1; Plan 4c may add `BuildingSaveData` persistence.
- `_unfulfillableMaterialHarvestQueue` on `AdministrativeBuilding` — server-only `List<UnfulfillableMaterial>`. Not persisted; idempotent on `(item)` so a restart re-derives the queue on the next logistics tick.
- All other state (carried items, construction progress, building completion) rides existing persistence machinery (`CharacterEquipment`, `Building.ConstructionProgress`, `Building._currentState`, `BuildingSaveData.TreasurySeeded`).

**Zero new save schema** — all surfaces reuse pre-Plan-4b persistence.

## Network rules

All mutations are server-only:
- `BuildOrder` is a plain C# class on a server-only list; clients don't see individual orders. The Quest Log UI is unaffected because `BuildOrder` is background-committed (`IsPlaced = true` semantically — never appears in player-facing quest panels).
- `JobBuilder.PlanNextActions` runs on the server (`BTAction_Work.HandleWorking` is server-gated).
- `JobHarvester.ExecuteCityHarvesterTick` runs on the server (same gate).
- `RequestStock` is server-only — internally fires server-authoritative `BuyOrder` placement on `LogisticsOrderBook`.
- `EnqueueUnfulfillableMaterial` / `DecrementUnfulfillableMaterial` early-return on `!IsServer`.

Replication paths (all existing):
- `Building.ConstructionProgress` NetworkVariable — client HUD progress bar lights up live as JobBuilder drives `FinishBuildingConstruction`.
- `Building._currentState` NetworkVariable — flips to `Complete` on `Building.Finalize`; client visuals swap via existing `ConstructionSiteScanner` path.
- `WorldItem` NetworkObject — loose dropped materials in the construction zone replicate to all peers.
- `Character` + `CharacterEquipment` + `HandsController` NetworkVariables — joining client sees workers carrying materials.

Per rule #19, validated scenarios:
- Host↔Client: Host runs the AB; joining client sees the HUD progress bar fill, the construction visuals swap on completion, and workers carrying materials.
- Client↔Client: not applicable (server-authoritative).
- Host/Client↔NPC: NPC behavior is server-side; clients see actions via existing AI sync. **CityHarvester branch verified via the Plan 4b commit ladder** (per-action ExecuteAction → OnActionFinished callbacks all run server-side; the actions themselves replicate via existing `CharacterAction` server-server pattern).

## Goal cascade — why the order matters

The cascade is a strict priority list:

1. **Use what you're carrying.** `hasMaterialsInHand && hasActiveBuildOrder` → `DeliverAndConstructGoal`. The planner walks preconditions backwards: `FinishBuildingConstruction` needs `materialDelivered=true`, which `DropMaterialAtZone` produces (which needs `insideConstructionSite=true`, which `GoToConstructionSite` produces). The whole chain runs because the worker is carrying a usable material. Skipping this priority leaves the worker walking back to AB storage even with a fresh wood plank in hand.
2. **Re-fetch when the cycle continues.** `hasActiveBuildOrder && hasMatchingMaterialInABStorage` → `FetchFromABStorageGoal`. After `FinishBuildingConstruction` completes (one trip's worth of material consumed), JobBuilder's force-replan clears `_currentPlan` and re-enters PlanNextActions. The cycle re-picks FetchFromABStorage if more material is in storage AND the building still isn't done. Multi-trip is handled by this replan, not by a deeper plan.
3. **Idle when stalled.** No goal achievable → no plan, `_currentAction = null`, worker stands at the AB. Throttled 1 Hz diagnostic dump surfaces *why* (gated behind `NPCDebug.VerboseJobs`). Mirrors JobFarmer's no-plan diagnostic dump pattern.

The `Idle` priority intentionally has NO `GoapAction_IdleInBuilding` entry — that action takes `HarvestingBuilding` in its constructor and is not reusable for AB. The implicit no-plan idle is correct for v1; if a visual wander is wanted later, a new `GoapAction_IdleInCommercialBuilding` (already exists in the JobLogisticsManager action library) can be added with cost 0.5 and goal `isIdling=true`.

## Common pitfalls — softlock-guard pattern

All four NEW JobBuilder GOAP actions and the CityHarvester state machine share rule #36's anti-freeze pattern: `IsCharacterInInteractionZone` (when the target has an `InteractableObject.InteractionZone`) → softlock guard (path exhausted within 2 m flat-XZ) → 1.5 m flat-XZ legacy fallback (no InteractionZone authored). The construction zone is a plain BoxCollider with no `InteractableObject`, so `GoapAction_GoToConstructionSite` falls back to 2D X-Z bounds containment + the softlock guard. See [[host-only-state-blindspot]] for the broader pattern and `GoapAction_FetchSeed` for the canonical reference implementation.

## Common pitfalls — chain-action IsValid must NOT pre-filter by carry state

Same rule as [[job-farmer]]: chain-consumer actions (`GoToConstructionSite`, `DropMaterialAtZone`, `FinishBuildingConstruction`) only check invariants the planner cannot deduce from preconditions:

- ✅ Workplace exists / target Building still under construction.
- ✅ Active `BuildOrder` exists.
- ✅ Worker inside `BuildingZone.bounds` (only for actions that require it).
- ❌ "Material in hand" — that's a precondition the planner uses to chain.

`TakeMaterialFromABStorage` correctly requires hands free in `IsValid` because there's no precondition-driven path that could free the hand mid-plan. See [[chain-action-isvalid-pre-filter]].

## Common pitfalls — RequestStock returns false ≠ no work

`LogisticsStockEvaluator.RequestStock` was changed from `void` to `bool` in Plan 4b Task 6. It returns `true` when stock was sourced (B2B succeeded OR new BuyOrder placed OR existing in-flight order absorbed the demand). It returns `false` *only* when no supplier of any tier exists — the dedup-on-already-ordered path returns `true` because "stock is on its way". Callers checking the bool to enqueue on the AB queue must NOT enqueue on the dedup case; otherwise the queue accumulates the same demand repeatedly. The Task 6 wiring in `BuildingLogisticsManager.ProcessActiveBuildOrders` is correct on this front.

## Common pitfalls — CityHarvester worker can't find dropped item

`CharacterHarvestAction.OnApplyEffect` spawns the harvested `WorldItem` at `worker.position + worker.forward * 0.5 + Vector3.up * 0.3` with a 0.25 m jitter (see [Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) `ApplyHarvestOnServer`). The CityHarvester's `FindNearbyDroppedItem` uses a 2 m radius around the worker — adequate for the standard spawn anchor. If a future change moves the spawn anchor further (e.g., toward the Harvestable instead of the worker), the radius MUST be bumped here too. Symptom of mismatch: the state machine sits in `PickupDroppedItem`, drops back to `Idle` via the carried-item fallback, and never deposits.

## Known gotchas / edge cases

- **`BuildOrder` persistence**: server restart clears the order book in v1. Acceptable because AB-side state (founder, charter, treasury, storage) all persist; the leader can re-place orders via the admin console on restart. Plan 4c may revisit.
- **Multi-builder competition**: `JobBuilder × 2` race on the same AB. Both can pick `FetchFromABStorageGoal` and walk to the same chest; only one succeeds at `RemoveItem`. The loser's `_claimedItem` resolves null in `Execute` and the action completes-with-no-effect; planner re-plans next tick. Acceptable; no double-take.
- **CityHarvester storage-full**: if every `StorageFurniture` in the AB is full, the state machine takes the `AddToInventory` fallback (logical add) + worker-side `DropFromWorker` (drops in-place). The item lands as a loose `WorldItem` near the worker. `JobLogisticsManager`'s `GoapAction_GatherStorageItems` picks it up on its next tick.
- **No-supplier scan spam**: the CityHarvester 30 u scan runs every 0.3 s when in `FindTarget`. A 2 s cooldown after a "no target found" tick (`_cityCooldownUntil`) prevents spam in worlds with no eligible Harvestable in range.
- **JobCategory.Builder appended last**: locked by `JobBuilderClassTests.JobCategory_Builder_AppendedLast`. Adding new categories must append, not reorder, or saved `JobCategory` values corrupt.
- **JobType.Builder = 13**: locked by `JobBuilderClassTests.JobType_Builder_IsThirteen`. Append-only convention.

## Open questions / TODO

- **CityHarvester full GOAP integration**: the manual state machine works but duplicates ~70% of `GoapAction_HarvestResources`'s logic. A future refactor could parametrize the GOAP actions on `Building` (base type) rather than `HarvestingBuilding`, letting JobBuilder/CityHarvester share the action library.
- **BuilderSkill formula tuning**: `Character.GetSkillLevelOrZero(SkillId.Builder)` flows through `CharacterAction_FinishConstruction.SkillBudgetDivisor` (currently 5). Calibration deferred.
- **Multi-AB cities**: hard-gated to one AB per community via `BuildingPlacementManager` (Plan 4a). Removing the gate is Phase Next.
- **BuildOrder persistence**: Plan 4c follow-up.
- **Admin-console UI**: Plan 4c. Until it ships, BuildOrders are placed programmatically (debug command / Roslyn).
- **Drifter migration → citizen → JobBuilder hire path**: Plan 4c.
- **PlayMode-MP smoketest**: end-to-end city build with two clients. Lands with Plan 4c.

## Change log

- 2026-05-18 — **JobBuilder + CityHarvester end-to-end shipped (Plan 4b).** Commits `68da6e95..1247b624` on `multiplayyer`: (a) Plan 4b doc, (b) four NEW `GoapAction`s, (c) Task 2 cherry-pick (LogisticsOrderBook + facade + `JobType.Builder`), (d) Task 3 fix (`BuildingLogisticsManager` → `LogisticsManager` property), (e) `JobBuilder` class + 10 contract tests, (f) `AdministrativeBuilding.InitializeJobs` + unfulfillable-material queue, (g) `BuildingLogisticsManager.ProcessActiveBuildOrders` cascade + `LogisticsStockEvaluator.RequestStock void → bool`, (h) `JobHarvester.ExecuteCityHarvesterTick` 7-state state machine, (i) wiki + SKILL change log entries. 196 EditMode tests pass. Pipeline ships fully functional for B2B + producer + virtual + physical-harvest tiers. — claude

## Sources

- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) — design spec.
- [docs/superpowers/plans/2026-05-18-job-builder-and-goap-actions.md](../../docs/superpowers/plans/2026-05-18-job-builder-and-goap-actions.md) — Plan 4b implementation plan.
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) "JobBuilder + CityHarvester (Plan 4b, 2026-05-18)" — operational procedures.
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md) "JobBuilder action library (Plan 4b, 2026-05-18)" — per-action precondition/effect tables.
- [Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs](../../Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs) — `PlanNextActions` is the canonical reference for the goal cascade + worldState build.
- [Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs) — `ExecuteCityHarvesterTick` is the 7-state state machine.
- [[administrative-building]] — workplace + unfulfillable queue.
- [[construction]] — Phase 1 cooperative loop + `CharacterAction_FinishConstruction`.
- [[job-farmer]] — JobBuilder's planner is a verbatim mirror of JobFarmer's shape.
- [[chain-action-isvalid-pre-filter]] — paired GOAP gotcha.
- [[worldstate-predicate-action-isvalid-divergence]] — paired Job-side gotcha.
- 2026-05-18 conversation with [[kevin]].
