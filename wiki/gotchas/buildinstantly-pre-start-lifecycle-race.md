---
type: gotcha
title: "BuildInstantly runs before Start's OnValueChanged subscription"
tags: [building, construction, network-lifecycle, navmesh, furniture]
created: 2026-05-08
updated: 2026-05-13
sources:
  - "[Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `BuildInstantly` (line ~1093), `Start` (~464), `OnNetworkSpawn` (~311), `HandleStateChanged` (~880)"
  - "[BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — instant-mode placement path"
  - "[building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — Instant Build Mode"
  - "2026-05-08 conversation with Kevin — bug report"
related:
  - "[[building]]"
  - "[[building-state]]"
  - "[[building-placement-manager]]"
  - "[[construction]]"
status: mitigated
confidence: high
---

# BuildInstantly runs before Start's OnValueChanged subscription

## Summary
The `BuildingPlacementManager` instant-mode flow calls `Building.BuildInstantly()` synchronously right after `NetworkObject.Spawn()` returns. By that point `OnNetworkSpawn` has already pinned `_currentState.Value` to `UnderConstruction` (when `_constructionRequirements` is non-empty and `_spawnAsComplete` is false), but `Start()` — which subscribes `_currentState.OnValueChanged += HandleStateChanged` — has not yet run. So when `BuildInstantly` flips state to `Complete`, there is no subscriber to drive the post-completion cascade (NavMesh carve, default-furniture spawn, leftover eviction, `OnConstructionComplete` event). The visual swap still works because `Start()` later calls `ApplyConstructionVisuals(_currentState.Value)` directly, masking the silent failure.

## Symptom
Place a building in instant mode (`SetInstantMode(true)` on `BuildingPlacementManager`) on a prefab whose `_constructionRequirements` is non-empty and `_spawnAsComplete` is false:
- Building visually appears completed (correct).
- **Default furniture (`_defaultFurnitureLayout` slots) is missing** — pre-placed crates / workstations / etc. never spawn.
- **NavMesh is not carved** — characters can walk straight through the building footprint until something else triggers a rebake (a second placement, save/load, etc.).
- `_furnitures` on every `FurnitureManager` is empty for the missed slots.
- Logs: `[Building.OnNetworkSpawn] {name} → state=UnderConstruction` followed by `[Building.BuildInstantly] {name} BYPASSING construction loop`, but **no** `[Building.HandleStateChanged]` line after the second write — that's the smoking gun.

## Root cause
Lifecycle ordering of three events on the same frame, on the host:

1. `BuildingPlacementManager.RequestPlacementServerRpc` → `Instantiate(prefab)` → `netObj.Spawn()`.
2. `OnNetworkSpawn` runs synchronously inside `Spawn()`: with non-empty requirements + `_spawnAsComplete=false`, sets `_currentState.Value = UnderConstruction`. The post-completion guards at `Building.cs:368` (`ConfigureNavMeshObstacles`) and `Building.cs:377` (`TrySpawnDefaultFurniture`) are gated on state==Complete and skipped.
3. `BuildingPlacementManager` immediately calls `placedBuilding.BuildInstantly()` on the same frame — before Unity has dispatched `Start()`. `BuildInstantly` writes `_currentState.Value = Complete`.
4. The `_currentState.OnValueChanged` subscription lives in `Building.Start()` (`Building.cs:467`) and runs at the next Unity lifecycle phase — **after** the second state write. NetworkVariable's previous-value snapshot at subscribe time is already `Complete`; no further state change occurs; `HandleStateChanged` never fires for the second write.
5. `Start()` does call `ApplyConstructionVisuals(_currentState.Value)` directly (line 502) — picks up `Complete` and swaps visuals correctly. **Visuals work; cascade doesn't.** That's why this ships looking like it works.

The `_spawnAsComplete = true` path is unaffected: `OnNetworkSpawn` writes `Complete` synchronously, so the line-368 + line-377 guards see the correct state and run the cascade inside `OnNetworkSpawn` itself — no callback dependency.

