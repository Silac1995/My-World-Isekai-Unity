---
name: interactable-system
description: Architecture and rules for the Interactable System, dictating how a Character interacts with world objects (Items, Characters, Harvestables) using interaction zones and Rigidbodies.
---

# Interactable System

The Interactable System defines how entities in the world can be interacted with by a `Character`. It is built around a base abstract class `InteractableObject` and several specialized subclasses depending on the nature of the object.

## Core Rules

1. **THE INTERACTION RULE — Rigidbody-in-Zone. No exceptions.**

   > **A Character — player or NPC — may ONLY interact with an `InteractableObject` when its `Rigidbody` position is physically inside that interactable's `InteractionZone` collider.** No exceptions. No "close enough" distances. No zone-overlap shortcuts. No trust-the-client fast paths.

   This applies **uniformly** to every interactable in the game — items, characters, furniture, harvestables, doors, crafting stations, time clocks, mailboxes, and any new type added in the future.

   **THE ONLY SANCTIONED CHECK** is the canonical helper on the base class:

   ```csharp
   // In InteractableObject.cs — single source of truth for the rule
   public bool IsCharacterInInteractionZone(Character character);
   ```

   **Every** code path that decides whether an interaction may proceed — `Interact()` overrides, `CharacterAction.CanExecute()`, GOAP / BT action preconditions, server-authoritative RPCs, UI gating — **must** call `interactable.IsCharacterInInteractionZone(character)` and abort when it returns `false`. Do not reinvent the check inline. Do not substitute a distance threshold. Do not use a zone-vs-zone overlap. If the method returns `false`, the interaction is not permitted — full stop.

   > ⚠️ **Do not confuse `InteractableObject.InteractionZone` with `CharacterInteraction.InteractionZone`.** The former is the **proximity zone** authored on every interactable prefab and is the single source of truth for "am I close enough to interact?". The latter lives on the `CharacterInteraction` social subsystem and exists **only** to detect other characters for dialogue/invitation exchanges — it is **not** a general-purpose proximity collider and must **never** be used to decide whether a character can pick up an item, use furniture, or harvest a resource.

