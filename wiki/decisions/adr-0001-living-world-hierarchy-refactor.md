---
type: decision
title: "ADR 0001 — Living World hierarchy refactor (Region → Map / WildernessZone / WeatherFront)"
tags: [world, architecture, living-world, region, wilderness, weather, refactor]
created: 2026-04-21
updated: 2026-04-22
sources:
  - "docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md"
  - "2026-04-21 conversation with Kevin"
related:
  - "[[world]]"
  - "[[world-community]]"
  - "[[world-biome-region]]"
  - "[[world-macro-simulation]]"
  - "[[world-map-hibernation]]"
  - "[[world-map-transitions]]"
  - "[[building-placement-manager]]"
  - "[[character-animal]]"
  - "[[terrain-and-weather]]"
  - "[[kevin]]"
status: accepted
confidence: high
decision_date: 2026-04-21
decided_by: "[[kevin]]"
supersedes: null
---

# ADR 0001 — Living World hierarchy refactor (Region → Map / WildernessZone / WeatherFront)

## Summary

Replace the `CommunityTracker`-driven NPC-cluster auto-promotion model with an explicit authored hierarchy: **`Region` → { `MapController`, `WildernessZone`, `WeatherFront` }**, all implementing a new `IWorldZone` interface. `BiomeRegion` is renamed to `Region` and extended with `Map` + `WildernessZone` child lists. A pluggable `IZoneMotionStrategy` lets any zone drift, react to weather, or stay static. Wilderness content (harvestables now, wildlife later) streams in only when a player is within a zone's radius. A global `WorldSettingsData.MapMinSeparation` replaces the per-manager join-radius. Phase 1 ships scaffolding + rename + rip; wildlife ecology, reactive motion, and weather-reactive behaviors are explicitly deferred to later phases.

## Context

The Living World system as of 2026-04-19 had three problems:

1. **Performance** — wilderness NPCs outside any `MapController` ran as live `NavMeshAgent`s + physics + NGO-replicated `NetworkObject`s every frame. Server-side ticks scaled linearly with population regardless of player proximity.
2. **Implicit control** — `CommunityTracker.EvaluatePopulations()` watched open-world chunks and auto-promoted NPC clusters into full `MapController`s via `PromoteToSettlement`. Designers had no direct handle on where maps could appear; cluster thresholds were opaque.
3. **Naming drift** — `BiomeRegion` (`Assets/Scripts/Weather/BiomeRegion.cs`) was introduced as a weather-spawning entity, but its runtime surface (bounds, biome reference, hibernation, static registry, save/load) already described what a top-level "region" would be. The wiki page itself ([[world-biome-region]]) flagged *"naming to reconcile"*.

Parallel to this, the user wanted two new capabilities: (a) buildings placed outside any existing map should still work (resolved in a preceding PR via `CommunityTracker.CreateMapAtPosition`), and (b) persistent but performant wilderness content (rare creatures the player can encounter again), achievable only if wilderness stops holding live GameObjects.

## Options considered

### Option A — Rename `BiomeRegion` → `Region`; extend with Map / WildernessZone lists (CHOSEN)

Move `BiomeRegion.cs` from `Assets/Scripts/Weather/` to `Assets/Scripts/World/MapSystem/`, rename the class to `Region`, update namespace `MWI.Weather` → `MWI.WorldSystem`, and add two new lists (`Maps`, `WildernessZones`) populated via `GetComponentsInChildren` in `Awake()`. Existing responsibilities (BiomeDefinition reference, BoxCollider bounds, WeatherFront spawning, ISaveable, static `GetRegionAtPosition`) stay intact.

- **Pros**: single class with the correct name; no duplication; existing save/hibernation/weather code keeps working; wiki-flagged naming issue resolved; `.meta` file move preserves scene GUIDs so existing placements survive.
- **Cons**: touches 11 source files for the rename/namespace update. Mitigated by full compile pass between rename and feature-add steps.

### Option B — Keep `BiomeRegion` name; just extend it

Add `Maps` + `WildernessZones` lists to `BiomeRegion` without renaming.

- **Pros**: zero rename churn.
- **Cons**: name stays misleading — "BiomeRegion" suggests a biome-only concern, but the class now holds all world content grouping. Future readers would repeat the author's original confusion.

### Option C — New `Region` parent class, `BiomeRegion` becomes a child component