## How to avoid
- Treat `OnValueChanged` callbacks as unreliable for state writes that happen between `Spawn()` returning and `Start()` running on the same frame.
- Subscribers in `Start()` must not be depended on by code paths the placement system invokes synchronously after `Spawn()`.
- For a NetworkBehaviour that exposes a server-only "force complete this" entry point, run the side-effect cascade directly in that method — don't rely on the state-write callback.
- Long-term cleaner pattern: relocate `_currentState.OnValueChanged += HandleStateChanged` to `OnNetworkSpawn` (where it belongs per NGO best practice). Requires a previous-value-equals early-exit guard inside `HandleStateChanged` because the initial state set inside `OnNetworkSpawn` would otherwise re-fire the callback synchronously and double-run the cascade. Out of scope for the reactive fix that landed 2026-05-08.

## How to fix (if already hit)
`Building.BuildInstantly()` (`Assets/Scripts/World/Buildings/Building.cs:1093`) defers the state flip when called before `Start()` has run, using a coroutine that polls `_isStarted` (set true at the end of `Start`). This keeps the instant-mode and construction-loop paths on the **same `state-flip → OnValueChanged → HandleStateChanged → cascade` flow** — single source of truth at `HandleStateChanged`, no duplicated cascade logic.

```csharp
public virtual void BuildInstantly()
{
    if (!IsServer) return;
    if (_currentState.Value == BuildingState.Complete) return;

    if (_isStarted) DoInstantBuildStateFlip();        // Subscription wired — flip inline.
    else            StartCoroutine(BuildInstantlyAfterStart());  // Defer until Start sets _isStarted.
}

private IEnumerator BuildInstantlyAfterStart()
{
    const int maxFrames = 600;
    int frames = 0;
    while (!_isStarted && frames < maxFrames) { yield return null; frames++; }
    if (!_isStarted) { Debug.LogError(...); yield break; }
    DoInstantBuildStateFlip();
}

private void DoInstantBuildStateFlip()
{
    if (_currentState.Value == BuildingState.Complete) return;
    _currentState.Value = BuildingState.Complete;
    if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
    _contributedMaterials.Clear();
    // OnValueChanged → HandleStateChanged drives the post-completion cascade.
}
```

**Why deferral and not an inline cascade**: an earlier fix duplicated the cascade body inside `BuildInstantly` to work around the missing subscriber. That worked but split the post-completion logic across two methods (`BuildInstantly` and `HandleStateChanged`), making it possible for one to drift out of sync — visual-swap-before-navmesh-bake ordering was already gotten wrong once. Deferring instead means there is exactly one place where the cascade lives (`HandleStateChanged`'s Complete branch). Both the construction-loop's `Building.Finalize()` and the instant-mode `BuildInstantly()` write the same state value and the same subscriber drives the same cascade.

The 600-frame paranoid backstop catches the pathological case of Start never running (GameObject disabled before lifecycle completion). Under normal Unity lifecycle, the deferral resolves in 1 frame.

## Alternative considered (and rejected)
**Move the OnValueChanged subscription from `Start` to `OnNetworkSpawn`** so it's wired before `BuildInstantly` runs. Architecturally cleaner per NGO conventions, but has knock-on effects: the initial-state set inside `OnNetworkSpawn` (server side) would synchronously fire `HandleStateChanged`, which on the `_spawnAsComplete=true` path would then re-trigger the same `ConfigureNavMeshObstacles` / `TrySpawnDefaultFurniture` calls already made inline in `OnNetworkSpawn` lines 368/377. Disentangling that requires removing the inline calls AND adding a peer-side initial-state handler in `Start` for clients (because clients' OnValueChanged does not fire for the initial NV spawn-payload value). The deferral pattern was preferred as a localized fix that doesn't restructure the lifecycle.

## Affected systems
- [[building]]
- [[building-state]]
- [[building-placement-manager]]
- [[construction]]
- [[navmesh]]

## Links
- [[save-restore-state-flip-no-subscriber]] — **same root cause**, save/load variant. The save-restore call site can't tolerate a one-frame delay (its synchronous content-restore step depends on furniture being live in the same call), so it uses a manual-cascade fix instead of this page's coroutine-defer.
- [[furnituremanager-replace-style-rescan]] — sibling FurnitureManager hazard around spawn-cascade ordering.

## Sources
- 2026-05-08 conversation with [[kevin]] — bug report on instant-mode placement skipping furniture + navmesh carve.
- [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — `BuildInstantly`, `Start`, `OnNetworkSpawn`, `HandleStateChanged`.
- [BuildingPlacementManager.cs](../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) — instant-mode placement path.
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — Instant Build Mode lifecycle hazard.
