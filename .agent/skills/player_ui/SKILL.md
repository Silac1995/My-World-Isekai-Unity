---
name: player-ui
description: Interactions between the Player UI system and Character Stats/Actions, including the window notification badge system.
---

# Player UI System

The player UI system displays vital information, active actions, and manages inventory/equipment windows.

## When to use this skill
- To understand the interaction between character statistics and the user interface.
- To implement new reactive UI elements (progress bars, resource indicators).
- To follow synchronization best practices (Push vs. Pull) between game logic and display.
- To add notification badges to window icons (new items, alerts, updates).

## Architecture
The UI follows a **Push/Event-driven** model.

- **PlayerUI.cs**: Central entry point. Standardized scene instance name: **"UI_PlayerHUD"**.
- **Stats Events**: UI components subscribe to events from stat classes (`CharacterHealth`, etc.).
- **Shader-based Bars**: Uses `UI_HealthBar` for performance.
- **Notification Channels**: `ScriptableObject`-based. The `PlayerUI` "pushes" these channels to the `CharacterEquipment` during initialization.

## Initialization Flow
1. `Character.SwitchToPlayer()` searches for the GameObject **"UI_PlayerHUD"**.
2. `PlayerUI.Initialize(playerCharacter)` is called.
3. `PlayerUI` retrieves stats and **pushes** its local `NotificationChannel` assets to the character's `CharacterEquipment`.
4. Character is now ready to trigger HUD events through these channels.

## Best Practices
- **Standardized Naming**: Always refer to the main player HUD as **"UI_PlayerHUD"** for automatic discovery.
- **Unsubscription**: Always pair event subscriptions in `OnEnable` with unsubscriptions in `OnDisable`/`CleanupEvents`.
- **Performance**: Never use `Update()` for UI visibility polling. Use `UI_NotificationClearer` on window objects instead.
- **Notification clearing**: `SwitchToNPC` calls `ClearNotifications` on equipment to prevent NPC events from triggering the user's HUD.

---

## Component Patterns

### UI_HealthBar
Drives the `UI/HealthBar` shader. Subscribes to a `CharacterPrimaryStats` and pushes fill/ghost/flash values to an instanced material.

**Features**:
- **Damage Ghost**: A delayed trailing bar showing recent damage. Settings (`Ghost Delay`, `Ghost Drain Speed`) are controlled via the **Material Inspector**.
- **Heal Flash**: Sine-wave flash overlay on heal. Duration is set on the script component.
- **Dynamic Coloring**: Green → red transition based on `Low Health %` threshold.
- **Shine**: Top gloss effect (`Shine Strength`, `Shine Sharpness`), configured on the Material.

---

### Notification Badge System

Displays a visual indicator (dot, icon, or counter) on a window button when new content is available. Follows a fully decoupled, event-driven architecture using `ScriptableObject`-based notification channels.

#### Architecture

```
Game System (e.g. InventorySystem)
    └─ raises ──▶ NotificationChannel (ScriptableObject)
                        └─ subscribed by ──▶ UI_NotificationBadge (UI component)
```

Game systems never reference UI components. The UI badge listens to its assigned channel and reacts independently.

#### NotificationChannel (ScriptableObject)
A lightweight SO that acts as a decoupled event relay. It contains `OnNotificationRaised` and `OnNotificationCleared` events.

#### UI_NotificationBadge (MonoBehaviour)
Attach to any window icon button (e.g., Inventory Button).

**Responsibilities**:
- Listen to `NotificationChannel` and toggle its badge visibility.
- **Auto-Hide**: If a `Parent Window` is assigned, the badge stays hidden while that window is active.

#### UI_NotificationClearer (MonoBehaviour)
Attach to the actual **Window GameObject** (e.g., the Inventory frame). Set the matching channel in the inspector.

**Responsibilities**:
- **Event-Driven Clearing**: Automatically calls `channel.Clear()` on `OnEnable` (if `Clear On Enable` is true).
- This ensures the badge is cleared the moment the window is opened without using `Update()` polling.
- **Granular Hover Clearing**: If `Clear On Enable` is disabled, clearing is deferred to child components (e.g., `UI_ItemSlot` triggering `CharacterEquipment.ClearInventoryNotification()`) once all specific elements flagged as `IsNewlyAdded` have been explicitly hovered over.

---

### Window Management (UI_WindowBase)

The Player HUD utilizes a consistent inheritance pattern for its functional windows (e.g., Equipment, Relations, Stats).

#### Architecture
All HUD windows inherit from `UI_WindowBase`, which provides centralized logic for:
- Initializing the generic structure (referencing the main panel).
- Handling the standard "Close Button" functionality natively.

#### Implementing a New Window
1. **Inheritance**: Create a new controller script inheriting from `UI_WindowBase` (e.g., `UI_CharacterStats`).
2. **Initialization**: Provide an `Initialize(Character character)` method. In this method, subscribe to the relevant character data events using the **Push/Event-driven** model (e.g., `_character.Stats.OnStatsUpdated += HandleStatsUpdated`).
3. **Memory Management**: Always override `protected virtual void OnDestroy()` to unsubscribe from these events, ensuring you call `base.OnDestroy()`.
4. **Instantiation**: Use a Prefab Variant of `UI_WindowBase.prefab` to automatically inherit the close button and background structure.
5. **HUD Integration**: In `PlayerUI.cs`, add a serialized reference to your new window (`_myNewUI`) and its toggle button (`_buttonMyNewUI`). Hook them up to toggle their active state alongside existing windows.

