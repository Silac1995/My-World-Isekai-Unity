---
trigger: always_on
---

# Architecture & Clean Code Rules (SOLID)

Adhere to SOLID principles and the following coding standards for all architectural decisions:

1. **Single Responsibility (SRP):** Each class must have one purpose. Separate logic (e.g., Health, Movement, Data) into distinct components.
2. **Open/Closed (OCP):** Code should be open for extension but closed for modification. Use interfaces and abstract classes to add features without altering existing logic.
3. **Liskov Substitution (LSP):** Subclasses must be fully substitutable for their base classes without breaking functionality. Avoid "NotImplementedException" in overrides.
4. **Interface Segregation (ISP):** Prefer many small, specific interfaces over one large, general-purpose interface.
5. **Dependency Inversion (DIP):** High-level modules must depend on abstractions (interfaces), not concrete implementations. Use Dependency Injection where possible.

# Implementation Specifics

- **Naming Convention:** Always use underscores for private attributes (e.g., `_privateVariable`).
- **Memory Management:** To prevent memory leaks and coroutine accumulation, always implement proper cleanup logic. Ensure events are unsubscribed and coroutines are stopped or deleted when the object is destroyed or no longer needed. **Always pair event subscriptions in `OnEnable`/`Awake` with unsubscriptions in `OnDisable`/`OnDestroy`.**
- **Project Context:** The game uses 2D sprites in a 3D environment and includes multiplayer functionality. Ensure all network-related logic follows these modular principles for scalability.

# Unity Architectural Rules: Update() Usage

When performing code structuring or refactoring in Unity, follow these architectural rules regarding the use of `Update()`:

## 1. Avoid unnecessary polling
Do not introduce logic inside `Update()` when the same behavior can be implemented using a more efficient or event-driven approach.
**Prefer:**
- C# events or `UnityEvents`
- Callbacks
- Coroutines (see caveat below)
- Timers
- State transitions
- Trigger-based interactions

*Example: Instead of checking conditions every frame inside Update(), trigger logic when the relevant state actually changes.*

## 2. Use Update() only for frame-dependent logic
`Update()` should only be used when the behavior must run every frame.
**Typical valid cases include:**
- Character movement
- Input reading
- Continuous interpolation (`Lerp`/`Slerp`)
- Smooth rotations or animations
- Camera tracking

*If the logic does not depend on frame-by-frame updates, Update() should not be used.*

## 3. Respect Unity's execution order
- Use `FixedUpdate()` for physics-related logic (`Rigidbody`, forces, collision responses).
- Use `LateUpdate()` for logic that must run after all `Update()` calls, such as camera following or post-movement adjustments.

*Do not place physics logic in Update() or camera logic where order of execution is not guaranteed.*

## 4. Prefer event-driven architecture
Whenever possible, structure gameplay systems around events rather than continuous checks.
**Examples:**
- Use `OnHealthChanged` instead of checking health every frame.
- Use interaction triggers instead of proximity checks in `Update()`.
- Use events when stats, states, or effects change.

## 5. Avoid idle Update loops
Do not leave `MonoBehaviours` with an `Update()` method that frequently runs without performing meaningful work.
**If a component only needs to run temporarily:**
- Enable the script only when needed.
- Disable it when the task is complete.

## 6. Be mindful of Coroutine overhead
Coroutines are a valid alternative to `Update()` for periodic or delayed logic, but they allocate memory on the heap and contribute to GC pressure. For systems involving many simultaneous entities, prefer a centralized timer manager, an event channel, or a polling-free architecture over spawning large numbers of coroutines.

## 7. Favor scalable patterns
When designing systems (characters, stats, status effects, interactions, AI behaviors), prefer architectures that scale well with many entities and minimize per-frame CPU overhead.
**Recommended patterns:**
- `ScriptableObject`-based event channels for stats, states, and global signals.
- Observer pattern for decoupled system communication.
- ECS/DOTS for performance-critical, large-scale simulations.

## 8. Multiplayer: never drive network logic from Update()
In multiplayer contexts, avoid sending network messages, syncing state, or triggering RPCs directly inside `Update()`. Running network calls every frame causes bandwidth overload, unnecessary latency, and desync issues.

**Prefer:**
- **Dirty flag pattern**: only sync state when it has actually changed, not every frame.
- **Event-driven RPCs**: trigger server calls (`Command`, `ServerRpc`) or client calls (`ClientRpc`, `TargetRpc`) in response to meaningful events (input, state change, interaction), not on a per-frame basis.
- **Interest management**: let the network layer decide when and what to sync, rather than forcing sync from `Update()`.
- **NetworkVariable / SyncVar**: for continuously replicated values (health, position, state), prefer built-in networked variables with change callbacks over manual sync in `Update()`.
- **Tick-based or interval-based sync**: if periodic sync is required, use a fixed network tick rate or a timed interval rather than every frame.

**Additionally:**
- Separate client-side prediction logic (which may use `Update()`) from server-authoritative validation (which should be event or tick-driven).

## 9. Time.timeScale and Game Speed Context
The project uses a dynamic Game Speed controller (up to 8x "Giga Speed"). You must always write time-dependent code that scales flawlessly:
- **Coroutines & UI:** Never use `WaitForSeconds` or `Time.deltaTime` for UI animations, Toast Notifications, or real-world UI pauses. Always use `WaitForSecondsRealtime` and `Time.unscaledDeltaTime` to prevent them from freezing when paused or rushing at 8x speed.
- **Tick Throttling:** Never use `if (timer >= interval)` for game-simulation fixed loops. At high speeds, `Time.deltaTime` exceeds the interval entirely, processing only 1 tick per frame and causing the game to fall behind. Use a `for` loop to process accumulated `ticksToProcess` seamlessly.
- **AI & Logic Staggers:** Never stagger AI using `Time.frameCount` (e.g. `if (Time.frameCount % 5 != 0) return;`). At 8x speed, 5 frames means almost a full in-game second of AI doing nothing. Always use `Time.time`-based staggering.
- **Pathing Timeouts:** For async systems like `NavMesh.CalculatePath`, never use `Time.time` for the fail timeout. Use `Time.unscaledTime` so the algorithm always gets the exact same amount of real-world computing time (e.g., 0.2s) regardless of whether the simulation is paused or sped up.