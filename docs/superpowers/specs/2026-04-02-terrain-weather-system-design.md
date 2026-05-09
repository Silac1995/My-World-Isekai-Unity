# Terrain & Weather System — Design Spec

**Date:** 2026-04-02
**Status:** Draft
**Branch:** feature/terrain-weather-system

---

## 1. Problem Statement

The project has no concept of ground type, weather, or atmospheric systems. Maps are defined by biomes (resource density) and zones (AI behavior areas), but nothing answers:

- What kind of ground is the character standing on?
- What sound do their footsteps make?
- Does terrain affect movement or apply damage?
- Does weather exist? Does it change terrain over time?
- Can plants grow? Where? Under what conditions?

This spec defines a layered Terrain & Weather system that answers all of these questions.

---

## 2. Architecture Overview

**Approach:** Layered Architecture (Bottom-Up) — five independent layers communicating via events and shared data. Each layer can be built, tested, and extended independently.

```
Layer 5 — Character Effects     (FootstepAudioResolver, CharacterTerrainEffects)
Layer 4 — Vegetation & Growth   (VegetationGrowthSystem, CropSystem)
Layer 3 — Weather↔Terrain       (TerrainWeatherProcessor)
Layer 2 — Weather & Atmosphere  (WeatherFront, GlobalWindController, BiomeRegion)
Layer 1 — Terrain Foundation    (TerrainType, TerrainCellGrid, TerrainPatch)
```

**World-level view:**

```
GlobalWindController (singleton, world-level)
    │ prevailing wind direction + strength
    ▼
BiomeRegion (per climate zone on world map)
    │ climate profile, bounds, spawns WeatherFronts
    │ hibernates when no players nearby
    ▼
WeatherFront (NetworkBehaviour GameObjects)
    │ Rain/Snow/Cloudy/Clear, moves within biome bounds
    │ casts shadow on ground, visual particles
    │ affects players in open world AND inside active maps
    ▼
MapController + TerrainCellGrid (when map is active)
    │ per-cell moisture, temperature, snow depth, terrain state
    │ TerrainPatch zones define base terrain type
    ▼
Character (walks on cells)
    │ CharacterTerrainEffects reads cell → speed/damage/slip
    │ FootstepAudioResolver reads cell + boot material → sound
```

---

## 3. Layer 1 — Terrain Foundation

### 3.1 TerrainType (ScriptableObject)

**Path:** `Assets/Scripts/Terrain/TerrainType.cs`
**Namespace:** `MWI.Terrain`

Defines the properties and behavior of a terrain type. Immutable definition data.

```csharp
[CreateAssetMenu(menuName = "MWI/Terrain/Terrain Type")]
public class TerrainType : ScriptableObject
{
    [Header("Identity")]
    public string TypeId;           // "Fertile", "Dirt", "Stone", "Mud", "Snow", "Ice", "Lava"
    public string DisplayName;
    public Color DebugColor;        // For editor visualization of the cell grid

    [Header("Character Effects")]
    public float SpeedMultiplier = 1f;      // Mud=0.6, Ice=0.8, normal=1.0
    public float DamagePerSecond = 0f;       // Lava=5, Ice cold=0.5
    public float SlipFactor = 0f;            // Ice=0.9, Mud=0.3
    public DamageType DamageType;            // Uses existing enum: Fire, Ice, Lightning, etc.
    public bool HasDamage => DamagePerSecond > 0f;  // Check this before reading DamageType

    [Header("Growth")]
    public bool CanGrowVegetation;           // Only Fertile=true

    [Header("Audio")]
    public FootstepAudioProfile FootstepProfile;  // Lookup table for this terrain

    [Header("Visuals")]
    public Material GroundOverlayMaterial;   // Optional visual overlay (mud splat, snow layer)
    public float OverlayOpacityAtFullSaturation = 0.8f;
}
```

**Initial terrain types to create:**

| TypeId | Speed | Damage | Slip | Grows | Notes |
|--------|-------|--------|------|-------|-------|
| Fertile | 1.0 | 0 | 0 | Yes | Grass, farmland |
| Dirt | 1.0 | 0 | 0 | No | Default dry ground |
| Stone | 1.0 | 0 | 0 | No | Rock, cobblestone |
| Mud | 0.6 | 0 | 0.3 | No | Wet dirt, slows movement |
| Snow | 0.8 | 0 | 0.1 | No | Accumulated snow |
| Ice | 0.8 | 0.5 (Ice) | 0.9 | No | Frozen surface, DamageType.Ice |
| Lava | 0.4 | 5.0 (Fire) | 0 | No | DamageType.Fire, heavily slows |
| WoodFloor | 1.0 | 0 | 0 | No | Interior building floors |
| StoneTile | 1.0 | 0 | 0 | No | Interior stone floors |

### 3.2 TerrainTransitionRule (ScriptableObject)

**Path:** `Assets/Resources/Data/Terrain/TerrainTransitionRule.cs`

Defines conditions under which one terrain type transforms into another. Evaluated by the cell state machine.

```csharp
[CreateAssetMenu(menuName = "MWI/Terrain/Transition Rule")]
public class TerrainTransitionRule : ScriptableObject
{
    public TerrainType SourceType;          // e.g., Dirt
    public TerrainType ResultType;          // e.g., Mud

    [Header("Conditions (ALL must be met)")]
    public float MinMoisture = -1f;         // -1 = don't check
    public float MaxMoisture = -1f;
    public float MinTemperature = -999f;    // -999 = don't check
    public float MaxTemperature = 999f;
    public float MinSnowDepth = -1f;

    public bool Evaluate(float moisture, float temperature, float snowDepth)
    {
        if (MinMoisture >= 0 && moisture < MinMoisture) return false;
        if (MaxMoisture >= 0 && moisture > MaxMoisture) return false;
        if (temperature < MinTemperature || temperature > MaxTemperature) return false;
        if (MinSnowDepth >= 0 && snowDepth < MinSnowDepth) return false;
        return true;
    }
}
```

**Example rules:**
- Dirt + moisture > 0.7 → Mud
- Mud + temperature < 0 → Ice
- Any + snowDepth > 0.3 + temperature < 2 → Snow
- Mud + moisture < 0.3 → Dirt (drying revert)
- Snow + temperature > 5 + snowDepth < 0.1 → revert to base type