2. **Base Class**: `InteractableObject` provides the core `_interactionZone` (Collider), `interactionPrompt` (string), an explicit `Rigidbody` property (representing the true physical body), the abstract `Interact(Character interactor)` method, and the canonical proximity gate `IsCharacterInInteractionZone(Character)` from Core Rule #1.
3. **Distance math for AI targeting ≠ interaction gating.** When AI awareness / GOAP ranks candidate targets by distance (e.g. "which of these three trees is closest?"), always measure against `InteractableObject.Rigidbody.position`, **not** the zone bounds — zones vary in size and would bias the ranking. **However, ranking a target is not the same as gating the interaction.** Once a target is chosen and the character reaches it, the decision to actually interact must still go through `IsCharacterInInteractionZone(character)` (Core Rule #1).
4. **Execution via CharacterActions**: The actual result of an interaction usually instantiates a `CharacterAction` (like `CharacterPickUpItem`, `CharacterEquipAction`, `CharacterStartInteraction`, `CharacterHarvestAction`) which is then sent to the interactor's `CharacterActions.ExecuteAction(...)`.
5. **Action Exclusivity (No Manual Overrides)**: You must **never** manually bypass an `InteractableObject`'s lifecycle. Do not forcefully inject items into hands.
6. **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE. Delegate completely to `CharacterPickUpItem` to prevent item logic desyncs and ghost duplication.
7. **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
    - If it's a brand new item, it MUST be instantiated through `Assets/Resources/Data/Item/ItemSO.cs` with the instantiate methods that take color and other parameters.

## Proximity-Check API — the one method to remember

### `InteractableObject.IsCharacterInInteractionZone(Character)` — **mandatory, single source of truth**

Every `InteractableObject` exposes:

```csharp
public bool IsCharacterInInteractionZone(Character character);
```

Semantics:
- Returns `true` iff `character.transform.position` is contained by this interactable's `InteractionZone` bounds.
- Null-safe: returns `false` if `character` or `_interactionZone` is null.
- Reads `character.transform.position` — never a distance threshold, never a mutual zone overlap.

**Why `transform.position` and not `Rigidbody.position`:** `ClientNetworkTransform` syncs `transform.position` directly to the server each tick. On the server, a client-owned player's Rigidbody is kinematic; `Rigidbody.position` only catches up to `transform.position` on the next `FixedUpdate`. A server-authoritative RPC validating the client's proximity (e.g. `RequestPunchAtTimeClockServerRpc`) runs at an arbitrary point in the frame, so reading `rb.position` can false-negative a genuinely-inside-the-zone client by up to one physics tick. `transform.position` is the same value the client saw when it clicked, so server and client agree.

**Usage is mandatory** in every code path that gates an interaction — player input, server RPCs, `CharacterAction.CanExecute()`, GOAP preconditions, BT action preconditions, UI button-enable logic:

```csharp
public override bool CanExecute()
{
    if (!_interactable.IsCharacterInInteractionZone(character))
    {
        Debug.Log($"[Action] {character.CharacterName} is not inside {_interactable.name}'s InteractionZone.");
        return false;
    }
    // …other preconditions…
    return true;
}
```

If the method returns `false`, the interaction must not proceed. There is no fallback, no grace distance, no "close enough" override. Move the character inside the zone, or cancel the interaction.

### What you must NOT do

| Anti-pattern | Why it's forbidden |
|-------------|-------------------|
| `Vector3.Distance(character.transform.position, interactable.transform.position) < 1.5f` (or any magic number) | Ignores the authored zone. Hard-codes a radius per call site. Drifts over time. |
| `charZone.bounds.Intersects(targetZone.bounds)` (zone-vs-zone) | Two zones can overlap while the character's rigidbody is well outside the target's zone. It is a *detection heuristic*, not an *interaction gate*. Violates Core Rule #1. |
| Reimplementing the bounds-contains check inline | Every copy is a new place the rule can drift. Call the method. |
| Reading `character.Rigidbody.position` inside a server-authoritative proximity RPC | The server's kinematic Rigidbody for a client-owned player trails `transform.position` by one physics tick, which false-negatives right-at-the-edge interactions. `IsCharacterInInteractionZone` already reads `transform.position` — just call it. |

### Pre-existing helpers on `CharacterInteractionDetector` — context only

The detector (`PlayerInteractionDetector` / `NPCInteractionDetector`) still exposes `IsInRange`, `IsInPhysicalRange`, `IsOverlapping`, `IsInContactWith`, `IsTargetInRange`. They predate the canonical helper and are retained for internal detector bookkeeping (trigger caching, nearby-list maintenance). **Do not use them to decide whether an interaction may proceed** — for any new code, call `interactable.IsCharacterInInteractionZone(character)` instead. The `IsOverlapping` / `IsInContactWith` helpers in particular compute a zone-vs-zone overlap and therefore violate Core Rule #1 if used as an interaction gate.

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

### Door Hold Menu (Lock / Unlock / Repair)
`MapTransitionDoor` and `BuildingInteriorDoor` override `GetHoldInteractionOptions()` to provide door-specific actions when the player holds E:
- **"Unlock"** — shown when: door is locked, player has a matching key, door is not broken.
- **"Lock"** — shown when: door is unlocked, player has a matching key, door is not broken.
- **"Repair"** — shown when: door is broken (requires `DoorHealth` component).

These options call the corresponding `DoorLock` / `DoorHealth` ServerRpcs. All checks are guarded with `doorLock.IsSpawned` before reading `NetworkVariable.Value` or calling RPCs. See the **door-lock-system** skill for full details.

### 3. Dynamic Dialogue Menu
- When an interaction dialogue officially starts (i.e. it is the player's turn in a turn-based dialogue sequence), the `PlayerInteractionDetector` subscribes to `OnPlayerTurnStarted`.
- It dynamically pulls `GetDialogueInteractionOptions()` (e.g., *Talk*, *Insult*) and displays them in the context menu.

### 4. UI Stability & Single Responsibility
- **Local-Player Guarding**: Event subscriptions in `PlayerInteractionDetector` (`OnInteractionStateChanged`, `OnPlayerTurnStarted`, etc.) MUST be guarded with `if (!IsLocalPlayerCharacter()) return;` — which checks both `Character.IsPlayer()` AND `(!Character.IsSpawned || Character.IsOwner)`. A plain `IsPlayer()` check is insufficient in multiplayer: remote player Characters also have a `PlayerController` (for control switching), so `IsPlayer()` returns true on every machine, which would cause every player's HUD to open its interaction menu whenever any player starts an interaction. The ownership check ensures only the LOCAL player's HUD reacts.
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
- **Never bypass the InteractionZone check — call `interactable.IsCharacterInInteractionZone(character)`.** Even with a selected target, the player's rigidbody MUST physically be inside the target's InteractionZone before the interaction fires. This is Core Rule #1 and applies to selection, proximity, auto-navigate arrival, and every other code path.
- **Selection overrides proximity**: While a target is selected, the E-prompt and interaction are locked to it. The closest-proximity auto-targeting is fully bypassed.
- **WASD cancels auto-navigate**: If the player presses a directional key while a `PlayerInteractCommand` is active, the command is cancelled immediately (standard `IPlayerCommand` behavior).
- **Combat overrides everything**: When `IsInBattle`, the `PlayerCombatCommand` takes full control. TAB and E-interaction are effectively bypassed.

## When to use this skill
- When creating a new interactable object in the game world.
- When scripting logic that requires a character to interact with an item, NPC, furniture, or resource node — **always gate with `interactable.IsCharacterInInteractionZone(character)`**.
- When debugging issues where interactions do not fire (ensure the Character is physically within the `InteractionZone` — verify with `interactable.IsCharacterInInteractionZone(character)` which reads `character.transform.position`).
- When modifying click-targeting, TAB cycling, or the auto-navigate-to-interact flow.

## How to use it
1. Ensure the object's GameObject structure includes a `Collider` set up as a trigger for the `_interactionZone`.
2. Inherit from `InteractableObject` (or use an existing subclass) and implement the `Interact(Character interactor)` method.
3. **CRITICAL — Core Rule #1**: At the very top of `Interact()` (and at the top of every `CharacterAction.CanExecute()` / GOAP precondition / server RPC handler that resolves the interaction), call `IsCharacterInInteractionZone(interactor)`. If it returns `false`, abort immediately. Never substitute a distance check, a zone-overlap, or any hand-rolled bounds-containment that reads a different coordinate source (e.g. `Rigidbody.position` on the server for a client-owned player — see the Anti-Pattern table above).
4. Delegate the resulting behavior to a specific `CharacterAction` to keep logic decoupled from the interactable itself.
