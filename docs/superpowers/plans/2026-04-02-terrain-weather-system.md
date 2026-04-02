# Terrain & Weather System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a layered terrain & weather system with terrain cell grids, weather fronts, vegetation growth, footstep audio, and offline simulation.

**Architecture:** 5-layer bottom-up system — Layer 1 (Terrain Foundation) → Layer 2 (Weather & Atmosphere) → Layer 3 (Weather↔Terrain) → Layer 4 (Vegetation) → Layer 5 (Character Effects). Each layer depends only on layers below it.

**Tech Stack:** Unity 2022+, Netcode for GameObjects (NGO), ScriptableObjects for definitions, NetworkVariables for sync.

**Spec:** `docs/superpowers/specs/2026-04-02-terrain-weather-system-design.md`

---

## File Map

### New Files (Create)

| File | Responsibility |
|------|---------------|
| `Assets/Scripts/Terrain/TerrainType.cs` | ScriptableObject — terrain type definition (speed, damage, slip, growth) |
| `Assets/Scripts/Terrain/TerrainTransitionRule.cs` | ScriptableObject — conditions for terrain type transitions |
| `Assets/Scripts/Terrain/TerrainTypeRegistry.cs` | Static dictionary — O(1) lookup of TerrainType by TypeId |
| `Assets/Scripts/Terrain/TerrainCell.cs` | Struct — per-cell runtime data (moisture, temp, snow, growth) |
| `Assets/Scripts/Terrain/TerrainCellSaveData.cs` | Serializable struct — cell data for hibernation/save |
| `Assets/Scripts/Terrain/TerrainCellGrid.cs` | MonoBehaviour on MapController — flat array of cells, world↔grid conversion |
| `Assets/Scripts/Terrain/TerrainPatch.cs` | MonoBehaviour — scene-placed collider defining base terrain type for a region |
| `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs` | MonoBehaviour on MapController — applies weather effects to cells each tick |
| `Assets/Scripts/Terrain/VegetationGrowthSystem.cs` | MonoBehaviour on MapController — wild plant growth on fertile cells |
| `Assets/Scripts/Weather/WeatherType.cs` | Enum — Clear, Cloudy, Rain, Snow |
| `Assets/Scripts/Weather/WeatherFrontSnapshot.cs` | Serializable struct — hibernated weather front data |
| `Assets/Scripts/Weather/GlobalWindController.cs` | NetworkBehaviour singleton — world-level wind direction + strength |
| `Assets/Scripts/Weather/BiomeClimateProfile.cs` | ScriptableObject — climate params per biome (temp, precipitation, moisture) |
| `Assets/Scripts/Weather/BiomeRegion.cs` | MonoBehaviour + ISaveable — biome zone on world map, spawns weather fronts |
| `Assets/Scripts/Weather/WeatherFront.cs` | NetworkBehaviour — moving weather entity with shadow + particles |
| `Assets/Scripts/Items/ItemMaterial.cs` | Enum — physical material for items (Leather, Iron, Steel, etc.) |
| `Assets/Scripts/Character/Archetype/FootSurfaceType.cs` | Enum — creature foot types (BareSkin, Hooves, Padded, etc.) |
| `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs` | CharacterSystem subsystem — reads terrain under feet, applies effects |
| `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs` | MonoBehaviour — resolves terrain × foot material → audio clip |
| `Assets/Scripts/Audio/FootstepAudioProfile.cs` | ScriptableObject — lookup table of material→clips per terrain |

### Existing Files (Modify)

| File | Changes |
|------|---------|
| `Assets/Resources/Data/Item/ItemSO.cs` | Add `_material` field (ItemMaterial enum) |
| `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` | Add `_defaultFootSurface` field (FootSurfaceType) |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | Add `GetFootMaterial()` method |
| `Assets/Scripts/World/Data/BiomeDefinition.cs` | Add `_climateProfile` reference (BiomeClimateProfile) |
| `Assets/Scripts/World/MapSystem/MapSaveData.cs` | Add `TerrainCells` array + `TerrainLastUpdateTime` |
| `Assets/Scripts/World/MapSystem/MapController.cs` | Add terrain grid serialization in Hibernate/WakeUp, add ClientRpcs for grid sync |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | Add `SimulateTerrainCatchUp()` + `SimulateVegetationCatchUp()` methods, simplify resource yield |
| `Assets/Scripts/World/Buildings/Rooms/Room.cs` | Add `_floorTerrainType` + `_isExposed` fields |
| `Assets/Scripts/Character/Character.cs` | Add `CharacterTerrainEffects` reference + auto-assign in Awake |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | Add BiomeRegion/TerrainTypeRegistry cleanup in `ResetForNewSession()` |

---

## Task Breakdown

### Task 1: Terrain Data Types (Layer 1 — Foundation Data)

**Files:**
- Create: `Assets/Scripts/Terrain/TerrainCell.cs`
- Create: `Assets/Scripts/Terrain/TerrainCellSaveData.cs`
- Create: `Assets/Scripts/Weather/WeatherType.cs`
- Create: `Assets/Scripts/Weather/WeatherFrontSnapshot.cs`
- Create: `Assets/Scripts/Items/ItemMaterial.cs`
- Create: `Assets/Scripts/Character/Archetype/FootSurfaceType.cs`

These are pure data types with no dependencies — structs and enums only.

- [ ] **Step 1: Create WeatherType enum**

```csharp
// Assets/Scripts/Weather/WeatherType.cs
namespace MWI.Weather
{
    public enum WeatherType : byte
    {
        Clear,
        Cloudy,
        Rain,
        Snow
    }
}
```

- [ ] **Step 2: Create ItemMaterial enum**

```csharp
// Assets/Scripts/Items/ItemMaterial.cs
public enum ItemMaterial : byte
{
    None = 0,
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

- [ ] **Step 3: Create FootSurfaceType enum**

```csharp
// Assets/Scripts/Character/Archetype/FootSurfaceType.cs
public enum FootSurfaceType : byte
{
    BareSkin = 0,
    Hooves,
    Padded,
    Clawed,
    Scaled
}
```

- [ ] **Step 4: Create TerrainCell struct**

```csharp
// Assets/Scripts/Terrain/TerrainCell.cs
using System;

namespace MWI.Terrain
{
    [Serializable]
    public struct TerrainCell
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

        public TerrainType GetBaseType() => TerrainTypeRegistry.Get(BaseTypeId);
        public TerrainType GetCurrentType() => TerrainTypeRegistry.Get(CurrentTypeId);
    }
}
```

- [ ] **Step 5: Create TerrainCellSaveData struct**

```csharp
// Assets/Scripts/Terrain/TerrainCellSaveData.cs
using System;

namespace MWI.Terrain
{
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

        public static TerrainCellSaveData FromCell(TerrainCell cell)
        {
            return new TerrainCellSaveData
            {
                BaseTypeId = cell.BaseTypeId,
                CurrentTypeId = cell.CurrentTypeId,
                Moisture = cell.Moisture,
                Temperature = cell.Temperature,
                SnowDepth = cell.SnowDepth,
                Fertility = cell.Fertility,
                IsPlowed = cell.IsPlowed,
                PlantedCropId = cell.PlantedCropId,
                GrowthTimer = cell.GrowthTimer,
                TimeSinceLastWatered = cell.TimeSinceLastWatered
            };
        }

        public TerrainCell ToCell()
        {
            return new TerrainCell
            {
                BaseTypeId = BaseTypeId,
                CurrentTypeId = CurrentTypeId,
                Moisture = Moisture,
                Temperature = Temperature,
                SnowDepth = SnowDepth,
                Fertility = Fertility,
                IsPlowed = IsPlowed,
                PlantedCropId = PlantedCropId,
                GrowthTimer = GrowthTimer,
                TimeSinceLastWatered = TimeSinceLastWatered
            };
        }
    }
}
```

- [ ] **Step 6: Create WeatherFrontSnapshot struct**

```csharp
// Assets/Scripts/Weather/WeatherFrontSnapshot.cs
using System;
using UnityEngine;
using MWI.Weather;