### 3.3 TerrainCellGrid (MonoBehaviour)

**Path:** `Assets/Scripts/Terrain/TerrainCellGrid.cs`
**Attached to:** MapController GameObject

A flat array of terrain cells covering the map area. Derives bounds from MapController's BoxCollider.

```csharp
public class TerrainCellGrid : MonoBehaviour
{
    [SerializeField] private float _cellSize = 4f;  // Unity units per cell (~61cm real-world)

    // Runtime data — flat array, row-major
    private TerrainCell[] _cells;
    private int _width, _depth;
    private Vector3 _origin;  // World-space bottom-left corner

    // Public API
    public TerrainCell GetCellAt(Vector3 worldPos);
    public TerrainCell GetCell(int x, int z);
    public void SetCellCurrentType(int x, int z, TerrainType type);
    public TerrainType GetTerrainAt(Vector3 worldPos);  // Main query for footsteps/effects
    public void InitializeFromPatches(List<TerrainPatch> patches);

    // Serialization for hibernation
    public TerrainCellSaveData[] SerializeCells();
    public void RestoreFromSaveData(TerrainCellSaveData[] data);
}
```

**Cell resolution rationale:** 4 Unity units per cell (~61cm real-world) is chosen over 2 units to manage memory. A 750×750 map at 4 units/cell = 187×187 = ~35,000 cells (vs. 140,625 at 2 units). Still granular enough for farming plots and puddles. Configurable per-map if finer detail is needed.

**TerrainCell struct:**

Uses string TypeIds instead of ScriptableObject references to keep the struct serializable and avoid managed references in a value type. A static `TerrainTypeRegistry` (populated on scene load from all TerrainType SOs in Resources) provides O(1) lookup by ID.

```csharp
[Serializable]
public struct TerrainCell
{
    // Static (from TerrainPatch)
    public string BaseTypeId;           // TerrainType.TypeId — what the patch defines (reverts to this)

    // Dynamic (modified by weather, gameplay)
    public string CurrentTypeId;        // TerrainType.TypeId — what it currently is
    public float Moisture;              // 0-1, driven by rain/watering
    public float Temperature;           // Local temperature (ambient + weather modifier)
    public float SnowDepth;             // 0-1, accumulates from snow fronts
    public float Fertility;             // 0-1, for fertile cells (growth potential)
    public bool IsPlowed;               // Player/NPC tilled this cell
    public string PlantedCropId;        // Null if empty, or crop ID if planted
    public float GrowthTimer;           // Progress toward next growth stage
    public float TimeSinceLastWatered;  // Tracks drought for plant death

    // Convenience (not serialized)
    public TerrainType GetBaseType() => TerrainTypeRegistry.Get(BaseTypeId);
    public TerrainType GetCurrentType() => TerrainTypeRegistry.Get(CurrentTypeId);
}
```

**TerrainTypeRegistry (static helper):**

```csharp
public static class TerrainTypeRegistry
{
    private static Dictionary<string, TerrainType> _types;

    public static void Initialize()  // Called once on scene load
    {
        _types = Resources.LoadAll<TerrainType>("Data/Terrain/TerrainTypes")
            .ToDictionary(t => t.TypeId);
    }

    public static TerrainType Get(string typeId) => _types.TryGetValue(typeId, out var t) ? t : null;
}
```

**TerrainCellSaveData:**

```csharp
[Serializable]
public struct TerrainCellSaveData
{
    public string BaseTypeId;
    public string CurrentTypeId;
    public float Moisture;
    public float Temperature;
    public float SnowDepth;
    public float Fertility;
    public bool IsPlowed;
    public string PlantedCropId;
    public float GrowthTimer;
    public float TimeSinceLastWatered;
}
```

### 3.4 TerrainPatch (MonoBehaviour)

**Path:** `Assets/Scripts/Terrain/TerrainPatch.cs`
**Scene-authored.** Placed on maps to define base terrain type for a region.

Named "TerrainPatch" instead of "TerrainZone" to avoid confusion with the existing `Zone.cs` (AI/gameplay zones).

```csharp
[RequireComponent(typeof(BoxCollider))]
public class TerrainPatch : MonoBehaviour
{
    [SerializeField] private TerrainType _baseTerrainType;
    [SerializeField] private float _baseFertility = 0.5f;  // Only relevant if type is Fertile
    [SerializeField] private int _priority = 0;             // Higher priority wins on overlap

    public TerrainType BaseTerrainType => _baseTerrainType;
    public float BaseFertility => _baseFertility;
    public int Priority => _priority;
    public Bounds Bounds => GetComponent<BoxCollider>().bounds;
}
```

**Not a NetworkBehaviour.** Static scene data — identical on all clients. The mutable cell state lives on TerrainCellGrid (synced via MapController).

**Overlap resolution:** When `InitializeFromPatches()` processes cells, each cell checks all overlapping patches. The patch with the highest `_priority` value wins. Equal priority: last-in-list wins (deterministic via scene hierarchy order). This allows layering a small Fertile patch on top of a large Dirt patch.

### 3.5 Sealed Room Floor Type

Existing `Room` component gains a field:

```csharp
// Added to Room.cs or ComplexRoom.cs
[SerializeField] private TerrainType _floorTerrainType;  // WoodFloor, StoneTile, etc.
[SerializeField] private bool _isExposed;                 // True for rooftops, courtyards

public TerrainType FloorTerrainType => _floorTerrainType;
public bool IsExposed => _isExposed;
```

- **Sealed rooms** (`_isExposed = false`): footsteps read `_floorTerrainType` directly. No cell grid.
- **Exposed rooms** (`_isExposed = true`): room participates in the outdoor cell grid. Weather affects it.

**Room detection mechanism:** `CharacterTerrainEffects` determines which terrain source to use via the existing `CharacterLocations` system, which already tracks whether a character is inside a building/room. The resolution priority:
1. If `CharacterLocations.CurrentRoom != null` and `!CurrentRoom.IsExposed` → use `Room.FloorTerrainType`
2. If inside an active MapController → use `TerrainCellGrid.GetTerrainAt(position)`
3. Otherwise (open world) → use `BiomeRegion.GetDefaultTerrainType()`

