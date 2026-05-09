---
type: system
title: "Terrain & Weather"
tags: [terrain, weather, vegetation, biome, footsteps, tier-1]
created: 2026-04-18
updated: 2026-04-29
sources: []
related:
  - "[[world]]"
  - "[[world-biome-region]]"
  - "[[world-macro-simulation]]"
  - "[[world-map-hibernation]]"
  - "[[ai]]"
  - "[[character]]"
  - "[[character-terrain]]"
  - "[[character-equipment]]"
  - "[[character-archetype]]"
  - "[[items]]"
  - "[[save-load]]"
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: world-system-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Terrain/"
depends_on:
  - "[[world]]"
  - "[[save-load]]"
depended_on_by:
  - "[[world]]"
  - "[[ai]]"
  - "[[character-terrain]]"
  - "[[farming]]"
  - "[[dev-mode]]"
---

# Terrain & Weather

## Summary
Layered 5-tier system that models ground surface, weather, and vegetation. Ground is a per-cell grid on active maps (`TerrainCellGrid`, `TerrainCell`, `TerrainPatch`) with typed surfaces (`TerrainType`). Weather is modeled as moving physical entities (`WeatherFront`) that live on the world map, driven by a `GlobalWindController` and spawned by per-biome `BiomeRegion` containers using `BiomeClimateProfile` parameters. Weather fronts modify cell state (moisture, snow, temperature) via `TerrainWeatherProcessor`, which in turn drives terrain type transitions (Dirt+moisture→Mud) and vegetation growth/drought death via `VegetationGrowthSystem`. All offline progression is simulated by `MacroSimulator` using aggregate math.

## Purpose
Ground the living world in visible environmental change. Ground type, weather, and vegetation are persistent inputs into both real-time gameplay (footsteps, movement speed, damage over time) and offline macro-simulation (cell drying, snow melt, crop growth, wild tree maturation). When players return to a map, they see the world that actually progressed — mud from yesterday's rain, grown bushes in fertile areas, snow accumulated during winter.

## Responsibilities

**Layer 1 — Terrain Foundation (`Assets/Scripts/Terrain/`)**
- Define terrain types with speed/damage/slip/growth properties (`TerrainType`).
- Maintain a flat cell grid on active maps, with `~4 Unity unit` resolution (`TerrainCellGrid`).
- Provide scene-authored base terrain regions via collider-based patches with priority-based overlap resolution (`TerrainPatch`).
- Evaluate state machine transitions between terrain types based on moisture/temperature/snow (`TerrainTransitionRule`).
- Runtime O(1) lookup of `TerrainType` by string `TypeId` (`TerrainTypeRegistry`).
- Serialize/restore cell state for hibernation (`TerrainCellSaveData` — implements `INetworkSerializable`).

**Layer 2 — Weather & Atmosphere (`Assets/Scripts/Weather/`)**
- Single world-level wind singleton with direction + strength (`GlobalWindController`).
- Per-biome climate definition (`BiomeClimateProfile`): temperature, precipitation probabilities, front spawn timing, default terrain.
- World-map biome container that spawns/moves/hibernates weather fronts within its bounds (`BiomeRegion`, implements `ISaveable`).
- Moving networked weather entity with shadow, particles, lifetime (`WeatherFront`, `NetworkBehaviour`).
- Weather type taxonomy: Clear / Cloudy / Rain / Snow (`WeatherType`).

**Layer 3 — Weather↔Terrain Bridge**
- Tick-based processing of weather fronts against terrain cells, server-only (`TerrainWeatherProcessor`).
- Spatial culling via front bounding rects + dirty-cell HashSet (never iterates all 35k cells).
- Ambient revert toward biome baseline when weather is absent.
- Transition rule evaluation per tick.

**Layer 4 — Vegetation**
- Wild plant growth on fertile cells through Sprout→Bush→Sapling→Tree stages (`VegetationGrowthSystem`).
- Drought death after 48 game-hours without moisture.
- Server-only ticks with Giga Speed catch-up loops.

**Layer 5 — Character Effects** — see [[character-terrain]] for details.

