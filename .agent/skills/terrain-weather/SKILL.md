---
name: terrain-weather
description: Four-layer terrain and weather simulation — terrain type definitions, per-cell grid state, server-authoritative weather fronts, and weather-driven terrain transitions. Integrates with MapController hibernation, MacroSimulator catch-up, and BiomeDefinition.
---

# Terrain & Weather System

## 0. Purpose

The Terrain & Weather system turns every map from a flat visual floor into a living, changing surface. Rain softens dirt into mud, snow accumulates and melts, temperature shifts by biome and time of day. All of this feeds character gameplay (movement speed, damage over time, audio) and agricultural simulation (vegetation growth, crop watering).

The system is organized into four layers that build on each other:

```
Layer 1 — Terrain Foundation   (cell data, type definitions, scene patches)
Layer 2 — Weather & Atmosphere (global wind, biome climate, moving weather fronts)
Layer 3 — Weather↔Terrain      (processor applies front effects to cells, evaluates transitions)
Layer 4 — Vegetation           (wild plant growth on fertile cells)
```

## 1. When to Use This Skill

- Adding a new terrain type (Mud, Ice, Lava, etc.)
- Adding a transition rule (Dirt + moisture → Mud, Snow + warmth → Wet Grass)
- Modifying how weather fronts are spawned, moved, or serialized
- Working with BiomeClimateProfile parameters (rain probability, temperature, moisture)
- Debugging why a cell did or did not transition during hibernation catch-up
- Adding offline catch-up math for terrain or vegetation in `MacroSimulator`
- Hooking a new system into the terrain type currently under a character's feet

## 2. Architecture Overview

### Layer 1 — Terrain Foundation

**`TerrainType` (ScriptableObject)**
The definition of a surface. Every terrain cell references a `TerrainType` by `TypeId` string.

| Field | Type | Purpose |
|---|---|---|
| `TypeId` | `string` | Primary lookup key in `TerrainTypeRegistry` |
| `DisplayName` | `string` | Human-readable name |
| `DebugColor` | `Color` | Editor visualization |
| `SpeedMultiplier` | `float` | Applied to character movement speed |
| `DamagePerSecond` | `float` | DoT applied by `CharacterTerrainEffects` |
| `SlipFactor` | `float` | Momentum loss coefficient (reserved for movement) |
| `DamageType` | `DamageType` | Type of DoT (Fire, Cold, Poison, etc.) |
| `HasDamage` | `bool` (computed) | True when `DamagePerSecond > 0` |
| `CanGrowVegetation` | `bool` | Enables wild plant growth on this surface |
| `FootstepProfile` | `FootstepAudioProfile` | Audio lookup for character footsteps |
| `GroundOverlayMaterial` | `Material` | Shader overlay rendered on cells of this type |
| `OverlayOpacityAtFullSaturation` | `float` | Max overlay alpha at moisture = 1 |

**`TerrainTypeRegistry` (static class)**
O(1) lookup dictionary keyed by `TypeId`. Loaded from `Resources/Data/Terrain/TerrainTypes/`.

```csharp
TerrainTypeRegistry.Initialize();          // Called by GameLauncher on boot
TerrainType t = TerrainTypeRegistry.Get("Mud");
TerrainTypeRegistry.Clear();               // Called by SaveManager before session reset
```

**`TerrainCell` (struct)**
Per-cell runtime data stored as a flat array inside `TerrainCellGrid`.

| Field | Type | Purpose |
|---|---|---|
| `BaseTypeId` | `string` | Permanent base type (Dirt, Grass, Stone) |
| `CurrentTypeId` | `string` | Transitioned type right now (may equal Base) |
| `Moisture` | `float` | 0–1, modified by rain, evaporated by wind/heat |
| `Temperature` | `float` | Degrees (no fixed scale), drifts to ambient |
| `SnowDepth` | `float` | 0–1, accumulates from Snow fronts, melts above 0°C |
| `Fertility` | `float` | Inherited from `TerrainPatch.BaseFertility` |
| `IsPlowed` | `bool` | Farming flag; suppresses wild vegetation |
| `PlantedCropId` | `string` | ID of the actively planted crop |
| `GrowthTimer` | `float` | Accumulated hours of viable growth |
| `TimeSinceLastWatered` | `float` | Hours since last rain or irrigation |