---

## 4. Layer 2 — Weather & Atmosphere

### 4.1 GlobalWindController (Singleton)

**Path:** `Assets/Scripts/Weather/GlobalWindController.cs`
**Namespace:** `MWI.Weather`

World-level singleton. Single source of truth for prevailing wind.

```csharp
public class GlobalWindController : NetworkBehaviour
{
    public static GlobalWindController Instance { get; private set; }

    // Networked state
    public NetworkVariable<Vector2> WindDirection;     // Normalized 2D (XZ plane)
    public NetworkVariable<float> WindStrength;         // 0-1 scale

    // Server-side drift
    [SerializeField] private float _driftSpeed = 0.01f;        // How fast direction shifts
    [SerializeField] private float _gustFrequency = 0.1f;      // Random strength spikes
    [SerializeField] private AnimationCurve _seasonalBias;      // Optional seasonal patterns

    // Events
    public event Action<Vector2, float> OnWindChanged;
}
```

Shifts gradually over time — never snaps. Server-authoritative, clients read NetworkVariables.

### 4.2 BiomeClimateProfile (ScriptableObject)

**Path:** `Assets/Resources/Data/Terrain/BiomeClimateProfile.cs`

Defines the climate characteristics of a biome region. Referenced by BiomeRegion at runtime and linked to BiomeDefinition for resource data.

```csharp
[CreateAssetMenu(menuName = "MWI/Terrain/Biome Climate Profile")]
public class BiomeClimateProfile : ScriptableObject
{
    [Header("Temperature")]
    public float AmbientTemperatureMin = 5f;    // Celsius, coldest baseline
    public float AmbientTemperatureMax = 25f;   // Celsius, warmest baseline
    public AnimationCurve TemperatureOverDay;    // 0-1 day cycle → temperature lerp

    [Header("Precipitation")]
    [Range(0f, 1f)] public float RainProbability = 0.3f;
    [Range(0f, 1f)] public float SnowProbability = 0.1f;
    [Range(0f, 1f)] public float CloudyProbability = 0.3f;
    // Remaining probability = Clear. Sum of Rain+Snow+Cloudy must be <= 1.0.
    // OnValidate() clamps: if sum > 1, scale down proportionally.
    public float FrontSpawnIntervalMinHours = 2f;
    public float FrontSpawnIntervalMaxHours = 8f;

    [Header("Front Properties")]
    public float FrontRadiusMin = 30f;     // Unity units
    public float FrontRadiusMax = 80f;
    public float FrontIntensityMin = 0.3f;
    public float FrontIntensityMax = 1.0f;
    public float FrontLifetimeMinHours = 1f;
    public float FrontLifetimeMaxHours = 6f;

    [Header("Moisture")]
    public float BaselineMoisture = 0.3f;       // Cells drift toward this when no weather
    public float EvaporationRate = 0.05f;        // Per game-hour moisture loss

    [Header("Default Terrain")]
    public TerrainType DefaultTerrainType;       // Fallback for world map traversal
    public TerrainType DefaultFloorOnSettlement;  // For dynamically promoted settlements
}
```

### 4.3 BiomeRegion (MonoBehaviour + ISaveable)

**Path:** `Assets/Scripts/Weather/BiomeRegion.cs`

Represents a climate zone on the world map. Spawns, contains, and manages WeatherFronts. Implements `ISaveable` for world-level persistence.

```csharp
[RequireComponent(typeof(BoxCollider))]
public class BiomeRegion : MonoBehaviour, ISaveable
{
    [SerializeField] private string _regionId;
    [SerializeField] private BiomeDefinition _biomeDefinition;
    [SerializeField] private BiomeClimateProfile _climateProfile;

    // Runtime state
    private List<WeatherFront> _activeFronts = new();
    private List<WeatherFrontSnapshot> _hibernatedFronts = new();
    private bool _isHibernating;
    private float _nextSpawnTimer;

    // Hibernation
    public bool IsHibernating => _isHibernating;

    // Public API
    public BiomeClimateProfile ClimateProfile => _climateProfile;
    public BiomeDefinition BiomeDefinition => _biomeDefinition;
    public float GetAmbientTemperature();               // Based on profile + time of day
    public TerrainType GetDefaultTerrainType();          // For world map traversal
    public List<WeatherFront> GetFrontsOverlapping(Bounds area);  // For MapController queries

    // Lifecycle
    public void Hibernate();       // Serialize fronts, despawn NetworkObjects
    public void WakeUp();          // Restore fronts, run catch-up
    public void CheckPlayerProximity();  // Activate self + adjacent regions

    // ISaveable
    public string SaveKey => _regionId;
    public object CaptureState();
    public void RestoreState(object state);

    // Static registry
    public static BiomeRegion GetRegionAtPosition(Vector3 worldPos);
    public static List<BiomeRegion> GetAdjacentRegions(BiomeRegion region);
}
```

**Hibernation rules:**
- Hibernates when no player is in this region AND no player is in any adjacent region
- On hibernation: serializes all WeatherFront GameObjects into `WeatherFrontSnapshot`, despawns NetworkObjects
- On wake-up: fast-forwards front positions using elapsed time + wind vectors, spawns/despawns fronts based on lifetime, creates new fronts that would have spawned during hibernation
- Adjacent region activation ensures players can see approaching weather

**Lifecycle dependency with MapController:**

BiomeRegion and MapController have independent but coordinated lifecycles:

```
BiomeRegion.WakeUp()  ←  MUST happen BEFORE  →  MapController.WakeUp()
BiomeRegion.Hibernate()  ←  MUST happen AFTER  →  all child MapControllers hibernate
```

- **Wake-up ordering:** When a player enters a MapController's trigger, the MapController calls `BiomeRegion.GetRegionAtPosition()` and ensures the BiomeRegion (+ adjacent regions) are awake BEFORE proceeding with its own wake-up. This guarantees WeatherFronts exist when `TerrainWeatherProcessor` starts ticking.
- **A BiomeRegion stays awake** as long as ANY MapController within it (or in an adjacent BiomeRegion) has active players. BiomeRegion checks `_childMapControllers.Any(m => !m.IsHibernating)` plus adjacent region player counts.
- **Hibernation cascade:** When the last player leaves a BiomeRegion (and no adjacent regions have players), BiomeRegion hibernates its WeatherFronts. This can only happen after all MapControllers inside it have already hibernated (since they hibernate when their own player count hits 0).

