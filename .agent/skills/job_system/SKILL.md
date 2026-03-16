---
name: job-system
description: The work ecosystem connecting the employee (CharacterJob), the pure data (Job), and the workplace (CommercialBuilding).
---

# Job System

Economy and work govern the game world. This skill details the physical-data-entity triad architecture that allows a character (NPC or Player) to hold a position in a building.

## When to use this skill
- When creating a new profession or worker logic (e.g., Blacksmith, Farmer).
- When debugging why characters are not going to work, or why they fail to interact with workplace tools.
- To understand how the GOAP task system dynamically assigns physical labor within a zone.

## The Physical-Data-Entity Architecture

The architecture strictly relies on three concepts to ensure that nobody works "in a vacuum". A character must be physically present and assigned to a specific role to execute work logic.

### 1. The Employee (`CharacterJob`)
This is the component attached to the character entity.
**Rule:** Characters cannot telepathically start working. They must physically navigate to the building via `PunchInBehaviour` to begin their shift.
- Manages assignment dictionaries to allow multiple jobs.
- Uses `DoesScheduleOverlap` to safeguard against scheduling conflicts.
- Injects work schedules (`InjectWorkSchedule`) directly into the `CharacterSchedule` AI planner.

### 2. The Role (`Job`)
Pure abstract C# class representing the position data (e.g., "Bartender").
**Rule:** Jobs must remain stateless regarding the Character's physical state but contain the pure logic and schedule.
- Defines `JobTitle`, `Category`, and `GetWorkSchedule()`.
- Actively pushes behaviors (`PerformCraftBehaviour`) to the character's controller during office hours in `Execute()`.

### 3. The Location (`CommercialBuilding`)
The physical anchor in the scene.
**Rule:** The building administers all task distributions dynamically. Characters should never run expensive physical queries to find work; they must query the `BuildingTaskManager`.
- Spawns all inherent Jobs via `InitializeJobs()`.
- Manages recruitment through `AskForJob` (requires an Owner, vacancy, and matching tier).
- Assigns spatial distribution via `GetWorkPosition(Character)` to prevent worker clipping/stacking.

### 4. Dynamic Job Fulfillment (Crafting & Logistics)
- **CraftingBuilding**: Scans `ComplexRoom`s for `CraftingStation`s to populate `GetCraftableItems()`.
- **JobCrafter**: Demands a `SkillSO` hierarchy. Driven purely by demand—only works if a `JobLogisticsManager` provides a `CraftingOrder`.
- **JobLogisticsManager**: Event-driven manager functioning asynchronously. Generates `PendingOrder`s and strictly requires physical handshakes (`InteractionPlaceOrder`) to confirm cross-building transactions.
- **JobTransporter**: Utilizes a native GOAP planner inside its `Execute()` block to autonomously travel, load, and deliver items based on `TransportOrder` availability.

### 5. Architectural Execution Rules
- **Interaction Ranges**: Always use `InteractionZone` colliders when measuring pathing or execution distance.
- **Physical World State**:
    - To destroy an item upon pickup: MUST execute exclusively inside `CharacterPickUpItem.cs`.
    - To spawn an item upon dropping: MUST funnel through `worker.CharacterActions.ExecuteAction(new CharacterDropItem(worker, item))` to synchronize animations and ground offsets. Never manually instantiate raw prefabs during GoapActions.
- **Null-Safety during Aborts**: If a job aborts (e.g. `CancelCurrentOrder()`), it forces `_currentAction = null`. Always ensure GOAP planners gracefully verify `if (_currentAction != null)` instead of crashing out with `NullReferenceException`.

## Existing Components
- `CharacterJob` -> The MonoBehaviour linking the AI to their economic role.
- `Job` -> The abstract ruleset indicating hours and actions.
- `CommercialBuilding` -> The zoning footprint offering jobs and broadcasting tasks.
