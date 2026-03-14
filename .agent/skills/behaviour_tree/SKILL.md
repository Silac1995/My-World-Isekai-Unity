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
2. **Combat** (`BTCond_IsInCombat`): The NPC is already engaged in combat.
3. **Assistance** (`BTCond_FriendInDanger`): The NPC sees an ally under attack.
4. **Aggression** (`BTCond_DetectedEnemy`): The NPC detects a threat and attacks it.
5. **GOAP** (`BTAction_ExecuteGoapPlan`): Proactive life planning (Job search, personal goals).
6. **Needs** (`BTCond_HasUrgentNeed`): Hunger, urgent rest, clothing... (Urgent fallback).
7. **Schedule** (`BTCond_HasScheduledActivity`): Daily routines (Work, regular sleep).
8. **Social** (`BTCond_WantsToSocialize`): Spontaneous discussions. (Native Node).
9. **Wander** (`BTAction_Wander`): The Fallback. (Native Node).

*If you add a new behavior, think about which node to insert it into. Prefer native `BTNode` implementations for high-frequency or foundational logic, and use `BTActionNode` wrappers only for complex legacy behaviours that require full stack management.*

### 2. Native Nodes vs Legacy Wrappers
The system is migrating towards native `BTNode` implementations for better performance and predictability:
- **Native Nodes** (e.g., `BTAction_Wander`): Implement logic directly in `OnExecute`. Do not use Coroutines. Use `UnityEngine.Time.time` for time-tracking to remain independent of BT frame staggering.
- **Legacy Wrappers** (`BTActionNode`): Wrap an `IAIBehaviour`. These push the behaviour to the character's stack on `Enter` and pop it on `Exit`. The BT pauses itself while a legacy behaviour (or any behaviour) is active on the stack.

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
