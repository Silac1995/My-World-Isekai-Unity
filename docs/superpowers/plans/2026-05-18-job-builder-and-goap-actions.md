# JobBuilder + Builder GOAP Actions Implementation Plan (Plan 4b)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the behavior layer of the city-construction pipeline — the `JobBuilder : Job` class with goal cascade, the 4 NEW `GoapAction`s it depends on (`TakeMaterialFromABStorage`, `GoToConstructionSite`, `DropMaterialAtZone`, `FinishBuildingConstruction`), the `JobLogisticsManager.ProcessActiveBuildOrders` cascade that turns `BuildOrder.GetMissingMaterials` into `BuyOrder` / `CraftingOrder` requests via the existing `LogisticsStockEvaluator.RequestStock` cascade, the `JobHarvester` CityHarvester variant that drains the AB's unfulfillable-material queue when no supplier exists, and the `AdministrativeBuilding.InitializeJobs` wiring that staffs the AB with `JobBuilder × 2 + JobHarvester × 1 + JobLogisticsManager × 1` plus the `_unfulfillableMaterialHarvestQueue`. This is the meat of Plan 4b — Tasks 1–2 (BuildOrder + LogisticsOrderBook + facade + JobType.Builder) are already shipped in commits `c27e1e3b`, `fe36debc`, `2f3164d4`. Plans 4a/4c continue to ship around it.

**Architecture:**

- **`JobBuilder : Job`** mirrors `JobFarmer`'s exact shape (`ExecuteIntervalSeconds = 0.3f`, scratch worldState dict, scratch valid-action list, cached goals, fresh action instances per plan, force-replan on action completion). Goal cascade (high → low):
  1. `DeliverAndConstructGoal` — when worker is **carrying** a needed material AND an active `BuildOrder` exists. Desired: `materialDelivered_<itemId> = true`.
  2. `FetchFromABStorageGoal` — when an active `BuildOrder` exists AND the AB's `StorageFurniture` chain has a matching material. Desired: `hasMaterialsInHand_<itemId> = true`.
  3. `IdleAtABGoal` — when an active `BuildOrder` exists but no material is currently available (logistics still in flight). Desired: `isIdling = true`. Wraps the existing `GoapAction_IdleInBuilding`.
  4. `IdleInBuildingGoal` — no active orders at all. Same desired key as `IdleAtABGoal`; just the fallback selector branch.

