---
name: character-needs
description: The autonomous decision-making layer that pushes NPCs to act based on internal drives (Social interaction, Finding a Job, Dressing up).
---

# Character Needs

The `CharacterNeeds` system is the primary driver of autonomous NPC behavior. It manages a list of "Needs" that decrease over time and trigger specialized AI behaviors when they become urgent.

## When to use this skill
- When creating a new interior drive for NPCs (e.g., Hunger, Sleep).
- When debugging why an NPC is not responding to a specific internal state.
- When refactoring the needs evaluation or resolution logic.

## How to use it

### 1. Creating a New Need
Inherit from `CharacterNeed` and implement the three abstract methods:
- `IsActive()`: Returns true if the need is currently relevant (usually based on a threshold).
- `GetUrgency()`: Returns a priority value (0-100).
- `Resolve(NPCController npc)`: Logic to find a target and push an `IAIBehaviour`. **Must return true if a resolution was started.**

### 2. Registering the Need
Add the new need to the `_allNeeds` list in `CharacterNeeds.Start()`.

### 3. Sequential Resolution Strategy
The system uses a **Sequential Resolution** approach (implemented in both `CharacterNeeds.EvaluateNeeds` and `BTCond_HasUrgentNeed.cs`):
1. Get all active needs.
2. Sort them by urgency (Descending).
3. Attempt to `Resolve()` each one in order.
4. Stop at the first successful resolution.

> [!IMPORTANT]
> This strategy ensures that if a high-priority need (like Social) is blocked because no partners are available, lower-priority needs (like Job) still get a chance to be resolved.

### 4. Integration with Behaviour Tree
The `BTCond_HasUrgentNeed` node handles needs for NPCs with a BT. It includes a **State Guard**:
- It only resolves needs if the NPC is in `WanderBehaviour` or is idle.
- This prevents "behavior push loops" where a need re-resolves every frame while the NPC is already moving toward a target.

## Existing Needs
- `NeedSocial`: Drives NPCs to find a partner and start an interaction.
- `NeedJob`: Drives unemployed NPCs to find a boss/building and ask for a job.
- `NeedToWearClothing`: Drives NPCs to put on clothes if they are naked.