Helper methods on the struct:
```csharp
TerrainType GetBaseType()    => TerrainTypeRegistry.Get(BaseTypeId);
TerrainType GetCurrentType() => TerrainTypeRegistry.Get(CurrentTypeId);
```

**`TerrainCellSaveData` (struct, INetworkSerializable)**
Mirror of `TerrainCell` that implements both `[Serializable]` (for JSON/save) and `INetworkSerializable` (for `ClientRpc` delivery). String fields are manually serialized with `WriteValueSafe`/`ReadValueSafe`.

```csharp
TerrainCellSaveData.FromCell(cell);    // Convert runtime → serializable
saveData.ToCell();                     // Convert serializable → runtime
```

**`TerrainCellGrid` (MonoBehaviour)**
Flat array of `TerrainCell` on the same GameObject as `MapController`. Manages world↔grid coordinate conversion and serialization.

| Method | Purpose |
|---|---|
| `Initialize(Bounds)` | Allocates the cell array based on map bounds and `_cellSize` (default 4 units) |
| `InitializeFromPatches(List<TerrainPatch>)` | Stamps base types onto cells from scene-placed `TerrainPatch` colliders, sorted by priority (highest wins) |
| `GetTerrainAt(Vector3)` | Returns the current `TerrainType` at a world position; null if outside grid |
| `GetCellAt(Vector3)` | Returns a copy of the `TerrainCell` struct at a world position |
| `GetCellRef(int x, int z)` | Returns a `ref TerrainCell` for in-place mutation (used by `TerrainWeatherProcessor`) |
| `WorldToGrid(Vector3, out int x, out int z)` | Converts world position to grid coords; returns false if out of bounds |
| `GridToWorld(int x, int z)` | Returns the world-space center of a cell |
| `GetCellRangeForBounds(Bounds, out minX, out minZ, out maxX, out maxZ)` | Spatial culling for weather processor; yields zero-iteration range if outside grid |
| `SerializeCells()` | Returns `TerrainCellSaveData[]` for hibernation or ClientRpc |
| `RestoreFromSaveData(TerrainCellSaveData[])` | Restores cell array from save data on wake-up |

**`TerrainPatch` (MonoBehaviour, requires BoxCollider)**
Scene-placed authoring object that defines the base terrain type for a region of the map. Higher `Priority` value overwrites lower when cells overlap.

| Property | Type | Purpose |
|---|---|---|
| `BaseTerrainType` | `TerrainType` | TerrainType SO assigned to overlapping cells |
| `BaseFertility` | `float` | Initial `Fertility` written to cells on initialization |
| `Priority` | `int` | Higher value = takes precedence over lower patches |
| `Bounds` | `Bounds` | World-space AABB from the `BoxCollider` |

**`TerrainTransitionRule` (ScriptableObject)**
Data-only condition set that maps a source terrain state to a result type when all thresholds are satisfied. Loaded at runtime from `Resources/Data/Terrain/TransitionRules/`.

```csharp
bool passed = rule.Evaluate(moisture, temperature, snowDepth);
```

| Field | Purpose |
|---|---|
| `SourceType` | The type that must match `CurrentTypeId` or `BaseTypeId` |
| `ResultType` | The type to assign on match |
| `Priority` | Higher-priority rules are tested first |
| `MinMoisture / MaxMoisture` | Set to -1 to skip check |
| `MinTemperature / MaxTemperature` | Set to -999/999 to skip check |
| `MinSnowDepth` | Set to -1 to skip check |

---

### Layer 2 — Weather & Atmosphere

**`GlobalWindController` (NetworkBehaviour, singleton)**
Server-authoritative global wind that all `WeatherFront` objects blend with their local wind. Clients receive direction/strength via `NetworkVariable` and can subscribe to `OnWindChanged`.

```csharp
GlobalWindController.Instance.WindDirection.Value   // NetworkVariable<Vector2>
GlobalWindController.Instance.WindStrength.Value    // NetworkVariable<float>
event Action<Vector2, float> OnWindChanged          // Fires on any NetworkVariable change
```