namespace MWI.Weather
{
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
}
```

- [ ] **Step 7: Verify compilation**

Run: Unity Editor → check Console for errors in the new files.
Expected: 0 errors. `TerrainCell.GetBaseType()` and `GetCurrentType()` will show a warning about missing `TerrainTypeRegistry` — that's expected, it's created in Task 2.

- [ ] **Step 8: Commit**

```
git add Assets/Scripts/Terrain/TerrainCell.cs Assets/Scripts/Terrain/TerrainCellSaveData.cs Assets/Scripts/Weather/WeatherType.cs Assets/Scripts/Weather/WeatherFrontSnapshot.cs Assets/Scripts/Items/ItemMaterial.cs Assets/Scripts/Character/Archetype/FootSurfaceType.cs
git commit -m "feat(terrain): add foundation data types — enums, structs, save data"
```

---

### Task 2: TerrainType SO + Registry + TransitionRule (Layer 1)

**Files:**
- Create: `Assets/Scripts/Terrain/TerrainType.cs`
- Create: `Assets/Scripts/Terrain/TerrainTypeRegistry.cs`
- Create: `Assets/Scripts/Terrain/TerrainTransitionRule.cs`

**Docs to check:**
- Spec Section 3.1 (TerrainType fields)
- Spec Section 3.2 (TerrainTransitionRule)
- `Assets/Scripts/Item/Equipment/EnumEquipment.cs` — for existing `DamageType` enum values

- [ ] **Step 1: Create TerrainType ScriptableObject**

```csharp
// Assets/Scripts/Terrain/TerrainType.cs
using UnityEngine;

namespace MWI.Terrain
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Terrain Type")]
    public class TerrainType : ScriptableObject
    {
        [Header("Identity")]
        public string TypeId;
        public string DisplayName;
        public Color DebugColor = Color.white;

        [Header("Character Effects")]
        public float SpeedMultiplier = 1f;
        public float DamagePerSecond = 0f;
        public float SlipFactor = 0f;
        public DamageType DamageType;
        public bool HasDamage => DamagePerSecond > 0f;

        [Header("Growth")]
        public bool CanGrowVegetation;

        [Header("Audio")]
        public FootstepAudioProfile FootstepProfile;

        [Header("Visuals")]
        public Material GroundOverlayMaterial;
        [Range(0f, 1f)] public float OverlayOpacityAtFullSaturation = 0.8f;
    }
}
```

Note: `FootstepAudioProfile` will be created in Task 9. For now, the field will be null — this is fine, it's a forward reference on a SO.

- [ ] **Step 2: Create TerrainTypeRegistry**

```csharp
// Assets/Scripts/Terrain/TerrainTypeRegistry.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWI.Terrain
{
    public static class TerrainTypeRegistry
    {
        private static Dictionary<string, TerrainType> _types;

        public static void Initialize()
        {
            _types = Resources.LoadAll<TerrainType>("Data/Terrain/TerrainTypes")
                .ToDictionary(t => t.TypeId);
            Debug.Log($"[TerrainTypeRegistry] Initialized with {_types.Count} terrain types.");
        }

        public static TerrainType Get(string typeId)
        {
            if (_types == null)
            {
                Debug.LogError("[TerrainTypeRegistry] Not initialized. Call Initialize() first.");
                return null;
            }
            return _types.TryGetValue(typeId, out var t) ? t : null;
        }

        public static void Clear()
        {
            _types?.Clear();
            _types = null;
        }
    }
}
```

- [ ] **Step 3: Create TerrainTransitionRule**

```csharp
// Assets/Scripts/Terrain/TerrainTransitionRule.cs
using UnityEngine;

namespace MWI.Terrain
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Transition Rule")]
    public class TerrainTransitionRule : ScriptableObject
    {
        public TerrainType SourceType;
        public TerrainType ResultType;
        [SerializeField] private int _priority = 0;

        [Header("Conditions (ALL must be met)")]
        [Tooltip("-1 = don't check")]
        public float MinMoisture = -1f;
        public float MaxMoisture = -1f;
        [Tooltip("-999 = don't check")]
        public float MinTemperature = -999f;
        public float MaxTemperature = 999f;
        public float MinSnowDepth = -1f;

        public int Priority => _priority;

        public bool Evaluate(float moisture, float temperature, float snowDepth)
        {
            if (MinMoisture >= 0 && moisture < MinMoisture) return false;
            if (MaxMoisture >= 0 && moisture > MaxMoisture) return false;
            if (temperature < MinTemperature || temperature > MaxTemperature) return false;
            if (MinSnowDepth >= 0 && snowDepth < MinSnowDepth) return false;
            return true;
        }
    }
}
```

- [ ] **Step 4: Create TerrainType SO assets in Unity**

Use MCP `assets-create-folder` to create `Assets/Resources/Data/Terrain/TerrainTypes/` and then create at least 3 initial TerrainType assets via Unity Editor:
- **Fertile** — SpeedMultiplier=1, DamagePerSecond=0, SlipFactor=0, CanGrowVegetation=true
- **Dirt** — SpeedMultiplier=1, DamagePerSecond=0, SlipFactor=0, CanGrowVegetation=false
- **Mud** — SpeedMultiplier=0.6, DamagePerSecond=0, SlipFactor=0.3, CanGrowVegetation=false

- [ ] **Step 5: Create initial TransitionRule SO assets**

Create `Assets/Resources/Data/Terrain/TransitionRules/` and one test rule:
- **DirtToMud** — SourceType=Dirt, ResultType=Mud, MinMoisture=0.7

- [ ] **Step 6: Verify compilation + assets load**

Open Unity, check Console for errors. Verify SO assets appear in Project window under `Resources/Data/Terrain/`.

- [ ] **Step 7: Commit**

```
git add Assets/Scripts/Terrain/TerrainType.cs Assets/Scripts/Terrain/TerrainTypeRegistry.cs Assets/Scripts/Terrain/TerrainTransitionRule.cs
git commit -m "feat(terrain): add TerrainType SO, TerrainTypeRegistry, and TerrainTransitionRule"
```

---

### Task 3: TerrainPatch + TerrainCellGrid (Layer 1)

**Files:**
- Create: `Assets/Scripts/Terrain/TerrainPatch.cs`
- Create: `Assets/Scripts/Terrain/TerrainCellGrid.cs`

**Docs to check:**
- Spec Section 3.3 (TerrainCellGrid — cell size, bounds derivation, API)
- Spec Section 3.4 (TerrainPatch — priority, overlap resolution)
- `MapController.cs:54` — `_mapTrigger` BoxCollider for bounds

- [ ] **Step 1: Create TerrainPatch**

```csharp
// Assets/Scripts/Terrain/TerrainPatch.cs
using UnityEngine;

namespace MWI.Terrain
{
    [RequireComponent(typeof(BoxCollider))]
    public class TerrainPatch : MonoBehaviour
    {
        [SerializeField] private TerrainType _baseTerrainType;
        [SerializeField] private float _baseFertility = 0.5f;
        [SerializeField] private int _priority = 0;

        public TerrainType BaseTerrainType => _baseTerrainType;
        public float BaseFertility => _baseFertility;
        public int Priority => _priority;

        private BoxCollider _collider;

