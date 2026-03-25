---
name: interactable-system
description: Architecture and rules for the Interactable System, dictating how a Character interacts with world objects (Items, Characters, Harvestables) using interaction zones and Rigidbodies.
---

# Interactable System

The Interactable System defines how entities in the world can be interacted with by a `Character`. It is built around a base abstract class `InteractableObject` and several specialized subclasses depending on the nature of the object.

## Core Rules

1. **Interaction Zone Constraint**: To interact with an object, and to get in range, **always use the `InteractionZone`**. For a `Character` to interact with any `InteractableObject`, the Character's Rigidbody (`_rb`) **MUST** be inside or explicitly checked against the object's `_interactionZone` (a `Collider`). This physical proximity check is mandatory for all interactions.
2. **Base Class**: `InteractableObject` provides the core `_interactionZone` (Collider), `interactionPrompt` (string), an explicit `Rigidbody` property (representing the true physical body), and the abstract `Interact(Character interactor)` method.
3. **Physical Distance Verification**: When performing AI awareness, targeting, or harvesting proximity checks, **always** mathematically verify distance via the `InteractableObject`'s `Rigidbody` property (e.g., `Vector3.Distance(pos, interactable.Rigidbody.position)`). Do not rely solely on trigger overlaps, as massive interaction zones will cause "miles away" false positives.
4. **Execution via CharacterActions**: The actual result of an interaction usually instantiates a `CharacterAction` (like `CharacterPickUpItem`, `CharacterEquipAction`, `CharacterStartInteraction`, `CharacterHarvestAction`) which is then sent to the interactor's `CharacterActions.ExecuteAction(...)`.
5. **Action Exclusivity (No Manual Overrides)**: You must **never** manually bypass an `InteractableObject`'s lifecycle. Do not forcefully inject items into hands.
6. **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE. Delegate completely to `CharacterPickUpItem` to prevent item logic desyncs and ghost duplication.
7. **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
    - If it's a brand new item, it MUST be instantiated through `Assets/Resources/Data/Item/ItemSO.cs` with the instantiate methods that take color and other parameters.

## Implementing Interactables

Here are the primary interactable types in the project:

### 1. ItemInteractable (`ItemInteractable.cs`)
Used for items placed in the world (e.g., dropped items, equippable gear).
- **Structure**: It is structurally bound as a child collider to a `WorldItem`. The `WorldItem` maintains a direct serialized reference (`ItemInteractable` property) to avoid `GetComponentInChildren` performance bottlenecks.
- **Behavior**: Depending on the type of `ItemInstance` (`WearableInstance` vs normal item), it creates a `CharacterEquipAction` or a `CharacterPickUpItem` action.
- **Consumption**: The root GameObject is explicitly destroyed by the `CharacterPickUpItem` or `CharacterEquipAction` ONCE the visual animation concludes. > **NEVER** destroy an `ItemInteractable` or its parent `WorldItem` manually inside fallback logs or AI GOAP code.

### 2. CharacterInteractable (`CharacterInteractable.cs`)
Used for interactions between two Characters (conversations, specific actions).
- **Behavior**: Triggers a `CharacterStartInteraction` action.
- **Exclusivity**: Contains an `_isBusy` flag. A Character cannot be interacted with if they are already busy (`_isBusy == true`). The interaction must be explicitly released via `Release()` when finished.

### 3. Harvestable (`Harvestable.cs`)
Used for resource nodes (trees, rocks, ore veins).
- **Behavior**: Triggers a `CharacterHarvestAction` to perform the harvesting over a specified `_harvestDuration`.
- **Outputs**: Produces items from a predefined `_outputItems` list (as `ItemSO`).
- **Depletion**: Tracks `_currentHarvestCount`. Once it hits `_maxHarvestCount`, the object becomes depleted (`_isDepleted = true`), hides its visuals, and waits for a `_respawnTime` before it can be harvested again.

## Player Input & Interaction Menus

The `PlayerInteractionDetector` evaluates player input to differentiate between quick interactions and extended actions:

### 1. Tap E (Quick Action)
- Automatically delegates to the interactable's `Interact()` method.
- For a `CharacterInteractable`, this defaults to sending an `InteractionStartDialogue` invitation (requesting a conversation).

### 2. Hold E (Extended Options)
- Pressing and holding "E" fills up a progress bar managed by `InteractionPromptUI`.
- Once the hold threshold is reached, instead of firing `Interact()`, it pulls `GetHoldInteractionOptions()` from the target.
- These options (e.g., *Follow Me*, *Greet*) are displayed dynamically in a radial or list context menu via the `PlayerUI`.