Wind direction drifts slowly over time. Random gusts add brief strength spikes. Delta time is clamped to 0.5f to prevent wind snapping at Giga Speed (CLAUDE.md Rule 26).

**`BiomeClimateProfile` (ScriptableObject)**
Per-biome climate parameters. Assigned to both `BiomeDefinition` and `BiomeRegion`.

| Field | Purpose |
|---|---|
| `AmbientTemperatureMin/Max` | Temperature range over a day cycle |
| `TemperatureOverDay` | `AnimationCurve` that maps time-of-day (0–1) to a temperature lerp value |
| `RainProbability / SnowProbability / CloudyProbability` | Normalized weights for `WeatherType` rolls; validated to sum ≤ 1 in `OnValidate` |
| `FrontSpawnIntervalMinHours/MaxHours` | Time between new front spawns (game hours) |
| `FrontRadiusMin/Max` | Spawn radius range for fronts |
| `FrontIntensityMin/Max` | Effect strength range for fronts |
| `FrontLifetimeMinHours/MaxHours` | How long a front lives before expiring |
| `BaselineMoisture` | Target moisture cells revert toward when no weather is active |
| `EvaporationRate` | Rate at which moisture drops per tick (accelerated by wind) |
| `DefaultTerrainType` | Fallback terrain when no `TerrainCellGrid` covers a position |
| `DefaultFloorOnSettlement` | Terrain type stamped on NPC settlement floors |

```csharp
float temp = profile.GetAmbientTemperature(TimeManager.Instance.CurrentTime01);
```

**`BiomeRegion` (MonoBehaviour, ISaveable, requires BoxCollider)**
A world-space zone that owns and manages weather fronts for its area. Multiple `BiomeRegion` instances can exist on the map (forest, desert, plains).

**Static API:**
```csharp
BiomeRegion.GetRegionAtPosition(Vector3 worldPos)            // Returns the region containing this position
BiomeRegion.GetAdjacentRegions(BiomeRegion region)          // Returns neighboring regions (within 20-unit expansion)
BiomeRegion.ClearRegistry()                                  // Called by SaveManager before session reset
```

**Instance API:**
```csharp
string RegionId                                              // Unique region identifier
bool IsHibernating                                          // True when no players are present on the map
BiomeClimateProfile ClimateProfile                          // Assigned SO
BiomeDefinition BiomeDefinition                             // Assigned biome definition
List<WeatherFront> ActiveFronts                             // Currently live fronts

float GetAmbientTemperature()                               // Reads TimeManager.CurrentTime01, evaluates BiomeClimateProfile
TerrainType GetDefaultTerrainType()                         // Returns ClimateProfile.DefaultTerrainType
List<WeatherFront> GetFrontsOverlapping(Bounds area)        // Spatial query used by TerrainWeatherProcessor

void WakeUp(double currentTime)                             // ISaveable lifecycle; fast-forwards hibernated fronts
void Hibernate(double currentTime)                          // ISaveable lifecycle; serializes fronts to WeatherFrontSnapshot
void OnFrontExpired(WeatherFront front)                     // Callback from WeatherFront when it despawns
```

**Hibernation:** When `Hibernate()` is called, all active `WeatherFront` NetworkObjects are despawned and serialized into `WeatherFrontSnapshot` structs. When `WakeUp()` is called, surviving fronts are fast-forwarded (position + lifetime), and new fronts are spawned proportional to elapsed time divided by mean spawn interval.

**ISaveable:** `CaptureState()` returns `BiomeRegionSaveData` (containing `IsHibernating`, `LastHibernationTime`, `HibernatedFronts[]`). `RestoreState()` rehydrates from that data.

**`WeatherFront` (NetworkBehaviour)**
A moving weather entity tracked by position and radius. Spawned by `BiomeRegion`, despawned when lifetime expires or the front exits its parent region bounds.

**NetworkVariables (server writes, clients read):**

