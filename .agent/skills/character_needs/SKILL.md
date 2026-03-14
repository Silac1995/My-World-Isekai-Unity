---
name: character-needs
description: The autonomous decision-making layer that pushes NPCs to act based on internal drives (Social interaction, Finding a Job, Dressing up).
---

# Character Needs System

This skill explains how the `CharacterNeeds` system operates, serving as the biological or psychological engine of an NPC. 
Needs are evaluated constantly but only translated into action if the NPC is currently idle (e.g., in a `WanderBehaviour`).

## When to use this skill
- To create a new fundamental drive for NPCs (e.g., `NeedSleep`, `NeedFood`, `NeedEntertainment`).
- To understand how NPCs decide what to do when they have nothing scheduled.
- If an NPC is stuck doing nothing, or alternatively, constantly obsessing over one specific action (like endlessly finding jobs).

## Architecture

The system relies on a Master component (`CharacterNeeds.cs`) attached to the `Character` GameObject and a list of Pure C# classes (`CharacterNeed`).

### 1. The Manager: `CharacterNeeds`
- Contains a list of all possible "Needs" for this character (`_allNeeds`).
- Every 30 frames (to save performance), it calls `EvaluateNeeds()`.
- **Optimization**: Use manual `for` loops and avoid LINQ (`Where`, `OrderBy`, `ToList`) inside `EvaluateNeeds` to minimize allocations in the game loop.
- **Condition for action**: The manager **will not** trigger any need if the character's Behaviour Tree is actively doing something else. It only steps in if the current behaviour is `WanderBehaviour` (which means "I am idle").
- It iterates through all needs to find the most urgent `IsActive()` one. If successful, it stops evaluating for this tick.

### 2. The Abstract Need: `CharacterNeed`
An abstract base class that every biological or social desire must implement.
- **`IsActive()`**: Returns true if the need currently requires attention. Example: `NeedJob` is active if `!CharacterJob.HasJob`.
- **`GetUrgency()`**: Returns a float (0 to 100+).
    - Survival needs (Sleep, Health) should be close to 100.
    - Life organization (Job, Clothing) around 60.
    - Casual desires (Socializing, Wander) around 20-30.
- **`Resolve(NPCController npc)`**: The execution method. This is where you inject a new active Behaviour into the `NPCController`, or where you instantly resolve the issue (e.g., claiming a vacant building). Must return `true` if an action was genuinely undertaken, or `false` if the system should try to resolve the next need in the list instead.

## Example: The Employment Need (`NeedJob.cs`)
- **IsActive**: Checks if the character lacks a job. It also ignores Player avatars.
- **Urgency**: Fixed at `60f`.
- **Resolve**:
  1. It loops through `BuildingManager` to see if there is a vacant `CommercialBuilding` to own.
  2. Alternatively, it looks for any `CommercialBuilding` that has `GetAvailableJobs()`.
  3. It calls `AskForJob` to get hired.

## How to add a new Need
1. Create a pure C# class (e.g., `NeedSleep.cs`).
2. Inherit from `CharacterNeed`.
3. Implement the three abstract methods.
4. Go to `CharacterNeeds.cs` and add `_allNeeds.Add(new NeedSleep(_character));` inside the `Start()` method.
