---
name: goap
description: Rules for using GOAP to dictate the daily life and ultimate goals of an NPC (e.g., starting a family).
---

# GOAP System

This skill defines how to use and extend the GOAP (Goal-Oriented Action Planning) system. 
**Important:** The GOAP in this project is not just a job queue manager (e.g., Gatherer). It is designed to be the orchestrator of the NPC's **daily life**, based on **ultimate goals**.

## Global Concept: GOAP as a "Life Manager"
GOAP gives the NPC a global and organic direction. Rather than saying "Go chop wood," we give the NPC a life Goal. 

Examples of ultimate goals:
- **Starting a family**: The GOAP Planner will prioritize daily life actions leading to this goal (Talking with NPCs, targeting the opposite sex, flirting, marrying, having children).
- **Being the best martial artist**: The Planner will chain training actions (finding a dojo, fighting opponents, improving on a CombatSO).
- **Financial ambition**: Amassing wealth (which will push them to find a job like Gatherer and deposit resources).

The GOAP manages the long/medium-term plan (Needs, Jobs, Socializing), while the Behaviour Tree (BT) handles short-term survival (reacting to aggression, fleeing, executing schedules).

## When to use this skill
- To design a new **Life Goal** or new daily action chains (e.g., a seduction cycle, a martial training cycle).
- When creating a `GoapAction` that advances the NPC's personal story or logic (e.g., `GoapAction_Socialize`, `GoapAction_PlaceOrder`).
- When defining purely functional job logistics loops that require decoupling state from strict FSM execution (e.g., `GoapAction_LoadTransport`, `GoapAction_UnloadTransport` used natively inside `JobTransporter.Execute()`).
- To structure the preconditions and effects linking the NPC's social life, jobs, and foundational Needs.

## How to use it

### 1. Create an Ultimate Goal (`GoapGoal`)
A GOAP Goal defines the absolute state the NPC wants to reach in a part of their life.
- `GoalName`: Name of the goal to accomplish (e.g., "StartAFamily").
- `DesiredState`: Dictionary of boolean states describing the success of the goal.
    - Ex: `new Dictionary<string, bool> { { "hasChildren", true } }`
- `Priority`: Priority level of the goal (allows the NPC to choose between training or developing their social circle).

### 2. Define a Life Action (`GoapAction`)
The action is the basic building block of everyday life.
- `ActionName`: String identifying the action.
- `Preconditions`: The necessary state to launch the action.
    - Ex: to have a child, the precondition might be `{"isMarried", true}`.
- `Effects`: The resulting state of the action.
    - Ex: having a child gives the condition `{"hasChildren", true}`.
- `Cost`: The "tediousness" or difficulty of the process (the Planner chooses the least costly route). This cost can vary according to the NPC's "traits"!
- `IsValid`, `Execute`, `IsComplete`, `Exit`: Functions controlling the action frame by frame.

### 4. Injecting Needs into GOAP (SOLID Architecture)
The `CharacterGoapController` does not directly manage or hardcode specific needs (e.g., jobs, socializing). Instead, the system uses **Dependency Inversion**:
- The `CharacterNeeds` system acts as a **Data Provider**.
- Each `CharacterNeed` subclass (like `NeedJob` or `NeedSocial`) implements `GetGoapGoal()` and `GetGoapActions()`.
- During `Replan()`, the `CharacterGoapController` iterates through all active needs and dynamically loads their goals and actions directly into the planner.
- **Rule:** Never execute logic inside `CharacterNeed`. Needs are exclusively read-only state sensors that provide GOAP goals via the `GetGoapGoal()` method.

### 5. GOAP & Interaction Synchronization (Critical Rule)
When a `GoapAction` triggers a `CharacterInteraction` (e.g., asking for a job, talking to a boss), the GOAP Action **must not complete** (`_isComplete = true`) immediately after starting the interaction. 
- It must explicitly remain running (`return;`) as long as `worker.CharacterInteraction.IsInteracting` is true.
- Failing to do so causes the GOAP planner to finish the plan prematurely, handing control back to the Behaviour Tree. The BT will then evaluate the fallback node (`BTAction_Wander`) and override the interaction's movement, causing the NPC to walk away mid-conversation or get stuck.

### 6. Critical Job Transitions & Action Lifecycle
When writing `GoapAction`s or Behaviour Tree wrappers (like `BTAction_PunchOut`), you must **strictly guarantee** that any physical `CharacterAction` triggered on the character is properly cleaned up if the logic branch abruptly changes.
- **Dangling Actions:** If a BT node triggers a visual animation (e.g., `CharacterActions.ExecuteAction(new Action_PunchOut(...))`) and then the node is aborted (because the 5:00 PM shift ended), the physical `CharacterAction` will keep running internally, permanently blocking all future actions (like `CharacterPickUpItem`). You must explicitly call `self.CharacterActions.ClearCurrentAction()` in the `OnExit()` of the BT node to shatter the lock.
- **Physical Possession:** For jobs involving item transport (`GoapAction_UnloadTransport`), never blindly trigger drop actions or award delivery progress. Always forcefully assert `hands.AreHandsFree() == false` or `inventory.HasItem(...)` immediately prior. Otherwise, rapid GOAP loops can generate "Ghost Drops" that fulfill targets with thin air.
- **Interaction Bounds Validation:** When navigating to a `WorldItem` for interaction, do **not** use `bounds.ClosestPoint(...)` for the destination. The `NavMeshAgent` deceleration will stop the agent just short of the bounds, causing `bounds.Intersects(...)` to randomly flap and the character to stutter endlessly. Always sequence the destination directly to the center (`targetPos`) of the item so the agent naturally deeply pierces the interaction boundary as it slows to a halt.

### 7. Strict Architectural Rules
- **Interaction Distance**: To interact with an object or get in range, **always** use the `InteractionZone` (its colliders or explicit properties).
- **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE. Never call Destroy manually from GOAP actions.
- **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
    - If it's a brand new item, it MUST be instantiated through `Assets/Resources/Data/Item/ItemSO.cs` with the methods that take color and other parameters.
