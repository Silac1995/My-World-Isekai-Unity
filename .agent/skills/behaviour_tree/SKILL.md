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
5. **Needs** (`BTCond_HasUrgentNeed`): Hunger, urgent rest, clothing...
6. **Schedule** (`BTCond_HasScheduledActivity`): Daily routines (Work, regular sleep).
7. **Social** (`BTCond_WantsToSocialize`): Spontaneous discussions and interactions.
8. **Wander** (`BTAction_Wander`): The Fallback, the NPC wanders.

*If you add a new behavior, think about which node to insert it into or the position of the new conditional node in the `BuildTree()` method of `NPCBehaviourTree`.*

### 2. The Tick (Performance)
- **Staggering**: The BT does not execute every frame. It executes every `_tickInterval` frames (default: 5), with a unique offset (`_frameOffset`) per NPC to spread the CPU load.
- **Tick Exceptions**:
    - The player does not tick the BT.
    - A dead character does not tick.
    - `Controller.IsFrozen` pauses the BT (useful for strong cutscenes/dialogues).
    - `CharacterInteraction.IsInteracting` pauses the BT to avoid unpredictable movements or interaction cancellations (e.g., sitting down).
    - The old system (`Controller.CurrentBehaviour != null`) pauses the BT. *The long-term goal is likely to replace all behaviors with the BT or GOAP, but this is the current rule*.

### 3. Public API (External Interaction)
To bypass autonomous AI and force an action (e.g., a mind-control spell, player's Build mode, etc.):
- `GiveOrder(NPCOrder order)`: Places an order in the `Blackboard`. It will take priority on the next tick. Cancels any previous ongoing order.
- `CancelOrder()`: Cancels the ongoing order.
- `ForceNextTick()`: Call this if the NPC has just been unfrozen (`IsFrozen = false`) and needs to react very quickly without waiting for its 5-frame cycle.

## Updating Nodes
The BT's terminal actions should, whenever possible, implement the `IAIBehaviour` interface so they can be properly managed (using `.Act()`, `.Exit()`, `.Terminate()`, `.IsFinished`).
