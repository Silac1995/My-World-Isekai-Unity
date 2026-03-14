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
