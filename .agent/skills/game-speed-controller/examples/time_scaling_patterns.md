# Game Speed Time-Scaling Patterns

When dealing with a dynamic `Time.timeScale` that can range from `0` (Paused) to `8` (Giga Speed), traditional Update loops and Coroutines can break down. Use these reference patterns to ensure robustness.

## 1. The "Accumulated Ticks" Pattern (Preventing Tick Throttling)

**WRONG (Breaks at 8x speed):**
```csharp
// If Time.timeScale is 8x, deltaTime is huge.
_tickTimer += Time.deltaTime; 
if (_tickTimer >= tickPeriod) // Only executes once per frame!
{
    _tickTimer -= tickPeriod;
    PerformTick();
}
```

**CORRECT (Catches up correctly):**
```csharp
_tickTimer += Time.deltaTime;
int ticksToProcess = Mathf.FloorToInt(_tickTimer / tickPeriod);

// Safety cap to prevent deadlocks on massive lag spikes
if (ticksToProcess > 30) ticksToProcess = 30;

for (int i = 0; i < ticksToProcess; i++)
{
    PerformTick();
}

_tickTimer -= ticksToProcess * tickPeriod;
```

## 2. Unscaled Time for UI

Whenever making UI elements fade, move, or wait, they must completely ignore `Time.timeScale` so they still function nicely when the game is paused.

```csharp
// In an Update loop:
transform.position = Vector3.Lerp(transform.position, target, 10f * Time.unscaledDeltaTime);

// In a Coroutine:
IEnumerator FadeRoutine() 
{
    yield return new WaitForSecondsRealtime(2f); // Correct
    // yield return new WaitForSeconds(2f); // WRONG! Freezes if game is paused.
}
```

## 3. High-Speed Physics Hit Detection

At `Time.timeScale = 8`, an attack animation might start and finish in *one frame*. The hitbox is instantiated and destroyed before `OnTriggerEnter` can fire in `FixedUpdate`.

**CORRECT (Force Hit Detection on Spawn):**
```csharp
public void Initialize()
{
    // Do not wait for OnTriggerEnter. Instantly grab targets.
    Collider[] hits = Physics.OverlapBox(
        hitCollider.bounds.center, 
        hitCollider.bounds.extents, 
        hitCollider.transform.rotation
    );

    foreach (var hit in hits)
    {
        ProcessDamage(hit);
    }
}
```
*Note: Ensure to use `Mathf.Abs` on scales if manually computing `halfExtents` to avoid negative sizes breaking the `Physics` engines.*

## 4. Time.time vs Time.frameCount

Never stagger AI or logic using `Time.frameCount` alone if the game features adjustable game speed. 

**WRONG (Causes massive AI delays at high speed):**
```csharp
// At 8x speed, 5 frames is almost a full in-game second of AI doing nothing!
if (Time.frameCount % 5 != 0) return; 
```

**CORRECT (Scale-respecting Stagger):**
```csharp
if (Time.time < _lastTickTime + _tickIntervalSeconds) return;
_lastTickTime = Time.time;
// AI Logic...
```
