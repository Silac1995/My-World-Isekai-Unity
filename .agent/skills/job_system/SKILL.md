---
name: job-system
description: The work ecosystem connecting the employee (CharacterJob), the pure data (Job), and the workplace (CommercialBuilding).
---

# Job System

Economy and work govern the game world. This skill details the physical-data-entity triad architecture that allows a character (NPC or Player) to hold a position in a building.

## The Holy Trinity of Work

The architecture strictly relies on three concepts to ensure that nobody works "in a vacuum":

1. The Employee -> `CharacterJob`
2. The Concept/Data -> `Job`
3. The Physical Location -> `CommercialBuilding`

### 1. The Employee (`CharacterJob`)
This is the component (MonoBehaviour) attached to the character.
- **Assignment Dictionaries (`JobAssignment`)**: Allows a character to have multiple jobs.
- **The Time Safeguard (`DoesScheduleOverlap`)**: When attempting to take a job (`TakeJob`), this algorithm checks that none of their current positions conflict with the new working hours of this job. 
- **AI Injection (`InjectWorkSchedule`)**: On success, `CharacterJob` will force the time slots (e.g., 8 AM - 5 PM Work) into the character's routine planner (`CharacterSchedule`), which will physically lead them to work.
- **Forced Imposition (`ForceAssignJob`)**: A Community Leader (`CommunityTracker.ImposeJobOnCitizen`) can forcefully assign a job. This intentionally dissolves and quits any existing overlapping jobs the character might have to make room for the new schedule, completely bypassing their choice.
- **Ownership**: Stores whether the character is the Boss/Owner of the Building.

### 2. The Role (`Job`)
Pure abstract C# class. This is the essence of the position (e.g., "Bartender").
- **Stateless/Data**: Contains the `JobTitle`, `Category`, and specifies the hours of the day dedicated to this role (`GetWorkSchedule()`).
- **Container**: Stores the references of `Worker` (who does the job) and `Workplace` (where it happens). A job can only have one worker. `IsAssigned` checks its availability.
- **The Action (`Execute()`)**: Method called every Tick during office hours. 
    - **Proactive AI Injection**: To prevent characters from stacking at the workplace entry, `Execute()` should push active behaviors (e.g. `PerformCraftBehaviour`, `WanderBehaviour`) to the character's controller if they are not already busy.
    - **State Management**: It manages the specific business logic (Forging, Assistance) by checking building needs.