| Variable | Type | Default |
|---|---|---|
| `Type` | `NetworkVariable<WeatherType>` | Clear |
| `LocalWindDirection` | `NetworkVariable<Vector2>` | zero |
| `LocalWindStrength` | `NetworkVariable<float>` | 0 |
| `Radius` | `NetworkVariable<float>` | 50 |
| `Intensity` | `NetworkVariable<float>` | 0.5 |
| `TemperatureModifier` | `NetworkVariable<float>` | 0 |
| `RemainingLifetime` | `NetworkVariable<float>` | 0 |

```csharp
Vector2 ActualVelocity   // GlobalWind + LocalWind combined
float GetShadowOpacity() // 0 (Clear) → 0.2 (Cloudy) → 0.5 (Rain) → 0.6 (Snow)
```

**`WeatherType` (enum : byte)**
```csharp
Clear, Cloudy, Rain, Snow
```

**`WeatherFrontSnapshot` (struct, Serializable)**
Pure data snapshot of a `WeatherFront` for hibernation serialization. Fields match the `WeatherFront` NetworkVariables plus `Position`.

---

### Layer 3 — Weather↔Terrain

**`TerrainWeatherProcessor` (MonoBehaviour)**
Server-only tick processor that reads active `WeatherFront` objects from `BiomeRegion`, applies their effects to overlapping cells in `TerrainCellGrid`, then evaluates `TerrainTransitionRule` conditions.

```csharp
processor.Initialize(TerrainCellGrid grid, BiomeRegion region);
event Action<int, int, TerrainType> OnCellTerrainChanged;   // (gridX, gridZ, newTerrainType)
```

**Tick loop (server-only, catch-up while loop for Giga Speed compliance):**
1. `ProcessWeatherFronts` — for each overlapping front, applies moisture (Rain), snow depth (Snow), and temperature modifier to cells within `front.Radius`. Falloff is distance-based with a wind-direction bias (+30% contribution on the downwind side). Modified cells are added to a dirty set.
2. `ProcessAmbientRevert` — for each dirty cell, evaporates moisture (accelerated by global wind strength), nudges temperature toward `BiomeRegion.GetAmbientTemperature()`, melts snow if temperature > 0. Cells that return to baseline are removed from the dirty set.
3. `EvaluateTransitions` — for each dirty cell, tests `TerrainTransitionRule` list (sorted descending by priority) against the cell's current moisture, temperature, and snow depth. First matching rule wins. If no rule matches, the cell reverts to `BaseTypeId`. Fires `OnCellTerrainChanged` when `CurrentTypeId` changes.

---

### Layer 4 — Vegetation

**`VegetationGrowthSystem` (MonoBehaviour)**
Server-only tick processor for wild plant growth on fertile cells. Uses a catch-up while loop for Giga Speed compliance.

```csharp
system.Initialize(TerrainCellGrid grid);
int stage = system.GetGrowthStage(float growthTimer);  // 0=Empty, 1=Sprout, 2=Bush, 3=Sapling, 4=Tree
```

Rules:
- Growth only occurs on cells where `TerrainType.CanGrowVegetation == true` and `cell.IsPlowed == false`.
- If `cell.Moisture >= _minimumMoistureForGrowth` (default 0.2), `GrowthTimer` advances and `TimeSinceLastWatered` resets.
- If moisture is too low, `TimeSinceLastWatered` advances. After 48 game-hours, `GrowthTimer` resets (drought death).
- Visual prefab spawning and harvestable object placement are planned but not yet implemented (marked with TODO comments in code).

Growth stage thresholds (configurable via SerializeField):
- Sprout: 6 game-hours
- Bush: 24 game-hours
- Sapling: 72 game-hours
- Tree: 168 game-hours

---

## 3. Offline Catch-Up (MacroSimulator Integration)

When a map wakes from hibernation, `MacroSimulator.SimulateCatchUp()` calls two static methods on cells serialized as `TerrainCellSaveData[]`. No live Unity systems (NavMesh, NetworkObject, physics) are involved.

```csharp
// Called from MapController wake-up sequence
MacroSimulator.SimulateTerrainCatchUp(
    TerrainCellSaveData[] cells,
    BiomeClimateProfile climate,
    float hoursPassed,
    List<TerrainTransitionRule> rules);

MacroSimulator.SimulateVegetationCatchUp(
    TerrainCellSaveData[] cells,
    BiomeClimateProfile climate,
    float hoursPassed,
    float minimumMoistureForGrowth = 0.2f,
    float droughtDeathHours = 48f);
```

