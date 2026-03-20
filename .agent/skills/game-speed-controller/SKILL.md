---
name: game-speed-controller
description: Architecture of the Game Speed Controller, Time.timeScale manipulation in multiplayer, and strict rules for preventing tick throttling at high speeds (Fast-Forward).
---

# Game Speed Controller

The Game Speed Controller system allows the server to dynamically alter the simulation speed of the game (Normal, Fast, Super Fast, Giga Speed). It relies on Unity's built-in `Time.timeScale` to alter physics and `Time.deltaTime`, while synchronizing this scale across all connected clients via a `NetworkVariable`.

## When to use this skill
- When adding new UI elements or animations that should **not** speed up when the game speeds up.
- When creating periodic loops, timers, or cooldowns (e.g., in Combat, AI, or logistics) that must execute accurately at 8x game speed.
- When expanding the `GameSpeedController` or `UI_GameSpeedController`.

## The Time-Scaling Architecture

### 1. The Core Controller (`GameSpeedController`)
The system is Server-Authoritative. The Host sets the speed via a `NetworkVariable<float> _serverTimeScale`. 
When `_serverTimeScale` changes, the `OnValueChanged` callback applies the scale to `UnityEngine.Time.timeScale` locally for all clients and invokes the `OnSpeedChanged` C# event.
**Rule:** Never modify `Time.timeScale` directly from outside this class. Always use `GameSpeedController.Instance.RequestSpeedChange(float newSpeed)`.

### 2. UI And Event-Driven Updates
UI elements that display the current speed or react to speed changes must subscribe to `GameSpeedController.OnSpeedChanged`.
**Rule:** UI animations, Toast Notifications, and visual Lerps *must* use `Time.unscaledDeltaTime` or `WaitForSecondsRealtime` instead of `Time.deltaTime`, to prevent them from freezing when the game pauses or rushing when it runs at 8x.

### 3. The "Tick Throttling" Danger
When the game runs at 8x speed ("Giga Speed"), `Time.deltaTime` becomes 8 times larger. A frame that normally represents `0.016s` will represent `0.128s` of in-game time. 
If an AI or combat system uses an `if` statement to process a tick, it will only process **a maximum of 1 tick per frame**, falling massively behind real game time and causing characters to freeze or act extremely slowly.
**Rule:** ANY time-based accumulation loop must process *multiple* ticks per frame if `Time.deltaTime` exceeds the tick interval. Use a `while` loop or calculate `ticksToProcess` to catch up.

### 4. Bypassing Frame-Skipped Hitboxes
At extremely high time scales, whole animations might start and finish in the *exact same frame*. This means Animation Events (e.g., spawn hitbox -> despawn hitbox) might execute sequentially without allowing Unity's `FixedUpdate` (Physics engine) to run even once. `OnTriggerEnter` will miss completely.
**Rule:** Combat hitboxes must perform an instantaneous `Physics.Overlap` check exactly upon initialization to guarantee hits at high game speeds.

[Note: View `examples/time_scaling_patterns.md` for proper implementation of accumulated ticks and instant hitboxes.]
