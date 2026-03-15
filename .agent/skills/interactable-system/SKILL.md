---
name: interactable-system
description: Architecture and rules for the Interactable System, dictating how a Character interacts with world objects (Items, Characters, Gatherables) using interaction zones and Rigidbodies.
---

# Interactable System

The Interactable System defines how entities in the world can be interacted with by a `Character`. It is built around a base abstract class `InteractableObject` and several specialized subclasses depending on the nature of the object.

## Core Rules

1. **Interaction Zone Constraint**: To interact with an object, and to get in range, **always use the `InteractionZone`**. For a `Character` to interact with any `InteractableObject`, the Character's Rigidbody (`_rb`) **MUST** be inside or explicitly checked against the object's `_interactionZone` (a `Collider`). This physical proximity check is mandatory for all interactions.
2. **Base Class**: `InteractableObject` provides the core `_interactionZone` (Collider), `interactionPrompt` (string), and the abstract `Interact(Character interactor)` method.
3. **Execution via CharacterActions**: The actual result of an interaction usually instantiates a `CharacterAction` (like `CharacterPickUpItem`, `CharacterEquipAction`, `CharacterStartInteraction`, `CharacterGatherAction`) which is then sent to the interactor's `CharacterActions.ExecuteAction(...)`.
4. **Action Exclusivity (No Manual Overrides)**: You must **never** manually bypass an `InteractableObject`'s lifecycle. Do not forcefully inject items into hands.
5. **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE. Delegate completely to `CharacterPickUpItem` to prevent item logic desyncs and ghost duplication.
6. **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
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

### 3. GatherableObject (`GatherableObject.cs`)
Used for resource nodes (trees, rocks, ore veins).
- **Behavior**: Triggers a `CharacterGatherAction` to perform the gathering over a specified `_gatherDuration`.
- **Outputs**: Produces items from a predefined `_outputItems` list (as `ItemSO`).
- **Depletion**: Tracks `_currentGatherCount`. Once it hits `_maxGatherCount`, the object becomes depleted (`_isDepleted = true`), hides its visuals, and waits for a `_respawnTime` before it can be gathered again.

## When to use this skill
- When creating a new interactable object in the game world.
- When scripting logic that requires a character to interact with an item, NPC, or resource node.
- When debugging issues where interactions do not fire (ensure the Character's Rigidbody `_rb` is physically within the `_interactionZone`!).

## How to use it
1. Ensure the object's GameObject structure includes a `Collider` set up as a trigger for the `_interactionZone`.
2. Inherit from `InteractableObject` (or use an existing subclass) and implement the `Interact(Character interactor)` method.
3. **CRITICAL**: Always check or enforce that the interacting Character's `Rigidbody` (`_rb`) is within the `_interactionZone` before processing the interaction logic.
4. Delegate the resulting behavior to a specific `CharacterAction` to keep logic decoupled from the interactable itself.