**TerrainCatchUp math (per cell):**
- Estimated rain hours = `hoursPassed * RainProbability`
- Estimated dry hours = `hoursPassed * (1 - Rain - Snow - Cloudy)`
- Moisture += rain contribution, -= dry evaporation, clamped to [0,1]
- Temperature snapped to `(Min + Max) / 2`
- `TimeSinceLastWatered` reset to 0 if rain, else incremented
- Transition rules evaluated once after moisture/temperature update

**VegetationCatchUp math (per cell):**
- Average moisture estimated from `BaselineMoisture + RainProbability * 0.3`
- If avg moisture >= threshold: `GrowthTimer += hoursPassed`
- If avg moisture < threshold: drought death after 48 hours

**Rule:** Any new terrain property that changes over time must have a corresponding formula in `MacroSimulator.SimulateTerrainCatchUp`.

---

## 4. Integration Points

### MapController (TerrainCellGrid lifecycle)

| Event | What Happens |
|---|---|
| Map wakes from hibernation | `MacroSimulator.SimulateTerrainCatchUp` + `SimulateVegetationCatchUp` run on `_hibernationData.TerrainCells`, then `terrainGrid.RestoreFromSaveData()` is called |
| Map hibernates | `terrainGrid.SerializeCells()` is written to `_hibernationData.TerrainCells` |
| New client joins map | `SendTerrainGridClientRpc(TerrainCellSaveData[])` sends the full grid to the joining client; client calls `terrainGrid.RestoreFromSaveData()` |

### Room

```csharp
TerrainType FloorTerrainType    // The terrain type inside this room (e.g., StoneFloor)
bool IsExposed                  // True = weather fronts can still affect this room
```

`Room.FloorTerrainType` will be the Priority 1 source for `CharacterTerrainEffects` once `CharacterLocations` tracks the current room. Currently, Priority 1 reads from `TerrainCellGrid`.

### SaveManager

```csharp
BiomeRegion.ClearRegistry();     // Called before session reset — clears the static _allRegions list
TerrainTypeRegistry.Clear();    // Called before session reset — clears the type dictionary
```

### GameLauncher

```csharp
TerrainTypeRegistry.Initialize();    // Called on boot, loads all TerrainType SOs from Resources
```

### BiomeDefinition (World System)

`BiomeDefinition` holds a reference to a `BiomeClimateProfile` SO. This is the bridge between the world-system biome layer and the terrain-weather layer. A new biome must have its `BiomeClimateProfile` configured with accurate precipitation and temperature parameters.

---

## 5. How to Add a New Terrain Type

1. Create a `TerrainType` SO under `Resources/Data/Terrain/TerrainTypes/`. Set a unique `TypeId` string.
2. Set `SpeedMultiplier`, `DamagePerSecond`, `SlipFactor`, `CanGrowVegetation`, and assign a `FootstepAudioProfile` SO.
3. Assign a `GroundOverlayMaterial` shader asset if this type needs a visual ground overlay.
4. Create `TerrainTransitionRule` SOs under `Resources/Data/Terrain/TransitionRules/` if other types can transition into this one.
5. Assign the new type to `TerrainPatch` components on maps where it appears as a base type.
6. If this type should be the default outdoor terrain for a biome, assign it to the `BiomeClimateProfile.DefaultTerrainType`.
7. Update `FootstepAudioProfile` SOs to include audio clips for this terrain.

## 6. How to Add a New Transition Rule

1. Create a `TerrainTransitionRule` SO under `Resources/Data/Terrain/TransitionRules/`.
2. Set `SourceType`, `ResultType`, threshold fields, and `Priority`.
3. Use `-1` (or `-999` for temperature) to skip a condition check.
4. Higher priority rules are evaluated first. Once a rule matches, evaluation stops — design rules from most-specific to least-specific.

## 7. Key File Locations

