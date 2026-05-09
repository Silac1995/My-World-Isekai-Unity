---
type: system
title: "World Biome & Region"
tags: [world, biome, region, tier-2]
created: 2026-04-19
updated: 2026-04-21
sources: []
related:
  - "[[world]]"
  - "[[terrain-and-weather]]"
  - "[[jobs-and-logistics]]"
  - "[[world-macro-simulation]]"
  - "[[save-load]]"
  - "[[adr-0001-living-world-hierarchy-refactor]]"
  - "[[kevin]]"
status: stable
confidence: medium
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/Data/"
depends_on:
  - "[[world]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
  - "[[terrain-and-weather]]"
  - "[[world-macro-simulation]]"
---

# World Biome & Region

> **Phase 1 refactor complete — see [[adr-0001-living-world-hierarchy-refactor]].**
> `BiomeRegion` is now `Region` at `Assets/Scripts/World/MapSystem/Region.cs`, namespace `MWI.WorldSystem`. **Upgraded to `NetworkBehaviour`** (late-phase fix — `MonoBehaviour` caused `Unity.Netcode.InvalidParentException` when NetworkObjects were parented under it). Adds `List<MapController> Maps` + `List<WildernessZone> WildernessZones` auto-discovered from child transforms in `Awake`, plus `RegisterMap` / `UnregisterMap` and `RegisterWildernessZone` / `UnregisterWildernessZone` helpers for runtime-spawned children. Implements `IWorldZone`. Existing responsibilities (BiomeDefinition ref, ClimateProfile, BoxCollider bounds, hibernation, static `GetRegionAtPosition`, `WeatherFront` spawning) preserved. `.cs` + `.meta` moved together preserving the asset GUID so scene component references survived. Scene-placed Regions auto-acquire a `NetworkObject` via `[RequireComponent]` on scene load. Sections below describe the **post-refactor** state.

## Summary
Subdivides the world map into climate-typed regions. `BiomeDefinition` (ScriptableObject) holds per-biome resource lists, yield weights, and a `BiomeClimateProfile` reference for weather/terrain parameters. `BiomeRegion` (MonoBehaviour + ISaveable) is the runtime placement of a biome on the world map — it owns a collider bounds, spawns [[terrain-and-weather|WeatherFronts]] on a timer, and hibernates them when no players are in or near its bounds. Feeds `JobYieldRegistry` and the macro-simulator's offline inventory pass.

## Purpose
Two layered entities answer two different questions:
- `BiomeDefinition` (data): *what kind of world is this?* — resources, harvestables, yield weights, climate.
- `BiomeRegion` (runtime): *where on the map is this biome?* — bounds, active weather fronts, hibernation state.

Together they let the world have a visible climate map (desert regions, temperate forests, snowy tundra) while keeping resource data reusable across multiple placements.

## Responsibilities

**`BiomeDefinition` (SO):**
- Resource list (`Harvestables` with `ResourceId`, yield weight, density).
- `HarvestableDensity` [0–0.8] hard cap.
- `_climateProfile` reference to a `BiomeClimateProfile` (added in the Phase 1 terrain/weather work).

**`BiomeRegion` (MonoBehaviour + ISaveable):**
- BoxCollider-bounded zone on the world map.
- References exactly one `BiomeDefinition` and one `BiomeClimateProfile`.
- Spawns `WeatherFront` NetworkBehaviours on a timer derived from the climate profile's `FrontSpawnIntervalMin/Max`.
- Maintains `List<WeatherFront> ActiveFronts` for TerrainWeatherProcessor queries.
- Hibernates active fronts into `WeatherFrontSnapshot[]` when no players are in this region or an adjacent one; fast-forwards positions on wake.
- Static registry: `GetRegionAtPosition(Vector3)`, `GetAdjacentRegions(BiomeRegion)`, `ClearRegistry()`.
- Implements `ISaveable` — persists hibernation state and hibernated fronts via `SaveManager` world saveables.

**Non-responsibilities:**
- Does **not** own cell-level terrain state — see [[terrain-and-weather]] for `TerrainCellGrid`.
- Does **not** own the resource pool runtime — that's on `CommunityTracker.CommunityData.ResourcePools`.
- Does **not** own community/city growth — see [[world]].

## Key classes / files

- `Assets/Scripts/World/Data/BiomeDefinition.cs` — ScriptableObject. Gains a `_climateProfile : BiomeClimateProfile` field.
- `Assets/Scripts/Weather/BiomeRegion.cs` — MonoBehaviour + ISaveable. Lives under `Weather/` because its primary runtime role is spawning/managing `WeatherFront` objects, even though it conceptually belongs to World. (See Open questions — naming to reconcile.)
- `Assets/Scripts/Weather/BiomeClimateProfile.cs` — ScriptableObject, the climate side of biome data.

## Public API / entry points

```csharp
// Static registry (on BiomeRegion)
static BiomeRegion GetRegionAtPosition(Vector3 worldPos)
static List<BiomeRegion> GetAdjacentRegions(BiomeRegion)
static void ClearRegistry()           // called by SaveManager.ResetForNewSession()

// Instance queries
string RegionId               { get; }
bool IsHibernating            { get; }
BiomeClimateProfile ClimateProfile { get; }
BiomeDefinition BiomeDefinition    { get; }
List<WeatherFront> ActiveFronts   { get; }
float GetAmbientTemperature()
TerrainType GetDefaultTerrainType()
List<WeatherFront> GetFrontsOverlapping(Bounds)

// Lifecycle
void WakeUp(double currentTime)    // fast-forwards hibernated fronts by elapsed × wind
void Hibernate(double currentTime) // serializes + despawns all active fronts

// ISaveable
string SaveKey => _regionId
object CaptureState()
void RestoreState(object)
```

