---
type: decision
title: "ADR 0001 — Living World hierarchy refactor (Region → Map / WildernessZone / WeatherFront)"
tags: [world, architecture, living-world, region, wilderness, weather, refactor]
created: 2026-04-21
updated: 2026-04-21
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

## Links

- [[world]]
- [[world-community]] — pending refactor (`CommunityTracker` → `MapRegistry`)
- [[world-biome-region]] — pending refactor (`BiomeRegion` → `Region`, file move)
- [[world-macro-simulation]] — pending extension (adds Zone Motion catch-up step)
- [[world-map-hibernation]]
- [[world-map-transitions]]
- [[building-placement-manager]]
- [[character-animal]]
- [[terrain-and-weather]]

## Change log

- 2026-04-21 — proposed, accepted same day — Claude / [[kevin]]

## Sources

- [docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md](../../docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md) — full design spec
- 2026-04-21 conversation with [[kevin]] — design dialogue and reconciliation with existing `BiomeRegion` code
- [Assets/Scripts/Weather/BiomeRegion.cs](../../Assets/Scripts/Weather/BiomeRegion.cs) — existing class targeted for rename/extension
- [Assets/Scripts/Weather/WeatherFront.cs](../../Assets/Scripts/Weather/WeatherFront.cs) — existing class preserved as-is
- [Assets/Scripts/World/MapSystem/CommunityTracker.cs](../../Assets/Scripts/World/MapSystem/CommunityTracker.cs) — target of rename (`MapRegistry`) and cluster-promotion rip
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — target of Zone Motion catch-up step