- **Four NEW GOAP actions (1 file each):**
  1. `GoapAction_TakeMaterialFromABStorage` — finds the AB's `StorageFurniture` slot holding the next missing material from the active `BuildOrder`, walks to it, queues `CharacterTakeFromFurnitureAction`. Mirrors `GoapAction_FetchSeed`'s pattern (which is the simplest "take from named storage" reference in the codebase). Effect: `hasMaterialsInHand_<itemId> = true`. Cost: 1.0.
  2. `GoapAction_GoToConstructionSite` — walks the worker into the active `BuildOrder.TargetBuilding.BuildingZone` (rule #36 — uses `IsCharacterInInteractionZone` if the Zone wraps `InteractableObject`, else 2D X-Z bounds). Effect: `insideConstructionSite_<netId> = true`. Cost: 0.5 (cheap because it's a pure move).
  3. `GoapAction_DropMaterialAtZone` — once inside the construction `BuildingZone`, queues `CharacterDropItem` so the carried item becomes a loose `WorldItem` inside the zone (where `CharacterAction_FinishConstruction.ConsumeFromZone` can despawn it). Precondition: `insideConstructionSite_<netId> = true && hasMaterialsInHand_<itemId> = true`. Effect: `materialDelivered_<netId>_<itemId> = true`. Cost: 0.5.
  4. `GoapAction_FinishBuildingConstruction` — wraps `CharacterAction_FinishConstruction` (the Phase 1 continuous action). Precondition: `insideConstructionSite_<netId> = true && materialDelivered_<netId>_<itemId> = true`. Effect: `materialDelivered_<itemId> = true` (provisional — multi-trip; the planner re-runs on action completion). Cost: 1.0.

- **`AdministrativeBuilding.InitializeJobs` override** — populate `_jobs` with `JobBuilder × 2 + JobHarvester × 1 + JobLogisticsManager × 1`. Each `Job` constructor signature mirrors the existing pattern: `new JobBuilder("Builder")`, `new JobBuilder("Builder 2")`, `new JobHarvester("Harvester")`, `new JobLogisticsManager("Logistics Manager")`. JobHarvester's `CityHarvester` mode is auto-detected at runtime — if `_workplace is AdministrativeBuilding`, it switches to harvest-queue-driven mode (Task 7). No new JobType / JobCategory entries needed for the harvester variant — the behavior switch is a runtime branch on `Workplace`.

- **`AdministrativeBuilding._unfulfillableMaterialHarvestQueue`** — a server-only `List<UnfulfillableMaterial>` field on `AdministrativeBuilding`. Each entry is `(ItemSO Item, int Qty, int LastEnqueuedDay)`. Two new public methods on AB:
  - `EnqueueUnfulfillableMaterial(ItemSO item, int qty)` — called by `JobLogisticsManager.ProcessActiveBuildOrders` when `RequestStock` returns false.
  - `GetUnfulfillableHarvestQueue()` — returns the read-only list for JobHarvester's worldState build.
  - `DecrementUnfulfillableMaterial(ItemSO item, int qtyDelivered)` — called by JobHarvester after a successful deposit; removes the entry when qty hits zero. Defensive: clamps qty to ≥0; ignores items not in the queue.

- **`JobLogisticsManager.ProcessActiveBuildOrders` extension** — wires the BuildOrder → BuyOrder/CraftingOrder cascade. Tick-driven (called from existing `Execute` after the existing `ProcessActiveBuyOrders` pass). For each `BuildOrder` on `_workplace.BuildingLogisticsManager.ActiveBuildOrders`:
  - For each `(itemSO, missing)` from `buildOrder.GetMissingMaterials()`:
    - Compute `inFlight = stockEvaluator.GetInFlightBuyOrderCount(itemSO) + currentStorageCount(itemSO)`
    - `needed = missing - inFlight`
    - If `needed > 0`: call `stockEvaluator.RequestStock(itemSO, needed)`. The existing RequestStock cascade handles B2B shop scan → producer/crafting → VirtualResourceSupplier internally.
    - If `RequestStock` returns false: call `(_workplace as AdministrativeBuilding)?.EnqueueUnfulfillableMaterial(itemSO, needed)`.

- **`JobHarvester` CityHarvester runtime branch** — new behavior triggered when `_workplace is AdministrativeBuilding`. Reads `AB.GetUnfulfillableHarvestQueue()`; for the first non-empty entry, scans `CharacterAwareness.GetVisibleInteractables<Harvestable>()` for a target whose yield matches the wanted item; runs the existing harvest→pickup→deposit chain (`GoapAction_HarvestResources` + `GoapAction_PickupLooseItem` + `GoapAction_DepositResources`). On successful deposit, calls `AB.DecrementUnfulfillableMaterial(itemSO, 1)` via a new event hook on `JobLogisticsManager.OnItemHarvested` (which already fires when a harvested item lands in storage). Minimal extension — does not introduce a separate class; the runtime branch is a new method `JobHarvester.PlanCityHarvesterTick()` invoked from `Execute()`'s top when the workplace check succeeds.

- **Tests** — EditMode unit tests for each new GOAP action (precondition/effect surface area) + an integration test for `JobLogisticsManager.ProcessActiveBuildOrders` covering the three branches: (a) stock available → no request, (b) request succeeds → BuyOrder placed, (c) request fails → AB queue gains entry. The full end-to-end planner test (worker carries → drops → finishes) is too physics-heavy for EditMode; defer to manual PlayMode-MP testing per the Plan 4a precedent (the AB's actual placement + the building grid land in Plan 4c, which is when end-to-end test surface becomes real).

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9. No new asmdef. Roslyn / MCP not required (all C#-only edits). New EditMode tests under `Assets/Editor/Tests/JobBuilder/` (existing folder, alongside the BuildOrder tests).

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first; the full GOAP path was walked through `JobFarmer` + `GoapAction_GatherStorageItems` + `CharacterAction_FinishConstruction` before drafting), #9-#14 (SOLID — each GOAP action has one verb; JobBuilder follows the existing Job base contract cleanly; JobHarvester's CityHarvester variant lives behind a clean `if (_workplace is AdministrativeBuilding)` branch with extracted helpers), #15 (`_underscorePrefix`), #16 (no events subscribed; nothing to unsubscribe), #18/#19/#19b (server-only state — full audit below), #22 (player↔NPC parity — every GOAP action wraps a `CharacterAction` so the player can do the same thing manually), #28/#29/#29b (skill/agent/wiki sync at the end), #31 (defensive null-checks throughout — missing AB, missing BuildOrder, missing TargetBuilding, missing storage), #34 (per-frame allocation discipline — scratch worldState/action list per-call, fresh GOAP action instances only at planning time which is 0.3s tick, no LINQ in hot paths, `Debug.Log` gated behind `NPCDebug.VerboseJobs` / `NPCDebug.VerboseActions`), #36 (interaction proximity uses `InteractableObject.IsCharacterInInteractionZone` where the target has an `InteractableObject` collider; 2D X-Z fallback otherwise; softlock guard pattern from rule #36 mirrored verbatim in `GoapAction_GoToConstructionSite`).

**Network safety audit (rule #19b — performed BEFORE writing the plan):**

1. **Who writes the new state?** Server-only across the board. `BuildOrder` instances live in `LogisticsOrderBook._activeBuildOrders` (server-only). `_unfulfillableMaterialHarvestQueue` is a server-only field on `AdministrativeBuilding`. JobBuilder's GOAP planner runs server-side (every `Job.Execute()` is gated by the `BehaviourTree` BTAction which fires server-only). All `CharacterAction`s queued via `worker.CharacterActions.ExecuteAction(...)` go through the existing server-authoritative pipeline.
2. **What replication channel?** **No NEW replication channels** added by Plan 4b. Every state read by clients flows through existing channels:
   - `Building.ConstructionProgress` (existing `NetworkVariable<float>`) replicates progress to clients — the HUD bar Phase 1 wired up reads this.
   - `Building._currentState` (existing `NetworkVariable<BuildingState>`) flips to `Complete` via the existing `Building.Finalize()` path; client sees the visual swap immediately.
   - `LogisticsOrderBook._activeBuildOrders` is server-only; clients never read individual BuildOrders. The Quest Log UI consumes the AB's `Quests` aggregator (existing) which already filters to client-visible quest snapshots — `BuildOrder.IsBackgroundCommitted = true` (inherited from BuildOrder's IQuest contract — `IsPlaced = true` semantically) means it never appears in player-facing quest panels.
   - `AdministrativeBuilding._unfulfillableMaterialHarvestQueue` is server-only and never needs client visibility (it's a logistics-internal scratch list).
   - `CharacterAction_FinishConstruction` already replicates via the existing `ContinuousActionTick` server-only pattern. Clients see the action via the existing `CancelActionVisualsClientRpc` proxy + the HUD progress bar.
3. **Late-joiner sees?**
   - Active construction sites: the `Building` GameObject's `_currentState` NetworkVariable replays on connect; the joining client sees `UnderConstruction` and the construction visuals swap in via the existing `ConstructionSiteScanner` path.
   - Materials dropped in the zone: `WorldItem` is a `NetworkObject`; existing NGO replication handles late-joiners.
   - BuildOrders themselves: not replicated. The joining client doesn't see the abstract "this AB has 3 orders pending." That's fine — there's no UI for it in v1, and Plan 4c's admin console will read the AB's `ActiveBuildOrders` server-side when it ships.
   - JobBuilder worker state (carrying materials, in transit): replicated via existing `Character` + `CharacterEquipment` + `HandsController` NetworkVariables — joining client sees workers carrying logs the same way they see farmers carrying seeds.
4. **Client-side pre-gate?** Plan 4b doesn't add a client-side pre-gate (no new UI surface). The only client-visible touchpoint is the construction progress bar, which reads the existing `Building.ConstructionProgress` NetworkVariable. The pre-gate exists implicitly through `CharacterAction.CanExecute` running on the server when the action is queued.
5. **`GetComponentInParent` spawn-race?** N/A — no new component added to an existing prefab. `JobBuilder` is a pure data class (not a `MonoBehaviour`); the GOAP actions are pure C# instantiated by the JobBuilder at plan time.
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?** `GoapAction_TakeMaterialFromABStorage` uses `IsCharacterInInteractionZone` for arrival at the `StorageFurniture` (matching the canonical pattern from `GoapAction_GatherStorageItems` / `GoapAction_FetchToolFromStorage`). `GoapAction_GoToConstructionSite` uses a 2D X-Z check against `Building.BuildingZone.bounds` (the construction zone is a `BoxCollider` Trigger, not an `InteractableObject` — same shape as `CharacterAction_FinishConstruction.IsActorInsideBuildingZone`). The softlock guard (path exhausted + within 2f flat-XZ) is mirrored verbatim. `GoapAction_DropMaterialAtZone` re-uses the same zone-check helper from `GoapAction_GoToConstructionSite` (the worker must still be inside the zone when dropping).

**Out of scope (deferred to Plan 4c or later):**
- `BuildingPlacementManager.RequestPlaceCityBlueprintServerRpc` (Plan 4c — admin console placement entry).
- `CityManagementFurniture` + `UI_CityManagementPanel` (Plan 4c).
- Tier-up gating logic (Plan 4c).
- Drifter migration + `JoinRequestDesk` furniture (Plan 4c).
- `BuildingGrid.Register` calls from city building placement (Plan 4c — Plan 2 shipped the grid itself, Plan 4c adds the city placement path).
- AB.prefab authoring with preplaced furniture (Plan 4c).
- BuilderSkill formula tuning (handoff — `Character.GetSkillLevelOrZero(SkillId.Builder)` is already seated in CharacterAction_FinishConstruction).
- Player-builder UI hooks (the player can already finish construction via the cooperative Phase 1 loop; JobBuilder is the NPC autonomy path, not an alternative).

---

## File Structure

**New files:**
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeMaterialFromABStorage.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToConstructionSite.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_DropMaterialAtZone.cs`
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_FinishBuildingConstruction.cs`
- `Assets/Scripts/World/Jobs/ServiceJobs/JobBuilder.cs` (new sub-folder pattern: jobs live next to peers — Builder isn't a Harvester/Crafting/Service categorisation cleanly; place under `Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs` to match the existing per-category sub-folder convention).
- `Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs` (final location — new sub-folder).
- `Assets/Scripts/World/Jobs/BuilderJobs/UnfulfillableMaterial.cs` (small struct or class — backing type for the AB queue).
- `Assets/Editor/Tests/JobBuilder/JobBuilderGoapActionTests.cs` — precondition/effect coverage for the 4 new actions.
- `Assets/Editor/Tests/JobBuilder/JobLogisticsManagerBuildOrderTests.cs` — three-branch test for `ProcessActiveBuildOrders`.

**Modified files:**
- `Assets/Scripts/World/Jobs/JobCategory.cs` — add `Builder` enum value (append; never reorder).
- `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs` — implement `InitializeJobs`; add the 3 unfulfillable-material methods + backing list.
- `Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs` — add `ProcessActiveBuildOrders` + hook into existing `Execute` tick.
- `Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs` — add `_workplace is AdministrativeBuilding` runtime branch in `Execute` + new `PlanCityHarvesterTick` helper.

**Docs to update (in the final wrap-up commit):**
- `.agent/skills/job_system/SKILL.md` — JobBuilder section + CityHarvester variant.
- `.agent/skills/goap/SKILL.md` — JobBuilder GOAP example + the 4 new actions.
- `wiki/systems/administrative-building.md` — add Plan 4b change log entry; refresh Public API section to include `InitializeJobs` + the unfulfillable-material methods.
- `wiki/systems/construction.md` — Phase 2 NPC autonomy unlocked; cross-link `wiki/systems/job-builder.md`.
- `wiki/systems/job-builder.md` (NEW) — full system page from `wiki/_templates/system.md`.
- `.claude/agents/npc-ai-specialist.md` — add JobBuilder + the 4 new GOAP actions to the registry section.

---

## Task 3: Four NEW GOAP Actions

**Files:**
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeMaterialFromABStorage.cs`
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToConstructionSite.cs`
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_DropMaterialAtZone.cs`
- Create: `Assets/Scripts/AI/GOAP/Actions/GoapAction_FinishBuildingConstruction.cs`

- [ ] **Step 1: GoapAction_TakeMaterialFromABStorage**

References to verify before writing: `GoapAction_FetchSeed`, `GoapAction_FetchToolFromStorage`, `CharacterTakeFromFurnitureAction`. The action holds a reference to the `JobBuilder` (so it can read `JobBuilder.CurrentBuildOrder` + the worker's AB workplace), finds the next missing-material `ItemSO`, scans `AdministrativeBuilding.GetStoragesWithRole(...)` + the legacy fallback for a `StorageFurniture` holding that item, walks to it, queues `CharacterTakeFromFurnitureAction`. Wraps the same arrival-precedence pattern as `GoapAction_GatherStorageItems.GatherState.MovingToStorage` (IsCharacterInInteractionZone → path-exhausted-and-close → legacy 1.5f flat-XZ).

```csharp
public class GoapAction_TakeMaterialFromABStorage : GoapAction
{
    public override string ActionName => "TakeMaterialFromABStorage";
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasMaterialsInHand", true }
    };
    public override float Cost => 1.0f;

    private readonly JobBuilder _job;
    private readonly AdministrativeBuilding _ab;
    private ItemSO _targetMaterial;
    private StorageFurniture _targetStorage;
    private Vector3 _targetPos;
    private bool _isComplete;
    private bool _actionStarted;
    private TakeState _state = TakeState.FindingMaterial;
    // ... state machine: FindingMaterial → MovingToStorage → TakingFromStorage → Done
}
```

  - State machine: `FindingMaterial → MovingToStorage → TakingFromStorage → Done` (4 states; mirrors `GoapAction_GatherStorageItems` structurally but with the take-from path rather than gather-loose).
  - `IsValid`: AB has at least one BuildOrder with a missing material AND that material exists in at least one of AB's StorageFurniture; worker hands are free.
  - `Exit`: clear all state fields (action instances re-used? **no** — JobBuilder constructs fresh instances per plan, same as JobFarmer); reset `_state = FindingMaterial`.
  - **Anti-spam:** every `Debug.Log` in `Execute` gated behind `NPCDebug.VerboseActions`.

- [ ] **Step 2: GoapAction_GoToConstructionSite**

```csharp
public class GoapAction_GoToConstructionSite : GoapAction
{
    public override string ActionName => "GoToConstructionSite";
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasMaterialsInHand", true }
    };
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "insideConstructionSite", true }
    };
    public override float Cost => 0.5f;

    private readonly JobBuilder _job;
    private Building _targetSite;
    private bool _isComplete;
    private bool _isMoving;
    // ...
}
```

  - Reads `_job.CurrentBuildOrder.TargetBuilding` at `IsValid` / Execute time.
  - `IsValid`: there's an active BuildOrder; its TargetBuilding is non-null and is `IsUnderConstruction`; the worker is carrying SOMETHING (we don't require the right item here — the planner's chained precondition handles correctness, and a builder carrying the wrong item should still walk to the site rather than freeze).
  - Movement: `worker.CharacterMovement.SetDestination(building.BuildingZone.bounds.center)`. Re-fire on `!movement.HasPath` (rule #36 anti-freeze pattern).
  - Arrival: 2D X-Z `BuildingZone.bounds.Contains` check (no `InteractableObject` on construction zones in v1 — they're just BoxColliders). Softlock guard: `!HasPath || RemainingDistance <= StoppingDistance + 0.5f` + within 2f flat-XZ.
  - Effect: sets `insideConstructionSite = true`. Action `IsComplete = true` on arrival.

- [ ] **Step 3: GoapAction_DropMaterialAtZone**

```csharp
public class GoapAction_DropMaterialAtZone : GoapAction
{
    public override string ActionName => "DropMaterialAtZone";
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasMaterialsInHand", true },
        { "insideConstructionSite", true }
    };
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "materialDelivered", true }
    };
    public override float Cost => 0.5f;
    // ...
}
```

  - `IsValid`: still inside zone; still carrying.
  - `Execute`: one-shot. Reads `_carried = GetCarriedItem(worker)` (same helper pattern as `GoapAction_GatherStorageItems.GetCarriedItem`). Queues `CharacterDropItem(worker, carried)`. `_actionStarted = true`. On `OnActionFinished`, set `_isComplete = true`.
  - **Important:** `CharacterDropItem` drops the item at the worker's feet — the worker is already inside the zone so the dropped item lands inside the zone, where `CharacterAction_FinishConstruction.ConsumeFromZone` will despawn it next tick. No need to teleport the drop.

- [ ] **Step 4: GoapAction_FinishBuildingConstruction**

```csharp
public class GoapAction_FinishBuildingConstruction : GoapAction
{
    public override string ActionName => "FinishBuildingConstruction";
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "materialDelivered", true },
        { "insideConstructionSite", true }
    };
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true } // satisfies the parent goal — multi-trip handled by replan
    };
    public override float Cost => 1.0f;
    // ...
}
```

  - `IsValid`: TargetBuilding still under construction; worker is inside zone.
  - `Execute`: one-shot. Queues `new CharacterAction_FinishConstruction(worker, _job.CurrentBuildOrder.TargetBuilding)` via `worker.CharacterActions.ExecuteAction(...)`. `_actionStarted = true`. On `OnActionFinished` (which fires when the continuous action's `OnTick` returns true — either Finalize, stall, or zone-exit), set `_isComplete = true`.
  - The replan loop handles multi-trip: after each FinishBuildingConstruction completes, JobBuilder's `_currentPlan = null` forces a re-plan. If the building is still under construction, the planner picks `DeliverAndConstructGoal` again and the worker walks back to the AB storage. If `IsUnderConstruction` flipped to false, `BuildOrder.RefreshState()` fires `OnStateChanged` → next tick `_workplace.BuildingLogisticsManager.RemoveBuildOrder(order)` (Task 5 wires this).

- [ ] **Step 5: Tests**

`Assets/Editor/Tests/JobBuilder/JobBuilderGoapActionTests.cs`:
- `TakeMaterialFromABStorage_IsValid_FalseWhenNoBuildOrder`
- `TakeMaterialFromABStorage_IsValid_FalseWhenStorageEmpty`
- `GoToConstructionSite_Preconditions_RequiresHasMaterialsInHand`
- `GoToConstructionSite_Effects_SetsInsideConstructionSite`
- `DropMaterialAtZone_Preconditions_RequiresBothFlags`
- `FinishBuildingConstruction_Preconditions_RequiresBothFlags`
- `FinishBuildingConstruction_Effects_SetsIsIdling`

Each test instantiates the action with stub args; asserts Preconditions/Effects dict contents; for IsValid tests, instantiates an AB + Building + JobBuilder with the minimal state needed (no NetworkObject spawn — these are dictionary-property tests, no Awake/OnNetworkSpawn path required). Mirrors `BuildOrderTests.cs` shape.

- [ ] **Step 6: Commit**

```
feat(goap): four NEW JobBuilder GOAP actions