---

### Dynamic Interaction Menu
The `PlayerUI` handles displaying context-sensitive actions through `OpenInteractionMenu(List<InteractionOption> options, bool persistAcrossClicks = false)`.

**Usage**:
- **Extended World Actions** (one-shot): Triggered by `PlayerInteractionDetector` when the player *holds* the interaction key. Shows actions like "Greet", "Follow", "Carry". Call with `persistAcrossClicks: false` (default) — the menu closes as soon as the player clicks any option.
- **Turn-based Dialogue** (persistent): Triggered from `HandleInteractionStateChanged(..., started: true)` when a `CharacterInteraction` formally begins. Call with `persistAcrossClicks: true` — the menu stays visible for the entire interaction, buttons re-lock after each click, and closure happens only when the `CharacterInteraction` terminates (the `started: false` branch calls `CloseInteractionMenu()`).

**Implementation Details**:
- `persistAcrossClicks` is forwarded to `UI_InteractionMenu.Initialize(options, lockByDefault: ...)`. When `true`, the menu also starts with all buttons locked, awaiting `SetInteractionMenuInteractable(true)` on `OnPlayerTurnStarted`.
- Action callbacks defined in the `InteractionOption` are executed directly when the corresponding UI button is clicked.
- For dialogue, the action should call `interactor.CharacterInteraction.PerformInteraction(action)` to register the player's choice and advance the turn logic.

---

### Invitation Prompt UI
Distinct from the passive `ToastNotificationSystem`, the `UI_InvitationPrompt` is an interactive popup requiring player input (Accept/Refuse).
- `PlayerUI` holds a reference to the `UI_InvitationPrompt` prefab/component.
- During initialization, it binds the `CharacterInvitation` events (`OnPlayerInvitationReceived`, `OnPlayerInvitationResolved`) to the prompt.
- The prompt drives its own visibility natively without `Update()` polling, and fires `ResolvePlayerInvitation()` back to the game logic when the player decides.

---

#### Current Windows
- **`UI_CharacterEquipment`**: Manages the inventory interaction logic. Uses `UI_NotificationClearer` to handle badges natively.
- **`UI_CharacterRelations`**: Dynamically displays a list of `UI_RelationshipSlot` instances based on `CharacterRelation` events.
- **`UI_CharacterStats`**: Dynamically maps and displays primary, secondary, and tertiary `CharacterStats` using `UI_StatSlot` components.

---

### Pause Menu

`PlayerUI` holds a serialized reference to `MWI.UI.PauseMenuController` (`_pauseMenu`), auto-assigned in `Awake()` via `GetComponentInChildren<PauseMenuController>(true)`.

**Key Properties:**
- `Character CharacterComponent` — read-only accessor for the bound character (used by `PauseMenuController` to check placement state)
- `bool IsInitialized` — whether a character is currently bound
- `void TogglePauseMenu()` — toggles the pause menu open/close

See `.agent/skills/pause-menu/SKILL.md` for full system documentation.

---

### Combat & Targeting UI
In combat (or specialized click-to-move states), the HUD takes over input handling using specific manager components:

- **`UI_PlayerTargeting`**: Manages the unified Point-and-Click + TAB targeting system.
  - **Unified Resolution**: Both click and TAB converge through `SelectInteractable(InteractableObject)`. Click uses `ResolveInteractableFromHit(Collider)` to extract the correct `InteractableObject` from a raycast hit (resolving characters via the `Character.CharacterInteractable` facade property, never `GetComponent`).
  - **LookTarget Consistency**: When targeting a character, always sets `LookTarget` to the **root Character transform** (not the `CharacterInteractable` child transform). This ensures `GetComponentInChildren<Collider>()` in `UpdateIndicatorTracking` finds the same collider (root CapsuleCollider) regardless of how the target was selected, preventing indicator height mismatches.
  - **Battle Target Lock**: During battle, `SelectInteractable` rejects non-battle participants (characters whose `GetTeamOf` returns null) and non-character interactables. Selecting a battle participant calls `CharacterCombat.SetPlannedTarget()` to redirect combat AI.
  - **Battle ClearSelection**: Clicking the ground during battle redirects the indicator to the current `PlannedTarget` (or `GetBestTargetFor` fallback) instead of fully clearing. Outside battle, it clears normally.
  - **Target Indicator Positioning**: Uses `col.bounds.max.y + _yOffset` for characters (anchored to top of collider bounds), falling back to `target.position + Vector3.up * _yOffset` for non-character targets.
- **`UI_CombatActionMenu`**: An initiative-driven HUD piece visible when `IsInBattle` is true.
  - **Attack Button**: Toggles the action intent. If queued, clicking cancels via `ClearActionIntent()`. If not queued, it validates `PlannedTarget` (must be alive AND in the battle via `GetTeamOf` check), falls back to `GetBestTargetFor`, and queues via `SetActionIntent` with a **dynamic closure** (`() => Attack(_characterCombat.PlannedTarget)`) so retargeting after queuing is respected.
  - **Visual Feedback**: Button text shows `"[Queued]"` in blue when `HasPlannedAction` is true. Switches between "Melee Attack" and "Ranged Attack" based on `CurrentCombatStyleExpertise`.

---

## Stats Integration
- `OnValueChanged(oldMax, newMax)`: Fired when the max value changes.
- `OnAmountChanged(oldAmount, newAmount)`: Fired when current resource changes.