**Non-responsibilities**
- Does **not** own `BiomeDefinition` — that remains on [[world-biome-region]] and holds the resource/harvestable lists. `BiomeDefinition` now references a `BiomeClimateProfile` via a new `_climateProfile` field.
- Does **not** own map hibernation lifecycle — see [[world-map-hibernation]].
- Does **not** own NPC schedule reactions to weather — see [[ai]] (future work).
- Does **not** own offline resource yield math — see [[world-macro-simulation]].

## Key classes / files

**Terrain:**
- `Assets/Scripts/Terrain/TerrainType.cs` — ScriptableObject definition.
- `Assets/Scripts/Terrain/TerrainTypeRegistry.cs` — static lookup, initialized in [[engine-plumbing|GameLauncher]].
- `Assets/Scripts/Terrain/TerrainTransitionRule.cs` — ScriptableObject condition rules.
- `Assets/Scripts/Terrain/TerrainCell.cs` — runtime cell struct.
- `Assets/Scripts/Terrain/TerrainCellSaveData.cs` — serializable + `INetworkSerializable` wrapper.
- `Assets/Scripts/Terrain/TerrainCellGrid.cs` — flat array on MapController.
- `Assets/Scripts/Terrain/TerrainPatch.cs` — scene-placed collider.
- `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs` — Layer 3 bridge.
- `Assets/Scripts/Terrain/VegetationGrowthSystem.cs` — Layer 4 growth ticker.

**Weather:**
- `Assets/Scripts/Weather/WeatherType.cs` — enum.
- `Assets/Scripts/Weather/GlobalWindController.cs` — `NetworkBehaviour` singleton.
- `Assets/Scripts/Weather/BiomeClimateProfile.cs` — ScriptableObject climate params.
- `Assets/Scripts/Weather/BiomeRegion.cs` — `MonoBehaviour` + `ISaveable`, spawns fronts on timer.
- `Assets/Scripts/Weather/WeatherFront.cs` — `NetworkBehaviour`, moves by global + local wind.
- `Assets/Scripts/Weather/WeatherFrontSnapshot.cs` — hibernation DTO.

**Integration:**
- `Assets/Scripts/World/MapSystem/MapController.cs` — serializes `TerrainCells` in `Hibernate()`, restores in `WakeUp()`, `SendTerrainGridClientRpc` for late-joiner sync.
- `Assets/Scripts/World/MapSystem/MapSaveData.cs` — holds `TerrainCellSaveData[] TerrainCells` and `double TerrainLastUpdateTime`.
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — `SimulateTerrainCatchUp()` + `SimulateVegetationCatchUp()` inserted between resource regen and NPC yields.
- `Assets/Scripts/World/Data/BiomeDefinition.cs` — gains `_climateProfile : BiomeClimateProfile` field.
- `Assets/Scripts/World/Buildings/Rooms/Room.cs` — gains `_floorTerrainType` + `_isExposed`.
- `Assets/Scripts/Core/GameLauncher.cs` — calls `TerrainTypeRegistry.Initialize()` after scene load.

## Public API / entry points

### TerrainCellGrid (on MapController)
```csharp
void Initialize(Bounds mapBounds)
void InitializeFromPatches(List<TerrainPatch>)
TerrainType GetTerrainAt(Vector3 worldPos)    // primary footstep/effect query
TerrainCell GetCellAt(Vector3 worldPos)
ref TerrainCell GetCellRef(int x, int z)       // mutable access for processors
TerrainCellSaveData[] SerializeCells()
void RestoreFromSaveData(TerrainCellSaveData[])
void GetCellRangeForBounds(Bounds, out int minX, out int minZ, out int maxX, out int maxZ)
```

### BiomeRegion (on world map)
```csharp
static BiomeRegion GetRegionAtPosition(Vector3)
static List<BiomeRegion> GetAdjacentRegions(BiomeRegion)
static void ClearRegistry()   // called by SaveManager.ResetForNewSession()
TerrainType GetDefaultTerrainType()
float GetAmbientTemperature()
List<WeatherFront> GetFrontsOverlapping(Bounds)
void WakeUp(double currentTime)   // fast-forwards hibernated fronts
void Hibernate(double currentTime)
```

