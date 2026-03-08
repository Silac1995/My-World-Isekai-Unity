---
description: The central hub of the entity. Dictates rules on the character's availability (IsFree), life cycle (Death/Unconscious), and brain (Player/NPC Switch).
---

# Character Core Skill

The `Character.cs` script is the most important class of the entity. It is the Central Architecture (Facade Pattern) through which **everything** passes.

## 1. Facade Pattern (Obligations)
The Agent **must NEVER search for components linked to a character via isolated `GetComponent` calls**.
If it has a reference to `Character`, then it already has safe and performant access to the majority of the system.
Examples:
- `character.CharacterJob` -> Manages work.
- `character.CharacterCombat` -> Manages fighting.
- `character.CharacterInteraction` -> Manages dialogues.
- `character.CharacterMovement` -> Manages navigation.
- `character.CharacterEquipment` -> Manages equippable inventory.
- `character.Stats` -> Provides vital statistics.

## 2. Justice of the Peace and Availability (`IsFree()`)
This is the ultimate safety method. `Character` scrutinizes all of its child components to tell the global system (GOAP, Player commands, Interactions) whether the character is allowed to be interrupted or is already busy.

`IsFree(out CharacterBusyReason reason)` will return False and explain why if the character is:
- Dead (`Dead`)
- KO (`Unconscious`)
- Currently fighting (`InCombat`)
- In dialogue (`Interacting`)
- Forging or building a complex object (`Crafting`)
- Teaching a class (`Teaching`)

## 3. Life Cycle and Statuses 
`Character` is responsible for major state changes. You must never manually tinker with HP or the collider to "kill" someone.

- **SetUnconscious(true)**:
  - The entity becomes physically inert (Rigidbody switches to Kinematic so falls are managed).
  - The AI brain (`Controller`) is turned off and its stack cleared.
  - The `NavMeshAgent` is disabled (essential for Unity).
  - The Animator switches to the Knockout state.
- **Die()**:
  - Performs the same routine (brain + navmesh deactivation).
  - But death (`_isDead = true`) permanently overrides the rest.

## 4. Context Switching (The Brain)
A character in your game can switch from an autonomous civilian AI (NPC) to a Player-controlled Avatar with a snap of a finger.

- `SwitchToPlayer()`: Turns off the `NPCController`, turns on the `PlayerController`. Disables the NavMeshAgent because the player uses physics (non-Kinematic Rigidbody) to move.
- `SwitchToNPC()`: Turns off the `PlayerController` and turns on the `NPCController`. Reactivates the NavMeshAgent and switches the Rigidbody back to Kinematic.

> In case of an input or navigation bug, always first verify that the correct Controller is turned on via this Switch system.
