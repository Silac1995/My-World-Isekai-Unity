---
name: point-click-system
description: Architecture for the Point-and-Click targeting, click-to-move, and RPG-style combat autonomous navigation.
---

# Point-and-Click System

The Point-and-Click system is designed to seamlessly integrate mouse-based interactions with RPG-style autonomous character movement.

## Architecture

The system strictly decouples the **input interpretation (Screen Space UI)** from the **character execution (World Space Logic)** to ensure multiplayer authority and scalable logic.

### 1. Input Layer (`UI_PlayerTargeting`)
Targeting is strictly handled at the UI layer (`PlayerUI`), preventing the `PlayerController` from running expensive Physics Raycasts every frame or muddying the waters between "Clicking UI" vs "Clicking the World."
- Raycasts are performed against `InteractableObject` masks.
- Upon a successful hit, it stores the `InteractableObject` reference as `SelectedInteractable` and updates `CharacterVisual.SetLookTarget()`.
- **UI Layer Guard**: `EventSystem.IsPointerOverGameObject()` prevents clicks on UI buttons/panels from clearing the selection.
- Clearing the target is handled by clicking empty world space (not UI).
- Exposes `SelectInteractable(InteractableObject)` for external callers (e.g., TAB cycling from `PlayerController`).
- Exposes `ClearSelection()` to explicitly remove the selection.

### 2. Execution Layer (`PlayerController` -> `Character` -> `CharacterMovement`)
When the `PlayerController` detects it has entered a specialized state (like `IsInBattle`), it ignores manual WASD logic and delegates movement to the AI routines.
- `Character.ConfigureNavMesh(true)` is explicitly enabled during combat.
- The player character polls its `CharacterCombat.CurrentBattleManager` for the `bestTarget`.
- Using `Time.time` based staggering (`PATH_UPDATE_INTERVAL` = 0.2s) to prevent tick throttling at high speeds, the controller calculates distance.
- If distance > `engagementDistance`, `_characterMovement.SetDestination()` is used.
- If within the engagement distance, `_characterMovement.Stop()` is called, and the character turns to face the target directly.

### 3. Interact-to-Navigate Layer (`PlayerInteractCommand`)
When the player presses E on a selected interactable that is outside its InteractionZone:
- `PlayerInteractionDetector` issues a `PlayerInteractCommand` via `PlayerController.SetOrder()`.
- The command auto-navigates the player to the target's position using `NavMeshAgent`.
- On arrival (player's rigidbody enters target's InteractionZone), the command triggers `PlayerInteractionDetector.TriggerInteract()` and completes.
- WASD input instantly cancels the command (standard `IPlayerCommand` behavior).

## Rules & Best Practices
- **Never poll UI Raycasts inside PlayerController**: The `PlayerController` should only know about its target, never how it was acquired. If you need new click interactions, build them in the HUD layer and pass the result down contextually.
- **Tick Throttling Prevention**: Always use staggers (`nextUpdateTime = Time.time + interval`) for repeating `NavMeshAgent` calculations! At 8x `GameSpeed`, running `CalculatePath` every frame will cripple the main thread.
- **Multiplayer Ready**: By separating standard Locomotion (WASD) from AI Locomotion (Click/Combat), we guarantee that Server Authoritative rules can easily reject or accept a `SetDestination` RPC without having to deal with continuous float input streams.
- **UI Click Protection**: Always check `EventSystem.IsPointerOverGameObject()` before processing world clicks. UI buttons must never interfere with world targeting state.