        public Bounds Bounds
        {
            get
            {
                if (_collider == null) _collider = GetComponent<BoxCollider>();
                return _collider.bounds;
            }
        }

        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            _collider.isTrigger = true;
        }
    }
}
```

- [ ] **Step 2: Create TerrainCellGrid**

```csharp
// Assets/Scripts/Terrain/TerrainCellGrid.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWI.Terrain
{
    public class TerrainCellGrid : MonoBehaviour
    {
        [SerializeField] private float _cellSize = 4f;

        private TerrainCell[] _cells;
        private int _width;
        private int _depth;
        private Vector3 _origin;

        public int Width => _width;
        public int Depth => _depth;
        public float CellSize => _cellSize;
        public int CellCount => _cells?.Length ?? 0;

        // --- Initialization ---

        public void Initialize(Bounds mapBounds)
        {
            _origin = new Vector3(mapBounds.min.x, 0f, mapBounds.min.z);
            _width = Mathf.CeilToInt(mapBounds.size.x / _cellSize);
            _depth = Mathf.CeilToInt(mapBounds.size.z / _cellSize);
            _cells = new TerrainCell[_width * _depth];

            Debug.Log($"[TerrainCellGrid] Initialized {_width}x{_depth} = {_cells.Length} cells " +
                      $"(cellSize={_cellSize}, origin={_origin})");
        }

        public void InitializeFromPatches(List<TerrainPatch> patches)
        {
            if (_cells == null)
            {
                Debug.LogError("[TerrainCellGrid] Call Initialize() before InitializeFromPatches().");
                return;
            }

            // Sort patches by priority (lowest first, so highest overwrites)
            var sorted = patches.OrderBy(p => p.Priority).ToList();

            for (int z = 0; z < _depth; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    Vector3 worldPos = GridToWorld(x, z);
                    TerrainPatch bestPatch = null;

                    foreach (var patch in sorted)
                    {
                        if (patch.Bounds.Contains(worldPos))
                            bestPatch = patch; // Higher priority overwrites
                    }

                    int idx = z * _width + x;
                    if (bestPatch != null)
                    {
                        _cells[idx].BaseTypeId = bestPatch.BaseTerrainType.TypeId;
                        _cells[idx].CurrentTypeId = bestPatch.BaseTerrainType.TypeId;
                        _cells[idx].Fertility = bestPatch.BaseTerrainType.CanGrowVegetation
                            ? bestPatch.BaseFertility : 0f;
                    }
                }
            }
        }

        // --- Queries ---

        public TerrainType GetTerrainAt(Vector3 worldPos)
        {
            if (!WorldToGrid(worldPos, out int x, out int z)) return null;
            return _cells[z * _width + x].GetCurrentType();
        }

        public ref TerrainCell GetCellRef(int x, int z)
        {
            return ref _cells[z * _width + x];
        }

        public TerrainCell GetCellAt(Vector3 worldPos)
        {
            if (!WorldToGrid(worldPos, out int x, out int z)) return default;
            return _cells[z * _width + x];
        }

        // --- Coordinate conversion ---

        public bool WorldToGrid(Vector3 worldPos, out int x, out int z)
        {
            x = Mathf.FloorToInt((worldPos.x - _origin.x) / _cellSize);
            z = Mathf.FloorToInt((worldPos.z - _origin.z) / _cellSize);
            bool inBounds = x >= 0 && x < _width && z >= 0 && z < _depth;
            if (!inBounds) { x = -1; z = -1; }
            return inBounds;
        }

        public Vector3 GridToWorld(int x, int z)
        {
            return new Vector3(
                _origin.x + (x + 0.5f) * _cellSize,
                _origin.y,
                _origin.z + (z + 0.5f) * _cellSize
            );
        }

        // --- Serialization ---

        public TerrainCellSaveData[] SerializeCells()
        {
            if (_cells == null) return null;
            var data = new TerrainCellSaveData[_cells.Length];
            for (int i = 0; i < _cells.Length; i++)
                data[i] = TerrainCellSaveData.FromCell(_cells[i]);
            return data;
        }

        public void RestoreFromSaveData(TerrainCellSaveData[] data)
        {
            if (data == null || _cells == null) return;
            int count = Mathf.Min(data.Length, _cells.Length);
            for (int i = 0; i < count; i++)
                _cells[i] = data[i].ToCell();
        }

        // --- Grid iteration helpers (for TerrainWeatherProcessor) ---

        public void GetCellRangeForBounds(Bounds worldBounds, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            WorldToGrid(worldBounds.min, out minX, out minZ);
            WorldToGrid(worldBounds.max, out maxX, out maxZ);
            minX = Mathf.Max(0, minX);
            minZ = Mathf.Max(0, minZ);
            maxX = Mathf.Min(_width - 1, maxX);
            maxZ = Mathf.Min(_depth - 1, maxZ);
        }
    }
}
```

- [ ] **Step 3: Verify compilation**

Expected: 0 errors. TerrainType forward reference to `FootstepAudioProfile` may warn — that's fine.

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/Terrain/TerrainPatch.cs Assets/Scripts/Terrain/TerrainCellGrid.cs
git commit -m "feat(terrain): add TerrainPatch and TerrainCellGrid with coordinate conversion"
```

---

### Task 4: Existing File Modifications — ItemSO, CharacterArchetype, Room, BiomeDefinition, MapSaveData

**Files:**
- Modify: `Assets/Resources/Data/Item/ItemSO.cs` — add `_material` field
- Modify: `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` — add `_defaultFootSurface`
- Modify: `Assets/Scripts/World/Buildings/Rooms/Room.cs` — add `_floorTerrainType` + `_isExposed`
- Modify: `Assets/Scripts/World/Data/BiomeDefinition.cs` — add `_climateProfile` reference
- Modify: `Assets/Scripts/World/MapSystem/MapSaveData.cs` — add terrain cell data

**Docs to check:**
- Spec Section 7.1 (ItemMaterial on ItemSO)
- Spec Section 7.2 (FootSurfaceType on CharacterArchetype)
- Spec Section 3.5 (Room floor type)
- Spec Section 10.1 (BiomeDefinition extension)

- [ ] **Step 1: Add `_material` to ItemSO**

Read `Assets/Resources/Data/Item/ItemSO.cs` first. Add the field near `_weight` (line ~16):

```csharp
[SerializeField] private ItemMaterial _material = ItemMaterial.None;
public ItemMaterial Material => _material;
```

- [ ] **Step 2: Add `_defaultFootSurface` to CharacterArchetype**

Read `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` first. Add near `_bodyType`:

```csharp
[SerializeField] private FootSurfaceType _defaultFootSurface = FootSurfaceType.BareSkin;
public FootSurfaceType DefaultFootSurface => _defaultFootSurface;
```

- [ ] **Step 3: Add floor terrain type to Room**

Read `Assets/Scripts/World/Buildings/Rooms/Room.cs` first. Add fields:

```csharp
[Header("Terrain")]
[SerializeField] private TerrainType _floorTerrainType;
[SerializeField] private bool _isExposed;

public TerrainType FloorTerrainType => _floorTerrainType;
public bool IsExposed => _isExposed;
```

Add `using MWI.Terrain;` at the top.

- [ ] **Step 4: Add ClimateProfile to BiomeDefinition**

Read `Assets/Scripts/World/Data/BiomeDefinition.cs` first. Add:

```csharp
[SerializeField] private BiomeClimateProfile _climateProfile;
public BiomeClimateProfile ClimateProfile => _climateProfile;
```

Add `using MWI.Weather;` at the top. Note: `BiomeClimateProfile` is created in Task 5 — this will temporarily show an error until Task 5 is done. If implementing sequentially, create a stub first or implement Task 5 first.

- [ ] **Step 5: Extend MapSaveData**

Read `Assets/Scripts/World/MapSystem/MapSaveData.cs` first. Add fields:

```csharp
using MWI.Terrain;

// Inside MapSaveData class, after existing fields:
public TerrainCellSaveData[] TerrainCells;
public double TerrainLastUpdateTime;
```

- [ ] **Step 6: Add `GetFootMaterial()` to CharacterEquipment**

Read `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` first. Add method:

```csharp
public ItemMaterial GetFootMaterial()
{
    var boots = armorLayer?.GetInstance(WearableType.Boots)
             ?? clothingLayer?.GetInstance(WearableType.Boots)
             ?? underwearLayer?.GetInstance(WearableType.Boots);

    if (boots != null)
        return boots.ItemSO.Material;

    return ItemMaterial.None;
}
```

- [ ] **Step 7: Verify compilation**

Check Unity Console. If BiomeClimateProfile doesn't exist yet (created in Task 5), the BiomeDefinition change will error. In that case, comment it out temporarily or implement Task 5 first.

- [ ] **Step 8: Commit**

```
git add Assets/Resources/Data/Item/ItemSO.cs Assets/Scripts/Character/Archetype/CharacterArchetype.cs Assets/Scripts/World/Buildings/Rooms/Room.cs Assets/Scripts/World/Data/BiomeDefinition.cs Assets/Scripts/World/MapSystem/MapSaveData.cs Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat(terrain): add material/terrain fields to ItemSO, CharacterArchetype, Room, BiomeDefinition, MapSaveData, CharacterEquipment"
```

---

### Task 5: BiomeClimateProfile + GlobalWindController (Layer 2 — Definitions)

**Files:**
- Create: `Assets/Scripts/Weather/BiomeClimateProfile.cs`
- Create: `Assets/Scripts/Weather/GlobalWindController.cs`

**Docs to check:**
- Spec Section 4.1 (GlobalWindController)
- Spec Section 4.2 (BiomeClimateProfile)
- CLAUDE.md Rule 26 — GameSpeedController compliance

- [ ] **Step 1: Create BiomeClimateProfile**

```csharp
// Assets/Scripts/Weather/BiomeClimateProfile.cs
using UnityEngine;
using MWI.Terrain;

namespace MWI.Weather
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Biome Climate Profile")]
    public class BiomeClimateProfile : ScriptableObject
    {
        [Header("Temperature")]
        public float AmbientTemperatureMin = 5f;
        public float AmbientTemperatureMax = 25f;
        public AnimationCurve TemperatureOverDay = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Precipitation")]
        [Range(0f, 1f)] public float RainProbability = 0.3f;
        [Range(0f, 1f)] public float SnowProbability = 0.1f;
        [Range(0f, 1f)] public float CloudyProbability = 0.3f;
        public float FrontSpawnIntervalMinHours = 2f;
        public float FrontSpawnIntervalMaxHours = 8f;

        [Header("Front Properties")]
        public float FrontRadiusMin = 30f;
        public float FrontRadiusMax = 80f;
        public float FrontIntensityMin = 0.3f;
        public float FrontIntensityMax = 1.0f;
        public float FrontLifetimeMinHours = 1f;
        public float FrontLifetimeMaxHours = 6f;

        [Header("Moisture")]
        public float BaselineMoisture = 0.3f;
        public float EvaporationRate = 0.05f;

        [Header("Default Terrain")]
        public TerrainType DefaultTerrainType;
        public TerrainType DefaultFloorOnSettlement;

        public float GetAmbientTemperature(float time01)
        {
            float t = TemperatureOverDay.Evaluate(time01);
            return Mathf.Lerp(AmbientTemperatureMin, AmbientTemperatureMax, t);
        }

        private void OnValidate()
        {
            float sum = RainProbability + SnowProbability + CloudyProbability;
            if (sum > 1f)
            {
                float scale = 1f / sum;
                RainProbability *= scale;
                SnowProbability *= scale;
                CloudyProbability *= scale;
            }
        }
    }
}
```