**Relationship to MapController:**
- BiomeRegion is a higher-level entity spanning multiple MapControllers
- When `CommunityTracker.PromoteToSettlement()` creates a new MapController, it should call `BiomeRegion.GetRegionAtPosition(worldPos)` to inherit the BiomeDefinition
- BiomeRegion tracks which MapControllers fall within its bounds but does not own them
- BiomeRegion registers with `SaveManager` via `ISaveable` and must be cleaned up in `SaveManager.ResetForNewSession()`

### 4.4 WeatherFront (NetworkBehaviour)

**Path:** `Assets/Scripts/Weather/WeatherFront.cs`

A physical weather entity that travels across the world map. Visible at all times when the parent BiomeRegion is active.

```csharp
public class WeatherFront : NetworkBehaviour
{
    [Header("Type")]
    public NetworkVariable<WeatherType> Type;  // Rain, Snow, Cloudy, Clear

    [Header("Movement")]
    public NetworkVariable<Vector2> LocalWindDirection;
    public NetworkVariable<float> LocalWindStrength;

    [Header("Properties")]
    public NetworkVariable<float> Radius;
    public NetworkVariable<float> Intensity;           // 0-1
    public NetworkVariable<float> TemperatureModifier;  // Additive offset from ambient
    public NetworkVariable<float> RemainingLifetime;

    // Server-only
    private BiomeRegion _parentRegion;

    // Computed
    public Vector2 ActualVelocity =>
        (GlobalWindController.Instance.WindDirection.Value * GlobalWindController.Instance.WindStrength.Value)
        + (LocalWindDirection.Value * LocalWindStrength.Value);

    // Visual components (children)
    // - Cloud sprite/mesh renderer (scales with Radius)
    // - Shadow projector (dark blob on ground plane, opacity by Type)
    // - Particle system (rain drops / snow flakes, only when camera is near)

    // Server-side methods
    public void Initialize(BiomeRegion parent, WeatherType type, Vector3 spawnPos, ...);
    public void ServerUpdate();  // Move, check bounds, decay lifetime
}
```

**WeatherType enum:**

```csharp
public enum WeatherType : byte
{
    Clear,
    Cloudy,
    Rain,
    Snow
}
```

**Shadow system:** Each WeatherFront has a shadow projector (or projected decal) that casts a dark blob on the ground below. Shadow properties:
- Clear: no shadow
- Cloudy: light shadow (opacity 0.2)
- Rain: medium shadow (opacity 0.5)
- Snow: dark shadow (opacity 0.6)
- Shadow scales with front Radius

**WeatherFrontSnapshot (for hibernation):**

```csharp
[Serializable]
public struct WeatherFrontSnapshot
{
    public WeatherType Type;
    public Vector3 Position;
    public Vector2 LocalWindDirection;
    public float LocalWindStrength;
    public float Radius;
    public float Intensity;
    public float TemperatureModifier;
    public float RemainingLifetime;
}
```

### 4.5 Player in Open World (Outside MapController)

When a player is on the world map but not inside any MapController:

1. **Terrain type** → read from `BiomeRegion.GetDefaultTerrainType()` at player position
2. **Weather effects** → if player position is within any WeatherFront's radius:
   - Rain particles on character
   - Temperature modifier applied to character
   - Character gets "wet" status (accumulates over time in rain)
3. **Footsteps** → BiomeRegion default terrain type × boot material
4. **No cell grid** — world map between MapControllers has no per-cell granularity

---

## 5. Layer 3 — Weather↔Terrain Interaction

### 5.1 TerrainWeatherProcessor (MonoBehaviour)

**Path:** `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs`
**Attached to:** MapController GameObject (sibling to TerrainCellGrid)

Runs only when map is active. Processes weather effects on terrain cells at configurable intervals.

```csharp
public class TerrainWeatherProcessor : MonoBehaviour
{
    [SerializeField] private float _tickIntervalGameMinutes = 2f;
    [SerializeField] private List<TerrainTransitionRule> _transitionRules;

    private TerrainCellGrid _grid;
    private MapController _mapController;

    // Each tick:
    // 1. Query BiomeRegion for overlapping WeatherFronts
    // 2. For each cell in the grid:
    //    a. Calculate weather contribution (sum of all overlapping fronts)
    //    b. Update cell moisture, temperature, snow depth
    //    c. Apply wind bias (downwind cells accumulate faster)
    //    d. Apply ambient drying/warming (revert toward biome baseline)
    //    e. Evaluate transition rules → update CurrentType if conditions met
    // 3. Fire OnTerrainChanged event for affected cells (visual updates)

    public event Action<int, int, TerrainType> OnCellTerrainChanged;
}
```

**Weather contribution per cell:**

```
For each overlapping WeatherFront:
    distance = cell position to front center
    if distance > front.Radius: skip
    falloff = 1 - (distance / front.Radius)  // Stronger at center
    windBias = dot(windDirection, cellOffsetFromCenter.normalized)
    contribution = falloff * (1 + windBias * 0.3) * front.Intensity

    if front.Type == Rain:
        cell.Moisture += contribution * rainRate * tickDelta
    if front.Type == Snow:
        cell.SnowDepth += contribution * snowRate * tickDelta
    cell.Temperature += front.TemperatureModifier * falloff * tickDelta
```

**Ambient revert (no weather overhead):**

```
cell.Moisture -= climateProfile.EvaporationRate * windFactor * tickDelta
cell.Moisture = Mathf.MoveTowards(cell.Moisture, climateProfile.BaselineMoisture, driftRate)
cell.Temperature = Mathf.MoveTowards(cell.Temperature, biomeAmbientTemp, warmingRate)
cell.SnowDepth -= meltRate * max(0, cell.Temperature) * tickDelta
```