### 3. The Location (`CommercialBuilding`)
The physical anchor in the scene.
- **Administration**: The building instantiates all its own Jobs in the abstract array (via `InitializeJobs()`).
- **Recruitment (`AskForJob`)**: For a character to volunteer for a position, the Building must have a direct Boss (`HasOwner`) OR exist in a map governed by a macro Community Leader (`HasCommunityLeader()`). The position must exist locally and be vacant. Alternatively, a Community Leader can forcefully assign work via `CommunityTracker.ImposeJobOnCitizen()`.
- **Punching In/Out**: Handled by strict `CharacterAction`s (`Action_PunchIn` / `Action_PunchOut`) triggered from a physical `TimeClockFurniture` authored inside the building. Players press E on the clock → `TimeClockFurnitureInteractable.Interact` → (client) `CommercialBuilding.RequestPunchAtTimeClockServerRpc` → server runs the action. NPCs walk to the clock in `BTAction_Work` / `BTAction_PunchOut` and call `Interact` directly (already server-side), sharing the player code path (Rule #22). Arrival is tested via `InteractableObject.IsCharacterInInteractionZone` (canonical — do NOT roll your own distance / collider math). `WorkerStartingShift` / `WorkerEndingShift` carry `!IsServer` guards (defence-in-depth); buildings with no clock authored yet fall back to the legacy "punch anywhere in `BuildingZone`" behaviour with a one-shot warning log.
- **Shift roster (who is punched in?)**: single-sourced from the replicated `NetworkList<FixedString64Bytes> _activeWorkerIds` on `CommercialBuilding`. `CommercialBuilding.IsWorkerOnShift(Character)` is the allocation-free containment check used by `BTAction_Work`, `BTAction_PunchOut`, `BTCond_NeedsToPunchOut`, `UI_CommercialBuildingDebugScript`, and the Time Clock interactable's `Punch In` vs `Punch Out` picker. `ActiveWorkersOnShift` is a materialiser that walks the list via `Character.FindByUUID` for UI / quest-eligibility code that needs full `Character` references; allocates a fresh list per call, safe for tick-rate consumers. Employment check (`IsWorkerEmployedHere`) similarly reads the replicated `_jobWorkerIds` NetworkList — both rosters answer identically on every peer.
- **Physical Dispersion**: `GetWorkPosition(Character)` provides a point within the `BuildingZone` with a unique offset (based on InstanceID) to ensure workers don't stack on top of each other.
- **Task Management (Blackboard Pattern)**: All Commercial Buildings require a `BuildingTaskManager`. Instead of workers individually running heavy `Physics.OverlapBox` queries every frame to find tasks, resources and systems register `BuildingTask`s (`HarvestResourceTask`, `PickupLooseItemTask`) to the building. Workers use `TaskManager.ClaimBestTask<T>()` to claim work autonomously via GOAP without race conditions.

### 4. Crafting (CraftingBuilding & JobCrafter)
Crafting follows a specialized overlay of this system.
- **CraftingBuilding**: A specialized `CommercialBuilding`. It scans its `ComplexRoom`s to find `CraftingStation`s and compiles a list of what can be manufactured there via `GetCraftableItems()`. 
- **JobCrafter**: The artisan job (e.g., Blacksmith).
   - **Requirements**: It requires the NPC to have a specific skill (`SkillSO`) and a minimum tier (`SkillTier` defined in `CharacterSkills`). Without this, the building refuses employment.
   - **Demand-Driven Logic**: The artisan does not produce in a vacuum. Their Behaviour Tree checks that the building's `BuildingLogisticsManager` has an active **`CraftingOrder`** (which follows the same time and reputation penalty logic as a `BuyOrder`). If there is an order, they find the right station, play their animation, and produce the item.

### 5. Logistics Cycle (BuildingLogisticsManager & JobLogisticsManager)
Every `CommercialBuilding` that needs supply management has a `BuildingLogisticsManager` component (a **facade** over `LogisticsOrderBook` + `LogisticsTransportDispatcher` + `LogisticsStockEvaluator`, under `Assets/Scripts/World/Buildings/Logistics/`) and employs a `JobLogisticsManager` worker.
- **Event-Driven & Physical**: Triggered by `OnWorkerPunchIn` natively on the Component (when the manager arrives at work) and `OnNewDay`.
- **Pending Order Queue**: Orders (`BuyOrder`, `CraftingOrder`, `TransportOrder`) are not executed instantly. They are added to a `PendingOrder` queue in the building. The worker's GOAP `Execute()` method pops these and pushes a `GoapAction_PlaceOrder`, forcing the character to physically travel to the target.
- **Autonomous Restock via `IStockProvider`**: Any `CommercialBuilding` can implement `IStockProvider` to declare `(ItemSO, MinStock)` targets. `ShopBuilding` projects its `_itemsToSell`; `CraftingBuilding` exposes `_inputStockTargets` in the Inspector. On `OnWorkerPunchIn`, the evaluator runs `CheckStockTargets(provider)` (unified for shops + crafters) and then `CheckCraftingIngredients(crafting)` for any already-placed commission aggregation. This is what finally lets an idle forge proactively request its own iron bars / coal — previously it only worked after a `CraftingOrder` came in.
- **Pluggable `LogisticsPolicy` SO**: Stocking strategy is a per-building `ScriptableObject` in the Inspector. Ships three policies: `MinStockPolicy` (default, refill to MinStock), `ReorderPointPolicy` (threshold % + multiplier), `JustInTimePolicy` (fixed batch size). Falls back to `Resources/Data/Logistics/DefaultMinStockPolicy` and then to a runtime `MinStockPolicy` with a one-time warning. See [`logistics-cycle` SKILL](../logistics_cycle/SKILL.md).
- **Order Types**: `BuyOrder` (inter-building commercial contract), `CraftingOrder` (internal production request), and `TransportOrder` (physical delivery of completed goods).
- **Physical Handshake (`IsPlaced`)**: Orders are only considered officially placed when `InteractionPlaceOrder` succeeds face-to-face. If the target is busy, the interaction fails, and the GOAP action will retry later by re-queueing the order because the `IsPlaced` flag remains `false`.
- **Duplicate Prevention**: Before placing/enqueuing an order, the `BuildingLogisticsManager` checks local logs (`_placedBuyOrders`, `_placedTransportOrders`) to avoid duplicating requests that are already active or awaiting physical interaction.
- **Expiration**: Orders have a `RemainingDays` counter. Expired orders trigger reputation penalties (`CharacterRelation.UpdateRelation`).
- **Diagnostics**: `BuildingLogisticsManager._logLogisticsFlow` (Inspector bool, exposed as `LogLogisticsFlow`) emits `[LogisticsDBG]` traces through the whole chain when enabled. `JobLogisticsManager` routes its early-exit log through the same flag so one building can be traced in isolation. Missing-`TransporterBuilding` is now a `Debug.LogError` with full context (was a silent warning pre-refactor).

### 6. Transporter (JobTransporter)
- **Logistics Delivery Mechanism**: Built to physically move items between a `CraftingBuilding` and a `ShopBuilding` following a `TransportOrder`.
- **Native GOAP Integration**: Unlike older FSM jobs, `JobTransporter` natively runs a GOAP planner inside its `Execute()` method.
  - Generates a `DeliverItems` goal if it has an active `TransportOrder`.
  - Pushes `GoapAction_LoadTransport` (travels to the source building and triggers `CharacterPickUpItem`) followed by `GoapAction_UnloadTransport` (travels to the destination building and triggers `CharacterDropItem`).
  - Idles in its assigned building using `GoapAction_IdleInCommercialBuilding` when waiting for new orders.

### 7. Work Positions
- **`CommercialBuilding.GetWorkPosition(Character)`**: Virtual method returning where a worker should stand. Defaults to `GetRandomPointInBuildingZone()`.
- **ShopBuilding Override**: Vendors go to a specific `VendorPoint` Transform (counter), all others wander in the building zone.
- **BTVendorBehaviour**: If a `VendorPoint` exists, the vendor paths to it before serving. If not, they wander in the building zone.

## How to Create a New Job?
In the future, if the AI Agent needs to create a "Blacksmith":
1. Write the abstract `JobCrafter` code, then `JobBlacksmith` inheriting from `JobCrafter`. Define its schedule, its `SkillSO`/`SkillTier` prerequisites, and its BT node `BTAction_PerformCraft`.
2. Create or modify the `ForgeBuilding` inheriting from `CraftingBuilding` (and not just `CommercialBuilding`) so its `InitializeJobs()` function adds a `JobBlacksmith` + a `JobLogisticsManager` (for orders).
3. Done! The player can go ask for the job (if they have the right skill level), and place crafting orders with the Logistics Manager.

## Save / Load Restoration

Job assignments survive both **map hibernation** and **player profile import** via a two-sided resolver. The same `(workerCharacterId, jobType, workplaceBuildingId)` link is stored on both ends so whichever side spawns first can rebuild the binding.

### Building side (authoritative)
`BuildingSaveData.Employees` (in `CommunityTracker`) holds the full crew per building. On load, `MapController.SpawnSavedBuildings()` / `WakeUp()` calls `CommercialBuilding.RestoreFromSaveData(ownerIds, employees)` immediately after `bNet.Spawn()`. The resolver:
- Tries `Character.FindByUUID` for each entry.
- Owner is bound via `SetOwner(owner, ownerJob, autoAssignJob: false)` — the new `autoAssignJob` flag bypasses the LogisticsManager auto-pick so the saved data is authoritative.
- Workers are bound via `worker.CharacterJob.TakeJob(job, building)`.
- Anything unresolved subscribes to `Character.OnCharacterSpawned` until empty (then unsubscribes). Cleanup in `OnNetworkDespawn` for re-hibernation.

### Character side
`CharacterJob.Deserialize(JobSaveData)` parks unresolved entries in `_pendingJobData`, then:
- Tries to bind any building already in `BuildingManager.allBuildings` (workplace map currently active).
- Subscribes to `BuildingManager.OnBuildingRegistered` for unresolved entries (workplace map hibernated and will spawn its buildings later).
- `Serialize()` re-emits unresolved pending entries so they survive multiple save/load cycles.

### Why both sides?
- **Building-first** (workplace map wakes, character lives elsewhere): building-side resolver waits on `Character.OnCharacterSpawned`.
- **Character-first** (player profile imports, workplace hibernated): character-side resolver waits on `BuildingManager.OnBuildingRegistered`.
- **Both already loaded**: whichever resolver fires first wins; the other's `alreadyActive`/`IsAssigned` guard prevents double-binding.

## Worker Replication (`Job.IsAssigned` / `Job.Worker` on clients)

`Job` is a plain `[System.Serializable]` class, not a `NetworkBehaviour`. Its `_worker` / `_workplace` fields are set by `Job.Assign(worker, workplace)`, which is called only on the server (through `CommercialBuilding.AssignWorker`). Without replication, every client would see `_worker == null` — so `Job.IsAssigned` would return false, and client-side queries (the hold-E "Apply for Job" menu, `IsOperational`, `UI_CommercialBuildingDebugScript`, etc.) would think every slot is vacant.

The replication model:

- `CommercialBuilding._jobWorkerIds` — `NetworkList<FixedString64Bytes>`, **parallel to `_jobs` by index**. Empty string = unassigned. Server writes it inside `AssignWorker` / `RemoveWorker`; NGO replicates to all peers automatically.
- `OnNetworkSpawn` pads the list to `_jobs.Count` on server, subscribes `HandleJobWorkerIdChanged`, and calls `SyncAllJobWorkersFromList()` once on clients to materialise the initial replicated state into local `Job._worker` via `Job.Assign(c, this)`.
- `HandleJobWorkerIdChanged` fires on clients whenever the server mutates a slot; it maps the changed index back to a `Character` via `Character.FindByUUID`, then calls `Job.Assign` / `Job.Unassign` locally so `job.IsAssigned` / `job.Worker` now answer correctly.
- **Late-join / map wake-up deferral**: if `FindByUUID` returns null (character not yet spawned on this peer), the slot is queued in `_pendingJobWorkerBinds` and a one-shot `Character.OnCharacterSpawned` subscription retries until the dictionary empties (mirrors the existing `RestoreFromSaveData` pattern). Cleanup in `OnNetworkDespawn`.
- **Server is source of truth**: `HandleJobWorkerIdChanged` short-circuits on `IsServer` — the server already has the correct `_worker` reference from `job.Assign`, and re-applying could flip state on `Clear`/`RemoveAt` events.

**Parallel-by-index invariant**: every peer runs `InitializeJobs()` locally in `Awake`, so `_jobs` has identical length and order across host, client, and NPC simulation. Never mutate `_jobs` conditionally per peer — breaking the invariant would desync the list.

**Consequence for call sites**: `job.IsAssigned` / `job.Worker` now return correct values on every peer. Any existing call site (`CharacterJob.GetInteractionOptions`, `CommercialBuilding.IsOperational`, `CommercialBuilding.GetAvailableJobs`, debug UIs, etc.) works without modification.

**Server-only API**: `AssignWorker` / `RemoveWorker` are now `if (!IsServer) return` guarded. Client callers must route through `CharacterJob.RequestJobApplicationServerRpc` or equivalent.

## Wage Integration

Jobs now carry a wage contract per assignment, seeded at hire-time and mutable by owners afterwards.

- **Hire-time seeding**: `CharacterJob.TakeJob` calls `WageSystemService.Instance?.SeedAssignmentDefaults(assignment)` after the `JobAssignment` is created and registered. This populates the new wage fields from the building's `JobWageDefaults` (or zero-fallback if the service is missing).
- **Per-assignment wage fields**: `JobAssignment` now carries `Currency` (`CurrencyId`), `PieceRate`, `MinimumShiftWage`, and `FixedShiftWage`. These travel with the worker across save/load (see `.agent/skills/save-load-system/SKILL.md` for the `JobAssignmentSaveEntry` extension) and are the single source of truth at payment time.
- **Owner-side mutation**: `CommercialBuilding.TrySetAssignmentWage(requester, worker, currency, pieceRate, minShiftWage, fixedShiftWage)` is the only public path that edits a live assignment's wage. It enforces owner / community-leader gating before mutating the worker's `JobAssignment`. UI and quest scripts must go through this method — never write the wage fields directly.
- **Per-job credit hooks**: punch-in/out fires in `Action_PunchIn` / `Action_PunchOut`, but the actual unit-of-work credit lives in each Job's gameplay path:
  - `JobHarvester` deposits credit through `GoapAction_DepositResources` (deficit-bounded; see `.agent/skills/logistics_cycle/SKILL.md`).
  - `JobCrafter` credits one unit per completed craft.
  - `JobTransporter` credits per item unloaded (see `.agent/skills/logistics_cycle/SKILL.md`).
- All hooks call `worker.CharacterWorkLog.LogShiftUnit(workplace, units)`. See `.agent/skills/character-worklog/SKILL.md` for the LogShiftUnit API and shift-window semantics.
- See `.agent/skills/wage-system/SKILL.md` for the full wage formula (piece-rate × units, clamped by min/max shift wage, plus the fixed-shift component) and the payer architecture (who debits whose `CharacterWallet`).

## Strict Architectural Rules
- **Interaction Distance**: To interact with an object or get in range, **always** use `InteractableObject.IsCharacterInInteractionZone(character)`. Never use `Vector3.Distance(workerPos, target)` for an "am I close enough" decision — 3D distance is sensitive to authored Y on InteractionPoint Transforms (e.g. an anvil's interaction anchor floating above the mesh) and produces stuck-at-arrival loops where the NavMeshAgent has reached the destination in 2D but the threshold check still fails. The InteractionZone collider is the single source of truth across BT, GOAP, server-side RPCs, and player input. Job code that triggers a `CharacterAction` at a station also re-validates the zone in the action's `CanExecute` and `OnApplyEffect` (see `CharacterCraftAction`) so the worker can't ghost-finish from outside the zone. If a station is genuinely missing an InteractableObject sibling, fall back to a horizontal-distance check (Y-flattened) with a one-shot `LogWarning` naming the prefab — never silently use 3D distance.
- **Worker furniture lookup uses the building's helper, never `cb.Rooms` directly.** `CraftingBuilding.GetAllStations()` is the canonical "every CraftingStation that physically belongs to this building" enumerator. It walks `Room.FurnitureManager._furnitures` (recursive over MainRoom + all SubRooms via `ComplexRoom.GetAllRooms()`) **and then** supplements with a transform-tree scan via `GetComponentsInChildren<CraftingStation>(true)`, deduped through a `HashSet<CraftingStation>`. This is robust to the `_defaultFurnitureLayout` registration race — stations that spawned via `TrySpawnDefaultFurniture` but didn't make it into a Room's `_furnitures` list (slot.TargetRoom unset, FurnitureGrid null on target room, mid-OnNetworkSpawn timing) still contribute to the search. The fallback emits a `LogWarning` naming the unregistered station so authors fix the prefab; production behaviour is unaffected. Same pattern in `CraftingBuilding.GetCraftableItems` / `GetStationsOfType` for the supplier-lookup path. Use `GetAllStations()` from `JobBlacksmith.HandleSearchOrder` and any future job that picks a station; do not iterate `Rooms.GetFurnitureOfType<CraftingStation>()` directly.
- **Don't cache stateful `GoapAction` instances across plans in `Job.PlanNextActions`.** `GoapAction_PlaceOrder`, `_StageItemForPickup`, `_GatherStorageItems`, etc. carry per-plan state (`_isComplete`, `_isMoving`, target refs). Reusing the same instance across plans risks `_isComplete=true` leaking from a previous plan, `IsValid` returns false on the next plan, the planner can't satisfy the goal, and any remaining queued orders stall (e.g. shop only ever places the *first* `BuyOrder` and never the rest). The safe pattern: pool the *list wrapper* (`_availableActions = new List<GoapAction>(N); _availableActions.Clear(); _availableActions.Add(new GoapAction_X(this));...`), pool the worldState `Dictionary<string,bool>` (`_scratchWorldState.Clear(); _scratchWorldState["..."] = ...`), and cache `GoapGoal` instances when their `DesiredState` dict is constant — but never cache the action instances themselves. `JobHarvester` and `JobTransporter` use this pattern; `JobLogisticsManager` had to be reverted to it after a regression. See `.agent/skills/goap/SKILL.md` §8.5 for the worldState/goal pooling pattern.
- **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE.
- **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
- **Centralized Drop Execution**: NEVER manually instantiate items on the ground via `WorldItem.SpawnWorldItem()` inside Job or GOAP scripts. You must always funnel dropping through the physical execution pipeline by calling `worker.CharacterActions.ExecuteAction(new CharacterDropItem(worker, item))` to ensure animations, inventory extractions, and ground offsets are perfectly synchronized.
- **Job Cancellation Null-Safety**: When an invalid state logically forces a job to abort (`CancelCurrentOrder()`), it will aggressively forcefully nullify `_currentAction = null` down the chain. Wrapper execution loops (like `JobTransporter.Execute()`) must rigidly verify `if (_currentAction != null)` upon returning from `.Execute()` before blindly querying `_currentAction.IsComplete`, preventing fatal `NullReferenceException` game locks.

## Player Entry Point: Hold-E Menu Provider (2026-04-24)

`CharacterJob` implements `IInteractionProvider`. When a player holds E on a character that owns a `CommercialBuilding` (`IsOwner == true`) with vacant jobs, the hold-E menu emits one `"Apply for {JobTitle}"` entry per vacant job.

- **Gating:** Entry is disabled with `(you already have a job)` when the interactor's `CharacterJob.HasJob == true`. Entries are NOT emitted for fully-staffed buildings, non-owner targets, or targets missing `CharacterJob`.
- **Stable job index:** the provider iterates the full `CommercialBuilding.Jobs` list (not the volatile `GetAvailableJobs()` subset) and passes the natural index to the click handler. This survives races where another NPC takes a slot between menu-build and click — the server revalidates `!job.IsAssigned` before constructing the invitation.
- **Click routing:** `IsServer` → direct `InteractionAskForJob(building, job).Execute(interactor, owner)`. Remote clients route via `RequestJobApplicationServerRpc(ownerNetId, jobStableIndex)`. Server re-validates ownership, index range, and vacancy before executing.
- **Player-to-player support:** `InteractionAskForJob` inherits `InteractionInvitation`, so player-owner targets automatically get the `UI_InvitationPrompt` accept/refuse flow with no new UI.

### Ownership state consistency fix shipped alongside

The old `CharacterJob._ownedBuilding` private field was a cached reference set by `BecomeOwner`, but it wasn't replicated — remote clients saw it as null even when the server had assigned ownership, breaking both the provider's `IsOwner` gate and any consumer of `CharacterJob.OwnedBuilding`. Fix: `OwnedBuilding` is now derived — it scans `BuildingManager.Instance.allBuildings` for the first `CommercialBuilding` whose `Room._ownerIds` NetworkList lists this character (`Room.IsOwner(Character)`). `IsOwner` is derived from `OwnedBuilding != null`. Since `Room._ownerIds` is a replicated NetworkList, every peer now sees consistent ownership.

### New public APIs
- `CharacterJob.OwnedBuilding` (property, was `_ownedBuilding` private field).
- `CharacterJob.GetInteractionOptions(interactor)` — `IInteractionProvider` hook.
- `CharacterJob.RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)` — `[Rpc(SendTo.Server)]` entry point.

## Quest Integration (2026-04-23)

`BuildingTask` now implements `MWI.Quests.IQuest` directly (Hybrid C unification). Existing NPC GOAP code (`BuildingTaskManager.ClaimBestTask<T>`) is unchanged — the returned objects just additionally satisfy `IQuest`. New player-side flow:

- `CommercialBuilding.WorkerStartingShift` auto-claims eligible quests for on-shift players AND subscribes for future-published ones during the shift.
- Eligibility per (JobType, IQuest concrete type) lives in `CommercialBuilding.DoesJobTypeAcceptQuest` switch — extend when adding new jobs/quests.
- `JobAssignment` already carries the wage fields; quest claim is orthogonal — workers can be claimed onto a quest without changing their employment.
- **Bookkeeping symmetry between the two claim paths**: NPC GOAP keeps using `BuildingTaskManager.ClaimBestTask<T>` / `UnclaimTask` (which mutate `Available`/`InProgress` buckets and call `Claim`/`Unclaim` on the task). Players go through `CharacterQuestLog.TryClaim/TryAbandon → IQuest.TryJoin/TryLeave`, which now also notifies the manager via `Manager.NotifyTaskExternallyClaimed` / `NotifyTaskExternallyUnclaimed`. The `Manager` back-reference on `BuildingTask` is wired in `RegisterTask`. Without this symmetry the InProgress bucket would either keep an orphaned entry (debug HUD shows "Unknown Worker") or never enter InProgress at all (NPC scheduler would see the task as still available).
- **GOAP↔auto-claim handoff (2026-04-29)**: `WorkerStartingShift` auto-claims eligible tasks (e.g. `HarvestResourceTask`, `DestroyHarvestableTask` for Woodcutter/Miner/Forager/Farmer) into `_inProgressTasks` *before* the worker's first GOAP plan tick. The GoapAction's `Execute` calls `ClaimBestTask<T>` which only walks `_availableTasks` — it returns null even though the worker DOES have a valid claim sitting in `_inProgressTasks`. Result was an Idle/Action ping-pong loop. **Pattern**: every GoapAction that consumes a `BuildingTask` MUST first call `BuildingTaskManager.FindClaimedTaskByWorker<T>(worker, predicate)` to retrieve any pre-existing claim, then fall back to `ClaimBestTask` only if none exists. See `GoapAction_DestroyHarvestable` / `GoapAction_HarvestResources` for canonical implementations. When adding a new task type that's quest-eligible, mirror this handoff pattern in its consumer GoapAction.

See `.agent/skills/quest-system/SKILL.md` and `wiki/systems/quest-system.md` for the full architecture.
