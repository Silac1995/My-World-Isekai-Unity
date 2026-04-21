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
- **Punching In/Out**: Handled by strict `CharacterAction`s (`Action_PunchIn` / `Action_PunchOut`). A character cannot telepathically start working; their `WorkBehaviour` first pushes a `PunchInBehaviour` to physically navigate them inside `BuildingZone.bounds`, which then spawns the `Action_PunchIn` to call `WorkerStartingShift`.
- **Physical Dispersion**: `GetWorkPosition(Character)` provides a point within the `BuildingZone` with a unique offset (based on InstanceID) to ensure workers don't stack on top of each other.
- **Task Management (Blackboard Pattern)**: All Commercial Buildings require a `BuildingTaskManager`. Instead of workers individually running heavy `Physics.OverlapBox` queries every frame to find tasks, resources and systems register `BuildingTask`s (`HarvestResourceTask`, `PickupLooseItemTask`) to the building. Workers use `TaskManager.ClaimBestTask<T>()` to claim work autonomously via GOAP without race conditions.

### 4. Crafting (CraftingBuilding & JobCrafter)
Crafting follows a specialized overlay of this system.
- **CraftingBuilding**: A specialized `CommercialBuilding`. It scans its `ComplexRoom`s to find `CraftingStation`s and compiles a list of what can be manufactured there via `GetCraftableItems()`. 
- **JobCrafter**: The artisan job (e.g., Blacksmith).
   - **Requirements**: It requires the NPC to have a specific skill (`SkillSO`) and a minimum tier (`SkillTier` defined in `CharacterSkills`). Without this, the building refuses employment.
   - **Demand-Driven Logic**: The artisan does not produce in a vacuum. Their Behaviour Tree checks that the building's `BuildingLogisticsManager` has an active **`CraftingOrder`** (which follows the same time and reputation penalty logic as a `BuyOrder`). If there is an order, they find the right station, play their animation, and produce the item.

### 5. Logistics Cycle (BuildingLogisticsManager & JobLogisticsManager)
Every `CommercialBuilding` that needs supply management has a `BuildingLogisticsManager` component and employs a `JobLogisticsManager` worker.
- **Event-Driven & Physical**: Triggered by `OnWorkerPunchIn` natively on the Component (when the manager arrives at work) and `OnNewDay`.
- **Pending Order Queue**: Orders (`BuyOrder`, `CraftingOrder`, `TransportOrder`) are not executed instantly. They are added to a `PendingOrder` queue in the building. The worker's GOAP `Execute()` method pops these and pushes a `GoapAction_PlaceOrder`, forcing the character to physically travel to the target.
- **Shop Restock & Crafting Ingredients**: Workplaces scan their inventories and enqueue `BuyOrder`s to suppliers for missing stock. If a supplier lacks items for a `BuyOrder`, they generate an internal `CraftingOrder`.
- **Order Types**: `BuyOrder` (inter-building commercial contract), `CraftingOrder` (internal production request), and `TransportOrder` (physical delivery of completed goods).
- **Physical Handshake (`IsPlaced`)**: Orders are only considered officially placed when `InteractionPlaceOrder` succeeds face-to-face. If the target is busy, the interaction fails, and the GOAP action will retry later by re-queueing the order because the `IsPlaced` flag remains `false`.
- **Duplicate Prevention**: Before placing/enqueuing an order, the `BuildingLogisticsManager` checks local logs (`_placedBuyOrders`, `_placedTransportOrders`) to avoid duplicating requests that are already active or awaiting physical interaction.
- **Expiration**: Orders have a `RemainingDays` counter. Expired orders trigger reputation penalties (`CharacterRelation.UpdateRelation`).

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

## Strict Architectural Rules
- **Interaction Distance**: To interact with an object or get in range, **always** use the `InteractionZone` (its colliders or explicit properties).
- **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE.
- **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
- **Centralized Drop Execution**: NEVER manually instantiate items on the ground via `WorldItem.SpawnWorldItem()` inside Job or GOAP scripts. You must always funnel dropping through the physical execution pipeline by calling `worker.CharacterActions.ExecuteAction(new CharacterDropItem(worker, item))` to ensure animations, inventory extractions, and ground offsets are perfectly synchronized.
- **Job Cancellation Null-Safety**: When an invalid state logically forces a job to abort (`CancelCurrentOrder()`), it will aggressively forcefully nullify `_currentAction = null` down the chain. Wrapper execution loops (like `JobTransporter.Execute()`) must rigidly verify `if (_currentAction != null)` upon returning from `.Execute()` before blindly querying `_currentAction.IsComplete`, preventing fatal `NullReferenceException` game locks.