### WeatherFront (spawned by BiomeRegion)
```csharp
NetworkVariable<WeatherType> Type
NetworkVariable<Vector2> LocalWindDirection
NetworkVariable<float> LocalWindStrength, Radius, Intensity, TemperatureModifier, RemainingLifetime
Vector2 ActualVelocity  // GlobalWind + LocalWind composition
void Initialize(BiomeRegion parent, WeatherType, Vector3 pos, Vector2 wind, float strength, float radius, float intensity, float tempMod, float lifetime)
float GetShadowOpacity()
```

### MacroSimulator
```csharp
static void SimulateTerrainCatchUp(TerrainCellSaveData[] cells, BiomeClimateProfile climate, float hoursPassed, List<TerrainTransitionRule> rules)
static void SimulateVegetationCatchUp(TerrainCellSaveData[] cells, BiomeClimateProfile climate, float hoursPassed, float minMoisture = 0.2f, float droughtDeathHours = 48f)
```

## Data flow

```
┌─────────────────────────────────────────────────────────────┐
│ WORLD LEVEL                                                  │
│                                                               │
│  GlobalWindController  ── NetworkVariable ──▶ all clients    │
│         │                                                     │
│         ▼                                                     │
│  BiomeRegion (N per world)                                   │
│     │                                                         │
│     ├─ spawns WeatherFront on FrontSpawnInterval timer       │
│     ├─ hibernates its fronts when no players nearby          │
│     │                                                         │
│     └─ WeatherFront (NetworkBehaviour, moves via wind vec)   │
│              │                                                │
│              ▼                                                │
└──────────────┼───────────────────────────────────────────────┘
               │ overlaps with active MapController bounds
┌──────────────┼───────────────────────────────────────────────┐
│ MAP LEVEL (active only)                                       │
│              │                                                │
│              ▼                                                │
│  TerrainWeatherProcessor (server-only, tick every ~2 game-min)│
│     │                                                         │
│     ├─ for each overlapping WeatherFront:                    │
│     │    add moisture/snow/temperature to cells in radius    │
│     │    (wind-biased, falloff from center)                  │
│     │    → _dirtyCells.Add(idx)                              │
│     │                                                         │
│     ├─ ambient revert: _dirtyCells drift toward baseline     │
│     │    cells back at baseline removed from dirty set       │
│     │                                                         │
│     └─ evaluate TerrainTransitionRules on dirty cells        │
│           cell.CurrentTypeId mutated if rule matches         │
│                                                               │
│  TerrainCellGrid                                              │
│     ▲     ▲                                                   │
│     │     └── VegetationGrowthSystem (server, hourly)        │
│     │             advances cell.GrowthTimer on fertile cells  │
│     │             drought death after 48h without water       │
│     │                                                         │
│     │ initialized from TerrainPatch colliders in scene        │
│     │ (highest-priority patch wins on overlap)                │
│     │                                                         │
│     └── MapController.SendTerrainGridClientRpc → all clients  │
└──────────────┬───────────────────────────────────────────────┘
               │ characters walk on cells
┌──────────────▼───────────────────────────────────────────────┐
│ CHARACTER LEVEL (see [[character-terrain]])                   │
│                                                               │
│  CharacterTerrainEffects reads GetTerrainAt(position)        │
│     → SpeedMultiplier, DamagePerSecond, SlipFactor (server)  │
│     → CurrentTerrainType for footstep audio (client)         │
└──────────────────────────────────────────────────────────────┘
```

## Dependencies

