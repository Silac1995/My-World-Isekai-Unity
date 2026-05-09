---
name: behaviour-tree
description: Structure, priorities, and control API for the NPC Behaviour Tree system.
---

# Behaviour Tree System

This skill details the architecture and inner workings of the Behaviour Tree (BT) system used by NPCs. 

## When to use this skill
- To add a new global behavior (e.g., sleeping, eating, social routines).
- To interact with an NPC from the outside via the `NPCBehaviourTree` script (e.g., giving an order).
- To debug why an NPC "does nothing" or acts unexpectedly.

## How to use it

### 1. Architecture and Node Priorities
The Behaviour Tree uses a root `BTSelector` (`_root`) that evaluates its children from top to bottom. **Order defines priority**.
The current tree evaluates in this order:
1. **Orders** (`BTCond_HasOrder`): The player or the game has given an explicit order (Max Priority).
2. **Shift Ends** (`BTCond_NeedsToPunchOut`): The NPC's work schedule ended while at work, forcing an immediate, safe Punch-Out action.
3. **Combat** (`BTCond_IsInCombat`): The NPC is already engaged in combat.
4. **Assistance** (`BTCond_FriendInDanger`): The NPC sees an ally under attack.
5. **Aggression** (`BTCond_DetectedEnemy`): The NPC detects a threat and attacks it.
6. **GOAP** (`BTAction_ExecuteGoapPlan`): Proactive life and needs planning (Shopping, socializing, fulfilling jobs).
7. **Schedule** (`BTCond_HasScheduledActivity`): Daily routines (Native `BTAction_Work`, regular sleep, etc.).
8. **Social** (`BTCond_WantsToSocialize`): Spontaneous discussions. (Native Node).
9. **Wander** (`BTAction_Wander`): The Fallback. (Native Node).

*If you add a new behavior, think about which node to insert it into. Prefer native `BTNode` implementations for high-frequency or foundational logic, and use `BTActionNode` wrappers only for complex legacy behaviours that require full stack management.*

### 2. Native Nodes vs Legacy Wrappers (Deprecation)
The system is actively **migrating away** from `IAIBehaviour` and the `_behavioursStack` towards fully native `BTNode` and `GoapAction` implementations.
- **Native Nodes** (e.g., `BTAction_Work`, `BTAction_Combat`): Implement logic directly in `OnExecute` as State Machines. Do not use Coroutines. Use `UnityEngine.Time.time` for time-tracking to remain independent of BT frame staggering. This is the **standard**.
- **Legacy Wrappers** (`BTCond_HasLegacyBehaviour`, `BTAction_ExecuteLegacyStack`): These bridge nodes pause the Behaviour Tree while any lingering `IAIBehaviour` is executing on the NPC's stack. *Do not create new legacy behaviours!*

### 3. The Tick (Performance)
- **Staggering**: The BT does not execute every frame. It executes every `_tickInterval` frames (default: 5), with a unique offset (`_frameOffset`) per NPC to spread the CPU load.
- **Tick Exceptions**:
    - The player does not tick the BT.
    - A dead character does not tick.
    - `Controller.IsFrozen` pauses the BT (useful for strong cutscenes/dialogues).
    - `CharacterInteraction.IsInteracting` pauses the BT to avoid unpredictable movements or interaction cancellations (e.g., sitting down).
    - **GOAP Bridge**: `BTAction_ExecuteGoapPlan` delegates specific life-planning logic to `CharacterGoapController`. If a GOAP action pushes a behaviour to the old stack (like a `MoveToTargetBehaviour`), the BT automatically pauses as long as that behaviour is active.
    - The old system (`Controller.CurrentBehaviour != null`) pauses the BT.

