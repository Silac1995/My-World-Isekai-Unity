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

- **PlayerUI.cs**: Central entry point, initialized by the player's `Character`.
- **Stats Events**: UI components subscribe to events from stat classes (`CharacterHealth`, etc.).
- **Shader-based Bars**: Uses `UI_HealthBar` with a custom `UI/HealthBar` shader for visual feedback.
- **Notification Channels**: `ScriptableObject`-based event channels allow any game system to raise a notification without depending on the UI layer.

## Initialization Flow
1. `PlayerController` or `UIManager` calls `PlayerUI.Initialize(playerCharacter)`.
2. `PlayerUI` retrieves stat components via `playerCharacter.Stats`.
3. Sub-components (Health bar, Action bar, Notification badges) subscribe to the appropriate events.

## Best Practices
- **Unsubscription**: Always unsubscribe from events in `OnDestroy()` or via a `CleanupEvents()` helper on re-initialization.
- **Performance**: Never use `GetComponent` in UI update loops. Use the shader-based approach for resource bars.
- **Property usage**: Always use the `CurrentAmount` **property setter** in stat classes; modifying the private field directly bypasses the `OnAmountChanged` event.
- **Notification raising**: Game systems (inventory, quest, shop) must never reference UI components directly. Always raise notifications through a `NotificationChannel` SO.

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
Attach to the icon button of any window. Assign the matching `NotificationChannel` in the Inspector.

**Responsibilities**:
- Subscribe to `OnNotificationRaised` → show the badge.
- Subscribe to `OnNotificationCleared` → hide the badge.
- Unsubscribe in `OnDisable`/`OnDestroy`.

#### Raising a Notification (from any game system)
Inject the `NotificationChannel` SO and call `Raise()`. The system doesn't need to know who is listening.

#### Clearing a Notification
Call `Clear()` on the channel when the window is opened or the content is viewed.

---

## Stats Integration
- `OnValueChanged(oldMax, newMax)`: Fired when the max value changes.
- `OnAmountChanged(oldAmount, newAmount)`: Fired when current resource changes.
