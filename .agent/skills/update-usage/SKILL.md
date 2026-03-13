---
name: update-usage
description: Rules for Update(), FixedUpdate(), and LateUpdate() usage — when to use, when to avoid, and architectural alternatives.
---

# Update Usage Rules

This skill provides the architectural rules for optimizing Unity's `Update()` usage and promoting a cleaner, more efficient event-driven architecture.

## When to use this skill
- When deciding whether logic should be placed in a frame-by-frame loop.
- When refactoring polling-heavy systems into event-driven ones.
- When managing physics or camera logic (lifecycle order).
- When optimizing multiplayer network synchronization.

## How to use it

### 1. Avoid Unnecessary Polling
Do not introduce logic inside `Update()` when the same behavior can be implemented using a more efficient approach.
**Prefer:**
- C# events or `UnityEvents`.
- Callbacks or state transitions.
- Coroutines (for non-frame-critical delays).
- Trigger-based interactions.

### 2. Use Update() Only for Frame-Dependent Logic
`Update()` should be reserved for behavior that truly requires frame-by-frame execution:
- Character movement and smooth rotations.
- Continuous interpolation (`Lerp`/`Slerp`).
- Input reading.
- Camera tracking.

### 3. Respect Unity's Execution Order
- **FixedUpdate()**: Use for all physics-related logic (Rigidbody, forces, collisions).
- **LateUpdate()**: Use for logic that must run after all `Update()` calls (e.g., camera following).

### 4. Prefer Event-Driven Architecture
Structure gameplay systems around events. Use `OnHealthChanged` or `OnStateEnter` instead of checking health or state variables every frame in `Update()`.

### 5. Avoid Idle Update Loops
Do not leave `Update()` methods running in `MonoBehaviours` that are currently performing no meaningful work. Enable/disable the script only when needed.

### 6. Be Mindful of Coroutine Overhead
While Coroutines are a valid alternative for periodic checks, they contribute to GC pressure. For large-scale systems (many entities), prefer a centralized manager or event channels.

### 7. Favor Scalable Patterns
Prioritize patterns that minimize per-frame overhead:
- `ScriptableObject`-based event channels.
- Observer pattern for decoupled communication.
- ECS (Entity Component System) for massive simulations.

### 8. Multiplayer: Never Drive Network Logic from Update()
Avoid sending network messages or triggering RPCs inside `Update()`.
**Instead use:**
- **Dirty Flag Pattern**: Only sync state when it changes.
- **NetworkVariable Callbacks**: React to value changes synced by the server.
- **Tick-based Sync**: Use a fixed-rate tick for periodic updates (e.g., entity positions).