### 3. Public API (External Interaction)
To bypass autonomous AI and force an action (e.g., a mind-control spell, player's Build mode, etc.):
- `GiveOrder(NPCOrder order)`: Places an order in the `Blackboard`. It will take priority on the next tick. Cancels any previous ongoing order.
- `CancelOrder()`: Cancels the ongoing order.
- `ForceNextTick()`: Call this if the NPC has just been unfrozen (`IsFrozen = false`) and needs to react very quickly without waiting for its 5-frame cycle.

## 4. AI Behaviour Life Cycle (`IAIBehaviour`)
Any terminal action or character state must implement the `IAIBehaviour` interface to ensure a clean transition life cycle managed by the `CharacterGameController`.

- `Enter(Character self)`: Initialization. Called once when the behaviour is pushed or set. Use for setting initial destinations or clearing animation triggers.
- `Act(Character self)`: The update loop. Called every frame while the behaviour is on top of the stack.
- `Exit(Character self)`: Cleanup. Called when the behaviour is popped or replaced. **Must stop ongoing coroutines and reset paths**.
- `Terminate()`: Logic flag (`_isFinished = true`) to signal the controller to pop the behaviour and resume the previous one.

### 5. Movement vs. Interaction (Macro vs Micro)
The architecture strictly separates pathfinding from visual positioning to prevent conflicts with the 2.5D visual rules:
- **Macro Navigation (`MoveToTargetBehaviour`)**: The general-purpose pathfinder. Used by the AI to cross the map or reach an area. It stops when the agent is "close enough" based on a distance threshold. It does not calculate visual Z-plane alignment.
- **Micro Positioning (`MoveToInteractionBehaviour`)**: A specialized, highly-constrained state completely owned by `CharacterInteraction`. Triggered *only* after an interaction has begun. It overrides the NavMesh to guarantee a mathematically perfect 2.5D visual alignment (exact `Z` plane match, exact `4f` distance on `X`, and overlapping `InteractionZone` colliders) before popping the dialogue bubbles.

### 6. PunchIn retry pattern (2026-05-02)

`BTAction_Work.HandlePunchingIn` MUST verify `workplace.IsWorkerOnShift(self)` before advancing to `WorkPhase.Working`. The check covers two paths after `Action_PunchIn` is no longer the worker's `CurrentAction`:

- **(a)** `Action_PunchIn` completed normally → `OnApplyEffect` ran → `WorkerStartingShift` registered the worker → `IsWorkerOnShift = true`. Advance to `WorkPhase.Working` and `HandleWorking` calls `jobInfo.Work()`.
- **(b)** `Action_PunchIn` never ran (`CharacterActions.ExecuteAction` returned false because the worker was busy with another action) OR was preempted before `OnApplyEffect` fired. `IsWorkerOnShift = false` even though the BT thinks PunchIn finished.

Without the gate, the BT used to advance to `Working` on path (b), `JobFarmer.Execute` ran with the worker NOT on the shift roster, `_currentGoal` was set, no plan formed (because `IsWorkerOnShift` gates several things downstream), and the worker pinged between Idle and Goal forever. **Symptom**: debug shows `Job Goal: PlantEmptyCells, Action: Planning / Idle` but `On shift` doesn't include this worker. Falling back to `MovingToTimeClock` retries `Interact` + `ExecuteAction` until PunchIn succeeds or the schedule ends.

When adding any new BT action that advances state on the basis of "another action's completion side-effect", verify the side-effect actually landed before advancing — never trust `CurrentAction == null` to mean "PunchIn / X / Y succeeded".

### 7. Social Filtering (Worker Focus)
Social nodes (`BTCond_WantsToSocialize`, `NeedSocial`) autonomously scan for free targets (`Character.IsFree()`). 
To ensure NPCs can do their jobs uninterrupted:
- Social scans must actively filter out characters currently scheduled to work (`CharacterSchedule.CurrentActivity == ScheduleActivity.Work`).
- This ensures workers remain focused so players or logistics managers can interact with them for business, but they will not be distracted by casual greetings from random passersby unless they are officially "on a break" (`CurrentGoal = "Idle"`).
