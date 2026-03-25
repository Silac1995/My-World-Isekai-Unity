---
name: player-movement
description: Architecture of the Player Movement system, detailing the integration of the Command Pattern for decoupling manual WASD inputs from autonomous actions (Click-to-Move, Combat, Interact).
---

# Player Movement System

This skill details how the player character navigates the world, transitioning between direct physical control and autonomous AI routines.

## The Command Pattern (`IPlayerCommand`)

To respect **SRP (Single Responsibility Principle)**, the `PlayerController` does NOT handle complex pathing, interaction distances, or combat pacing directly. Instead, it relies on the Command Pattern.

The `PlayerController` holds a single `IPlayerCommand _currentOrder`. Every frame:
1. If `_currentOrder != null`, the controller calls `_currentOrder.Tick()`.
2. If `_currentOrder == null`, the controller falls back to manual physical inputs (`Input.GetAxisRaw`).

### 1. Manual Physics (The Fallback)
If no command is active, `PlayerController` reads WASD/Joypad inputs, computes a camera-relative vector (`moveDir`), and passes it to `CharacterMovement.SetDesiredDirection()`.
This translates into continuous forces applied to the `Rigidbody` inside `FixedUpdate`.

### 2. Autonomous Actions (The Commands)
When the player initiates a high-level action, a command is assigned. The moment a command is assigned, physical WASD inputs are ignored until the command finishes or is explicitly canceled.

Currently supported commands:
- **`PlayerMoveCommand(Vector3 destination)`**: Initiated by a Right-Click. Automatically paths the character to the destination using `NavMeshAgent`. It resolves `isFinished = true` when the agent arrives.
- **`PlayerCombatCommand(Character target)`**: Automatically initiated when `CharacterCombat.IsInBattle` becomes true. This command takes full control over the player's pacing, pulling logic from `CombatAILogic`. It moves the player into valid strike ranges, paces back to tactical fallback points, and executes `ActionIntents`. 
- **`PlayerInteractCommand(InteractableObject target, PlayerInteractionDetector detector)`**: Initiated when the player presses E on a selected interactable that is outside the target's InteractionZone. Automatically paths the character to the target using `NavMeshAgent`. On arrival (when `PlayerInteractionDetector.IsTargetInRange()` returns true — meaning the player's rigidbody has entered the target's InteractionZone), it calls `detector.TriggerInteract()` and completes.

## Cancelling & Overriding

- **Standard Commands**: If the player presses a directional input (WASD) while an autonomous command is running (like `PlayerMoveCommand` or `PlayerInteractCommand`), the `PlayerController` instantly sets `_currentOrder = null`, returning full physical control to the player.
- **Combat Lockout**: The `PlayerCombatCommand` is the sole exception. While in combat, WASD movement is strictly ignored to prevent players from overriding the tactical AI positioning. The only way to regain WASD control is for the battle to end.

## NavMesh vs Rigidbody Toggling
Commands that require autonomous pathing (like `PlayerMoveCommand`, `PlayerCombatCommand`, and `PlayerInteractCommand`) rely on the `NavMeshAgent`. 
Because the `NavMeshAgent` writes directly to the Transform, it fatally conflicts with `Rigidbody` interpolation and velocity calculations. 

The switch is automatically handled inside `PlayerController.Move()` which compares `_currentOrder != null` and securely invokes `Character.ConfigureNavMesh(bool)` to lock out physics BEFORE enabling the agent, and vice versa. 
> [!IMPORTANT]
> See the `player-ai-nav-switch` skill for the strict implementation rules regarding the physics/agent switch.

## TAB Targeting

The `PlayerController` handles `KeyCode.Tab` input for cycle-selecting interactables:
1. Queries `Character.CharacterAwareness.GetVisibleInteractables()` for all interactables within the awareness radius.
2. Sorts by distance from the player.
3. Selects the closest, or cycles to the next if the closest is already selected.
4. Calls `UI_PlayerTargeting.SelectInteractable()` which also sets `CharacterVisual.SetLookTarget()`.

> [!NOTE]
> TAB targeting is complementary to click-to-select. Both feed into the same selection state in `UI_PlayerTargeting`. See the `interactable-system` skill for the full Targeted Selection System documentation.