**Spatial culling optimization:** The processor does NOT iterate all 35,000 cells blindly. Instead:
1. First, check if ANY WeatherFront overlaps this map. If none, only process cells that are NOT at baseline values (tracked via a dirty-cell set).
2. When a front overlaps, compute its bounding rect on the grid and only iterate cells within that rect + a margin.
3. Maintain a `HashSet<int> _dirtyCells` — cells whose properties differ from baseline. Only these need ambient revert processing when no front is overhead.
4. When a cell fully reverts to baseline (moisture ≈ baseline, temp ≈ ambient, snow = 0), remove it from the dirty set.

This means a map with no weather and fully baseline cells does zero iteration per tick.

**Cell state machine:** After updating properties, evaluate all transition rules in priority order. First matching rule sets `cell.CurrentType`. If no rule matches and cell has drifted back to baseline conditions, revert to `cell.BaseType`.

### 5.2 MacroSimulator Integration

**Call site:** `SimulateTerrainCatchUp` is called from INSIDE `MacroSimulator.SimulateCatchUp()`, inserted between the existing Resource Pool Regeneration step and Inventory Yields step. The MapController passes its terrain data and climate profile into `SimulateCatchUp()` via the extended `MapSaveData` (which now includes `TerrainCells`). This keeps the single entry point pattern — callers of `SimulateCatchUp` do not need to know about terrain separately.

**Added to `MacroSimulator.SimulateCatchUp()` between Resource Pool Regeneration and Inventory Yields:**

```csharp
// New Step 2: Terrain Cell Catch-Up
public static void SimulateTerrainCatchUp(
    TerrainCellSaveData[] cells,
    BiomeClimateProfile climate,
    float hoursPassed,
    List<TerrainTransitionRule> rules)
{
    // Simplified offline model:
    // 1. Estimate weather exposure from climate profile probabilities
    float estimatedRainHours = hoursPassed * climate.RainProbability;
    float estimatedSnowHours = hoursPassed * climate.SnowProbability;
    float estimatedDryHours = hoursPassed - estimatedRainHours - estimatedSnowHours;

    // 2. For each cell: apply aggregate moisture/snow/temp changes
    // 3. Evaluate transition rules
    // 4. Advance growth timers for fertile cells with sufficient moisture
    // 5. Kill plants that went too long without water
}
```

**MapSaveData extension:**

```csharp
// Added to MapSaveData
public TerrainCellSaveData[] TerrainCells;
public double TerrainLastUpdateTime;
```

---

## 6. Layer 4 — Vegetation & Growth

### 6.1 VegetationGrowthSystem (MonoBehaviour)

**Path:** `Assets/Scripts/Terrain/VegetationGrowthSystem.cs`
**Attached to:** MapController GameObject

Manages wild vegetation growth on fertile cells that are NOT plowed.

```csharp
public class VegetationGrowthSystem : MonoBehaviour
{
    [SerializeField] private float _tickIntervalGameHours = 1f;
    [SerializeField] private float _minimumMoistureForGrowth = 0.2f;
    [SerializeField] private float _droughtDeathHours = 48f;  // Game hours without water → death

    [Header("Growth Stage Prefabs")]
    [SerializeField] private GameObject _sproutPrefab;
    [SerializeField] private GameObject _bushPrefab;
    [SerializeField] private GameObject _saplingPrefab;
    [SerializeField] private GameObject _treePrefab;

    private TerrainCellGrid _grid;
}
```

**Growth stages (wild vegetation):**

| Stage | Time Required | Moisture Required | Visual | Harvestable? |
|-------|--------------|-------------------|--------|-------------|
| Empty | — | — | Nothing | No |
| Sprout | 6 game-hours | > 0.2 | Small grass sprite | No |
| Green/Bush | 24 game-hours | > 0.2 | Bush prefab | Yes (Plant category) |
| Sapling | 72 game-hours | > 0.15 | Small tree | Yes (Wood, small yield) |
| Tree | 168 game-hours (7 days) | > 0.1 | Full tree | Yes (Wood, full yield) |

- Growth timer only advances when `cell.Moisture >= minimumMoistureForGrowth`
- If `cell.TimeSinceLastWatered > droughtDeathHours` → plant dies, regress to Empty
- Rain resets `TimeSinceLastWatered` automatically
- Grown trees/bushes become `Harvestable` objects (integrate with existing Harvestable system)
- Wild growth is slow and organic — meant to populate untouched fertile areas over time

### 6.2 CropSystem (MonoBehaviour) — PHASE 2

> **Note:** This section is a design preview for Phase 2. Do not implement CropSystem, CharacterAction_Plow, CharacterAction_PlantSeed, CharacterAction_Water, CharacterAction_Harvest, or CropDefinition in Phase 1.

**Path:** `Assets/Scripts/Terrain/CropSystem.cs`
**Attached to:** MapController GameObject

Manages plowed cells and planted crops. Stardew Valley-style farming.

```csharp
public class CropSystem : MonoBehaviour
{
    [SerializeField] private float _tickIntervalGameHours = 1f;
    [SerializeField] private float _minimumMoistureForCropGrowth = 0.3f;
    [SerializeField] private float _cropDroughtDeathHours = 24f;  // Crops die faster than wild plants

    private TerrainCellGrid _grid;
}
```

**Farming flow (all via CharacterAction):**
1. **Plow** → `CharacterAction_Plow` sets `cell.IsPlowed = true`. Prevents wild vegetation.
2. **Plant** → `CharacterAction_PlantSeed` sets `cell.PlantedCropId` to a crop definition ID
3. **Water** → `CharacterAction_Water` adds moisture to cell (or rain does it automatically)
4. **Grow** → CropSystem advances `cell.GrowthTimer` each tick if moisture sufficient
5. **Harvest** → `CharacterAction_Harvest` yields crop items, resets cell to empty plowed state
6. **Death** → no water for `_cropDroughtDeathHours` → crop dies, cell stays plowed but empty

**CropDefinition (ScriptableObject):**