GoapAction_TakeMaterialFromABStorage — find AB storage furniture holding the
  next missing material, walk to it, queue CharacterTakeFromFurnitureAction.
GoapAction_GoToConstructionSite — walk into TargetBuilding.BuildingZone using
  the rule #36 anti-freeze pattern.
GoapAction_DropMaterialAtZone — queue CharacterDropItem inside the zone so
  CharacterAction_FinishConstruction can consume it next tick.
GoapAction_FinishBuildingConstruction — wrap the Phase 1 continuous action.

Plan 4b Task 3 of 5.
```

---

## Task 4: JobBuilder class

**Files:**
- Modify: `Assets/Scripts/World/Jobs/JobCategory.cs` (add `Builder` enum value — append last).
- Create: `Assets/Scripts/World/Jobs/BuilderJobs/JobBuilder.cs`
- Create: `Assets/Editor/Tests/JobBuilder/JobBuilderClassTests.cs`

- [ ] **Step 1: JobCategory enum**

```csharp
public enum JobCategory
{
    Harvester,
    Crafting,
    Service,
    Transport,
    Builder,   // ← append last
}
```

- [ ] **Step 2: JobBuilder.cs**

Mirror `JobFarmer.cs` line-for-line where possible. Key differences:
- `Category = JobCategory.Builder`; `Type = JobType.Builder`.
- `Workplace` runtime cast: `_workplace is AdministrativeBuilding ab`.
- `CurrentBuildOrder` public accessor: `_workplace?.BuildingLogisticsManager?.GetFirstActiveBuildOrder()` (called per tick — cheap, returns the first order or null).
- `GetWorkSchedule()` returns 6h-18h Work (same as JobFarmer).
- `Assign` adds the worker to `_workplace`'s employee list via `_workplace.AddEmployee(worker)` (same as JobFarmer pattern).
- `Unassign` removes the worker + cleans up GOAP state.
- `HasWorkToDo()` returns `true` if there's any active BuildOrder OR the worker is carrying something deposit-able (mirrors JobFarmer's carried-item branch so a builder who clocks off mid-trip still deposits).

```csharp
[System.Serializable]
public class JobBuilder : Job
{
    [SerializeField] private string _jobTitle;
    [SerializeField] private JobType _jobType;