Keep `BiomeRegion` for biome + weather; add a new `Region` MonoBehaviour that owns Map / WildernessZone lists and references one `BiomeRegion` child for biome/weather behavior.

- **Pros**: cleanest single-responsibility split.
- **Cons**: two MonoBehaviours with overlapping bounds; doubles the serialization surface; forces every scene-authored region to wire two components; `GetRegionAtPosition` callers must disambiguate; fixes a naming issue by adding a layer.

## Decision

**Option A.** The wiki author already surfaced the naming mismatch. `BiomeRegion` is ~80% of what `Region` needs; two more lists and a rename complete the job. Option C would add accidental complexity (a layer that solves nothing the rename doesn't), and Option B perpetuates a name the author already flagged as wrong.

Detailed class layout, file moves, and step order live in [the Phase 1 design spec](../../docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md).

## Consequences

**Positive**
- Wilderness content becomes streaming / data-first → live NPC tick cost drops proportional to non-streamed records (estimated ~80–90% reduction for large worlds).
- Designers gain explicit control — maps and zones are scene-authored or placement-driven, not opaque cluster thresholds.
- `IWorldZone` abstraction lets any future spatial entity (caves, mineral fields, landmark zones) plug in without structural changes.
- Pluggable `IZoneMotionStrategy` enables emergent mechanics (weather spawning zones, wildlife migrating toward resources) without touching `WildernessZone` internals.
- Wiki-flagged naming issue on `BiomeRegion` resolved.

**Negative**
- Rename touches 11 source files + scene-serialized component references. Mitigated by moving `.cs` and `.meta` together to preserve GUIDs, and running a full compile pass after the rename step before any new-class work.
- Cluster-driven settlement birth stops firing. Existing game loops that relied on "wandering NPCs eventually form a town" (debug scripts, playtest content) need new entry points (either scene authoring or `MapRegistry.CreateMapAtPosition` from a debug tool).
- Old saves with `CommunityTrackerSaveData.PendingClusters` will have that field silently dropped on load. Acceptable on the multiplayer dev branch; not a concern for shipped content.

**Neutral / TBD**
- `MapController.MapId` / `WildernessZone.ZoneId` are still plain string fields, not `NetworkVariable`s. Dynamic maps created at runtime won't be resolvable by ID on clients. Carried forward from the pre-refactor state; tracked as a separate follow-up PR.
- Harvestable live-prefab spawning is explicitly deferred to Phase 2 — Phase 1 lands only the data model. Macro-sim regen math continues to work.
- `WeatherFront` is not adapted to `IWorldZone` in Phase 1 — it remains a first-class object that simply isn't queryable through the new interface yet. Adapting it is a Phase-2 concern.

## Post-implementation notes (2026-04-21, same day)

Implementation landed across 25 commits on `feature/living-world-phase-1`. Deviations and late fixes:

- **`Region` upgraded from `MonoBehaviour` to `NetworkBehaviour`.** The initial plan modeled `Region` as a server-side MonoBehaviour to keep the network surface small. In practice, NGO throws `InvalidParentException` when a `NetworkObject` (MapController / WildernessZone) is parented under a non-NetworkObject. Switching `Region` to `NetworkBehaviour` (with `[RequireComponent(NetworkObject)]`) restored valid parenting and — as a bonus — now replicates region hierarchy to clients.
- **Placement became region-aware.** `BuildingPlacementManager.RegisterBuildingWithMap` first checks if the placement position is inside a `Region`. If yes and no MapController covers the point, it creates a new wild map in that region rather than falling through to "join nearest map within MinSep," which previously poached maps across region boundaries.
- **`MapMinSeparation` became region-scoped.** `MapRegistry.CreateMapAtPosition` only counts maps/zones in the SAME region when rejecting; two regions can legitimately host close maps.
- **`CurrentMapID` now updates on `OnTriggerEnter`/`OnTriggerExit`.** Original design assumed every map change went through a door RPC; wild maps on the same plane break that assumption.
- **Interiors re-parent under their exterior MapController** via NGO-aware `NetworkObject.TrySetParent` in `BuildingInteriorSpawner`.
- **Wild maps strip `Biome`/`JobYields`** on instantiation (`MapRegistry.CreateMapAtPosition`) so `MapController.SpawnVirtualBuildings` short-circuits — no `VirtualResourceSupplier_*` children on small player outposts.
- **Save/load round-trip** for dynamic wild maps: `CommunityData.SpawnPosition` (new field) records where each wild map lives; `MapRegistry.RestoreState` schedules a 1.5s-deferred `RespawnDynamicMapsDeferred` that re-instantiates each non-predefined community's MapController and calls `SpawnSavedBuildings` so the children come back too.
- **Debug overlay** `UI_CharacterMapTrackerOverlay` added because NGO 2.10's `NetworkBehaviourEditor` shows `Type not renderable` for `NetworkVariable<FixedString128Bytes>` and `NetworkVariable<Vector3>` — only `int/uint/long/float/bool/string/enum` are drawn. Runtime values replicate fine; the overlay makes them visible in the top-left corner during play-mode (toggle F6).
- **Elastic MapControllers (post-merge iteration, 2026-04-22):** early play-testing on small Regions (400x400) showed the initial "reject placements that would produce a misaligned map" logic blocked almost all placements. Replaced with adaptive bounds:
  - `MapController.ClampBoundsToRegion(regionBounds)` shrinks a freshly-spawned MapController's BoxCollider to fit inside its Region at creation time (called from `MapRegistry.CreateMapAtPosition` after `NetworkObject.Spawn`).
  - `MapController.ExpandBoundsToInclude(worldPoint, footprint, regionBounds)` grows an existing MapController to envelop a new building's footprint, also clamped to Region bounds.
  - `BuildingPlacementManager.RegisterBuildingWithMap` now prefers expanding a same-region map within `MapMinSeparation` (via `MapRegistry.FindNearestMapInRegion`) over spawning a new one — communities stay contiguous.
  - `MapMinSeparation` is now a **soft threshold** that routes placement to expansion instead of rejection. `WouldNewMapFitInRegion` / `WouldViolateMapMinSeparation` helpers and the two associated client toasts were removed.
  - Known limitation: `BoxCollider.center`/`size` are plain fields, not `NetworkVariable`s — resize is server-only. Clients see the prefab's authored bounds until a follow-up PR syncs them. Hibernation/placement/logic are unaffected.

## Links

- [[world]]
- [[world-community]] — refactor implemented (`CommunityTracker` → `MapRegistry`)
- [[world-biome-region]] — refactor implemented (`BiomeRegion` → `Region` as NetworkBehaviour)
- [[world-macro-simulation]] — extension implemented (Zone Motion step 6)
- [[world-map-hibernation]]
- [[world-map-transitions]]
- [[building-placement-manager]]
- [[character-animal]]
- [[terrain-and-weather]]

## Change log

- 2026-04-21 — proposed, accepted same day — Claude / [[kevin]]
- 2026-04-22 — Post-merge iteration: elastic MapController bounds (ClampBoundsToRegion + ExpandBoundsToInclude); MapMinSeparation demoted from hard rejection to soft "route to expansion" threshold; rejection helpers/toasts removed. Navmesh architecture changes (single world NavMeshSurface, per-placement full rebake, NavMeshObstacle kept for later optimization) also noted here. — Claude / [[kevin]]
- 2026-04-21 — implementation complete (25 commits on `feature/living-world-phase-1`). Added post-implementation notes covering Region→NetworkBehaviour upgrade, region-aware placement, region-scoped MapMinSeparation, CurrentMapID on trigger events, interior re-parenting, wild-map Biome/JobYields stripping, save/load round-trip with SpawnPosition, and debug overlay. — Claude / [[kevin]]

## Sources

- [docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md](../../docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md) — full design spec
- 2026-04-21 conversation with [[kevin]] — design dialogue and reconciliation with existing `BiomeRegion` code
- [Assets/Scripts/Weather/BiomeRegion.cs](../../Assets/Scripts/Weather/BiomeRegion.cs) — existing class targeted for rename/extension
- [Assets/Scripts/Weather/WeatherFront.cs](../../Assets/Scripts/Weather/WeatherFront.cs) — existing class preserved as-is
- [Assets/Scripts/World/MapSystem/CommunityTracker.cs](../../Assets/Scripts/World/MapSystem/CommunityTracker.cs) — target of rename (`MapRegistry`) and cluster-promotion rip
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — target of Zone Motion catch-up step