### Upstream
- [[world]] — `MapController` hosts `TerrainCellGrid` and `TerrainWeatherProcessor`. Its `BoxCollider` defines grid bounds.
- [[world-biome-region]] — `BiomeDefinition` now references a `BiomeClimateProfile`. `BiomeRegion` references a `BiomeDefinition`.
- [[world-map-hibernation]] — terrain cells are serialized into `MapSaveData` during `Hibernate()` and restored on `WakeUp()`.
- [[world-macro-simulation]] — `SimulateTerrainCatchUp` and `SimulateVegetationCatchUp` are inserted between resource regen and NPC yields.
- [[save-load]] — `BiomeRegion` implements `ISaveable`. `SaveManager.ResetForNewSession()` calls `BiomeRegion.ClearRegistry()` and `TerrainTypeRegistry.Clear()`.
- [[engine-plumbing]] — `GameLauncher.LaunchSequence()` calls `TerrainTypeRegistry.Initialize()` after scene load.

### Downstream
- [[character-terrain]] — `CharacterTerrainEffects` reads terrain under feet; `FootstepAudioResolver` uses the matrix of terrain type × boot material.
- [[items]] — new `ItemMaterial` enum on `ItemSO` for footstep audio + future impact/drop sounds.
- [[character-archetype]] — new `FootSurfaceType` enum + `_defaultFootSurface` field.
- [[character-equipment]] — new `GetFootMaterial()` helper walks armor → clothing → underwear layers.

## State & persistence

| Data | Authority | Persistence | Sync |
|------|-----------|-------------|------|
| `GlobalWindController.WindDirection/Strength` | Server | Not persisted (regenerated per session) | `NetworkVariable` |
| `WeatherFront.*` (Type, Position, wind, intensity, lifetime) | Server | Serialized to `WeatherFrontSnapshot[]` on BiomeRegion hibernate | `NetworkVariable` per field |
| `BiomeRegion` (hibernation state, hibernated fronts) | Server | `ISaveable` via `SaveManager` world saveables | n/a (MonoBehaviour) |
| `TerrainCell[]` (per-map) | Server | `TerrainCellSaveData[]` in `MapSaveData.TerrainCells` | `MapController.SendTerrainGridClientRpc` (full grid on join) |
| `TerrainType` / `TerrainTransitionRule` / `BiomeClimateProfile` | Read-only SOs | Not persisted (asset data) | Client loads same assets |

## Offline (MacroSimulator) execution order

```
1. Resource Pool Regeneration   (existing)
2. Terrain Cell Catch-Up         (NEW — moisture/temp/transitions)
3. Vegetation Catch-Up           (NEW — growth/drought death)
4. Inventory Yields              (existing, to be simplified)
5. Needs Decay + Position Snap   (existing)
6. City Growth                   (existing)
```

Terrain offline math is intentionally simple: estimated rain hours = `hoursPassed × climate.RainProbability`, then per-cell moisture/snow/temperature aggregated from climate baselines, then a single pass of transition rule evaluation. No WeatherFront simulation offline — the BiomeRegion simply counts how many fronts would have spawned and respawns a fraction on wake-up.

## Known gotchas / edge cases