    public override string JobTitle => _jobTitle;
    public override JobCategory Category => JobCategory.Builder;
    public override JobType Type => _jobType;
    public override float ExecuteIntervalSeconds => 0.3f;

    // GOAP
    private GoapGoal _currentGoal;
    private List<GoapAction> _availableActions;
    private List<GoapAction> _scratchValidActions = new List<GoapAction>(8);
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    private readonly Dictionary<string, bool> _scratchWorldState = new Dictionary<string, bool>(16);
    private GoapGoal _cachedDeliverGoal;
    private GoapGoal _cachedFetchGoal;
    private GoapGoal _cachedIdleAtABGoal;
    private GoapGoal _cachedIdleGoal;

    private float _lastIdleDumpTime = -10f;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _currentGoal != null ? _currentGoal.GoalName : "No Goal";

    /// <summary>Server-only convenience. Returns the first active BuildOrder on this
    /// builder's AB workplace, or null if none.</summary>
    public BuildOrder CurrentBuildOrder
    {
        get
        {
            if (_workplace == null) return null;
            var blm = _workplace.BuildingLogisticsManager;
            return blm != null ? blm.GetFirstActiveBuildOrder() : null;
        }
    }

    public JobBuilder(string jobTitle = "Builder", JobType jobType = JobType.Builder)
    {
        _jobTitle = jobTitle;
        _jobType = jobType;
    }