```csharp
[CreateAssetMenu(menuName = "MWI/Terrain/Crop Definition")]
public class CropDefinition : ScriptableObject
{
    public string CropId;
    public string DisplayName;
    public ItemSO SeedItem;
    public ItemSO HarvestItem;
    public int HarvestYield = 3;
    public float GrowthTimeHours = 72f;      // Total time from seed to harvestable
    public int GrowthStages = 4;              // Visual stages
    public GameObject[] StagePrefabs;          // One per stage
    public float MinimumMoisture = 0.3f;
    public float OptimalMoisture = 0.6f;       // Grows faster at optimal
}
```

**Offline:** MacroSimulator advances crop growth timers and checks drought death using the same formulas.

---

## 7. Layer 5 — Character Effects & Footsteps

### 7.1 ItemMaterial (Enum)

**Path:** `Assets/Scripts/Items/ItemMaterial.cs`

New physical material property for all items. Added alongside existing `ItemWeight` enum.

```csharp
public enum ItemMaterial : byte
{
    None = 0,       // Unspecified / bare
    Cloth,
    Leather,
    Hide,
    Wood,
    Bone,
    Iron,
    Steel,
    ChainMail,
    Stone,
    Crystal,
    Fur
}
```

**Added to ItemSO (base class):**

```csharp
// In ItemSO.cs, alongside _weight
[SerializeField] private ItemMaterial _material = ItemMaterial.None;
public ItemMaterial Material => _material;
```

This is immutable SO data — does not affect `ItemInstance`, `NetworkItemData`, or save serialization. Existing `.asset` files default to `ItemMaterial.None` and need bulk assignment via Editor script.

**Future uses beyond footsteps:**
- Impact sounds: weapon material vs armor material → clang/thud/crack
- Drop sounds: item material × terrain type → sound when `WorldItem.OnCollisionEnter` fires
- Crafting: material-based recycling yields

### 7.2 FootSurfaceType (Enum)

**Path:** `Assets/Scripts/Character/Archetype/FootSurfaceType.cs`

Separate from ItemMaterial because creature feet are not items.

```csharp
public enum FootSurfaceType : byte
{
    BareSkin = 0,   // Humans, humanoids without boots
    Hooves,         // Horses, deer, goats
    Padded,         // Wolves, cats — soft paw pads
    Clawed,         // Bears, dragons — hard claws
    Scaled          // Reptiles, fish-folk
}
```

**Added to CharacterArchetype:**

```csharp
// In CharacterArchetype.cs
[SerializeField] private FootSurfaceType _defaultFootSurface = FootSurfaceType.BareSkin;
public FootSurfaceType DefaultFootSurface => _defaultFootSurface;
```

### 7.3 CharacterTerrainEffects (Character Subsystem)

**Path:** `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs`
**Hierarchy:** Child GameObject of Character root, per facade pattern.

```csharp
public class CharacterTerrainEffects : CharacterSystem
{
    // Runs on BOTH server and client.
    // Terrain detection logic is identical on both sides (same grid data, same patches).
    //
    // Server responsibilities:
    //   - Apply speed modifier → CharacterMovement
    //   - Apply damage over time → Character health system
    //   - Apply slip factor → CharacterMovement
    //
    // Client responsibilities:
    //   - Read CurrentTerrainType for FootstepAudioResolver
    //   - Read IsInWeatherFront for local weather VFX
    //
    // No NetworkVariable needed for CurrentTerrainType — clients resolve locally
    // from the synced grid data (received via MapController ClientRpcs).

    // Sources (priority order):
    // 1. Inside sealed room (CharacterLocations.CurrentRoom != null && !IsExposed)
    //    → Room.FloorTerrainType
    // 2. Inside active MapController → TerrainCellGrid.GetTerrainAt(position)
    // 3. On world map → BiomeRegion.GetDefaultTerrainType()

    // Exposes
    public TerrainType CurrentTerrainType { get; private set; }
    public bool IsInWeatherFront { get; private set; }
    public WeatherType CurrentWeather { get; private set; }
    public float Wetness { get; private set; }  // Accumulates in rain, dries over time

    public event Action<TerrainType> OnTerrainChanged;
}
```

### 7.4 FootstepAudioResolver (Component)

**Path:** `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs`
**Hierarchy:** Same child GameObject as CharacterTerrainEffects.

```csharp
public class FootstepAudioResolver : MonoBehaviour
{
    [SerializeField] private AudioSource _footstepAudioSource;

    private CharacterTerrainEffects _terrainEffects;
    private Character _character;

    // Triggered by ICharacterVisual.OnAnimationEvent("footstep")
    // Resolution:
    // 1. Get terrain type from CharacterTerrainEffects.CurrentTerrainType
    // 2. Get foot material:
    //    a. Check CharacterEquipment.GetFootMaterial() (outermost boot)
    //    b. If None → CharacterArchetype.DefaultFootSurface
    // 3. Look up: TerrainType.FootstepProfile.GetClip(material)
    // 4. Play with random pitch/volume variation at character position
}
```

**FootstepAudioProfile (ScriptableObject):**

**Path:** `Assets/Resources/Data/Audio/FootstepAudioProfile.cs`

```csharp
[CreateAssetMenu(menuName = "MWI/Audio/Footstep Audio Profile")]
public class FootstepAudioProfile : ScriptableObject
{
    [Serializable]
    public class MaterialClipSet
    {
        public ItemMaterial BootMaterial;
        public FootSurfaceType FootSurface;    // Used when BootMaterial is None
        public AudioClip[] Clips;               // Array for randomization
        public float VolumeMultiplier = 1f;
        public float PitchVariation = 0.1f;
    }

    [SerializeField] private List<MaterialClipSet> _materialClips;
    [SerializeField] private AudioClip[] _fallbackClips;  // If no specific match

    public AudioClip GetClip(ItemMaterial bootMaterial, FootSurfaceType footSurface);
}
```

Each TerrainType SO references one FootstepAudioProfile. The profile contains clip sets for each boot material / foot surface combination. Fallback clips ensure something always plays even if a specific combo isn't authored.

### 7.5 CharacterEquipment Addition

```csharp
// New method on CharacterEquipment.cs
public ItemMaterial GetFootMaterial()
{
    // Check outermost layer first
    var boots = armorLayer?.GetInstance(WearableType.Boots)
             ?? clothingLayer?.GetInstance(WearableType.Boots)
             ?? underwearLayer?.GetInstance(WearableType.Boots);

    if (boots != null)
        return boots.ItemSO.Material;  // _material is on ItemSO base class

    return ItemMaterial.None;  // Caller falls back to archetype FootSurfaceType
}
```

