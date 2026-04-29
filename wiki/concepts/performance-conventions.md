---
type: concept
title: "Performance & Optimisation Conventions"
tags: [performance, optimisation, gc, allocation, profiling, conventions, hot-path]
created: 2026-04-27
updated: 2026-04-27
sources: []
related:
  - "[[optimisation-backlog]]"
  - "[[host-progressive-freeze-debug-log-spam]]"
  - "[[jobs-and-logistics]]"
  - "[[building-logistics-manager]]"
  - "[[ai-goap]]"
  - "[[character-job]]"
  - "[[network-architecture]]"
status: active
confidence: high
---

# Performance & Optimisation Conventions

## Summary
Conventions and patterns for writing high-performance gameplay code in this project. Distilled from the **2026-04-25 → 2026-04-27 logistics performance pass** that took a worker-heavy scene from `< 30 fps` to a stable `60 fps` target. Future code should default to these patterns; deviations should be measured-and-justified, not stylistic.

The headline rule lives in [CLAUDE.md rule #34](../../CLAUDE.md). This page is the deeper "why and how" — the catalogue of concrete patterns + anti-patterns + profiler workflow that future Claude (or any contributor) should consult before touching per-frame code.

## Definition

**"Performance" in this project means three things, in priority order:**

1. **No per-frame `GC.Alloc`.** Spike frames almost always come from major GC. The Mono GC fires on cumulative allocation thresholds (~4-16 MB depending on settings). 100 KB / frame at 60 fps = 6 MB / sec ≈ a major GC every ~1-2 seconds, each pausing 20-60 ms. The fix isn't "reduce alloc cost," it's "eliminate alloc."
2. **No idempotent polling.** Logic that runs at tick rate but produces zero work on a stable state is a bug, even if it's allocation-free. Every method that polls "is there work to do?" must early-exit cheaply.
3. **No surprise CPU.** Debug instrumentation, `Debug.Log` with stack-trace extraction, ungated `GetComponentsInChildren`, `Physics.OverlapSphere` (alloc variant) — these silently dominate frame budget.

The 2026-04-27 profiler session measured all three failure modes at once: `UI_CommercialBuildingDebugScript` was 28% of frame, allocating 59 KB / frame from 633 separate `GC.Alloc` events, while the actual game logic (BT, Jobs, GOAP, logistics) measured at ~4 KB / frame total after the optimisation pass.

## Context

Applies to every per-frame, per-tick, per-NPC code path in the project:

- `MonoBehaviour.Update` / `LateUpdate` / `FixedUpdate`
- `BTNode.OnExecute` (BT tick rate is 10 Hz per NPC — see [`NPCBehaviourTree.cs:46`](../../Assets/Scripts/AI/NPCBehaviourTree.cs))
- `Job.Execute` (gated by `Job.ExecuteIntervalSeconds`, default 0.1 s)
- `GoapAction.IsValid` / `GoapAction.Execute`
- Coroutine `MoveNext()` paths
- Network serialization callbacks, `OnNetworkSpawn`, `NetworkVariable` `OnValueChanged`
- UI `IPointerHandler` / `Graphic.RebuildLayout` triggers
- Per-frame physics queries

Does NOT typically apply to:
- One-shot lifecycle (`Awake`, `Start`, `OnDestroy`)
- Save / load paths (run rarely; allocation OK)
- Editor-only scripts (`#if UNITY_EDITOR`)
- Macro-simulation catch-up math (runs offline; pure-data, no per-frame budget)

## Pattern catalogue

Every pattern below has a real-code anchor in the project so future Claude can `goto definition` and copy the structure.

### Pattern 1 — Dirty-flag dispatcher gating

**Use when:** a method runs at tick rate doing work that's idempotent on stable state.

**Where to find it:** [`LogisticsOrderBook._dispatchDirty`](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs) + [`LogisticsTransportDispatcher.ProcessActiveBuyOrders`](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs).

**Shape:**
```csharp
private bool _dispatchDirty = true;          // start dirty so first tick processes
public bool IsDispatchDirty => _dispatchDirty;
public void MarkDispatchDirty() => _dispatchDirty = true;
public void ClearDispatchDirty() => _dispatchDirty = false;

// Every state-change method on the same class:
public bool AddX(...) { ...; _dispatchDirty = true; return true; }

// Caller hot path:
public void Tick()
{
    if (!_orderBook.IsDispatchDirty) return;        // early-exit, zero work
    // ... do the actual work ...
    _orderBook.ClearDispatchDirty();                 // clear at the end
}
```

**Critical rules:**
- Initial state = dirty (covers warm-start cases like load-from-save).
- Every Add / Remove / mutate method on the gated state sets dirty AT THE END of its work.
- External mutations (e.g. inventory changes that affect dispatch) call a public `MarkDispatchDirty()` pass-through.
- Clear the flag at the end of a successful pass, not the start. State changes that happen DURING the pass are already absorbed by the same call.

### Pattern 2 — TTL cache with explicit invalidation

**Use when:** a query walks recursive hierarchies / `GetComponentsInChildren` / cross-system scans, and the underlying state changes rarely.

**Where to find it:** [`CommercialBuilding.GetStorageFurnitureCached`](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) + [`CraftingBuilding.RebuildCraftableCacheIfStale`](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs).

**Shape:**
```csharp
protected const float CacheTTLSeconds = 2f;
private List<T> _cached;
private float _cacheValidUntil = -1f;

public IReadOnlyList<T> Get() {
    if (Time.time < _cacheValidUntil && _cached != null) return _cached;
    if (_cached == null) _cached = new List<T>();
    else _cached.Clear();
    // ... rebuild into _cached, including any defensive fallback walks ...
    _cacheValidUntil = Time.time + CacheTTLSeconds;
    return _cached;
}

/// <summary>Force the next Get() to re-walk. Call after a known state change.</summary>
public void Invalidate() { _cacheValidUntil = -1f; }
```

**Critical rules:**
- The returned list is a **shared reference**. Callers MUST treat it as read-only. Document this on the public method.
- **Preserve existing fallback paths inside the rebuild** (e.g. `GetComponentsInChildren` registration-race fallbacks). Pay them once per refresh, not once per query.
- Add a public `Invalidate()` hook so callers with known state changes can force freshness without waiting for TTL.
- Use `Time.time` (gameplay-time, respects timeScale) for simulation caches. Use `Time.unscaledTime` for UI caches.
- For O(1) `Contains` lookups, maintain a parallel `HashSet<T>` alongside the list.

### Pattern 3 — Centralised invalidation through chokepoint methods

**Use when:** a TTL cache should be invalidated by mutations that happen across many callsites.

**Where to find it:** [`FurnitureManager.InvalidateOwnerBuildingCaches`](../../Assets/Scripts/World/Buildings/FurnitureManager.cs).

**Shape:**
```csharp
// Single central invalidation method on the class everyone goes through:
private CommercialBuilding _ownerBuilding;
private bool _ownerBuildingResolved;

private void InvalidateOwnerBuildingCaches() {
    if (!_ownerBuildingResolved) {
        _ownerBuilding = GetComponentInParent<CommercialBuilding>();
        _ownerBuildingResolved = true;
    }
    if (_ownerBuilding == null) return;
    _ownerBuilding.InvalidateStorageFurnitureCache();
    if (_ownerBuilding is CraftingBuilding crafting) crafting.InvalidateCraftableCache();
}

// Every mutation method calls it:
public bool AddX(...) { ...; InvalidateOwnerBuildingCaches(); ... }
public void RemoveX(...) { ...; InvalidateOwnerBuildingCaches(); ... }
```

**Critical rules:**
- Lazy-resolve the parent reference (the hierarchy may not be fully wired at Awake on every spawn path).
- Cache the resolution so each invalidation is O(1).
- Hook EVERY mutation site, not callsite-by-callsite. Less surface to forget.

### Pattern 4 — `Physics.*NonAlloc` with a reused buffer

**Use when:** any `Physics.OverlapBox` / `OverlapSphere` / `Raycast` inside a per-frame or per-tick path.

**Where to find it:** [`CommercialBuilding.OverlapBuffer`](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs), [`GoapAction_GatherStorageItems.s_overlapBuffer`](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs), [`CharacterAwareness._overlapBuffer`](../../Assets/Scripts/Character/CharacterAwareness.cs).

**Shape:**
```csharp
private const int OverlapBufferSize = 128;
private readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];
// or static if shared across instances on the main thread:
// private static readonly Collider[] s_overlapBuffer = new Collider[128];

int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer,
                                          rotation, layerMask, queryTriggerInteraction);
if (hitCount == OverlapBufferSize) {
    Debug.LogWarning($"[X] saturated the OverlapBox buffer ({OverlapBufferSize}) — bump it. Items beyond #{OverlapBufferSize} truncated.", this);
}
for (int i = 0; i < hitCount; i++) {
    var col = _overlapBuffer[i];
    if (col == null) continue;
    // ...
}
```

**Critical rules:**
- Always check for saturation (`hitCount == buffer.Length`) and warn — that signals "bump the buffer size" instead of silently truncating data.
- Buffer size: `64` for typical small zones, `128` for larger ones, `256` for whole-map sweeps. Don't go below 64.
- Static shared buffers are safe in single-threaded Unity main-thread code. Use them when the buffer is shared across many action instances.
- Never use `Physics.OverlapBox` / `OverlapSphere` (the alloc variants) in per-frame or per-tick paths.

### Pattern 5 — Cadence stagger via `ExecuteIntervalSeconds`

**Use when:** a system runs at the BT/Update tick rate (10 Hz) but its work is reactive to slower-changing state.

**Where to find it:** [`Job.ExecuteIntervalSeconds`](../../Assets/Scripts/World/Jobs/Job.cs) + [`BTAction_Work.HandleWorking`](../../Assets/Scripts/AI/Actions/BTAction_Work.cs).

**Shape:**
```csharp
// On the per-system base class:
public virtual float ExecuteIntervalSeconds => 0.1f;   // default = BT tick rate (no throttle)

// Heavy-planning subclass:
public override float ExecuteIntervalSeconds => 0.3f;  // 3.3 Hz instead of 10 Hz

// In the BT tick that drives it:
private float _lastExecuteTime = -1f;

protected override void OnEnter(Blackboard bb) {
    _lastExecuteTime = -1f;  // first call after entering always fires
}

private BTNodeStatus HandleHeavyWork(...) {
    float interval = component.ExecuteIntervalSeconds;
    if (UnityEngine.Time.time - _lastExecuteTime >= interval) {
        component.HeavyWork();
        _lastExecuteTime = UnityEngine.Time.time;
    }
    return BTNodeStatus.Running;
}
```

**Critical rules:**
- DO NOT slow the BT itself. Combat reaction, schedule transitions, animation gating still need 10 Hz. Only throttle the heavy domain logic.
- Reset the timer in `OnEnter` so re-entering the branch always fires immediately.
- Use `UnityEngine.Time.time` fully-qualified — there's a `MWI.Time` namespace in this project that clashes.
- For projects with multiple Job archetypes: profile each before staggering. Don't slow `JobVendor` (customer queue needs responsiveness) or `JobCrafter` (animation event timing). DO slow heavy planners (LogisticsManager, Harvester).

### Pattern 6 — Per-action TTL cache for GOAP results

**Use when:** a `GoapAction.IsValid` does heavy `Physics.Overlap*` or scan work to determine "is there a target?" and the same answer is fine for ~0.5 s.

**Where to find it:** [`GoapAction_GatherStorageItems.FindLooseWorldItem`](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs).

**Shape:**
```csharp
private const float CacheTTLSeconds = 0.5f;
private float _lastSearchTime = -1f;
private T _lastSearchResult;

public T Find(...) {
    // Cache hit: re-use if fresh AND target still valid
    if (Time.time - _lastSearchTime < CacheTTLSeconds) {
        if (_lastSearchResult == null) return null;
        if (_lastSearchResult.gameObject != null && _lastSearchResult.IsStillValid()) {
            return _lastSearchResult;
        }
    }
    // ... do the actual search ...
    _lastSearchTime = Time.time;
    _lastSearchResult = result;
    return result;
}

public override void Exit(Character worker) {
    // Cache MUST clear on action exit so a fresh action invocation starts cold.
    base.Exit(worker);
    _lastSearchTime = -1f;
    _lastSearchResult = null;
}
```

**Critical rules:**
- TTL must be > the action's own tick interval (default BT 10 Hz × Cₐ throttling 0.3 s = at most 0.3 s; cache TTL of 0.5 s comfortably covers).
- **Always clear the cache in `Exit()`** — GOAP actions are pooled in some patterns; stale state from a previous invocation can leak.
- Validate the cached object is still good (not null, not picked up, not destroyed) on cache hit. A stale cached `WorldItem` that another worker grabbed must trigger a refresh.

### Pattern 7 — Reused scratch list / dictionary

**Use when:** any per-call `new List<T>()` or `new Dictionary<,>()` for short-lived materialization.

**Where to find it:** [`LogisticsStockEvaluator._scratchStockTargets`](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs), [`JobLogisticsManager._scratchWorldState`](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs).

**Shape:**
```csharp
private readonly List<T> _scratchList = new List<T>(8);

public void DoWork() {
    _scratchList.Clear();
    foreach (var x in source) _scratchList.Add(x);
    // ... use _scratchList ...
    _scratchList.Clear();   // optional defensive clear at end
}
```

**Critical rules:**
- Pre-size the initial capacity to a sensible expected count.
- `Clear()` instead of `new`. List capacity is preserved across clears.
- Never expose the scratch list publicly. Callers calling concurrently would corrupt each other.
- For dicts and hash sets, same pattern.

### Pattern 8 — Lazy-built singleton cache for inspector-authored data

**Use when:** a getter projects from a `[SerializeField]` collection (e.g. `_itemsToSell.Select(e => e.Item).ToList()`) that doesn't change at runtime.

**Where to find it:** [`ShopBuilding.ItemsToSell`](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs).

**Shape:**
```csharp
private List<T> _cachedProjection;
public IReadOnlyList<T> Projection {
    get {
        if (_cachedProjection == null) {
            _cachedProjection = new List<T>(_source.Count);
            foreach (var e in _source) {
                if (e.IsValid) _cachedProjection.Add(e.X);
            }
        }
        return _cachedProjection;
    }
}
```

**Critical rules:**
- Only safe when the underlying data is inspector-authored / immutable at runtime. If it could change, use a TTL cache (Pattern 2).
- Returns `IReadOnlyList<T>` to discourage callers from mutating the shared cache.

## Anti-patterns

### A1 — `LINQ` in hot paths
`Where`, `Select`, `OfType`, `ToList`, `Any`, `FirstOrDefault`, `Sum`, `OrderBy` — every call allocates an enumerator + the result collection. Use `for` / `foreach` loops with explicit predicates. **Even one LINQ call per Update per NPC × 12 NPCs × 60 fps = 720 enumerator allocs/sec.**

### A2 — `Debug.Log` without `if (NPCDebug.Verbose…)`
Every ungated log triggers `StackTraceUtility.ExtractStackTrace` (~4 KB / call) and on Windows the `Console.Log` hooks back to the editor console buffer, which causes the documented [[host-progressive-freeze-debug-log-spam]] pattern. The single line that triggered the deepest perf bug in the 2026-04-25 audit was `Debug.Log` on every successful `CharacterAwareness.GetVisibleInteractables<T>()` call.

### A3 — `Physics.OverlapBox` / `OverlapSphere` (alloc variants) per-frame
Returns a freshly-allocated `Collider[]` every call. Always use the `NonAlloc` variants with a reused buffer. See Pattern 4.

### A4 — `GetComponentsInChildren<T>(true)` per-frame
Walks the entire transform tree, including inactive objects. Allocates an array. Use a register-on-spawn pattern + a maintained `List<T>` instead. **One legitimate exception:** as a defensive fallback inside a TTL-cached method (e.g. `CraftingBuilding.GetCraftableItems`), where the cost is paid once per refresh, not once per query.

### A5 — `new List<T>()` / `new Dictionary<,>()` in `Update` / `IsValid`
Use Pattern 7 (reused scratch). For per-action state, instance fields. For per-method temporaries, static if main-thread.

### A6 — Polling for "is there work?" without dirty-flag gating
A method that runs every tick to check whether to do anything is wrong if its decision could be event-driven. See Pattern 1.

### A7 — Debug UI / overlays running in production scenes
A debug overlay running on every `CommercialBuilding` was 28% of frame in our 2026-04-27 profiler session. Gate behind dev-mode, stagger updates, or bind to a single `Selection` instance. Never let "I'll disable it later" code ship as always-on.

### A8 — `Instantiate(prefab)` per gameplay event without pooling
Every `Instantiate` of a multi-child prefab allocates ~hundreds of KB. Items handed off in the logistics cycle currently allocate ~0.8 MB / frame this way (see [[optimisation-backlog]] Tier 4). For things created/destroyed per gameplay loop, pool. **Network-aware pooling** (NGO 1.x) — see [[network-architecture]].

### A9 — `Debug.LogWarning` / `Debug.LogError` inside coroutine `MoveNext` without gate
Even error logs allocate. If a coroutine logs every iteration, that's per-frame alloc. Gate.

### A10 — `string.Format` / interpolated strings outside diagnostic gates
`$"{x} {y}"` allocates a string AND boxes any value-type args. If the string is only consumed by a `Debug.Log` that's gated off, the string still allocates because the interpolation happens at the call site. Use `if (verbose) Debug.Log($"...")` to skip the alloc.

## Profiler workflow

The 2026-04-27 session shape — repeat this any time a frame budget question comes up:

### 1. Capture
- **Unity Profiler in Play Mode**, with **Deep Profile** ON and **Allocation Tracking** ON.
- 30-60 seconds of steady-state gameplay (everything punched in, mid-shift).
- Capture twice: **once Editor, once Standalone Mono build.** Editor adds ~15% overhead and inflates `LogStringToConsole` cost. Standalone is the truth.

### 2. Read CPU Hierarchy
- Switch the bottom panel from "Timeline" to "Hierarchy."
- Sort by **Self ms (descending).** This is the cost AVERAGED across all frames.
- Top-level rows: `EditorLoop` (won't ship), `PlayerLoop` (the real game).
- Drill: `PlayerLoop` → `UpdateScene` → `Update.ScriptRunBehaviourUpdate` (Update phase) and `PreLateUpdate.ScriptRunBehaviourLateUpdate` (LateUpdate phase) and `Update.ScriptRunDelayedDynamicFrameRate` (coroutines).
- Inside `BehaviourUpdate`, sort by GC.Alloc. The top 3-5 rows are usually the culprits.

### 3. Read Allocation
- Switch sort to **GC.Alloc (descending).**
- The total `GC.Alloc` per frame at PlayerLoop is the headline number. If > 100 KB / frame, GC will fire ~ every 1-3 seconds and cause spike frames.
- Find the leaves: rows with `Calls > 1` and `GC.Alloc > 1 KB` per call are the heaviest allocators.

### 4. Read Timeline (for spikes)
- Click on a tall (≥ 50 ms) frame in the top timeline.
- Switch the bottom panel back to **Timeline.** It shows the call chain that lit up that single frame.
- Spikes are usually GC pauses (yellow chunk dominant) or one-off heavy allocations (`Instantiate` of a large prefab, e.g.).

### 5. Decide
- **Steady-state CPU bottleneck** → apply Patterns 1, 5, 8 (gate, throttle, cache).
- **GC.Alloc / spike frames** → apply Patterns 4, 7 (NonAlloc, reused scratch). Eliminate alloc, don't reduce it.
- **Specific debug overlay dominating** → gate to dev-mode (rule #34).
- **Per-frame `GetComponentsInChildren` / cross-hierarchy walks** → apply Patterns 2, 3 (TTL cache + centralised invalidation).
- **Per-action heavy `Physics.Overlap*`** → apply Pattern 6 (per-action TTL cache).

### 6. Verify
- Re-capture after the fix. Same scene, same workload.
- Confirm the targeted marker dropped. If not, the hypothesis was wrong; revert and re-examine.

## Network safety

Every optimisation pattern above must respect the project's network architecture (rules #18, #19):

- **Server-side caches and dirty flags** are fine. They affect server-authoritative decisions only.
- **Per-peer caches** (e.g. each client maintains its own `CharacterAwareness` cache) are fine — the cache is local to that peer's view.
- **Never put a per-frame cache on a `NetworkBehaviour` field that's read on both sides without `[ServerOnly]` reasoning.** If the server has the cache, clients must reach the same answer through the existing replicated state (`NetworkVariable` / `NetworkList`), not by trying to read the server's cache.
- **Pooling networked objects** (e.g. `WorldItem`) requires NGO-aware pool patterns. Plain `Instantiate`/`Destroy` is the wrong pattern but you can't naïvely pool a `NetworkObject`. See [[network-architecture]] § NGO Pooling.

## Examples

### "I'm adding a new GOAP action that scans the building"
1. Default to `for` loops, no LINQ.
2. If it scans `Physics.Overlap*`, use `NonAlloc` + a static shared buffer.
3. If `IsValid` does the same scan, cache the result with Pattern 6 (per-action TTL).
4. Clear the cache in `Exit()`.
5. Profile the action's first run on the audited worker mix. If it's the top row in the Hierarchy, refactor.

### "I'm adding a new debug overlay"
1. Default to **disabled in production scenes.** Gate via `if (DevMode.Active)` or `#if UNITY_EDITOR`.
2. If it must run in production, **stagger updates** (one building / NPC / etc. per frame, or 1 Hz refresh).
3. Profile on the audited mix BEFORE shipping. If the overlay shows up at >1% of frame self time, fix or gate.
4. Don't rely on "I'll disable it later" — the 2026-04-27 session caught a debug overlay eating 28% of frame.

### "I'm changing how a building processes orders / items"
1. Read [[building-logistics-manager]] first to understand the dirty-flag pattern.
2. If your change adds new state mutations, add `MarkDispatchDirty` calls.
3. If your change adds a new `Update`-driven system, gate it behind `IsDispatchDirty` or its equivalent.
4. Re-run the profiler with the audited 12-worker mix. Confirm `ProcessActiveBuyOrders` self-time stays near zero on stable state.

### "I'm adding a new MonoBehaviour that walks `Building.Rooms`"
1. Default to using the existing `CommercialBuilding.GetStorageFurnitureCached` / `CraftingBuilding.GetCraftableItems` if your data fits.
2. If you need new building-level data, follow Pattern 2 (TTL cache) + hook into `FurnitureManager.InvalidateOwnerBuildingCaches` for invalidation.
3. Document the staleness window (default 2s) and add a public `Invalidate()` method.

## Open questions / TODO

- Tier 4 items in [[optimisation-backlog]] (currently 4 deferred) — pick up when scaling concern returns.
- Pattern 8 (Inspector-authored cache) is currently used only on `ShopBuilding.ItemsToSell`. There may be more `Select(...).ToList()` getters across the codebase to fix.
- No automated lint for these patterns yet. Consider a simple Roslyn analyzer that flags `Physics.OverlapBox(...)` (alloc variant) and `.ToList()` calls inside `Update` / `OnExecute` methods.

## Links
- [[optimisation-backlog]] — active deferrals, Tier 4 todos with profiler-measured costs.
- [[host-progressive-freeze-debug-log-spam]] — the gotcha that triggered the awareness-system Debug.Log fix.
- [[jobs-and-logistics]] — system that received the most refactor surface (B / Cₐ / D / A).
- [[building-logistics-manager]] — owns the dirty-flag gating canonical example.
- [[ai-goap]] — owns the per-action TTL cache canonical example.
- [[character-job]] — owns the `Job.ExecuteIntervalSeconds` cadence stagger.
- [[network-architecture]] — defines the network-safety constraints every cache pattern must respect.

## Sources
- [CLAUDE.md](../../CLAUDE.md) — project rule #34 anchors the conventions.
- 2026-04-25 → 2026-04-27 logistics performance pass with Kevin (this is the conversation that produced the patterns above).
- 2026-04-27 Unity Profiler session screenshots (frame-by-frame Hierarchy + Timeline measurements; informs the workflow section).
- [Assets/Scripts/Character/CharacterAwareness.cs](../../Assets/Scripts/Character/CharacterAwareness.cs) — Pattern 2 + 4 (TTL cache + OverlapSphereNonAlloc).
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — Pattern 2 + 4 (StorageFurniture cache + OverlapBoxNonAlloc).
- [Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs) — Pattern 2 (Craftable cache + HashSet ProducesItem).
- [Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs) — Pattern 8 (lazy inspector-authored cache).
- [Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs) — Pattern 1 (dirty flag).
- [Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs) — Pattern 1 (early-exit gating).
- [Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) — Pattern 7 (reused scratch list).
- [Assets/Scripts/World/Buildings/FurnitureManager.cs](../../Assets/Scripts/World/Buildings/FurnitureManager.cs) — Pattern 3 (centralised invalidation).
- [Assets/Scripts/World/Jobs/Job.cs](../../Assets/Scripts/World/Jobs/Job.cs) — Pattern 5 (`ExecuteIntervalSeconds`).
- [Assets/Scripts/AI/Actions/BTAction_Work.cs](../../Assets/Scripts/AI/Actions/BTAction_Work.cs) — Pattern 5 (cadence gate).
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs) — Pattern 4 + 6 (NonAlloc + per-action TTL).