    public override void Execute()
    {
        if (_workplace == null || !(_workplace is AdministrativeBuilding ab)) return;

        // Standard tick-the-action pattern from JobFarmer:
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
        PlanNextActions(ab);
    }

    private void PlanNextActions(AdministrativeBuilding ab)
    {
        BuildOrder order = CurrentBuildOrder;
        bool hasActiveBuildOrder = order != null;

        // Carry-aware: are we carrying ANY material that helps the active order?
        bool hasMaterialsInHand = false;
        if (hasActiveBuildOrder)
        {
            var carried = GetCarriedItem(_worker);
            if (carried != null)
            {
                foreach (var (item, missing) in order.GetMissingMaterials())
                {
                    if (item == carried.ItemSO) { hasMaterialsInHand = true; break; }
                }
            }
        }

        // Storage-aware: is the AB's storage chain holding any missing material?
        bool hasMatchingMaterialInABStorage = false;
        if (hasActiveBuildOrder)
        {
            foreach (var (item, _) in order.GetMissingMaterials())
            {
                if (ab.FindStorageFurnitureForItem(new ItemInstance(item)) != null)
                { hasMatchingMaterialInABStorage = true; break; }
            }
        }

        // Inside-zone: are we already in the construction zone? (the planner's chained
        // GoapAction_GoToConstructionSite handles the move, but we set the worldState
        // here so a builder who's already arrived doesn't backtrack.)
        bool insideConstructionSite = false;
        if (hasActiveBuildOrder && order.TargetBuilding != null && order.TargetBuilding.BuildingZone != null)
        {
            var bounds = order.TargetBuilding.BuildingZone.bounds;
            var pos = _worker.transform.position;
            insideConstructionSite =
                pos.x >= bounds.min.x && pos.x <= bounds.max.x &&
                pos.z >= bounds.min.z && pos.z <= bounds.max.z;
        }

        _scratchWorldState.Clear();
        _scratchWorldState["hasActiveBuildOrder"] = hasActiveBuildOrder;
        _scratchWorldState["hasMaterialsInHand"] = hasMaterialsInHand;
        _scratchWorldState["hasMatchingMaterialInABStorage"] = hasMatchingMaterialInABStorage;
        _scratchWorldState["insideConstructionSite"] = insideConstructionSite;
        _scratchWorldState["materialDelivered"] = false;
        _scratchWorldState["isIdling"] = false;

        if (_availableActions == null) _availableActions = new List<GoapAction>(6);
        _availableActions.Clear();
        _availableActions.Add(new GoapAction_TakeMaterialFromABStorage(this, ab));
        _availableActions.Add(new GoapAction_GoToConstructionSite(this));
        _availableActions.Add(new GoapAction_DropMaterialAtZone(this));
        _availableActions.Add(new GoapAction_FinishBuildingConstruction(this));
        _availableActions.Add(new GoapAction_IdleInBuilding(ab));

        if (_cachedDeliverGoal == null)
            _cachedDeliverGoal = new GoapGoal("DeliverAndConstruct",
                new Dictionary<string, bool> { { "isIdling", true } }, priority: 5);
        if (_cachedFetchGoal == null)
            _cachedFetchGoal = new GoapGoal("FetchFromABStorage",
                new Dictionary<string, bool> { { "hasMaterialsInHand", true } }, priority: 4);
        if (_cachedIdleAtABGoal == null)
            _cachedIdleAtABGoal = new GoapGoal("IdleAtAB",
                new Dictionary<string, bool> { { "isIdling", true } }, priority: 2);
        if (_cachedIdleGoal == null)
            _cachedIdleGoal = new GoapGoal("Idle",
                new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);

        // Cascade:
        GoapGoal targetGoal;
        if (hasMaterialsInHand && hasActiveBuildOrder) targetGoal = _cachedDeliverGoal;
        else if (hasActiveBuildOrder && hasMatchingMaterialInABStorage) targetGoal = _cachedFetchGoal;
        else if (hasActiveBuildOrder) targetGoal = _cachedIdleAtABGoal;
        else targetGoal = _cachedIdleGoal;

        _currentGoal = targetGoal;

        _scratchValidActions.Clear();
        for (int i = 0; i < _availableActions.Count; i++)
        {
            var a = _availableActions[i];
            if (a.IsValid(_worker)) _scratchValidActions.Add(a);
        }

        _currentPlan = GoapPlanner.Plan(_scratchWorldState, _scratchValidActions, targetGoal);
        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[JobBuilder]</color> {_worker.CharacterName} : new plan ({_currentGoal.GoalName}); first action → {_currentAction.ActionName}");
        }
        // Idle-fallback diagnostic dump: mirror JobFarmer's pattern (1 Hz throttle).
        // ... (full block follows the JobFarmer template)
    }

    // GetCarriedItem helper — same pattern as GoapAction_GatherStorageItems.

    public override bool CanExecute() => base.CanExecute() && _workplace is AdministrativeBuilding;

    public override bool HasWorkToDo()
    {
        if (_workplace is not AdministrativeBuilding ab) return false;
        if (ab.BuildingLogisticsManager != null && ab.BuildingLogisticsManager.ActiveBuildOrders.Count > 0) return true;
        // Carry-completion guard: if we're carrying a build material, deposit-trip is work-to-do.
        var carried = GetCarriedItem(_worker);
        return carried != null;
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
        if (workplace is AdministrativeBuilding ab) ab.AddEmployee(worker);
    }

    public override void Unassign()
    {
        if (_workplace is AdministrativeBuilding ab && _worker != null) ab.RemoveEmployee(_worker);
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

- [ ] **Step 3: Tests**

`Assets/Editor/Tests/JobBuilder/JobBuilderClassTests.cs`:
- `JobBuilder_Constructor_DefaultsTitleAndType`
- `JobBuilder_Category_IsBuilder`
- `JobBuilder_GetWorkSchedule_Returns6To18`
- `JobBuilder_HasWorkToDo_FalseWhenNoOrderAndNoCarry`
- `JobBuilder_HasWorkToDo_TrueWhenOrderExists`
- `JobBuilder_CanExecute_FalseWhenWorkplaceNotAB`

- [ ] **Step 4: Commit**

```
feat(jobs): JobBuilder class with goal cascade

Mirror JobFarmer shape — ExecuteIntervalSeconds=0.3f, scratch worldState dict,
_scratchValidActions pre-filter, cached goals, force-replan on action completion.
Goal cascade: DeliverAndConstruct → FetchFromABStorage → IdleAtAB → Idle.
JobCategory.Builder appended.

Plan 4b Task 4 of 5.
```

---

## Task 5: AdministrativeBuilding wiring

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`
- Create: `Assets/Scripts/World/Jobs/BuilderJobs/UnfulfillableMaterial.cs`

- [ ] **Step 1: UnfulfillableMaterial backing type**

```csharp
[System.Serializable]
public class UnfulfillableMaterial
{
    public ItemSO Item;
    public int Qty;
    public int LastEnqueuedDay;

    public UnfulfillableMaterial(ItemSO item, int qty, int day)
    {
        Item = item; Qty = qty; LastEnqueuedDay = day;
    }
}
```

- [ ] **Step 2: AdministrativeBuilding.InitializeJobs**

Replace the empty body:

```csharp
protected override void InitializeJobs()
{
    _jobs.Add(new JobBuilder("Builder"));
    _jobs.Add(new JobBuilder("Builder 2"));
    _jobs.Add(new JobHarvester("Harvester"));
    _jobs.Add(new JobLogisticsManager("Logistics Manager"));
}
```

- [ ] **Step 3: AdministrativeBuilding._unfulfillableMaterialHarvestQueue + methods**

```csharp
private readonly List<UnfulfillableMaterial> _unfulfillableMaterialHarvestQueue = new List<UnfulfillableMaterial>();

public IReadOnlyList<UnfulfillableMaterial> GetUnfulfillableHarvestQueue() => _unfulfillableMaterialHarvestQueue;

/// <summary>Server-only. Adds (or increments) a wanted material that the AB's logistics
/// chain couldn't source from shops / crafters / virtual suppliers. JobHarvester reads
/// this queue and goes physical-harvest hunting for it.</summary>
public void EnqueueUnfulfillableMaterial(ItemSO item, int qty)
{
    if (!IsServer || item == null || qty <= 0) return;
    int day = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
    for (int i = 0; i < _unfulfillableMaterialHarvestQueue.Count; i++)
    {
        var entry = _unfulfillableMaterialHarvestQueue[i];
        if (entry.Item == item)
        {
            entry.Qty = Mathf.Max(entry.Qty, qty);   // dedupe — keep the highest demand
            entry.LastEnqueuedDay = day;
            return;
        }
    }
    _unfulfillableMaterialHarvestQueue.Add(new UnfulfillableMaterial(item, qty, day));
}

/// <summary>Server-only. Decrements (or removes) a queued material entry after a successful
/// harvest deposit. Clamps to zero; removes when qty hits zero.</summary>
public void DecrementUnfulfillableMaterial(ItemSO item, int qtyDelivered)
{
    if (!IsServer || item == null || qtyDelivered <= 0) return;
    for (int i = 0; i < _unfulfillableMaterialHarvestQueue.Count; i++)
    {
        var entry = _unfulfillableMaterialHarvestQueue[i];
        if (entry.Item == item)
        {
            entry.Qty = Mathf.Max(0, entry.Qty - qtyDelivered);
            if (entry.Qty == 0) _unfulfillableMaterialHarvestQueue.RemoveAt(i);
            return;
        }
    }
}
```

- [ ] **Step 4: BuildOrder lifecycle hook**

Subscribe to the BuildOrder's `OnStateChanged` in `AddBuildOrder` (the facade method) so that when `IsCompleted` flips, the order auto-removes itself. Actually — this is cleaner on `BuildingLogisticsManager` itself (already wired in Task 2 commits). Verify: read `LogisticsOrderBook.AddBuildOrder` to confirm OnStateChanged is wired; if not, add the subscription here. (Quick grep before writing.)

- [ ] **Step 5: Commit**

```
feat(building): AdministrativeBuilding job slots + unfulfillable-material queue

InitializeJobs adds JobBuilder x2 + JobHarvester + JobLogisticsManager.
EnqueueUnfulfillableMaterial / GetUnfulfillableHarvestQueue / DecrementUnfulfillableMaterial
provide the JobHarvester CityHarvester read+write surface for materials that the
logistics chain couldn't source.

Plan 4b Task 5 of 5.
```

---

## Task 6: JobLogisticsManager.ProcessActiveBuildOrders

**Files:**
- Modify: `Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs`

- [ ] **Step 1: Add ProcessActiveBuildOrders**

Read the existing `JobLogisticsManager.Execute()` first to find the right insertion point — after the existing `ProcessActiveBuyOrders()` and the restock-evaluation pass, before any idle-handling fallback.

```csharp
private void ProcessActiveBuildOrders()
{
    if (_workplace == null) return;
    var blm = _workplace.BuildingLogisticsManager;
    if (blm == null) return;

    var stockEvaluator = blm.StockEvaluator;
    if (stockEvaluator == null) return;

    var orders = blm.ActiveBuildOrders;
    for (int i = 0; i < orders.Count; i++)
    {
        var order = orders[i];
        if (order == null || order.IsCompleted) continue;

        foreach (var (itemSO, missing) in order.GetMissingMaterials())
        {
            int inFlight = stockEvaluator.GetInFlightBuyOrderCount(itemSO) + CurrentStorageCount(itemSO);
            int needed = missing - inFlight;
            if (needed <= 0) continue;

            bool success = stockEvaluator.RequestStock(itemSO, needed);
            if (!success && _workplace is AdministrativeBuilding ab)
            {
                ab.EnqueueUnfulfillableMaterial(itemSO, needed);
            }
        }
    }
}

private int CurrentStorageCount(ItemSO item)
{
    if (_workplace == null || _workplace.Inventory == null) return 0;
    int count = 0;
    foreach (var stack in _workplace.Inventory)
    {
        if (stack.ItemSO == item) count += stack.Count;
    }
    return count;
}
```

- [ ] **Step 2: Hook into Execute**

```csharp
public override void Execute()
{
    // existing tick + planning ...
    // After ProcessActiveBuyOrders + restock pass:
    ProcessActiveBuildOrders();
}
```

Exact placement TBD by reading existing Execute structure; the key invariant is: `ProcessActiveBuildOrders` runs once per `JobLogisticsManager` tick, after `ProcessActiveBuyOrders` so that new BuyOrders triggered by build orders compete with restock orders in the normal order book.

- [ ] **Step 3: Tests**

`Assets/Editor/Tests/JobBuilder/JobLogisticsManagerBuildOrderTests.cs`:
- `ProcessActiveBuildOrders_NoMissing_DoesNothing`
- `ProcessActiveBuildOrders_StockAvailable_NoBuyOrderPlaced`
- `ProcessActiveBuildOrders_RequestStockSucceeds_NoQueueGrowth`
- `ProcessActiveBuildOrders_RequestStockFails_EnqueuesOnAB`

These tests mock the StockEvaluator (or use a minimal stub) since the real one needs a full world setup; the test verifies the branching logic + AB queue side-effect.

- [ ] **Step 4: Commit**

```
feat(logistics): JobLogisticsManager.ProcessActiveBuildOrders cascade

Per tick, for each active BuildOrder, compute missing-qty - in-flight + storage,
call stockEvaluator.RequestStock. On RequestStock=false (no supplier in any tier),
enqueue (item, qty) on AdministrativeBuilding._unfulfillableMaterialHarvestQueue
for JobHarvester CityHarvester to pick up.

Plan 4b Task 6 of 5.
```

---

## Task 7: JobHarvester CityHarvester variant

**Files:**
- Modify: `Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs`

- [ ] **Step 1: Workplace check + new branch**

At the top of `JobHarvester.Execute()` (before the existing HarvestingBuilding branch):

```csharp
public override void Execute()
{
    if (_workplace is AdministrativeBuilding ab)
    {
        ExecuteCityHarvesterTick(ab);
        return;
    }
    // existing HarvestingBuilding branch unchanged ...
}

private void ExecuteCityHarvesterTick(AdministrativeBuilding ab)
{
    // Tick-the-action pattern (mirrors the existing branch):
    if (_currentAction != null)
    {
        // ... standard tick-validate-execute-complete loop
    }

    // Plan:
    var queue = ab.GetUnfulfillableHarvestQueue();
    if (queue.Count == 0)
    {
        // Idle
        return;
    }

    UnfulfillableMaterial wanted = queue[0]; // pick the first queued; FIFO for simplicity

    // Use CharacterAwareness to scan for a Harvestable yielding wanted.Item.
    Harvestable target = FindHarvestableYielding(_worker, wanted.Item);
    if (target == null)
    {
        // Nothing visible — fall back to wider scan or just idle.
        return;
    }

    // Plan using the existing harvest→pickup→deposit chain. Worker-state mirrors
    // JobHarvester's existing planner; the wanted-item filter becomes a constraint
    // on the HarvestResources action's target selection.
    // ... uses GoapAction_HarvestResources / PickupLooseItem / DepositResources
}

private static Harvestable FindHarvestableYielding(Character worker, ItemSO wanted)
{
    if (worker == null || wanted == null) return null;
    var awareness = worker.CharacterAwareness;
    if (awareness == null) return null;

    // CharacterAwareness exposes GetVisibleInteractables<T>(). Each Harvestable's
    // HasAnyYieldOutput accepts a List<ItemSO>; we wrap wanted in a single-item list.
    var wantedList = new List<ItemSO> { wanted }; // ALLOC: short-lived, called once per plan tick.
    foreach (var h in awareness.GetVisibleInteractables<Harvestable>())
    {
        if (h == null) continue;
        if (h.HasAnyYieldOutput(wantedList)) return h;
    }
    return null;
}
```

  - **API verification needed before writing:** confirm `CharacterAwareness.GetVisibleInteractables<T>()` signature; confirm `Harvestable.HasAnyYieldOutput(List<ItemSO>)` exists. If either is missing, fall back to a `Physics.OverlapSphereNonAlloc` scan from the worker's position with a radius (e.g. 30u) and a wanted-item filter walked per-collider.

- [ ] **Step 2: Hook DecrementUnfulfillableMaterial on deposit success**

The cleanest seam is the existing `JobLogisticsManager.OnItemHarvested` event (fired from `GoapAction_GatherStorageItems.FinishDropoff` / `GoapAction_DepositResources` paths). Subscribe in `JobHarvester.Assign` (when `Workplace is AdministrativeBuilding`):

```csharp
// In Assign:
if (workplace is AdministrativeBuilding) {
    var blm = workplace.BuildingLogisticsManager;
    if (blm != null) blm.OnItemHarvested += OnAbItemHarvested;
}

// In Unassign:
if (_workplace is AdministrativeBuilding) {
    var blm = _workplace.BuildingLogisticsManager;
    if (blm != null) blm.OnItemHarvested -= OnAbItemHarvested;
}

private void OnAbItemHarvested(ItemSO item)
{
    if (_workplace is AdministrativeBuilding ab) ab.DecrementUnfulfillableMaterial(item, 1);
}
```

  - **API verification:** confirm `BuildingLogisticsManager.OnItemHarvested` is the right event surface (read `JobLogisticsManager.OnItemHarvested` referenced in `GatherStorageItems.FinishDropoff` line 555). If not, expose it on the facade.

- [ ] **Step 3: Tests**

Minimal — `JobHarvester` is heavy enough that a unit test for the city branch ends up over-mocked. The unit-test surface for Plan 4b stops at the GOAP actions + JobLogisticsManager cascade; JobHarvester's city branch is verified via PlayMode-MP integration in Plan 4c.

- [ ] **Step 4: Commit**

```
feat(jobs): JobHarvester CityHarvester runtime branch

When the workplace is AdministrativeBuilding, JobHarvester reads the AB's
unfulfillable-material queue and runs the existing harvest→pickup→deposit
chain against any visible Harvestable yielding the wanted item. On successful
deposit (via OnItemHarvested), the AB queue entry is decremented.

Plan 4b Task 7 of 5.
```

---

## Task 8: SKILL.md / wiki / agents sync + final commit

**Files:**
- Modify: `.agent/skills/job_system/SKILL.md`
- Modify: `.agent/skills/goap/SKILL.md`
- Modify: `wiki/systems/administrative-building.md` (change log + Public API refresh)
- Modify: `wiki/systems/construction.md` (Phase 2 NPC autonomy unlocked)
- Create: `wiki/systems/job-builder.md` (full system page from template)
- Modify: `.claude/agents/npc-ai-specialist.md`

- [ ] **Step 1: job_system/SKILL.md**

Add a `JobBuilder` subsection: Job class shape, goal cascade, the 4 GOAP actions it owns. Cross-link to `wiki/systems/job-builder.md`. Mention the CityHarvester variant on JobHarvester.

- [ ] **Step 2: goap/SKILL.md**

Add the 4 new actions to the action registry. Note that `GoapAction_FinishBuildingConstruction` wraps `CharacterAction_FinishConstruction` (the Phase 1 continuous action).

- [ ] **Step 3: wiki/systems/administrative-building.md**

Add change log entry: `- 2026-05-18 — Plan 4b: InitializeJobs adds JobBuilder x2 + JobHarvester + JobLogisticsManager; unfulfillable-material harvest queue added — claude`. Bump `updated:` date. Refresh Public API section.

- [ ] **Step 4: wiki/systems/construction.md**

Append change log: `- 2026-05-18 — Phase 2 NPC autonomy: JobBuilder employed at AdministrativeBuilding drives autonomous construction via BuildOrder → JobLogisticsManager → JobHarvester cascade — claude`. Cross-link to `wiki/systems/job-builder.md`.

- [ ] **Step 5: wiki/systems/job-builder.md (NEW)**

Full 10-section system page from `wiki/_templates/system.md`:
- Purpose
- Responsibilities
- Key classes / files (JobBuilder, the 4 GOAP actions, JobLogisticsManager extension, JobHarvester CityHarvester branch)
- Public API
- Data flow (BuildOrder placed → JobLogisticsManager.ProcessActiveBuildOrders → RequestStock cascade → BuyOrder/CraftingOrder OR EnqueueUnfulfillableMaterial → JobBuilder picks up materials from storage → walks to site → drops → finishes construction)
- Dependencies (BuildOrder, AdministrativeBuilding, BuildingLogisticsManager, CharacterAction_FinishConstruction, JobFarmer template pattern)
- State & persistence (BuildOrders not persisted across server restarts in v1 — Plan 4c may add this)
- Gotchas (cross-link to rule #36 anti-freeze, the multi-trip replan pattern)
- Open questions (BuilderSkill formula tuning, BuildOrder persistence, multi-AB cities — explicitly out of scope)
- Change log
- Sources (link to SKILL.md, this plan doc, the spec)

- [ ] **Step 6: .claude/agents/npc-ai-specialist.md**

Add `JobBuilder` + the 4 new GOAP actions to the agent's domain registry. Same shape as the existing `JobFarmer` entry.

- [ ] **Step 7: Commit**

```
docs(plan-4b): wiki + skill + agent sync for JobBuilder + GOAP actions

wiki/systems/job-builder.md — new full system page.
wiki/systems/administrative-building.md + construction.md — change log entries.
.agent/skills/job_system + goap — JobBuilder subsection + 4 new actions.
.claude/agents/npc-ai-specialist.md — domain expansion.

Plan 4b of 5 (skeleton scope) complete for the City Founding spec.
```

---

## Verification

Manual smoke (post-merge — defers full integration to Plan 4c when the AB prefab + admin console ship):
- Spawn an `AdministrativeBuilding` in the Editor. Confirm `InitializeJobs` populates 4 job slots (visible in the building inspector).
- Hire 2 builders + 1 harvester + 1 logistics manager. Confirm each clocks in on the 6h-18h shift.
- Place a `BuildOrder` manually via Roslyn / debug command: `ab.BuildingLogisticsManager.AddBuildOrder(new BuildOrder(targetBuilding, ab, leader, day))`.
- Pre-populate the AB's StorageFurniture with the required materials. Confirm a builder picks one up, walks to the site, drops it, the construction progress bar advances, and on completion the building flips to Complete and the BuildOrder is removed.
- Repeat with the AB's storage empty + no shop on the map. Confirm `ProcessActiveBuildOrders` enqueues the material on `_unfulfillableMaterialHarvestQueue`. Confirm the harvester walks to a nearby tree (in a forest map biome), harvests, returns, deposits, and the queue decrements.
- Confirm the construction proceeds end-to-end with this fallback path.

EditMode tests:
- All `Assets/Editor/Tests/JobBuilder/*` tests green via `mcp__ai-game-developer__tests-run`.

PlayMode-MP smoke (handoff to Plan 4c):
- Two-client session — host places AB blueprint + finishes construction; client sees the progress bar live; both clients see the AB flip to Complete simultaneously.
- BuildOrder cascade replicates correctly to late-joiner — joining client sees in-progress structures and active workers.

---

## Risk register

- **R1: `CharacterAwareness.GetVisibleInteractables<T>()` may not exist or may have different semantics than assumed.** Mitigation: verify before writing `JobHarvester.FindHarvestableYielding`; fall back to `Physics.OverlapSphereNonAlloc` if needed.
- **R2: `BuildingLogisticsManager.OnItemHarvested` event might not be on the facade.** Mitigation: verify by reading `GatherStorageItems.FinishDropoff` (line 555 references `_manager.OnItemHarvested(item.ItemSO)`). If it's on `JobLogisticsManager` not `BuildingLogisticsManager`, expose a facade event.
- **R3: `LogisticsStockEvaluator.GetInFlightBuyOrderCount(ItemSO)` / `RequestStock(ItemSO, int)` exact signatures.** Mitigation: read `LogisticsStockEvaluator.cs` before writing `ProcessActiveBuildOrders`.
- **R4: `AdministrativeBuilding.FindStorageFurnitureForItem` doesn't exist (inherited from CommercialBuilding but worth verifying).** Mitigation: grep CommercialBuilding's storage API.
- **R5: BuildOrder auto-removal on completion.** The spec says "BuildOrder removed from _activeBuildOrders" when IsCompleted. This needs a wire — either (a) JobLogisticsManager removes it lazily on the next ProcessActiveBuildOrders tick (preferred, simpler), or (b) BuildOrder.OnStateChanged → LogisticsOrderBook.RemoveBuildOrder subscription wired in AddBuildOrder. Mitigation: check what `LogisticsOrderBook.AddBuildOrder` already does (Task 2 commit `fe36debc`); if it doesn't subscribe, add the subscription in Task 5 wiring.

---

## Out of scope (final summary)

- AB.prefab authoring with preplaced furniture (Plan 4c).
- Admin console UI + RTS-style placement flow (Plan 4c).
- Drifter migration + JoinRequestDesk (Plan 4c).
- Tier-up gating + CommunityTierRequirementsSO (Plan 4c).
- BuildOrder persistence across server restarts.
- BuilderSkill formula tuning.
- Multi-AB cities.

---

**Status:** ready to execute.