### 3. Dynamic Dialogue Menu
- When an interaction dialogue officially starts (i.e. it is the player's turn in a turn-based dialogue sequence), the `PlayerInteractionDetector` subscribes to `OnPlayerTurnStarted`.
- It dynamically pulls `GetDialogueInteractionOptions()` (e.g., *Talk*, *Insult*) and displays them in the context menu.

### 4. UI Stability & Single Responsibility
- **Player-Only Guarding**: Event subscriptions in `PlayerInteractionDetector` (`OnInteractionStateChanged`, `OnPlayerTurnStarted`, etc.) MUST be strictly guarded with an `if (!Character.IsPlayer()) return;` check. This prevents the Player's HUD from reacting to background NPC-to-NPC interactions.
- **Menu Closure Efficiency**: The `PlayerUI.CloseInteractionMenu()` should only be called once when the target changes or is lost, **instead of polling every empty frame**. Check if the target was actually lost (`if (_currentInteractableObjectTarget != null)`) before destroying the UI prompt and closing the menu to preserve performance and prevent log spam.

## 5. Targeted Selection System

When multiple interactables are near each other, the player can **click** or **TAB** to select a specific target. This selection **locks** the E-key interaction to that target, overriding proximity-based auto-targeting.

### Architecture

The system is layered across three components:

| Layer | Component | Responsibility |
|-------|-----------|---------------|
| **UI Input** | `UI_PlayerTargeting` | Click raycast → stores `SelectedInteractable`, manages `LookTarget` |
| **Interaction** | `PlayerInteractionDetector` | Reads selection, locks E-key to selected target, issues auto-navigate |
| **Controller** | `PlayerController` | TAB key cycles through visible interactables via `CharacterAwareness` |

### Click-to-Select
- `UI_PlayerTargeting.UpdateTargeting()` raycasts on left-click.
- If the hit is an `InteractableObject` or a `Character` (with `CharacterInteractable`), it calls `SelectInteractable()`.
- `SelectInteractable()` stores the reference AND sets `CharacterVisual.SetLookTarget()` so the sprite faces the target and the `UI_TargetIndicator` tracks it.
- **UI Layer Guard**: Clicks on UI elements (`EventSystem.IsPointerOverGameObject()`) are ignored — they do NOT clear the selection.
- Clicking empty world space calls `ClearSelection()`.

### TAB Cycling
- `PlayerController` handles `KeyCode.Tab` input.
- Queries `Character.CharacterAwareness.GetVisibleInteractables()` for all interactables within the awareness radius.
- Sorts by distance and selects the closest, or cycles to the next if the closest is already selected.
- Calls `UI_PlayerTargeting.SelectInteractable()`.

### E-Key Interaction with Selection
When a selection exists in `UI_PlayerTargeting`:
1. **Target IS in `nearbyInteractables`** (player's rigidbody is inside target's InteractionZone) → interact immediately, same as before.
2. **Target is NOT in `nearbyInteractables`** → pressing E issues a `PlayerInteractCommand` (IPlayerCommand) that auto-navigates the player via NavMeshAgent to the target. On arrival (when `nearbyInteractables` contains the target), it triggers the interaction automatically.
3. **No selection** → falls back to existing proximity-based closest-target behavior.

### Rules
- **Never bypass the InteractionZone check**: Even with a selected target, the player's rigidbody MUST physically enter the target's InteractionZone before the interaction fires.
- **Selection overrides proximity**: While a target is selected, the E-prompt and interaction are locked to it. The closest-proximity auto-targeting is fully bypassed.
- **WASD cancels auto-navigate**: If the player presses a directional key while a `PlayerInteractCommand` is active, the command is cancelled immediately (standard `IPlayerCommand` behavior).
- **Combat overrides everything**: When `IsInBattle`, the `PlayerCombatCommand` takes full control. TAB and E-interaction are effectively bypassed.

## When to use this skill
- When creating a new interactable object in the game world.
- When scripting logic that requires a character to interact with an item, NPC, or resource node.
- When debugging issues where interactions do not fire (ensure the Character's Rigidbody `_rb` is physically within the `_interactionZone`!).
- When modifying click-targeting, TAB cycling, or the auto-navigate-to-interact flow.

## How to use it
1. Ensure the object's GameObject structure includes a `Collider` set up as a trigger for the `_interactionZone`.
2. Inherit from `InteractableObject` (or use an existing subclass) and implement the `Interact(Character interactor)` method.
3. **CRITICAL**: Always check or enforce that the interacting Character's `Rigidbody` (`_rb`) is within the `_interactionZone` before processing the interaction logic.
4. Delegate the resulting behavior to a specific `CharacterAction` to keep logic decoupled from the interactable itself.