- **`TerrainTypeRegistry.Initialize()` ordering** — must run before any MapController wakes up, or `TerrainCell.GetCurrentType()` / `GetBaseType()` return null. Currently called in [[engine-plumbing|GameLauncher.LaunchSequence]] right after the scene loads. `SimulateVegetationCatchUp` silently skips all cells if the registry isn't initialized.
- **TerrainCell struct holds string IDs, not SO refs** — avoids managed references in a value type and keeps serialization trivial. Cost: lookup via registry on every query.
- **`TerrainCellSaveData.NetworkSerialize`** — string fields require manual `FastBufferWriter`/`Reader` handling (strings aren't supported by `SerializeValue`).
- **Cell resolution trade-off** — `_cellSize = 4f` means ~35,000 cells on a 750×750 map. `TerrainWeatherProcessor` relies on dirty-cell HashSet + bounding-rect culling to avoid iterating all cells. A map with no weather and baseline cells does zero work per tick.
- **`TerrainPatch` overlap resolution** — highest `_priority` wins; equal priority → last-in-list wins (scene hierarchy order). Deterministic but can surprise if priorities aren't set deliberately.
- **`BiomeRegion.WakeUp()` simplification** — fast-forwards hibernated front positions by `velocity × elapsed` and respawns survivors, then spawns new fronts matching average spawn interval. Does not simulate front-to-front interaction or precise trajectories — good enough for gameplay, not physically accurate.
- **`_isExposed` flag on Room** — enables outdoor cell grid inside rooftops/courtyards. The room→grid detection itself is a TODO (see Open questions).

## Open questions / TODO

- [ ] Room detection priority (spec §3.5) not wired into `CharacterTerrainEffects` yet — currently falls through to MapController grid. Needs `CharacterLocations.CurrentRoom` tracking before Room.FloorTerrainType can be consulted.
- [ ] `Wetness` property on `CharacterTerrainEffects` (spec §7.3) not yet implemented — deferred alongside player wetness tracking.
- [ ] `TerrainWeatherProcessor` caches `GetComponent<BoxCollider>()` per tick — move to `Initialize()` for zero per-tick allocations.
- [ ] Snow / Ice terrain types, Lava terrain, full footstep audio clip assignment — deferred to Phase 2.
- [ ] WeatherFront shadow projectors — `GetShadowOpacity()` returns per-type opacity but the actual visual projector is not yet built. Needs a shadow decal/projector child object on the prefab.
- [x] ~~Dirty-cell incremental `SendDirtyCellsClientRpc`~~ — landed 2026-04-28 alongside the [[farming]] system. `MapController.NotifyDirtyCells(int[])` builds the payload + fires the ClientRpc, which updates client cell mirrors then fans out to subscribers (`CropVisualSpawner` today; future terrain transition processors).
- [x] ~~Farming consumer for `IsPlowed` / `PlantedCropId` / `GrowthTimer` / `TimeSinceLastWatered` cell fields~~ — implemented 2026-04-28 by the [[farming]] system. See [[farming]] for the consumer.
- [ ] BiomeRegion ownership split — `BiomeRegion.cs` is under `Assets/Scripts/Weather/` on this branch while [[world-biome-region]] historically described biome data as part of World. Reconcile naming in the next pass.

## Change log

- 2026-04-18 — Stub created during wiki bootstrap. Confidence **low** because code was on a feature branch not yet merged. — Claude / [[kevin]]
- 2026-04-19 — Full pass against implemented code on `feature/character-archetype-system`. Populated Purpose, Responsibilities, Key classes (5 layers), Public API, Data flow, Dependencies, State/persistence, Gotchas, and open post-Phase-1 items. Confidence raised to **medium**. — Claude / [[kevin]]
- 2026-04-28 — Cell farming fields (`IsPlowed`, `PlantedCropId`, `GrowthTimer`, `TimeSinceLastWatered`) now consumed by [[farming]]. `SendDirtyCellsClientRpc` landed via `MapController.NotifyDirtyCells`. — Claude / [[kevin]]

## Sources
- [Assets/Scripts/Terrain/](../../Assets/Scripts/Terrain/) — Layer 1, 3, 4 code.
- [Assets/Scripts/Weather/](../../Assets/Scripts/Weather/) — Layer 2 code.
- [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — grid hibernation hooks + ClientRpc.
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — offline terrain/vegetation catch-up.
- [Assets/Scripts/World/MapSystem/MapSaveData.cs](../../Assets/Scripts/World/MapSystem/MapSaveData.cs) — TerrainCells field.
- [Assets/Scripts/World/Data/BiomeDefinition.cs](../../Assets/Scripts/World/Data/BiomeDefinition.cs) — ClimateProfile linkage.
- [Assets/Scripts/Core/GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs) — registry initialization.
- [.agent/skills/terrain-weather/SKILL.md](../../.agent/skills/terrain-weather/SKILL.md) — procedural source of truth.
- [docs/superpowers/specs/2026-04-02-terrain-weather-system-design.md](../../docs/superpowers/specs/2026-04-02-terrain-weather-system-design.md) — original design spec.
- [docs/superpowers/plans/2026-04-02-terrain-weather-system.md](../../docs/superpowers/plans/2026-04-02-terrain-weather-system.md) — implementation plan (14 tasks).
- Feature branch commits `074e179` → `51277db` — 12 commits implementing all 5 layers + docs.
