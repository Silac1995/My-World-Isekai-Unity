---
name: character-needs
description: The autonomous decision-making layer that pushes NPCs to act based on internal drives (Social interaction, Finding a Job, Dressing up).
---

# Character Needs (GOAP Providers)

The `CharacterNeeds` system holds a list of "Needs" that decrease over time and trigger specialized GOAP goals when they become urgent.

## When to use this skill
- When creating a new interior drive for NPCs (e.g., Hunger, Sleep).
- When debugging why an NPC isn't prioritizing a specific internal state.
- When creating new `GoapAction`s to satisfy a need.

## The SOLID Provider Architecture

Previously, the Needs system was imperative: a need would evaluate itself and directly push a `MoveToTargetBehaviour` into the `NPCController`, entirely bypassing the intelligence of the Planner.

Now, `CharacterNeeds` acts exclusively as a **Dependency Injector** for the `CharacterGoapController`.

### 1. Creating a New Need
Inherit from `CharacterNeed` and implement the abstract provider methods. **Rule:** A Need should NEVER execute logic or touch the Behaviour Tree.
- `IsActive()`: Returns a boolean to indicate if the need should be fulfilled.
- `GetUrgency()`: Returns a priority value.
- `GetGoapGoal()`: Returns the concrete `GoapGoal` (e.g., `isFull = true`) the planner must achieve.
- `GetGoapActions()`: Returns the list of logical actions capable of fulfilling the goal (e.g., `new GoapAction_EatFood()`).

### 2. Event-Driven Decay (`update-usage`)
To adhere to the `update-usage` constraints, do not check `need.Tick(Time.deltaTime)` every frame in `Update()`.
Instead, `CharacterNeeds` manages slow-ticking Coroutines:
```csharp
private IEnumerator SocialDecayCoroutine()
{
    while (true)
    {
        yield return new WaitForSeconds(1f);
        _socialNeed?.DecreaseValue(3f);
    }
}
```

### 3. Execution via GOAP
Because Needs are simply Data Providers, the resolution happens naturally in Priority 5 of the `NPCBehaviourTree` (`BTAction_ExecuteGoapPlan`):
1. `CharacterGoapController` iterates through `_character.CharacterNeeds.AllNeeds`.
2. It extracts their Goals if `IsActive() == true` and their Urgency.
3. The Planner chains the `GoapActions` provided by the needs to reach the Desire state.

### 4. Existing Needs & Actions
- `NeedSocial` -> `GoapGoal("Socialize")` -> `GoapAction_Socialize`.
- `NeedJob` -> `GoapGoal("FindJob")` -> `GoapAction_AskForJob`.
- `NeedToWearClothing` -> `GoapGoal("WearClothing")` -> `GoapAction_WearClothing`.
- `NeedShopping` -> `GoapGoal("GoShopping")` -> `GoapAction_GoShopping`.
- `NeedHunger` -> `GoapGoal({"isHungry": false})` -> `[GoapAction_GoToFood, GoapAction_Eat]`.

---

## NeedHunger

Phase-decay need that drains 25 per `TimeManager.OnPhaseChanged` tick (4× per in-game day, fully empty in 24 h).

### Public API
- `OnValueChanged(float)` — fired on every decay or restore step.
- `OnStarvingChanged(bool)` — fired whenever the starving flag transitions.
- `IncreaseValue(float)`, `DecreaseValue(float)` — clamped to [0, MaxValue=100].
- `IsStarving` — true when `CurrentValue == 0`.
- `IsLow()` — true at or below 30.
- `TrySubscribeToPhase()` / `UnsubscribeFromPhase()` — defensive TimeManager subscription (re-attempted in `Start` if `Instance` was null in ctor).
- `SetCooldown()` — rearms the GOAP activation cooldown after eating.

### Lifecycle
- Constructed in `CharacterNeeds.Start()` after `NeedJob`.
- Subscribes to `MWI.Time.TimeManager.OnPhaseChanged` defensively (re-attempts in `Start` if `Instance` was null at construction time).
- Unsubscribed in `CharacterNeeds.OnDestroy()`.

### GOAP integration
- `IsActive()` returns true when controller is `NPCController` AND `IsLow()` AND cooldown has elapsed.
- `GetGoapGoal()` → `{"isHungry": false}` with urgency `MaxValue - CurrentValue`.
- `GetGoapActions()` scans `CharacterJob.Workplace.GetItemsInStorageFurniture()` for any `FoodSO` item and returns `[GoapAction_GoToFood, GoapAction_Eat]`.

### Persistence
- Auto-handled by the existing `NeedsSaveData` serialization strategy (serializes by need-type-name + current value). No extra code required.

### Macro-simulation catch-up
- `MacroSimulator.SimulateNPCCatchUp` has a NeedHunger branch that calls `MWI.Needs.HungerCatchUpMath.ApplyDecay` at a rate of 100/24 per hour (matching the online decay of 25 per phase × 4 phases/day).

### Key files
- `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs` — need implementation.
- `Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs` — pure math helpers (no Unity dependencies).
- `Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs` — offline catch-up formula.
- `Assets/Resources/Data/Item/FoodSO.cs` — `ConsumableSO` subtype with `_hungerRestored` + `FoodCategory`.
- `Assets/Scripts/Item/FoodInstance.cs` — `ConsumableInstance` subtype; `ApplyEffect` overrides to call `NeedHunger.IncreaseValue`.
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs` — navigates to storage furniture with food.
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs` — executes the eat action and restores hunger.
