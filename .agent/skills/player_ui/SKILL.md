---
name: player-ui
description: Interactions between the Player UI system and Character Stats/Actions.
---

# Player UI System

The player UI system is responsible for displaying vital information, active actions, and managing inventory/equipment windows.

## When to use this skill
- To understand the interaction between character statistics (`CharacterHealth`, etc.) and the user interface.
- To implement new reactive UI elements (progress bars, resource indicators).
- To follow synchronization best practices (Push vs. Pull) between game logic and display.

## How to use it
1. Refer to the architecture below to understand the data flow.
2. Use the `OnValueChanged` and `OnAmountChanged` events from the stats to update your UI components.
3. Refer to key components like `UI_SegmentedBar` for implementation examples.

## Architecture
The UI follows a **Push/Event-driven** model to avoid the overhead of updates in `Update()`.

- **PlayerUI.cs**: The central entry point. Initialized by the player's `Character`.
- **Stats Events**: UI components subscribe to events triggered by statistics classes (`CharacterHealth`, `CharacterMana`, etc.).
- **Segmented Bars**: Uses `UI_SegmentedBar` with a custom shader for advanced visual feedback.

## Initialization Flow
1. `PlayerController` or a `UIManager` calls `PlayerUI.Initialize(playerCharacter)`.
2. `PlayerUI` retrieves stat components via `playerCharacter.Stats`.
3. Sub-components (Health, Action bars) subscribe to the appropriate events.

## Best Practices
- **Unsubscription**: Always unsubscribe from events in `OnDestroy()` or during `Initialize` with a new character (`CleanupEvents`).
- **Performance**: Never use `GetComponent` or search for objects by name in UI update loops. Use the shader-based approach for resource bars.
- **Property usage**: Ensure you use the `CurrentAmount` **property setter** in the stats classes to trigger the `OnAmountChanged` event; modifying the private field directly will bypass the UI updates.

## Component Patterns

### UI_SegmentedBar
This component manages a generic resource bar divided into segments using a **custom HLSL shader** (`UI/SegmentedHealthBar`) for maximum optimization and visual polish.

**Core Features**:
- **Segmented Display**: Automatically divides the bar based on `HP Per Segment`.
- **Damage Ghost**: A delayed "ghost" bar that drains slowly after taking damage.
- **Heal Flash**: A visual ping-pong flash effect when receiving health.
- **Dynamic Coloring**: Automatically transitions to a "Low Health" color based on a configurable threshold.
- **Visual Polish**: Built-in shine effect with adjustable strength and sharpness.

**Required Setup**:
1. **Bar Object**: A UI Image object with the `UI_SegmentedBar` script.
   - **IMPORTANT**: Set **Image Type** to **Simple**.
   - **IMPORTANT**: Use a **White Square** or a Sprite with **Mesh Type: Full Rect**. The shader relies on 0-1 UV coordinates.
2. **Material**: Create a material using the `UI/SegmentedHealthBar` shader and assign it to the Image component.
3. **Configuration**:
   - `Bar Image`: Assign the Image component.
   - `Value Per Segment`: Amount of HP represented by one segment (default: 10).
   - `Gap Width`: Visual space between segments.
   - `Ghost/Heal Settings`: Adjust delays and speeds for the visual effects.

**Shader Properties**:
- `_HealthColor` / `_LowHealthColor`: The main bar colors.
- `_LowHealthThreshold`: % at which the bar turns red.
- `_GhostColor`: Color of the draining damage bar.
- `_HealColor`: Color of the heal flash overlay.
- `_ShineStrength` / `_ShineSharpness`: Controls for the top gloss effect.

## Stats Integration
The system relies on the following events in `CharacterBaseStats` and `CharacterPrimaryStats`:
- `OnValueChanged(oldMax, newMax)`: Triggered when the maximum value of a stat changes (e.g., equipment bonus).
- `OnAmountChanged(oldAmount, newAmount)`: Triggered when the current resource level changes (e.g., damage or healing).