| File | Purpose |
|---|---|
| `Assets/Scripts/Terrain/TerrainType.cs` | TerrainType ScriptableObject definition |
| `Assets/Scripts/Terrain/TerrainTransitionRule.cs` | Transition condition ScriptableObject |
| `Assets/Scripts/Terrain/TerrainTypeRegistry.cs` | Static O(1) lookup by TypeId |
| `Assets/Scripts/Terrain/TerrainCell.cs` | Per-cell data struct |
| `Assets/Scripts/Terrain/TerrainCellSaveData.cs` | Serializable + INetworkSerializable cell version |
| `Assets/Scripts/Terrain/TerrainCellGrid.cs` | Flat cell array with world↔grid conversion |
| `Assets/Scripts/Terrain/TerrainPatch.cs` | Scene-placed collider defining base terrain type |
| `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs` | Weather effect applicator + transition evaluator |
| `Assets/Scripts/Terrain/VegetationGrowthSystem.cs` | Wild plant growth on fertile cells |
| `Assets/Scripts/Weather/GlobalWindController.cs` | NetworkBehaviour singleton, server-authoritative wind |
| `Assets/Scripts/Weather/BiomeClimateProfile.cs` | Climate parameters ScriptableObject |
| `Assets/Scripts/Weather/BiomeRegion.cs` | World zone that spawns and manages WeatherFronts |
| `Assets/Scripts/Weather/WeatherFront.cs` | NetworkBehaviour moving weather entity |
| `Assets/Scripts/Weather/WeatherType.cs` | Enum: Clear, Cloudy, Rain, Snow |
| `Assets/Scripts/Weather/WeatherFrontSnapshot.cs` | Hibernation snapshot struct |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | SimulateTerrainCatchUp + SimulateVegetationCatchUp |
| `Assets/Scripts/World/Buildings/Rooms/Room.cs` | FloorTerrainType + IsExposed properties |
| `Assets/Scripts/Core/GameLauncher.cs` | TerrainTypeRegistry.Initialize() on boot |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | ClearRegistry() + TerrainTypeRegistry.Clear() on reset |
| `Resources/Data/Terrain/TerrainTypes/` | All TerrainType SO assets |
| `Resources/Data/Terrain/TransitionRules/` | All TerrainTransitionRule SO assets |

## 8. Dependencies

| Depends On | Why |
|---|---|
| `world-system` | MapController owns TerrainCellGrid; MacroSimulator runs offline catch-up; BiomeDefinition links to BiomeClimateProfile |
| `multiplayer` | WeatherFront and GlobalWindController are NetworkBehaviours; TerrainCellSaveData is INetworkSerializable; ClientRpc sends grid to joining players |
| `save-load-system` | BiomeRegion implements ISaveable; MapSaveData.TerrainCells carries the serialized grid through save/load |
| `character-archetype` | FootstepAudioProfile is referenced from TerrainType; FootSurfaceType comes from CharacterArchetype |
| `building_system` | Room.FloorTerrainType is the planned Priority 1 source for CharacterTerrainEffects |
| `game-speed-controller` | TerrainWeatherProcessor and VegetationGrowthSystem use catch-up while loops; GlobalWindController clamps delta to 0.5f |

## 9. Golden Rules

1. **TypeId is the contract.** Never compare `TerrainType` references directly — always compare by `TypeId` string. The registry may return different instances across sessions.
2. **Server applies effects, clients read state.** `TerrainWeatherProcessor` and `VegetationGrowthSystem` are server-only. Clients receive the cell state via `SendTerrainGridClientRpc` on join.
3. **Every new terrain property needs a catch-up formula.** If a field changes over time (moisture, growth timer), `MacroSimulator.SimulateTerrainCatchUp` or `SimulateVegetationCatchUp` must account for it.
4. **Transition rules are data, not code.** Do not hardcode "Dirt becomes Mud" in any script. Author a `TerrainTransitionRule` SO.
5. **Use TerrainTypeRegistry.Get(), never Resources.Load() at runtime.** The registry is initialized once on boot for O(1) access. Direct `Resources.Load` inside Update or tick loops is a performance violation.
6. **BiomeRegion.ClearRegistry() and TerrainTypeRegistry.Clear() must both run on session reset.** Missing either will leave stale static state across scenes.