- [ ] **Step 2: Create GlobalWindController**

```csharp
// Assets/Scripts/Weather/GlobalWindController.cs
using System;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Weather
{
    public class GlobalWindController : NetworkBehaviour
    {
        public static GlobalWindController Instance { get; private set; }

        public NetworkVariable<Vector2> WindDirection = new(Vector2.right);
        public NetworkVariable<float> WindStrength = new(0.3f);

        [Header("Drift Settings")]
        [SerializeField] private float _driftSpeed = 0.01f;
        [SerializeField] private float _gustFrequency = 0.1f;
        [SerializeField] private float _maxGustStrength = 0.2f;

        public event Action<Vector2, float> OnWindChanged;

        private float _driftAngle;
        private float _gustTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            WindDirection.OnValueChanged += (_, _) => OnWindChanged?.Invoke(WindDirection.Value, WindStrength.Value);
            WindStrength.OnValueChanged += (_, _) => OnWindChanged?.Invoke(WindDirection.Value, WindStrength.Value);

            if (IsServer)
            {
                _driftAngle = UnityEngine.Random.Range(0f, 360f);
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            // Gradual wind direction drift (simulation time)
            _driftAngle += _driftSpeed * Time.deltaTime * 10f;
            float rad = _driftAngle * Mathf.Deg2Rad;
            WindDirection.Value = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            // Random gusts
            _gustTimer -= Time.deltaTime;
            if (_gustTimer <= 0f)
            {
                _gustTimer = 1f / Mathf.Max(0.01f, _gustFrequency);
                float gust = UnityEngine.Random.Range(0f, _maxGustStrength);
                WindStrength.Value = Mathf.Clamp01(0.3f + gust);
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
```

- [ ] **Step 3: Create initial BiomeClimateProfile asset**

Use MCP to create `Assets/Resources/Data/Terrain/ClimateProfiles/` folder and a "Temperate" profile asset via Unity Editor.

- [ ] **Step 4: Verify compilation**

Now BiomeDefinition's `_climateProfile` reference should compile. Check Unity Console.

- [ ] **Step 5: Commit**

```
git add Assets/Scripts/Weather/BiomeClimateProfile.cs Assets/Scripts/Weather/GlobalWindController.cs
git commit -m "feat(weather): add BiomeClimateProfile SO and GlobalWindController singleton"
```

---

### Task 6: BiomeRegion (Layer 2 — World Map Entity)

**Files:**
- Create: `Assets/Scripts/Weather/BiomeRegion.cs`

**Docs to check:**
- Spec Section 4.3 (BiomeRegion — lifecycle, ISaveable, static registry)
- `Assets/Scripts/Core/SaveLoad/ISaveable.cs` — interface uses `SaveKey`, `CaptureState()`, `RestoreState(object)`
- `Assets/Scripts/Core/SaveLoad/SaveManager.cs:105` — `ResetForNewSession()`

- [ ] **Step 1: Create BiomeRegion**

```csharp
// Assets/Scripts/Weather/BiomeRegion.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.WorldSystem;
using MWI.Terrain;

namespace MWI.Weather
{
    [RequireComponent(typeof(BoxCollider))]
    public class BiomeRegion : MonoBehaviour, ISaveable
    {
        [SerializeField] private string _regionId;
        [SerializeField] private BiomeDefinition _biomeDefinition;
        [SerializeField] private BiomeClimateProfile _climateProfile;

        private List<WeatherFront> _activeFronts = new();
        private List<WeatherFrontSnapshot> _hibernatedFronts = new();
        private bool _isHibernating = true;
        private float _nextSpawnTimer;
        private BoxCollider _bounds;
        private double _lastHibernationTime;

        // --- Static Registry ---
        private static List<BiomeRegion> _allRegions = new();

        public static BiomeRegion GetRegionAtPosition(Vector3 worldPos)
        {
            foreach (var region in _allRegions)
            {
                if (region._bounds != null && region._bounds.bounds.Contains(worldPos))
                    return region;
            }
            return null;
        }

        public static List<BiomeRegion> GetAdjacentRegions(BiomeRegion region)
        {
            var result = new List<BiomeRegion>();
            if (region._bounds == null) return result;

            var expanded = region._bounds.bounds;
            expanded.Expand(10f); // Small margin for adjacency detection

            foreach (var other in _allRegions)
            {
                if (other == region) continue;
                if (other._bounds != null && expanded.Intersects(other._bounds.bounds))
                    result.Add(other);
            }
            return result;
        }

        // --- Public API ---
        public string RegionId => _regionId;
        public bool IsHibernating => _isHibernating;
        public BiomeClimateProfile ClimateProfile => _climateProfile;
        public BiomeDefinition BiomeDefinition => _biomeDefinition;
        public List<WeatherFront> ActiveFronts => _activeFronts;

        public float GetAmbientTemperature()
        {
            float time01 = TimeManager.Instance != null ? TimeManager.Instance.CurrentTime01 : 0.5f;
            return _climateProfile.GetAmbientTemperature(time01);
        }

        public TerrainType GetDefaultTerrainType()
        {
            return _climateProfile != null ? _climateProfile.DefaultTerrainType : null;
        }

        public List<WeatherFront> GetFrontsOverlapping(Bounds area)
        {
            var result = new List<WeatherFront>();
            foreach (var front in _activeFronts)
            {
                if (front == null) continue;
                float dist = Vector3.Distance(front.transform.position, area.center);
                if (dist < front.Radius.Value + area.extents.magnitude)
                    result.Add(front);
            }
            return result;
        }

        // --- Lifecycle ---
        private void Awake()
        {
            _bounds = GetComponent<BoxCollider>();
            _bounds.isTrigger = true;
            _allRegions.Add(this);
        }

        private void OnDestroy()
        {
            _allRegions.Remove(this);
        }

        public void WakeUp(double currentTime)
        {
            if (!_isHibernating) return;
            _isHibernating = false;

            double elapsed = currentTime - _lastHibernationTime;
            // TODO: Fast-forward hibernated front positions, spawn new ones
            // For now, just spawn fresh fronts
            _hibernatedFronts.Clear();

            Debug.Log($"[BiomeRegion] '{_regionId}' woke up after {elapsed:F1} time units.");
        }

        public void Hibernate(double currentTime)
        {
            if (_isHibernating) return;
            _isHibernating = true;
            _lastHibernationTime = currentTime;

            // Serialize active fronts
            _hibernatedFronts.Clear();
            foreach (var front in _activeFronts)
            {
                if (front == null) continue;
                _hibernatedFronts.Add(new WeatherFrontSnapshot
                {
                    Type = front.Type.Value,
                    Position = front.transform.position,
                    LocalWindDirection = front.LocalWindDirection.Value,
                    LocalWindStrength = front.LocalWindStrength.Value,
                    Radius = front.Radius.Value,
                    Intensity = front.Intensity.Value,
                    TemperatureModifier = front.TemperatureModifier.Value,
                    RemainingLifetime = front.RemainingLifetime.Value
                });
                front.NetworkObject.Despawn(true);
            }
            _activeFronts.Clear();

            Debug.Log($"[BiomeRegion] '{_regionId}' hibernated. Serialized {_hibernatedFronts.Count} fronts.");
        }

        // --- ISaveable ---
        public string SaveKey => _regionId;

        public object CaptureState()
        {
            return new BiomeRegionSaveData
            {
                RegionId = _regionId,
                IsHibernating = _isHibernating,
                LastHibernationTime = _lastHibernationTime,
                HibernatedFronts = _hibernatedFronts.ToArray()
            };
        }

        public void RestoreState(object state)
        {
            if (state is BiomeRegionSaveData data)
            {
                _isHibernating = data.IsHibernating;
                _lastHibernationTime = data.LastHibernationTime;
                _hibernatedFronts = data.HibernatedFronts?.ToList() ?? new List<WeatherFrontSnapshot>();
            }
        }

        public static void ClearRegistry()
        {
            _allRegions.Clear();
        }
    }

    [Serializable]
    public class BiomeRegionSaveData
    {
        public string RegionId;
        public bool IsHibernating;
        public double LastHibernationTime;
        public WeatherFrontSnapshot[] HibernatedFronts;
    }
}
```

