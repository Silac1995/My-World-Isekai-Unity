---
type: gotcha
title: "Static registry uninitialised on joining client (TerrainTypeRegistry / CropRegistry / …)"
tags: [networking, ngo, registry, late-joiner, multiplayer, gamelauncher, race-condition]
created: 2026-04-29
updated: 2026-04-29
sources:
  - "[Assets/Scripts/Terrain/TerrainTypeRegistry.cs](../../Assets/Scripts/Terrain/TerrainTypeRegistry.cs)"
  - "[Assets/Scripts/Farming/Pure/CropRegistry.cs](../../Assets/Scripts/Farming/Pure/CropRegistry.cs)"
  - "[Assets/Scripts/Core/GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs)"
  - "[Assets/Scripts/Core/Network/GameSessionManager.cs](../../Assets/Scripts/Core/Network/GameSessionManager.cs)"
  - "2026-04-29 conversation with Kevin (multi-issue debugging session — host plant → client join with empty menu / no growth visual / spammy errors)"
related:
  - "[[farming]]"
  - "[[terrain-and-weather]]"
  - "[[world]]"
  - "[[network-architecture]]"
status: mitigated
confidence: high
---

# Static registry uninitialised on joining client (TerrainTypeRegistry / CropRegistry / …)

## Summary
Any `static class XRegistry { Initialize(); Get(...); }` whose `Initialize()` is called only from `GameLauncher.LaunchSequence` will be **empty on every joining client** for at least the first several frames after they connect — and will stay empty forever if nothing else ever calls `Initialize()` on the client. `LaunchSequence` is the **host/solo path only**; joining clients run `GameSessionManager.JoinMultiplayer() → StartClient()` and never visit that block. NGO can replicate spawned NetworkObjects (which Update-poll the registry) into the client's scene before any client-side bootstrap fires, so the symptom is per-frame `LogError` spam plus silently broken behaviour (empty hold-E menus, missing growth visuals, terrain effects that never apply, …).

## Symptom
- Joining client console shows a per-frame error loop like:
  ```
  [TerrainTypeRegistry] Not initialized. Call Initialize() first.
  …CharacterTerrainEffects.UpdateTerrainDetection (CharacterTerrainEffects.cs:61)
  …CharacterTerrainEffects.Update (CharacterTerrainEffects.cs:37)
  ```
- Or silent variants where a client-side query returns null and the calling code degrades gracefully — e.g.:
  - [[farming|CropHarvestable]] hold-E menu is empty (no Pick / Destroy rows).
  - `CropHarvestable.CanHarvest()` returns false even on a mature crop, so tap-E does nothing and the harvest action never fires.
  - Growth visual never updates (initial scale renders, but day-by-day stage changes don't trigger any visual update because the registry-resolved `CropSO` is null).
- Works fine on the host and the FIRST client in some scenarios (the order of NGO replication vs `HandleClientConnected` happens to favour them); breaks on the SECOND client where the timing window widens.
- `Empty world → client plants → grows → harvests` works, because the host hasn't planted before the client arrived. `Host plants → client joins` breaks because NGO replicates the existing crop's NetworkObject into the joiner's scene before the registry gets populated.

## Root cause
1. Registries (`TerrainTypeRegistry`, `CropRegistry`, …) are static classes loaded via `Resources.LoadAll<...>` in their `Initialize()`. Idempotent but explicit — `Get()` returns null and logs an error if `Initialize()` has not been called.
2. `Initialize()` lives only in `GameLauncher.LaunchSequence`, which runs after a host's `Launch...` button. Joining clients enter the game via `GameSessionManager.JoinMultiplayer() → StartClient()` and **never** run `LaunchSequence`.
3. Even when `GameSessionManager.HandleClientConnected` is patched to call `Initialize()` for joining clients, NGO can replicate networked GameObjects (the host's player Character, pre-existing CropHarvestables, …) into the joiner's scene as soon as the connection is approved — and their `MonoBehaviour.Update` starts ticking on the next Unity update before `OnClientConnectedCallback` (and thus `HandleClientConnected`) fires. That is the multi-frame error window.

## How to avoid
- **Make every static registry self-bootstrap on first access.** The `Get()` method should call `Initialize()` if `_types == null` (or equivalent flag). `Initialize()` must be idempotent (early-return if already populated) so multiple call sites don't reload from `Resources` twice. Pattern:
  ```csharp
  public static T Get(string id)
  {
      if (_types == null) Initialize();   // ← lazy auto-init
      …
  }

  public static void Initialize()
  {
      if (_types != null) return;          // ← idempotent
      _types = Resources.LoadAll<T>("…").ToDictionary(…);
  }
  ```
- Keep the explicit `Initialize()` calls in `GameLauncher.LaunchSequence` **and** in `GameSessionManager.HandleClientConnected` for telemetry (eager init logs the count); they're harmless duplicates with lazy init.
- When adding a new static registry, write a one-line `Get` that lazy-inits + an idempotent `Initialize`. Don't rely on a single bootstrap path.

## How to fix (if already hit)
1. Open the registry's `Get(...)` method. Confirm it's the `if (_types == null) { Debug.LogError(...); return null; }` shape.
2. Replace the error guard with `if (_types == null) Initialize();`.
3. Confirm `Initialize()` is idempotent — add an early `if (_types != null) return;` if it's not already there.
4. Test: stop and restart Play Mode in both host + client editors. Have host plant / build / spawn whatever the registry-dependent object is, then join a client. Console should be clean.
5. Optional cleanup: search for the `[XRegistry] Not initialized` log string elsewhere in the codebase — every other static registry in the project deserves the same treatment.

## Affected systems
- [[farming]] — `CropRegistry` (`CropSO` lookup, used by `CropHarvestable.ResolveCropFromNet`, `FarmGrowthSystem`, `CharacterAction_PlaceCrop`).
- [[terrain-and-weather]] — `TerrainTypeRegistry` (`TerrainType` lookup, used every frame by `CharacterTerrainEffects.UpdateTerrainDetection`, `TerrainCell.GetCurrentType`).
- Any future static registry that follows the same `Initialize() / Get(string id)` shape (job-yield registries, item registries, race registries, … — audit before shipping multiplayer-visible features that read them).

## Links
- [[network-architecture]] — for the NGO connection flow (`StartClient` → `OnClientConnectedCallback` ordering relative to spawned-object replication).
- [[gamelauncher-launch-sequence]] (if doc'd) — the host-only init path that this gotcha sidesteps.

## Sources
- 2026-04-29 conversation with [[kevin]] — multi-message debugging session that surfaced the issue from three different symptoms (empty hold-E menu on client, growth visual stuck on client, "TerrainTypeRegistry not initialized" error spam on second-client join). Final fix shipped as lazy auto-init in both `TerrainTypeRegistry.Get` and `CropRegistry.Get`.
- [Assets/Scripts/Core/GameLauncher.cs:140-141](../../Assets/Scripts/Core/GameLauncher.cs) — the eager-init block that the joining-client path skips.
- [Assets/Scripts/Core/Network/GameSessionManager.cs:472](../../Assets/Scripts/Core/Network/GameSessionManager.cs) — `JoinMultiplayer()`, the joining-client entry that bypasses `LaunchSequence`.
- [Assets/Scripts/Terrain/TerrainTypeRegistry.cs](../../Assets/Scripts/Terrain/TerrainTypeRegistry.cs) — first registry fixed.
- [Assets/Scripts/Farming/Pure/CropRegistry.cs](../../Assets/Scripts/Farming/Pure/CropRegistry.cs) — second registry fixed.