---

## 8. Networking

### 8.1 Server-Authoritative State

| Data | Authority | Sync Method |
|------|-----------|-------------|
| GlobalWindController wind | Server | NetworkVariable |
| WeatherFront position/type/properties | Server | NetworkVariable per field |
| TerrainCellGrid cell state | Server | Not synced per-cell (too expensive) |
| Character terrain effects (speed/damage) | Server | Applied server-side, visible via existing movement/health sync |
| BiomeRegion hibernation state | Server | Server manages lifecycle |

**TerrainCellGrid sync routing:** `TerrainCellGrid` is a plain `MonoBehaviour` (not a `NetworkBehaviour`). All network sync is routed through `MapController` (which IS a `NetworkBehaviour`). MapController gains:
- `SendTerrainGridClientRpc(TerrainCellSaveData[] cells)` — full grid on initial load / late joiners
- `SendDirtyCellsClientRpc(int[] cellIndices, TerrainCellSaveData[] cellData)` — incremental updates at tick intervals (only changed cells)

### 8.2 Client-Side Resolution

| Data | Resolution |
|------|-----------|
| Footstep audio | Client reads TerrainType at position + equipment locally. No RPC needed. |
| Weather visuals (rain/snow particles) | Client checks WeatherFront positions (synced) and renders locally. |
| Shadow rendering | Client renders shadow projectors based on synced WeatherFront positions. |
| Terrain visual overlays | Client reads cell state from server snapshots. |

### 8.3 TerrainCellGrid Sync Strategy

Full per-cell sync is too expensive. Instead:
- **Initial load:** Server sends full grid state when player enters a MapController (similar to how buildings are loaded)
- **Updates:** Server sends dirty-cell updates via ClientRpc at TerrainWeatherProcessor tick intervals (only changed cells)
- **Late joiners:** Receive full grid state on entering the map

---

## 9. Offline Simulation (MacroSimulator)

### 9.1 Execution Order (Updated)

```
1. Resource Pool Regeneration          (existing)
2. Terrain Cell Catch-Up               (NEW)
3. WeatherFront Position Catch-Up      (NEW - for BiomeRegion fronts)
4. Vegetation Growth Catch-Up          (NEW)
5. Inventory Yields                    (existing)
6. Needs Decay + Position Snap         (existing)
7. City Growth                         (existing)
```

### 9.2 Simplified Resource Yield (Design Note)

The existing MacroSimulator resource/yield math should be simplified. For offline catch-up, resource generation is reduced to:

```
For each map:
    harvestableCount = count of harvestable objects on this map (serialized)
    harvesterCount = count of NPCs with JobType.Harvester assigned to this map

    resourcesPerDay = harvestableCount * baseYieldPerHarvestable
                    + harvesterCount * baseYieldPerHarvester

    totalYield = resourcesPerDay * daysPassed
    → distribute into ResourcePools by biome weight
```

No per-NPC skill multipliers, no complex yield recipes — just **harvestable count + harvester count = resources/day**. This keeps offline math dead simple and predictable. Finer-grained yield logic (skill bonuses, tool quality) runs only during micro-simulation (map active).

### 9.3 Terrain Cell Offline Math

```
estimatedRainHours = hoursPassed * climateProfile.RainProbability
estimatedClearHours = hoursPassed * (1 - RainProb - SnowProb - CloudyProb)

For each cell:
    // Moisture
    cell.Moisture += estimatedRainHours * averageRainIntensity * rainRate
    cell.Moisture -= estimatedClearHours * evaporationRate
    cell.Moisture = clamp(0, 1)

    // Snow
    if estimatedSnowHours > 0 && ambientTempAvg < 2:
        cell.SnowDepth += estimatedSnowHours * snowRate
    cell.SnowDepth -= max(0, ambientTempAvg) * meltRate * hoursPassed
    cell.SnowDepth = clamp(0, 1)

    // Temperature reverts to ambient
    cell.Temperature = ambientTempAvg  // Just snap for offline

    // Evaluate transition rules
    EvaluateTransitions(cell, rules)
```

### 9.4 Vegetation Offline Math

```
For each fertile cell where GrowthTimer > 0 or wild growth enabled:
    avgMoisture = climateProfile.BaselineMoisture + (estimatedRainHours / hoursPassed * 0.3)

    if avgMoisture >= minimumMoistureForGrowth:
        cell.GrowthTimer += hoursPassed
        cell.TimeSinceLastWatered = 0
    else:
        cell.TimeSinceLastWatered += hoursPassed
        if cell.TimeSinceLastWatered > droughtDeathHours:
            cell.GrowthTimer = 0
            cell.PlantedCropId = null
```

---

## 10. Integration Points with Existing Systems

### 10.1 BiomeDefinition Extension

BiomeDefinition gains a reference to BiomeClimateProfile:

```csharp
// Added to BiomeDefinition.cs
[SerializeField] private BiomeClimateProfile _climateProfile;
public BiomeClimateProfile ClimateProfile => _climateProfile;
```

BiomeDefinition remains the resource/harvestable definition. BiomeClimateProfile is the weather/terrain definition. They are linked but separate SOs — one biome might share a climate profile with another.

### 10.2 CommunityTracker.PromoteToSettlement()

When a new MapController is created for a dynamically promoted settlement:

```csharp
// In PromoteToSettlement(), after creating the MapController:
var region = BiomeRegion.GetRegionAtPosition(worldPos);
if (region != null)
{
    newMapController.Biome = region.BiomeDefinition;
    // TerrainCellGrid initializes with BiomeClimateProfile.DefaultTerrainType as base
}
```

### 10.3 MapController Hibernation/WakeUp

```csharp
// In MapController.Hibernate():
if (_terrainCellGrid != null)
    _hibernationData.TerrainCells = _terrainCellGrid.SerializeCells();

// In MapController.WakeUp(), after MacroSimulator:
if (_terrainCellGrid != null && _hibernationData.TerrainCells != null)
    _terrainCellGrid.RestoreFromSaveData(_hibernationData.TerrainCells);
```