## Data flow

```
BiomeDefinition (SO)
    ├── Harvestables[] ────────────────▶ [[jobs-and-logistics]] JobYieldRegistry
    ├── HarvestableDensity
    └── _climateProfile ──▶ BiomeClimateProfile (SO)
                                   │
                                   │   used by
                                   ▼
BiomeRegion (MonoBehaviour)
    │
    ├── ActiveFronts<WeatherFront> ────▶ [[terrain-and-weather|TerrainWeatherProcessor]]
    │
    ├── GetDefaultTerrainType() ───────▶ [[character-terrain|CharacterTerrainEffects]]
    │                                      (world-map traversal fallback)
    │
    ├── Hibernate/WakeUp ──────────────▶ [[save-load|SaveManager]] (ISaveable)
    │
    └── static GetRegionAtPosition ────▶ CommunityTracker.PromoteToSettlement
                                         (new settlements inherit this biome)
```

## Dependencies

### Upstream
- [[world]] — exists within the world-map scene; `CommunityTracker.PromoteToSettlement` calls `BiomeRegion.GetRegionAtPosition(worldPos)` so new dynamic settlements inherit the correct biome.
- [[save-load]] — registered as `ISaveable`; `SaveManager.ResetForNewSession()` calls `BiomeRegion.ClearRegistry()` to avoid stale static state across sessions.

### Downstream
- [[terrain-and-weather]] — consumes `BiomeClimateProfile` for weather spawn rules, temperature ambient, evaporation. `TerrainWeatherProcessor` queries `GetFrontsOverlapping(bounds)`.
- [[jobs-and-logistics]] — reads `BiomeDefinition.Harvestables` for yield recipes.
- [[world-macro-simulation]] — `MacroSimulator.SimulateTerrainCatchUp` pulls `ClimateProfile` off the map's `BiomeDefinition`.

## State & persistence

| Data | Type | Persistence |
|------|------|-------------|
| `BiomeDefinition` fields | SO asset | Read-only asset data |
| `BiomeClimateProfile` fields | SO asset | Read-only asset data |
| `BiomeRegion._isHibernating` | runtime bool | `ISaveable` via `BiomeRegionSaveData` |
| `BiomeRegion._hibernatedFronts` | `List<WeatherFrontSnapshot>` | `ISaveable` via `BiomeRegionSaveData` |
| `BiomeRegion._lastHibernationTime` | double | `ISaveable` via `BiomeRegionSaveData` |

## Known gotchas / edge cases

- **Adjacency margin is hardcoded to 20f** — `GetAdjacentRegions` expands bounds by 20 Unity units (~3m real-world per the 11u=1.67m scale). Small for large world maps. Consider making this configurable per-region.
- **File placement feels wrong** — `BiomeRegion.cs` lives under `Assets/Scripts/Weather/` but conceptually belongs to World. The rationale: its *primary job* is spawning and hibernating WeatherFronts. Moving it to `Assets/Scripts/World/` would put more includes on the Weather side than the other way around.
- **WeatherFront prefab required** — `BiomeRegion` has a `[SerializeField] GameObject _weatherFrontPrefab`. If unassigned, the region silently never spawns fronts. Wire this in the scene.
- **Static registry cleared on new session** — all regions must re-register in their `Awake()` after a scene reload, which they do automatically via `_allRegions.Add(this)`.

## Open questions / TODO

- [ ] Reconcile code location — should `BiomeRegion.cs` move to `Assets/Scripts/World/` or stay in `Weather/`? Currently under Weather because of primary runtime role.
- [ ] Adjacency detection hardcodes 20f margin — make configurable.
- [ ] `BiomeDefinition.cs` does not yet expose `ClimateProfile` publicly as a property everywhere code expects it — double-check when integrating MacroSimulator offline paths.

## Change log

- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-19 — Full pass after Phase 1 terrain/weather implementation landed. Described runtime `BiomeRegion` alongside data-side `BiomeDefinition`. Added API, data flow, dependencies. — Claude / [[kevin]]
- 2026-04-21 — Added pending-refactor notice pointing to [[adr-0001-living-world-hierarchy-refactor]]. — Claude / [[kevin]]
- 2026-04-21 — Refactor implemented: `BiomeRegion` renamed to `Region`, moved to `Assets/Scripts/World/MapSystem/`, upgraded to `NetworkBehaviour` (NGO valid-parent requirement), `IWorldZone` implemented, Maps + WildernessZones child tracking added. — Claude / [[kevin]]

## Sources
- [Assets/Scripts/World/Data/BiomeDefinition.cs](../../Assets/Scripts/World/Data/BiomeDefinition.cs) — SO definition (now with climate profile linkage).
- [Assets/Scripts/Weather/BiomeRegion.cs](../../Assets/Scripts/Weather/BiomeRegion.cs) — runtime region with front spawning and hibernation.
- [Assets/Scripts/Weather/BiomeClimateProfile.cs](../../Assets/Scripts/Weather/BiomeClimateProfile.cs) — climate SO.
- [.agent/skills/terrain-weather/SKILL.md](../../.agent/skills/terrain-weather/SKILL.md) — procedural source of truth for the full terrain/weather stack.
- [[world]] — parent system.
- [[terrain-and-weather]] — primary consumer.
