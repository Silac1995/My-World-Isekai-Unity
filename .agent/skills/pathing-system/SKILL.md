---
name: pathing-system
description: Defines the rules for Path Diversification, Target Blacklisting (PathingMemory), and how Characters gracefully handle unreachable destinations.
---

# Pathing System

This skill covers the standard practices for navigating Characters within the `MWI.AI` namespace, specifically focusing on handling failure gracefully when paths are blocked or targets are unreachable.

## When to use this skill
- When writing new Movement or Navigation logic (e.g., in a `GoapAction` or Behaviour Tree Node).
- When a character gets stuck trying to reach an object that is enclosed or physically unreachable.
- When you need to filter targets (like `WorldItem` or `Character`) based on a worker's previous failed attempts to reach them.

## Core Concepts

### 1. CharacterPathingMemory (Target Blacklisting)
To prevent infinite loops where an NPC endlessly tries to walk towards a blocked item, the `Character` possesses a lightweight memory component (`CharacterPathingMemory`) instantiated in its `Awake()`.
- **How it works**: It maps the `InstanceID` of a target `GameObject` to a failure count `int`.
- **Thresholds**: If a target fails to be reached 3 consecutive times, `RecordFailure(int)` returns `true` (just blacklisted) and `IsBlacklisted(int)` will thereafter return `true`.
- **Memory Safety**: `PathingMemory` is NOT a `MonoBehaviour`. It avoids memory leaks by strictly tracking primitive `int` IDs instead of strong object references. It subscribes to `TimeManager.Instance.OnNewDay` and `OnHourChanged` and completely flushes itself automatically, and cleans up event subscriptions during `Character.OnDestroy()`.

### 2. Path Diversification
When a path fails but the target is not yet fully blacklisted (fail count < 3), the agent should attempt to "Diversify" its approach angle.
- This is achieved by taking the direct vector to the target and rotating it sideways (e.g., +90 degrees on attempt 1, -90 degrees on attempt 2).
- Example usage in a movement loop:
```csharp
int failCount = worker.PathingMemory.GetFailCount(targetInstanceID);
if (failCount > 0)
{
    Vector3 dir = (finalDest - worker.transform.position).normalized;
    float sign = (failCount % 2 == 0) ? -1f : 1f;
    Vector3 rotatedOffset = Quaternion.Euler(0, 90f * sign, 0) * dir;
    finalDest += rotatedOffset * 1.5f;
    // Always validate the offset on the NavMesh!
    if (NavMesh.SamplePosition(finalDest, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
        finalDest = hit.position;
}
```

### 3. NavMeshUtility 
Never implement raw distance or bounds checks directly inside a GOAP action unless strictly necessary. Always use the central utility:
- `NavMeshUtility.HasPathFailed(movement, requestTime, delayThreshold)`: Safely detects if the path calculation explicitly failed or resulted in a dead end.
- `NavMeshUtility.IsCharacterAtTargetZone(worker, targetCollider, extraMargin)`: Robust physics intersection check prior to assuming arrival.
- `NavMeshUtility.GetOptimalDestination(worker, target)`: Used to snap destinations to the nearest bounding box edge, preventing gridlock.

## How to use it

1. **Detecting Failure**: In your movement evaluation loop (often checking `HasPathFailed`), extract the target's `InstanceID`.
2. **Recording**: Call `bool blacklisted = worker.PathingMemory.RecordFailure(targetId)`.
3. **Aborting**: If `blacklisted` is true, immediately stop the movement (`movement.Stop(); movement.ResetPath();`) and cleanly fail/abort the action so GOAP or the BT can proceed.
4. **Filtering Scans**: When using physics overlaps or lists to find targets (like harvesting items), ignore objects where `worker.PathingMemory.IsBlacklisted(item.gameObject.GetInstanceID())` is true.

## Anti-Patterns
- **Never hold hard references** to `WorldItem`s or `GameObject`s in long-standing lists for failure tracking. The items might be destroyed by other systems (e.g., picked up by the player) causing null reference exceptions. Always use `GetInstanceID()`.
- **Never forget OnDestroy**: Ensure that any class subscribing to static/singleton events (like `TimeManager`) provides a clean-up method called explicitly when the owner dies/destroys.