### 10.4 Harvestable System

Grown vegetation (trees, bushes) becomes `Harvestable` objects using the existing system:
- `HarvestableCategory.Plant` for bushes/greens
- `HarvestableCategory.Wood` for trees
- `Harvestable.RespawnDelayDays` is NOT used — respawn is handled by VegetationGrowthSystem restarting the growth cycle on the cell

### 10.5 SaveManager.ResetForNewSession()

If BiomeRegion uses any static registry or singleton, it must be cleaned up here alongside CommunityTracker and WorldOffsetAllocator.

### 10.6 GameLauncher Boot Sequence

Terrain cell restoration inserted between LoadWorldAsync and SpawnSavedBuildings:

```
LoadWorldAsync
    → BiomeRegions initialize, restore ISaveable state
    → TerrainCellGrid restore from MapSaveData
SpawnSavedBuildings
SpawnNPCsFromPendingSnapshot
```

### 10.7 ICharacterVisual.OnAnimationEvent

The existing animation event pipeline already anticipates footstep events (comment on line 29 of ICharacterVisual.cs: `// Animation events -> gameplay (footsteps, VFX triggers)`). FootstepAudioResolver subscribes to this with event name `"footstep"`.

### 10.8 WorldItem.OnCollisionEnter (Future)

Existing collision handler in WorldItem.cs is the hook point for material-based drop sounds. Out of scope for Phase 1 but the `ItemMaterial` field on ItemSO enables it.

---

## 11. Phase 1 Deliverable Scope

**Included:**
- Layer 1: TerrainType SO, TerrainCellGrid, TerrainPatch, TerrainCell struct
- Layer 2: GlobalWindController, BiomeRegion (with hibernation), BiomeClimateProfile, WeatherFront (Rain + Clear types only)
- Layer 3: TerrainWeatherProcessor (rain → moisture → transition rules)
- Layer 4: VegetationGrowthSystem (wild growth only)
- Layer 5: CharacterTerrainEffects (speed modifier, terrain detection), FootstepAudioResolver (framework), ItemMaterial on ItemSO, FootSurfaceType on CharacterArchetype
- WeatherFront shadow projection
- MacroSimulator integration for terrain cell catch-up
- MapSaveData extension for terrain serialization
- BiomeDefinition → BiomeClimateProfile linkage
- Room.FloorTerrainType + IsExposed for buildings

**Required documentation:**
- `.agent/skills/terrain-weather/SKILL.md` — full system documentation per CLAUDE.md rule 21
- `.agent/skills/character-terrain/SKILL.md` — CharacterTerrainEffects subsystem documentation
- Update `.agent/skills/world-system/SKILL.md` — BiomeRegion integration, updated MacroSimulator steps
- Update `.agent/skills/item-system/SKILL.md` (or equivalent) — ItemMaterial addition

**GameSpeedController compliance (CLAUDE.md rule 26):**
- `TerrainWeatherProcessor`, `VegetationGrowthSystem`, and `WeatherFront.ServerUpdate()` are simulation systems — they use `Time.deltaTime` (scaled by GameSpeedController)
- At Giga Speed, `TerrainWeatherProcessor` must use catch-up loops (`while ticksToProcess > 0`) to avoid skipping weather ticks
- `GlobalWindController` drift is simulation time. WeatherFront visual particles are cosmetic — can use unscaled time for smooth rendering during pause
- FootstepAudioResolver is triggered by animation events (already tied to simulation time via animation speed)

**Deferred to Phase 2+:**
- Snow and Ice terrain types (WeatherFront Snow type, freezing logic)
- Lava/Toxic terrain
- CropSystem (plowing, planting, crop definitions)
- Full footstep audio clip assignment (framework ships, clips assigned later)
- Impact/drop sounds using ItemMaterial
- Weather visual effects on character (rain on player, getting wet)
- Seasonal variation on GlobalWindController and BiomeClimateProfile
- Player wetness tracking
- Advanced wind effects (snow drift, directional rain visuals)

---

## 12. File Structure

```
Assets/Scripts/
├── Terrain/
│   ├── TerrainType.cs                    (ScriptableObject)
│   ├── TerrainTransitionRule.cs          (ScriptableObject)
│   ├── TerrainCellGrid.cs               (MonoBehaviour, on MapController)
│   ├── TerrainCell.cs                    (Struct)
│   ├── TerrainCellSaveData.cs           (Serializable struct)
│   ├── TerrainPatch.cs                  (MonoBehaviour, scene-placed)
│   ├── TerrainWeatherProcessor.cs       (MonoBehaviour, on MapController)
│   ├── VegetationGrowthSystem.cs        (MonoBehaviour, on MapController)
│   └── CropSystem.cs                    (MonoBehaviour, on MapController — Phase 2)
├── Weather/
│   ├── GlobalWindController.cs          (NetworkBehaviour, singleton)
│   ├── BiomeClimateProfile.cs           (ScriptableObject)
│   ├── BiomeRegion.cs                   (MonoBehaviour + ISaveable, scene-placed)
│   ├── WeatherFront.cs                  (NetworkBehaviour, spawned by BiomeRegion)
│   ├── WeatherType.cs                   (Enum)
│   └── WeatherFrontSnapshot.cs          (Serializable struct)
├── Character/
│   ├── CharacterTerrain/
│   │   ├── CharacterTerrainEffects.cs   (CharacterSystem subsystem)
│   │   └── FootstepAudioResolver.cs     (MonoBehaviour)
│   └── Archetype/
│       └── FootSurfaceType.cs           (Enum)
├── Items/
│   └── ItemMaterial.cs                  (Enum)
└── Audio/
    └── FootstepAudioProfile.cs          (ScriptableObject)

Assets/Resources/Data/
├── Terrain/
│   ├── TerrainTypes/                    (TerrainType SO assets)
│   ├── TransitionRules/                 (TerrainTransitionRule SO assets)
│   ├── ClimateProfiles/                 (BiomeClimateProfile SO assets)
│   └── CropDefinitions/                 (Phase 2)
└── Audio/
    └── FootstepProfiles/                (FootstepAudioProfile SO assets)
```