- [ ] **Step 2: Add BiomeRegion cleanup to SaveManager.ResetForNewSession()**

Read `Assets/Scripts/Core/SaveLoad/SaveManager.cs`. In `ResetForNewSession()`, add:

```csharp
using MWI.Weather;
using MWI.Terrain;

// Inside ResetForNewSession():
BiomeRegion.ClearRegistry();
TerrainTypeRegistry.Clear();
```

- [ ] **Step 3: Verify compilation**

Note: `TimeManager.Instance` reference — verify that `TimeManager` has a static `Instance` and `CurrentTime01`. `WeatherFront` doesn't exist yet so the `_activeFronts` list and related code referencing `front.Type.Value` etc. will fail. **Workaround:** Comment out WeatherFront-dependent code blocks temporarily, or create a stub `WeatherFront.cs` first (Task 7). Recommended: implement Task 7 immediately after.

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/Weather/BiomeRegion.cs Assets/Scripts/Core/SaveLoad/SaveManager.cs
git commit -m "feat(weather): add BiomeRegion with hibernation, ISaveable, and static registry"
```

---

### Task 7: WeatherFront (Layer 2 — Moving Weather Entity)

**Files:**
- Create: `Assets/Scripts/Weather/WeatherFront.cs`

**Docs to check:**
- Spec Section 4.4 (WeatherFront — NetworkVariables, movement, shadow, lifetime)
- CLAUDE.md Rule 18 — network architecture
- CLAUDE.md Rule 26 — GameSpeedController (simulation time for movement)

- [ ] **Step 1: Create WeatherFront**

```csharp
// Assets/Scripts/Weather/WeatherFront.cs
using Unity.Netcode;
using UnityEngine;

namespace MWI.Weather
{
    public class WeatherFront : NetworkBehaviour
    {
        [Header("State")]
        public NetworkVariable<WeatherType> Type = new();
        public NetworkVariable<Vector2> LocalWindDirection = new();
        public NetworkVariable<float> LocalWindStrength = new();
        public NetworkVariable<float> Radius = new(50f);
        public NetworkVariable<float> Intensity = new(0.5f);
        public NetworkVariable<float> TemperatureModifier = new();
        public NetworkVariable<float> RemainingLifetime = new();

        private BiomeRegion _parentRegion;

        public Vector2 ActualVelocity
        {
            get
            {
                var global = GlobalWindController.Instance;
                if (global == null) return LocalWindDirection.Value * LocalWindStrength.Value;
                return (global.WindDirection.Value * global.WindStrength.Value)
                     + (LocalWindDirection.Value * LocalWindStrength.Value);
            }
        }

