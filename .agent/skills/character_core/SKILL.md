---
name: character-core
description: The central hub of the entity. Dictates rules on the character's availability (IsFree), life cycle (Death/Unconscious), and brain (Player/NPC Switch).
---

# Character Core

## 0. Character Prefab Structure
The root (most parent) GameObject of a Character prefab contains the essential components that form the entity's foundation:
- `Character.cs` (`Assets/Scripts/Character/Character.cs`)
- `CharacterActions.cs` (`Assets/Scripts/Character/CharacterActions/CharacterActions.cs`)
- `NPCController.cs` and `PlayerController.cs` (`Assets/Scripts/Character/CharacterControllers/NPCController.cs` and `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`)
- `Rigidbody`
- `CapsuleCollider`

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
- `character.PathingMemory` -> Specialized memory container that tracks unreachable targets to prevent infinite evaluation/movement loops (self-cleaning on TimeManager resets via `OnDestroy()`).

### CharacterSystem Pipeline (Decoupled Modules)
All core character systems (`CharacterMovement`, `CharacterVisual`, `CharacterInteraction`, `CharacterActions`, `CharacterGameController`, `CharacterGoapController`, `NPCBehaviourTree`, `CharacterCombat`) now inherit from the abstract class **`CharacterSystem`**.
This abstract base automatically caches `_character` during `Awake` and subscribes to essential lifecycle events (`OnIncapacitated`, `OnDeath`, `OnWakeUp`, `OnCombatStateChanged`). `Character.cs` no longer explicitly micro-manages the shutdown of its modules; each subsystem gracefully handles its own cleanup by overriding `HandleIncapacitated(Character)` or `HandleCombatStateChanged(bool)`.

### System-to-System Communication (Inspector Linking)
> [!IMPORTANT]
> **New Architectural Rule**: Every time a `CharacterSystem` needs to call another `CharacterSystem` on the same entity, you **must use a [SerializeField]** to link them directly in the Unity Inspector instead of dynamically querying the Facade at runtime. This prevents missing component bugs and reduces rigid caching dependency. If you add a reference this way, always remind the user to link it in the prefab inspector!

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
  - Triggers the `OnIncapacitated` event.
  - Subsystems inheriting from `CharacterSystem` independently react to power down (e.g., `CharacterMovement.Stop()`, `NPCBehaviourTree.CancelOrder()`, `CharacterVisual.ClearLookTarget()`).
  - The entity becomes physically inert (Rigidbody switches to Kinematic so falls are managed).
- **Die()**:
  - Performs the same routine (fires `OnDeath` and `OnIncapacitated`).
  - But death (`_isDead = true`) permanently overrides the rest.

## 4. Context Switching (The Brain)
**A Player is exactly like an NPC character.** They share the exact same `Character` object, underlying components, stats, and game logic. A character in your game can switch from an autonomous civilian AI (NPC) to a Player-controlled Avatar with a snap of a finger just by swapping the active controller.

- `SwitchToPlayer()`: 
  - Swaps controllers and interaction detectors.
  - **UI Setup**: Finds the GameObject **"UI_PlayerHUD"** and calls `PlayerUI.Initialize(this)`. This pushes notification channels to the equipment system.
- `SwitchToNPC()`: 
  - Reverts controllers and reactivates NavMesh.
  ## 5. Character Actions and Movement Control
The `CharacterActions` component manages distinct, timed actions (Harvesting, Crafting, Attacking). These actions are integrated into the `CharacterGameController` via an event-driven system to manage character availability and movement.

- **`OnActionStarted`**: Triggered when a `CharacterAction` begins. The controller automatically stops movement and sets the `isDoingAction` animator bool (if the action allows it).
- **`OnActionFinished`**: Triggered when an action ends or is cancelled. This initiates a short **Action Cooldown** (default: 0.5s) before the character can resume navigation.
- **`ShouldPlayGenericActionAnimation`**: Each `CharacterAction` can opt-out of the generic "busy" animation to prevent flickering or overriding specific animations (like Combat).

> [!IMPORTANT]
> To stop a character during an action, always prefer using the `CharacterActions` system rather than manually calling `Stop()` in `Update()`. This ensures consistent behavior across Player and NPC controllers.

> In case of an input or navigation bug, always first verify that the correct Controller is turned on via this Switch system.
