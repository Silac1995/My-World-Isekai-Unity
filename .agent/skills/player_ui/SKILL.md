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
- **Event-Driven Clearing**: Automatically calls `channel.Clear()` on `OnEnable`. 
- This ensures the badge is cleared the moment the window is opened without using `Update()` polling.

---

## Stats Integration
- `OnValueChanged(oldMax, newMax)`: Fired when the max value changes.
- `OnAmountChanged(oldAmount, newAmount)`: Fired when current resource changes.
