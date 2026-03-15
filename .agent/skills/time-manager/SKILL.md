---
name: time-manager
description: Event-driven architecture for the Day/Night cycle, managing in-game time, hours, and phase transitions to avoid per-frame ticking.
---

# Time Manager (MWI.Time)

The `TimeManager` is the central source of truth for all in-game time progression. It handles translating real-world seconds into in-game hours and broadcasts events when significant time boundaries are crossed.

## When to use this skill
- When a system needs to trigger an action periodically (e.g., daily decay, hourly checks) without using `Update()` or `Coroutines`.
- When designing systems that rely on time of day (e.g., working hours, sleeping, shop opening times).
- When listening for Day/Night cycle transitions (`DayPhase`).

## The Event-Driven Time Architecture

Relying on `Update()` or `Coroutines` with `yield return new WaitForSeconds()` on hundreds of characters creates massive, scaling performance overhead. To respect the strict `update-usage` constraints of the project, all time-based logic should rely on the `TimeManager`'s events.

### 1. Available Events
The `TimeManager` provides three primary `event Action`s for systems to subscribe to:
- `OnPhaseChanged(DayPhase)`: Fired when the time of day transitions between Morning, Afternoon, Evening, and Night.
- `OnHourChanged(int)`: Fired exactly once every in-game hour. Ideal for recurring hourly checks (e.g., wage calculations, routine changes).
- `OnNewDay()`: Fired once when the time rolls over from 23:59 to 00:00. Ideal for daily resets or large periodic stat decays.

### 2. Implementation Rules for Subscriptions

**Rule 1: Always unsubscribe.**
Memory leaks are critical in Unity. If a MonoBehaviour subscribes to `TimeManager.Instance.OnNewDay`, it **MUST** unsubscribe in `OnDestroy` or `OnDisable`.

```csharp
private void Start()
{
    if (MWI.Time.TimeManager.Instance != null)
    {
        MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
    }
}

private void OnDestroy()
{
    if (MWI.Time.TimeManager.Instance != null)
    {
        MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
    }
}
```

**Rule 2: Read properties, don't track time independently.**
Never keep a local timer variable to guess what time it is. Always read `TimeManager.Instance.CurrentHour` or `TimeManager.Instance.CurrentPhase`.

### 3. Current Time Settings
- A full in-game day (`0.0f` to `1.0f`) is defined by `_secondsPerHour` (e.g., 50 real seconds = 1 in-game hour).
- The `DayPhase` breaks down as follows, determining ambient lighting and high-level behavioral switches:
  - `Morning` (06:00 - 11:59)
  - `Afternoon` (12:00 - 17:59)
  - `Evening` (18:00 - 20:59)
  - `Night` (21:00 - 05:59)