        public void Initialize(BiomeRegion parent, WeatherType type, Vector3 spawnPos,
            Vector2 localWind, float localWindStrength, float radius, float intensity,
            float tempModifier, float lifetime)
        {
            _parentRegion = parent;
            transform.position = spawnPos;

            if (IsServer)
            {
                Type.Value = type;
                LocalWindDirection.Value = localWind;
                LocalWindStrength.Value = localWindStrength;
                Radius.Value = radius;
                Intensity.Value = intensity;
                TemperatureModifier.Value = tempModifier;
                RemainingLifetime.Value = lifetime;
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            // Move based on combined wind (simulation time)
            Vector2 vel = ActualVelocity;
            transform.position += new Vector3(vel.x, 0f, vel.y) * Time.deltaTime;

            // Decay lifetime
            RemainingLifetime.Value -= Time.deltaTime;
            if (RemainingLifetime.Value <= 0f)
            {
                _parentRegion?.OnFrontExpired(this);
                NetworkObject.Despawn(true);
                return;
            }

            // Check bounds — if exited parent region, despawn
            if (_parentRegion != null)
            {
                var bounds = _parentRegion.GetComponent<BoxCollider>().bounds;
                if (!bounds.Contains(transform.position))
                {
                    _parentRegion.OnFrontExpired(this);
                    NetworkObject.Despawn(true);
                }
            }
        }

        // Shadow opacity based on weather type
        public float GetShadowOpacity()
        {
            return Type.Value switch
            {
                WeatherType.Clear => 0f,
                WeatherType.Cloudy => 0.2f,
                WeatherType.Rain => 0.5f,
                WeatherType.Snow => 0.6f,
                _ => 0f
            };
        }
    }
}
```

- [ ] **Step 2: Add OnFrontExpired to BiomeRegion**

Add this method to `BiomeRegion.cs`:

```csharp
public void OnFrontExpired(WeatherFront front)
{
    _activeFronts.Remove(front);
}
```

- [ ] **Step 3: Verify compilation**

All Layer 2 classes should now compile. Check Unity Console.

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/Weather/WeatherFront.cs Assets/Scripts/Weather/BiomeRegion.cs
git commit -m "feat(weather): add WeatherFront NetworkBehaviour with movement and lifetime"
```

---

### Task 8: TerrainWeatherProcessor (Layer 3)

**Files:**
- Create: `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs`

**Docs to check:**
- Spec Section 5.1 (weather contribution, ambient revert, spatial culling, dirty cells)
- Spec Section 5.2 (MacroSimulator integration)
- CLAUDE.md Rule 26 — catch-up loops at Giga Speed

- [ ] **Step 1: Create TerrainWeatherProcessor**

```csharp
// Assets/Scripts/Terrain/TerrainWeatherProcessor.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Weather;

namespace MWI.Terrain
{
    public class TerrainWeatherProcessor : MonoBehaviour
    {
        [SerializeField] private float _tickIntervalGameMinutes = 2f;
        [SerializeField] private List<TerrainTransitionRule> _transitionRules;
        [SerializeField] private float _rainMoistureRate = 0.1f;
        [SerializeField] private float _snowAccumulationRate = 0.05f;

        private TerrainCellGrid _grid;
        private BiomeRegion _biomeRegion;
        private float _timeSinceLastTick;
        private HashSet<int> _dirtyCells = new();

        public event Action<int, int, TerrainType> OnCellTerrainChanged;

        public void Initialize(TerrainCellGrid grid, BiomeRegion region)
        {
            _grid = grid;
            _biomeRegion = region;
            if (_transitionRules != null)
                _transitionRules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        private void Update()
        {
            if (_grid == null || _biomeRegion == null) return;

            // Simulation time — accumulate and process in catch-up loops for Giga Speed
            _timeSinceLastTick += Time.deltaTime;

            float tickInterval = _tickIntervalGameMinutes * 60f; // Convert to game-seconds
            while (_timeSinceLastTick >= tickInterval)
            {
                _timeSinceLastTick -= tickInterval;
                ProcessTick(tickInterval);
            }
        }

        private void ProcessTick(float tickDelta)
        {
            var overlappingFronts = _biomeRegion.GetFrontsOverlapping(
                GetComponent<BoxCollider>()?.bounds ?? new Bounds());

            if (overlappingFronts.Count > 0)
                ProcessWeatherFronts(overlappingFronts, tickDelta);

            ProcessAmbientRevert(tickDelta);
            EvaluateTransitions();
        }

        private void ProcessWeatherFronts(List<WeatherFront> fronts, float tickDelta)
        {
            foreach (var front in fronts)
            {
                if (front == null) continue;

                // Compute bounding rect on grid for this front
                var frontBounds = new Bounds(front.transform.position,
                    Vector3.one * front.Radius.Value * 2f);
                _grid.GetCellRangeForBounds(frontBounds, out int minX, out int minZ, out int maxX, out int maxZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector3 cellWorld = _grid.GridToWorld(x, z);
                        float dist = Vector3.Distance(cellWorld, front.transform.position);
                        if (dist > front.Radius.Value) continue;

                        float falloff = 1f - (dist / front.Radius.Value);
                        Vector2 cellOffset = new Vector2(
                            cellWorld.x - front.transform.position.x,
                            cellWorld.z - front.transform.position.z).normalized;
                        float windBias = Vector2.Dot(front.ActualVelocity.normalized, cellOffset);
                        float contribution = falloff * (1f + windBias * 0.3f) * front.Intensity.Value;

                        ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                        int idx = z * _grid.Width + x;

                        if (front.Type.Value == WeatherType.Rain)
                        {
                            cell.Moisture = Mathf.Clamp01(cell.Moisture + contribution * _rainMoistureRate * tickDelta);
                            cell.TimeSinceLastWatered = 0f;
                        }
                        else if (front.Type.Value == WeatherType.Snow)
                        {
                            cell.SnowDepth = Mathf.Clamp01(cell.SnowDepth + contribution * _snowAccumulationRate * tickDelta);
                        }

                        cell.Temperature += front.TemperatureModifier.Value * falloff * tickDelta * 0.1f;
                        _dirtyCells.Add(idx);
                    }
                }
            }
        }

        private void ProcessAmbientRevert(float tickDelta)
        {
            if (_dirtyCells.Count == 0) return;

            var profile = _biomeRegion.ClimateProfile;
            float ambientTemp = _biomeRegion.GetAmbientTemperature();
            float windFactor = GlobalWindController.Instance != null
                ? 1f + GlobalWindController.Instance.WindStrength.Value
                : 1f;

            var toRemove = new List<int>();

            foreach (int idx in _dirtyCells)
            {
                int x = idx % _grid.Width;
                int z = idx / _grid.Width;
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);

                // Moisture drying
                cell.Moisture -= profile.EvaporationRate * windFactor * tickDelta * 0.01f;
                cell.Moisture = Mathf.MoveTowards(cell.Moisture, profile.BaselineMoisture, 0.001f * tickDelta);
                cell.Moisture = Mathf.Clamp01(cell.Moisture);

                // Temperature revert
                cell.Temperature = Mathf.MoveTowards(cell.Temperature, ambientTemp, 0.5f * tickDelta);

                // Snow melt
                if (cell.SnowDepth > 0f && cell.Temperature > 0f)
                    cell.SnowDepth = Mathf.Max(0f, cell.SnowDepth - 0.01f * cell.Temperature * tickDelta);

                // Drought tracking
                cell.TimeSinceLastWatered += tickDelta / 3600f; // Convert to game-hours

                // Check if cell is back to baseline
                bool atBaseline = Mathf.Approximately(cell.Moisture, profile.BaselineMoisture)
                    && Mathf.Approximately(cell.Temperature, ambientTemp)
                    && cell.SnowDepth <= 0f;
                if (atBaseline) toRemove.Add(idx);
            }

            foreach (int idx in toRemove)
                _dirtyCells.Remove(idx);
        }

        private void EvaluateTransitions()
        {
            if (_transitionRules == null) return;

            foreach (int idx in _dirtyCells)
            {
                int x = idx % _grid.Width;
                int z = idx / _grid.Width;
                ref TerrainCell cell = ref _grid.GetCellRef(x, z);

                string previousTypeId = cell.CurrentTypeId;
                bool matched = false;

                foreach (var rule in _transitionRules)
                {
                    if (rule.SourceType.TypeId != cell.CurrentTypeId
                        && rule.SourceType.TypeId != cell.BaseTypeId) continue;

                    if (rule.Evaluate(cell.Moisture, cell.Temperature, cell.SnowDepth))
                    {
                        cell.CurrentTypeId = rule.ResultType.TypeId;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                    cell.CurrentTypeId = cell.BaseTypeId;

                if (cell.CurrentTypeId != previousTypeId)
                {
                    var newType = TerrainTypeRegistry.Get(cell.CurrentTypeId);
                    OnCellTerrainChanged?.Invoke(x, z, newType);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Check Unity Console. Ensure `GetComponent<BoxCollider>()` usage aligns with MapController's collider.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Terrain/TerrainWeatherProcessor.cs
git commit -m "feat(terrain): add TerrainWeatherProcessor with spatial culling and transition rules"
```

---

### Task 9: FootstepAudioProfile + CharacterTerrainEffects + FootstepAudioResolver (Layer 5)

**Files:**
- Create: `Assets/Scripts/Audio/FootstepAudioProfile.cs`
- Create: `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs`
- Create: `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs`
- Modify: `Assets/Scripts/Character/Character.cs` — add CharacterTerrainEffects reference

**Docs to check:**
- Spec Section 7.3 (CharacterTerrainEffects — sources, server/client split)
- Spec Section 7.4 (FootstepAudioResolver — animation event hook)
- Spec Section 7.5 (FootstepAudioProfile — material→clip lookup)
- `Assets/Scripts/Character/CharacterSystem.cs` — base class is `NetworkBehaviour`, has `_character`
- `Assets/Scripts/Character/Visual/ICharacterVisual.cs:30` — `event Action<string> OnAnimationEvent`

- [ ] **Step 1: Create FootstepAudioProfile**

```csharp
// Assets/Scripts/Audio/FootstepAudioProfile.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "MWI/Audio/Footstep Audio Profile")]
public class FootstepAudioProfile : ScriptableObject
{
    [Serializable]
    public class MaterialClipSet
    {
        public ItemMaterial BootMaterial;
        public FootSurfaceType FootSurface;
        public AudioClip[] Clips;
        public float VolumeMultiplier = 1f;
        [Range(0f, 0.3f)] public float PitchVariation = 0.1f;
    }

    [SerializeField] private List<MaterialClipSet> _materialClips = new();
    [SerializeField] private AudioClip[] _fallbackClips;

    public (AudioClip clip, float volume, float pitchVariation) GetClip(
        ItemMaterial bootMaterial, FootSurfaceType footSurface)
    {
        // Try exact boot material match first
        if (bootMaterial != ItemMaterial.None)
        {
            foreach (var set in _materialClips)
            {
                if (set.BootMaterial == bootMaterial && set.Clips != null && set.Clips.Length > 0)
                {
                    var clip = set.Clips[UnityEngine.Random.Range(0, set.Clips.Length)];
                    return (clip, set.VolumeMultiplier, set.PitchVariation);
                }
            }
        }

        // Try foot surface match
        foreach (var set in _materialClips)
        {
            if (set.BootMaterial == ItemMaterial.None && set.FootSurface == footSurface
                && set.Clips != null && set.Clips.Length > 0)
            {
                var clip = set.Clips[UnityEngine.Random.Range(0, set.Clips.Length)];
                return (clip, set.VolumeMultiplier, set.PitchVariation);
            }
        }

        // Fallback
        if (_fallbackClips != null && _fallbackClips.Length > 0)
        {
            var clip = _fallbackClips[UnityEngine.Random.Range(0, _fallbackClips.Length)];
            return (clip, 1f, 0.1f);
        }

        return (null, 0f, 0f);
    }
}
```

- [ ] **Step 2: Create CharacterTerrainEffects**

```csharp
// Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs
using System;
using UnityEngine;
using MWI.Terrain;
using MWI.Weather;

public class CharacterTerrainEffects : CharacterSystem
{
    public TerrainType CurrentTerrainType { get; private set; }
    public bool IsInWeatherFront { get; private set; }
    public WeatherType CurrentWeather { get; private set; }

    public event Action<TerrainType> OnTerrainChanged;

    private TerrainType _lastTerrainType;

    private void Update()
    {
        UpdateTerrainDetection();

        if (IsServer)
            ApplyTerrainEffects();
    }

    private void UpdateTerrainDetection()
    {
        Vector3 pos = transform.root.position;
        TerrainType newTerrain = null;

        // Priority 1: Check if in a sealed room
        // Room detection: use Zone._charactersInside via the Character's current zone tracking
        // For now, check MapController's TerrainCellGrid
        // TODO: Integrate with Room.FloorTerrainType when CharacterLocations tracks current room

        // Priority 2: Inside active MapController
        if (newTerrain == null)
        {
            var map = MapController.GetMapAtPosition(pos);
            if (map != null)
            {
                var grid = map.GetComponent<TerrainCellGrid>();
                if (grid != null)
                    newTerrain = grid.GetTerrainAt(pos);
            }
        }

        // Priority 3: Open world — BiomeRegion default
        if (newTerrain == null)
        {
            var region = BiomeRegion.GetRegionAtPosition(pos);
            if (region != null)
                newTerrain = region.GetDefaultTerrainType();
        }

        CurrentTerrainType = newTerrain;

        if (CurrentTerrainType != _lastTerrainType)
        {
            _lastTerrainType = CurrentTerrainType;
            OnTerrainChanged?.Invoke(CurrentTerrainType);
        }

        // Weather front detection
        var biome = BiomeRegion.GetRegionAtPosition(pos);
        if (biome != null)
        {
            IsInWeatherFront = false;
            foreach (var front in biome.ActiveFronts)
            {
                if (front == null) continue;
                float dist = Vector3.Distance(pos, front.transform.position);
                if (dist < front.Radius.Value)
                {
                    IsInWeatherFront = true;
                    CurrentWeather = front.Type.Value;
                    break;
                }
            }
        }
    }

    private void ApplyTerrainEffects()
    {
        if (CurrentTerrainType == null || _character == null) return;

        // Speed modifier — apply via CharacterMovement if available
        // TODO: Hook into CharacterMovement.SpeedMultiplier when that API is exposed
        // For now, log for debugging
        if (CurrentTerrainType.SpeedMultiplier < 1f)
        {
            // Debug.Log($"[TerrainEffects] {_character.name} speed modifier: {CurrentTerrainType.SpeedMultiplier}");
        }

        // Damage over time
        if (CurrentTerrainType.HasDamage)
        {
            // TODO: Apply via Character health system
            // float damage = CurrentTerrainType.DamagePerSecond * Time.deltaTime;
        }
    }
}
```

- [ ] **Step 3: Create FootstepAudioResolver**

```csharp
// Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs
using UnityEngine;
using MWI.Terrain;

public class FootstepAudioResolver : MonoBehaviour
{
    [SerializeField] private AudioSource _footstepAudioSource;

    private CharacterTerrainEffects _terrainEffects;
    private Character _character;

    private void Awake()
    {
        _terrainEffects = GetComponent<CharacterTerrainEffects>();
        _character = GetComponentInParent<Character>();
    }

    private void OnEnable()
    {
        if (_character != null && _character.Visual != null)
            _character.Visual.OnAnimationEvent += HandleAnimationEvent;
    }

    private void OnDisable()
    {
        if (_character != null && _character.Visual != null)
            _character.Visual.OnAnimationEvent -= HandleAnimationEvent;
    }

    private void HandleAnimationEvent(string eventName)
    {
        if (eventName != "footstep") return;
        PlayFootstep();
    }

    public void PlayFootstep()
    {
        if (_terrainEffects == null || _terrainEffects.CurrentTerrainType == null) return;
        if (_footstepAudioSource == null) return;

        var terrainType = _terrainEffects.CurrentTerrainType;
        if (terrainType.FootstepProfile == null) return;

        // Resolve foot material
        ItemMaterial bootMaterial = ItemMaterial.None;
        FootSurfaceType footSurface = FootSurfaceType.BareSkin;

        if (_character != null)
        {
            var equipment = _character.GetComponentInChildren<CharacterEquipment>();
            if (equipment != null)
                bootMaterial = equipment.GetFootMaterial();

            var archetype = _character.Archetype;
            if (archetype != null)
                footSurface = archetype.DefaultFootSurface;
        }

        var (clip, volume, pitchVar) = terrainType.FootstepProfile.GetClip(bootMaterial, footSurface);
        if (clip == null) return;

        _footstepAudioSource.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        _footstepAudioSource.PlayOneShot(clip, volume);
    }
}
```

- [ ] **Step 4: Add CharacterTerrainEffects to Character.cs facade**

Read `Assets/Scripts/Character/Character.cs`. Add:

```csharp
[SerializeField] private CharacterTerrainEffects _terrainEffects;
public CharacterTerrainEffects TerrainEffects => _terrainEffects;
```

In `Awake()`, add auto-assign fallback:
```csharp
if (_terrainEffects == null) _terrainEffects = GetComponentInChildren<CharacterTerrainEffects>();
```

- [ ] **Step 5: Verify compilation**

Check Unity Console. `_character.Visual` — verify `Character.cs` exposes a `Visual` property of type `ICharacterVisual`. `_character.Archetype` — verify `Character.cs` exposes an `Archetype` property.

- [ ] **Step 6: Commit**

```
git add Assets/Scripts/Audio/FootstepAudioProfile.cs Assets/Scripts/Character/CharacterTerrain/ Assets/Scripts/Character/Character.cs
git commit -m "feat(terrain): add CharacterTerrainEffects, FootstepAudioResolver, FootstepAudioProfile"
```

---

### Task 10: VegetationGrowthSystem (Layer 4)

**Files:**
- Create: `Assets/Scripts/Terrain/VegetationGrowthSystem.cs`

**Docs to check:**
- Spec Section 6.1 (growth stages, moisture dependency, drought death)
- CLAUDE.md Rule 26 — catch-up loops for Giga Speed

- [ ] **Step 1: Create VegetationGrowthSystem**

```csharp
// Assets/Scripts/Terrain/VegetationGrowthSystem.cs
using UnityEngine;
using Unity.Netcode;

namespace MWI.Terrain
{
    public class VegetationGrowthSystem : MonoBehaviour
    {
        [Header("Tick Settings")]
        [SerializeField] private float _tickIntervalGameHours = 1f;
        [SerializeField] private float _minimumMoistureForGrowth = 0.2f;
        [SerializeField] private float _droughtDeathHours = 48f;

        [Header("Growth Stage Thresholds (game-hours)")]
        [SerializeField] private float _sproutTime = 6f;
        [SerializeField] private float _bushTime = 24f;
        [SerializeField] private float _saplingTime = 72f;
        [SerializeField] private float _treeTime = 168f;

        [Header("Growth Stage Prefabs")]
        [SerializeField] private GameObject _sproutPrefab;
        [SerializeField] private GameObject _bushPrefab;
        [SerializeField] private GameObject _saplingPrefab;
        [SerializeField] private GameObject _treePrefab;

        private TerrainCellGrid _grid;
        private float _timeSinceLastTick;

        public void Initialize(TerrainCellGrid grid)
        {
            _grid = grid;
        }

        private void Update()
        {
            if (_grid == null) return;
            if (!NetworkManager.Singleton.IsServer) return;

            _timeSinceLastTick += Time.deltaTime;

            float tickInterval = _tickIntervalGameHours * 3600f; // Convert to game-seconds
            while (_timeSinceLastTick >= tickInterval)
            {
                _timeSinceLastTick -= tickInterval;
                ProcessGrowthTick(_tickIntervalGameHours);
            }
        }

        private void ProcessGrowthTick(float hoursElapsed)
        {
            for (int z = 0; z < _grid.Depth; z++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    ref TerrainCell cell = ref _grid.GetCellRef(x, z);
                    var terrainType = cell.GetCurrentType();
                    if (terrainType == null || !terrainType.CanGrowVegetation) continue;
                    if (cell.IsPlowed) continue; // Plowed cells handled by CropSystem (Phase 2)

                    // Check moisture for growth
                    if (cell.Moisture >= _minimumMoistureForGrowth)
                    {
                        cell.GrowthTimer += hoursElapsed;
                        cell.TimeSinceLastWatered = 0f;
                    }
                    else
                    {
                        cell.TimeSinceLastWatered += hoursElapsed;
                    }

                    // Drought death
                    if (cell.TimeSinceLastWatered > _droughtDeathHours && cell.GrowthTimer > 0f)
                    {
                        cell.GrowthTimer = 0f;
                        // TODO: Despawn visual prefab at this cell
                        Debug.Log($"[VegetationGrowth] Plant died at ({x},{z}) — drought.");
                    }

                    // TODO: Spawn/update visual prefabs based on growth stage
                    // GetGrowthStage(cell.GrowthTimer) → spawn appropriate prefab
                }
            }
        }

        public int GetGrowthStage(float growthTimer)
        {
            if (growthTimer >= _treeTime) return 4;     // Tree
            if (growthTimer >= _saplingTime) return 3;   // Sapling
            if (growthTimer >= _bushTime) return 2;      // Bush
            if (growthTimer >= _sproutTime) return 1;    // Sprout
            return 0;                                     // Empty
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Terrain/VegetationGrowthSystem.cs
git commit -m "feat(terrain): add VegetationGrowthSystem with growth stages and drought death"
```

---

### Task 11: MacroSimulator Integration + MapController Terrain Hooks

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — add terrain catch-up methods
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs` — add TerrainCellGrid lifecycle hooks, ClientRpcs for grid sync

**Docs to check:**
- Spec Section 5.2 (MacroSimulator call site, terrain offline math)
- Spec Section 9.1-9.4 (execution order, simplified resource yield, terrain math, vegetation math)
- Spec Section 10.3 (MapController Hibernate/WakeUp hooks)
- Spec Section 8.1 (TerrainCellGrid sync via MapController ClientRpcs)
- `MacroSimulator.cs:13` — `SimulateCatchUp()` signature
- `MapController.cs:814` — `Hibernate()`, `MapController.cs:979` — `WakeUp()`

- [ ] **Step 1: Read MacroSimulator.cs**

Read `Assets/Scripts/World/MapSystem/MacroSimulator.cs` in full to understand exact structure.

- [ ] **Step 2: Add SimulateTerrainCatchUp to MacroSimulator**

Add after existing resource pool regeneration logic, before inventory yields:

```csharp
using MWI.Terrain;
using MWI.Weather;

// New static method:
public static void SimulateTerrainCatchUp(
    TerrainCellSaveData[] cells,
    BiomeClimateProfile climate,
    float hoursPassed,
    List<TerrainTransitionRule> rules)
{
    if (cells == null || climate == null) return;

    float estimatedRainHours = hoursPassed * climate.RainProbability;
    float estimatedDryHours = hoursPassed * (1f - climate.RainProbability - climate.SnowProbability - climate.CloudyProbability);
    float ambientTempAvg = (climate.AmbientTemperatureMin + climate.AmbientTemperatureMax) / 2f;

    for (int i = 0; i < cells.Length; i++)
    {
        // Moisture
        cells[i].Moisture += estimatedRainHours * 0.1f; // average rain intensity * rate
        cells[i].Moisture -= estimatedDryHours * climate.EvaporationRate;
        cells[i].Moisture = Mathf.Clamp01(cells[i].Moisture);

        // Temperature snap to ambient
        cells[i].Temperature = ambientTempAvg;

        // Watering tracking
        if (estimatedRainHours > 0)
            cells[i].TimeSinceLastWatered = 0f;
        else
            cells[i].TimeSinceLastWatered += hoursPassed;

        // Evaluate transitions
        if (rules != null)
        {
            foreach (var rule in rules)
            {
                if (rule.SourceType.TypeId != cells[i].CurrentTypeId
                    && rule.SourceType.TypeId != cells[i].BaseTypeId) continue;

                if (rule.Evaluate(cells[i].Moisture, cells[i].Temperature, cells[i].SnowDepth))
                {
                    cells[i].CurrentTypeId = rule.ResultType.TypeId;
                    break;
                }
            }
        }
    }
}

public static void SimulateVegetationCatchUp(
    TerrainCellSaveData[] cells,
    BiomeClimateProfile climate,
    float hoursPassed,
    float minimumMoistureForGrowth = 0.2f,
    float droughtDeathHours = 48f)
{
    if (cells == null || climate == null) return;

    float avgMoisture = climate.BaselineMoisture +
        (climate.RainProbability * 0.3f);

    for (int i = 0; i < cells.Length; i++)
    {
        var type = TerrainTypeRegistry.Get(cells[i].CurrentTypeId);
        if (type == null || !type.CanGrowVegetation) continue;
        if (cells[i].IsPlowed) continue;

        if (avgMoisture >= minimumMoistureForGrowth)
        {
            cells[i].GrowthTimer += hoursPassed;
            cells[i].TimeSinceLastWatered = 0f;
        }
        else
        {
            cells[i].TimeSinceLastWatered += hoursPassed;
            if (cells[i].TimeSinceLastWatered > droughtDeathHours)
            {
                cells[i].GrowthTimer = 0f;
                cells[i].PlantedCropId = null;
            }
        }
    }
}
```

- [ ] **Step 3: Call terrain catch-up from SimulateCatchUp**

Inside `SimulateCatchUp()`, after resource pool regeneration and before inventory yields, add:

```csharp
// Terrain catch-up (new step 2)
if (savedData.TerrainCells != null)
{
    var transitionRules = Resources.LoadAll<TerrainTransitionRule>("Data/Terrain/TransitionRules");
    var climateProfile = map?.Biome?.ClimateProfile; // map found via FindObjectsByType
    if (climateProfile != null)
    {
        SimulateTerrainCatchUp(savedData.TerrainCells, climateProfile, hoursPassed,
            new List<TerrainTransitionRule>(transitionRules));
        SimulateVegetationCatchUp(savedData.TerrainCells, climateProfile, hoursPassed);
    }
}
```

Note: The exact insertion point depends on the current structure of `SimulateCatchUp()`. Read the file first to find the right location.

- [ ] **Step 4: Read MapController.cs — Hibernate and WakeUp methods**

Read `Assets/Scripts/World/MapSystem/MapController.cs` around lines 814 and 979 to understand the exact hibernation/wakeup flow.

- [ ] **Step 5: Add terrain hooks to MapController**

In `Hibernate()`, before despawning objects, add:
```csharp
// Serialize terrain cell grid
var terrainGrid = GetComponent<TerrainCellGrid>();
if (terrainGrid != null)
    _hibernationData.TerrainCells = terrainGrid.SerializeCells();
```

In `WakeUp()`, after MacroSimulator.SimulateCatchUp and before spawning NPCs, add:
```csharp
// Restore terrain cell grid
var terrainGrid = GetComponent<TerrainCellGrid>();
if (terrainGrid != null && _hibernationData?.TerrainCells != null)
    terrainGrid.RestoreFromSaveData(_hibernationData.TerrainCells);
```

- [ ] **Step 6: Add terrain grid sync ClientRpcs to MapController**

Add to MapController:
```csharp
using MWI.Terrain;

[ClientRpc]
private void SendTerrainGridClientRpc(TerrainCellSaveData[] cells, ClientRpcParams rpcParams = default)
{
    var grid = GetComponent<TerrainCellGrid>();
    if (grid != null)
        grid.RestoreFromSaveData(cells);
}
```

In the existing player-enter logic (around `OnTriggerEnter`), after the player is registered, send the grid state to the new player:
```csharp
// Send terrain grid to joining player
var grid = GetComponent<TerrainCellGrid>();
if (grid != null && IsServer)
{
    var data = grid.SerializeCells();
    if (data != null)
    {
        SendTerrainGridClientRpc(data, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }
}
```

- [ ] **Step 7: Verify compilation**

- [ ] **Step 8: Commit**

```
git add Assets/Scripts/World/MapSystem/MacroSimulator.cs Assets/Scripts/World/MapSystem/MapController.cs
git commit -m "feat(terrain): integrate MacroSimulator terrain catch-up and MapController grid sync"
```

---

### Task 12: TerrainTypeRegistry Initialization + GameLauncher Hook

**Files:**
- Modify: `Assets/Scripts/Core/GameLauncher.cs` — add TerrainTypeRegistry.Initialize() in boot sequence

- [ ] **Step 1: Read GameLauncher.cs**

Read `Assets/Scripts/Core/GameLauncher.cs` to find the boot sequence.

- [ ] **Step 2: Add TerrainTypeRegistry initialization**

Early in the boot sequence (before any MapController can wake up), add:
```csharp
using MWI.Terrain;

// In LaunchSequence() or LaunchSolo(), before map loading:
TerrainTypeRegistry.Initialize();
```

- [ ] **Step 3: Verify compilation and boot**

Enter Play Mode in Unity Editor, check Console for `[TerrainTypeRegistry] Initialized with N terrain types.`

- [ ] **Step 4: Commit**

```
git add Assets/Scripts/Core/GameLauncher.cs
git commit -m "feat(terrain): add TerrainTypeRegistry initialization to GameLauncher boot sequence"
```

---

### Task 13: SKILL.md Documentation

**Files:**
- Create: `.agent/skills/terrain-weather/SKILL.md`
- Create: `.agent/skills/character-terrain/SKILL.md`

- [ ] **Step 1: Create terrain-weather SKILL.md**

Document the full terrain & weather system: TerrainType, TerrainCellGrid, TerrainPatch, TerrainWeatherProcessor, WeatherFront, BiomeRegion, GlobalWindController, BiomeClimateProfile, VegetationGrowthSystem. Include public APIs, events, dependencies, integration points.

- [ ] **Step 2: Create character-terrain SKILL.md**

Document CharacterTerrainEffects and FootstepAudioResolver subsystems. Include terrain detection priority, footstep resolution flow, animation event hook, equipment integration.

- [ ] **Step 3: Commit**

```
git add .agent/skills/terrain-weather/SKILL.md .agent/skills/character-terrain/SKILL.md
git commit -m "docs: add SKILL.md files for terrain-weather and character-terrain systems"
```

---

### Task 14: Manual Integration Test

This is a manual verification task — no code to write.

- [ ] **Step 1: Set up a test scene**

1. Open the main game scene in Unity Editor
2. Add a `TerrainPatch` with BoxCollider to a MapController area, assign "Fertile" TerrainType
3. Add a second `TerrainPatch` overlapping partially, assign "Dirt" TerrainType, lower priority
4. Add `TerrainCellGrid` component to the MapController GameObject
5. Add `TerrainWeatherProcessor` component to the MapController GameObject
6. Add `VegetationGrowthSystem` component to the MapController GameObject

- [ ] **Step 2: Verify terrain grid initialization**

Enter Play Mode. Check Console for `[TerrainCellGrid] Initialized` message. Verify cell counts make sense for the map size.

- [ ] **Step 3: Place a BiomeRegion**

1. Create a BiomeRegion GameObject on the world map with BoxCollider covering the test area
2. Assign a BiomeClimateProfile with RainProbability > 0
3. Enter Play Mode and verify BiomeRegion appears in the static registry

- [ ] **Step 4: Test CharacterTerrainEffects**

1. Add `CharacterTerrainEffects` + `FootstepAudioResolver` as a child GameObject of a Character prefab
2. Walk the character over the TerrainPatch areas
3. Check Console for terrain type changes via `OnTerrainChanged` event
4. Verify speed modifier logs appear on Mud terrain

- [ ] **Step 5: Verify network sync**

Host a multiplayer session. Check that:
- GlobalWindController NetworkVariables sync to client
- TerrainCellGrid state is sent to joining client via ClientRpc
- Both host and client resolve terrain type correctly at the same position
