---
name: player-ui
description: Interactions between the Player UI system and Character Stats/Actions.
---

# Player UI System

The player UI system displays vital information, active actions, and manages inventory/equipment windows.

## When to use this skill
- To understand the interaction between character statistics and the user interface.
- To implement new reactive UI elements (progress bars, resource indicators).
- To follow synchronization best practices (Push vs. Pull) between game logic and display.

## Architecture
The UI follows a **Push/Event-driven** model.

- **PlayerUI.cs**: Central entry point, initialized by the player's `Character`.
- **Stats Events**: UI components subscribe to events from stat classes (`CharacterHealth`, etc.).
- **Shader-based Bars**: Uses `UI_HealthBar` with a custom `UI/HealthBar` shader for visual feedback.

## Initialization Flow
1. `PlayerController` or `UIManager` calls `PlayerUI.Initialize(playerCharacter)`.
2. `PlayerUI` retrieves stat components via `playerCharacter.Stats`.
3. Sub-components (Health bar, Action bar) subscribe to the appropriate events.

## Best Practices
- **Unsubscription**: Always unsubscribe from events in `OnDestroy()` or via a `CleanupEvents()` helper on re-initialization.
- **Performance**: Never use `GetComponent` in UI update loops. Use the shader-based approach for resource bars.
- **Property usage**: Always use the `CurrentAmount` **property setter** in stat classes; modifying the private field directly bypasses the `OnAmountChanged` event.

## Component Patterns

### UI_HealthBar
Drives the `UI/HealthBar` shader. Subscribes to a `CharacterPrimaryStats` and pushes fill/ghost/flash values to an instanced material.

**Features**:
- **Damage Ghost**: A delayed trailing bar showing recent damage. Settings (`Ghost Delay`, `Ghost Drain Speed`) are controlled via the **Material Inspector**.
- **Heal Flash**: Sine-wave flash overlay on heal. Duration is set on the script component.
- **Dynamic Coloring**: Green → red transition based on `Low Health %` threshold.
- **Shine**: Top gloss effect (`Shine Strength`, `Shine Sharpness`), configured on the Material.

**Setup**:
1. Create a UI Image (Image Type: **Simple**, Sprite: **Full Rect** white square).
2. Create a Material using the `UI/HealthBar` shader and assign it.
3. Attach `UI_HealthBar` to the same object; link the Image in `Bar Image`.

**Shader property categories**:
| Category     | Properties (Material Inspector)                     |
|--------------|------------------------------------------------------|
| Ghost Bar    | `Ghost Delay`, `Ghost Drain Speed`                   |
| Colors       | `Health Color`, `Low Health Color`, `Empty Color`, `Ghost Color`, `Heal Color` |
| Threshold    | `Low Health Pct`                                     |
| Shine        | `Shine Strength`, `Shine Sharpness`                  |

> Runtime-driven properties (`Fill Amount`, `Ghost Fill`, `Heal Flash`) are hidden in the inspector and set exclusively by `UI_HealthBar.cs`.

## Stats Integration
- `OnValueChanged(oldMax, newMax)`: Fired when the max value changes (e.g., equipment bonus).
- `OnAmountChanged(oldAmount, newAmount)`: Fired when current resource changes (damage/healing).
