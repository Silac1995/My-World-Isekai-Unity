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
- **Recruitment (`AskForJob`)**: For a character to get a position here, the Building must have a Boss (`HasOwner`), the position must exist locally, and it must be vacant.
- **Punching In/Out**: Handled by strict `CharacterAction`s (`Action_PunchIn` / `Action_PunchOut`). A character cannot telepathically start working; their `WorkBehaviour` first pushes a `PunchInBehaviour` to physically navigate them inside `BuildingZone.bounds`, which then spawns the `Action_PunchIn` to call `WorkerStartingShift`.
- **Physical Dispersion**: `GetWorkPosition(Character)` provides a point within the `BuildingZone` with a unique offset (based on InstanceID) to ensure workers don't stack on top of each other.

### 4. Crafting (CraftingBuilding & JobCrafter)
Crafting follows a specialized overlay of this system.
- **CraftingBuilding**: A specialized `CommercialBuilding`. It scans its `ComplexRoom`s to find `CraftingStation`s and compiles a list of what can be manufactured there via `GetCraftableItems()`. 
- **JobCrafter**: The artisan job (e.g., Blacksmith).
   - **Requirements**: It requires the NPC to have a specific skill (`SkillSO`) and a minimum tier (`SkillTier` defined in `CharacterSkills`). Without this, the building refuses employment.
   - **Demand-Driven Logic**: The artisan does not produce in a vacuum. Their Behaviour Tree checks that the building's `JobLogisticsManager` has an active **`CraftingOrder`** (which follows the same time and reputation penalty logic as a `BuyOrder`). If there is an order, they find the right station, play their animation, and produce the item.

### 5. Logistics Cycle (JobLogisticsManager)
Every `CommercialBuilding` that needs supply management has a `JobLogisticsManager`.
- **Event-Driven & Physical**: Triggered by `OnWorkerPunchIn` (when the manager arrives at work) and `OnNewDay`.
- **Pending Order Queue**: Orders (`BuyOrder`, `CraftingOrder`, `TransportOrder`) are not executed instantly. They are added to a `PendingOrder` queue. The manager's `Execute()` method pops these and pushes a `PlaceOrderBehaviour`, forcing the character to physically travel to the supplier.
- **Shop Restock**: `CheckShopInventory()` scans `ItemsToSell` vs `Inventory` and enqueues orders for missing stock.
- **Crafting Ingredients**: `CheckCraftingIngredients()` scans active `CraftingOrder`s, calculates missing materials based on `CraftingRecipe`, and enqueues `BuyOrder`s to fetch them.
- **Order Types**: `BuyOrder` (ingredient/stock procurement), `CraftingOrder` (production request), and `TransportOrder` (physical delivery of completed goods).
- **Duplicate Prevention**: Before placing/enqueuing an order, it checks if an order for that item is already active or pending.
- **Expiration**: Orders have a `RemainingDays` counter. Expired orders trigger reputation penalties (`CharacterRelation.UpdateRelation`).

### 6. Work Positions
- **`CommercialBuilding.GetWorkPosition(Character)`**: Virtual method returning where a worker should stand. Defaults to `GetRandomPointInBuildingZone()`.
- **ShopBuilding Override**: Vendors go to a specific `VendorPoint` Transform (counter), all others wander in the building zone.
- **BTVendorBehaviour**: If a `VendorPoint` exists, the vendor paths to it before serving. If not, they wander in the building zone.

## How to Create a New Job?
In the future, if the AI Agent needs to create a "Blacksmith":
1. Write the abstract `JobCrafter` code, then `JobBlacksmith` inheriting from `JobCrafter`. Define its schedule, its `SkillSO`/`SkillTier` prerequisites, and its BT node `BTAction_PerformCraft`.
2. Create or modify the `ForgeBuilding` inheriting from `CraftingBuilding` (and not just `CommercialBuilding`) so its `InitializeJobs()` function adds a `JobBlacksmith` + a `JobLogisticsManager` (for orders).
3. Done! The player can go ask for the job (if they have the right skill level), and place crafting orders with the Logistics Manager.
