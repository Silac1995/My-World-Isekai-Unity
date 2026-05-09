# Living World — Phase 1 Design

**Date:** 2026-04-21
**Author:** Claude (with Kevin)
**Status:** Approved for implementation planning
**Scope:** Refactor the Living World foundation — rip NPC-cluster-driven settlement promotion, introduce a `Region → { MapController, WildernessZone, WeatherFront }` hierarchy, and lay the plumbing for future wildlife / weather systems.

---

## 1. Motivation

The current `CommunityTracker` watches NPC clusters in open-world chunks and auto-promotes them into `MapController`s via `PromoteToSettlement`. This is:

1. **Costly** — live wilderness NPCs with NavMeshAgents, physics, and NGO replication tick every frame regardless of player proximity.
2. **Hard to reason about** — maps appear "magically" from population thresholds; designers have little control.
3. **Coupled** — `CommunityTracker` mixes social state (leaders, permits, claims) with lifecycle triggers (cluster evaluation, promotion).

The refactor replaces the implicit cluster-promotion model with an **explicit, designer-authored** world hierarchy and a **pluggable zone-motion** system. Wilderness content becomes a data-first, stream-on-demand pool rather than a live-simulation pool. Future weather fronts, wildlife ecology, and reactive motion plug in without further refactoring.

Phase 1 delivers the scaffolding, performs the rip, and lands the minimal classes. Wildlife ecology, weather-front reactive logic, and reactive motion strategies are later phases.

## 2. Goals

- Replace the cluster-driven `CommunityTracker` with an explicit `MapRegistry` that performs map creation on demand, not via auto-promotion.
- Introduce the `IWorldZone` abstraction and three implementations: `MapController` (existing, adapted), `WildernessZone` (new), `WeatherFront` (new stub).
- Introduce `Region` as an authored container for world content.
- Introduce pluggable `IZoneMotionStrategy` evaluated daily by `MacroSimulator`.
- Introduce `IStreamable` as the content contract for wilderness streaming.
- Promote the "minimum distance between maps" rule from a per-manager field to a global `WorldSettingsData.MapMinSeparation`.
- Ship with **harvestable data model** only; live harvestable prefab instantiation is out of scope (deferred to Phase 2 alongside `RandomDriftMotion`).

## 3. Non-goals (explicitly deferred)

- Live harvestable GameObject spawning / visual prefabs.
- Wildlife ecology (animals streamed into wilderness zones).
- Reactive motion strategies (`AvoidWeatherFront`, `FollowResourceAbundance`, `FleeFromPlayer`, etc.).
- **Changing existing `WeatherFront` behavior.** `WeatherFront.cs`, `WeatherFrontSnapshot.cs`, and wind-driven movement are already implemented and stay as-is. Adapting them to `IWorldZone` is a Phase-2 concern.
- Procedural Region generation — all regions are scene-authored for MVP.
- Cross-server tamed-animal portability.

## 4. World hierarchy

```
Region (authored MonoBehaviour in scene — renamed/extended from existing BiomeRegion)
├── BoxCollider Bounds
├── BiomeDefinition DefaultBiome
├── BiomeClimateProfile ClimateProfile  (existing field, kept as-is)
├── RegionId
└── Children (Unity transform hierarchy, auto-discovered in Awake):
    ├── MapController        (existing — discrete places, hibernate individually)
    ├── WildernessZone       (new — virtual content, streams on player approach)
    └── WeatherFront         (existing — fully implemented; spawned on timer by Region)
```

- All three child types implement `IWorldZone`.
- `Region` is **server-side MonoBehaviour only** — no `NetworkBehaviour` inheritance. Clients learn their current region via a new `CharacterMapTracker.CurrentRegionId` NetworkVariable. (`WeatherFront` children are themselves `NetworkBehaviour`s — that is independent of `Region`.)
- A `MapController` or `WildernessZone` can exist **without a parent Region** (rare, e.g., isolated wild placement). `CurrentRegionId` is empty in that case. This preserves flexibility without forcing a region around every orphan map.

### 4.1 Relationship to existing code

`BiomeRegion` already provides ~80% of what `Region` needs — bounds, biome reference, climate profile, static `GetRegionAtPosition` registry, hibernation, and live `WeatherFront` spawning on a timer. Phase 1 renames the class (and moves the file) rather than creating a parallel type. See Section 11 for the concrete rename steps.

## 5. Classes and interfaces

### 5.1 Interfaces

```csharp
public interface IWorldZone {
    string ZoneId { get; }
    Vector3 Center { get; }
    float Radius { get; }
    bool Contains(Vector3 worldPos);
    float DistanceTo(Vector3 worldPos);
}

public interface IZoneMotionStrategy {
    Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
}

public interface IStreamable {
    string Id { get; }
    Vector3 WorldPosition { get; }
    GameObject MaterializeAt(Vector3 pos);
    void SnapshotAndRelease(GameObject live);
}
```

### 5.2 MonoBehaviours

**`Region` — rename & extend existing `BiomeRegion`:**

```csharp
// Before: namespace MWI.Weather { public class BiomeRegion : MonoBehaviour, ISaveable { ... } }
// After:  namespace MWI.WorldSystem { public class Region : MonoBehaviour, IWorldZone, ISaveable { ... } }
//
// All existing fields / methods preserved. Additions:
public class Region : MonoBehaviour, IWorldZone, ISaveable {
    // EXISTING (kept as-is):
    //   [SerializeField] string _regionId;
    //   [SerializeField] BiomeDefinition _biomeDefinition;
    //   [SerializeField] BiomeClimateProfile _climateProfile;
    //   [SerializeField] GameObject _weatherFrontPrefab;
    //   List<WeatherFront> _activeFronts;
    //   List<WeatherFrontSnapshot> _hibernatedFronts;
    //   static GetRegionAtPosition / GetAdjacentRegions / ClearRegistry
    //   WakeUp / Hibernate / ISaveable

    // NEW:
    public IReadOnlyList<MapController> Maps { get; }                 // auto-discovered in Awake
    public IReadOnlyList<WildernessZone> WildernessZones { get; }     // auto-discovered + dynamic additions

    // IWorldZone impl:
    public string ZoneId => _regionId;
    public Vector3 Center => _bounds.bounds.center;
    public float Radius => _bounds.bounds.extents.magnitude;
    public bool Contains(Vector3 p) => _bounds.bounds.Contains(p);
    public float DistanceTo(Vector3 p) => Vector3.Distance(_bounds.ClosestPoint(p), p);
}
```

File moves from `Assets/Scripts/Weather/BiomeRegion.cs` → `Assets/Scripts/World/MapSystem/Region.cs`. Move the `.meta` file alongside so the asset GUID is preserved and existing scene references stay intact. Namespace `MWI.Weather` → `MWI.WorldSystem`.

**`WildernessZone` — new class:**

```csharp
public class WildernessZone : NetworkBehaviour, IWorldZone, ISaveable {
    public string ZoneId;
    public float Radius;
    public Region ParentRegion;   // null allowed
    public List<ResourcePoolEntry> Harvestables;           // existing type
    public List<HibernatedNPCData> Wildlife;               // existing type, empty in Phase 1
    public List<ScriptableZoneMotionStrategy> MotionStrategies;
}
```

**`WeatherFront` — unchanged.** Existing class in `Assets/Scripts/Weather/WeatherFront.cs` stays exactly as it is. In Phase 1 it is **not** adapted to `IWorldZone` — that's a Phase-2 concern. The abstraction hierarchy tolerates this: `WeatherFront` is a first-class world object but simply isn't queryable through `IWorldZone` yet.

### 5.3 Motion strategy base & default impl

```csharp
public abstract class ScriptableZoneMotionStrategy : ScriptableObject, IZoneMotionStrategy {
    public abstract Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
}

[CreateAssetMenu(menuName = "World/Motion/Static")]
public class StaticMotionStrategy : ScriptableZoneMotionStrategy {
    public override Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay) => Vector3.zero;
}
```

### 5.4 Managers and registries

```csharp
// Renamed from CommunityTracker. Server-side only. ISaveable.
public class MapRegistry : MonoBehaviour, ISaveable {
    public static MapRegistry Instance { get; }
    public CommunityData GetCommunity(string mapId);
    public void AddCommunity(CommunityData community);
    public IReadOnlyList<CommunityData> GetAllCommunities();
    public MapController CreateMapAtPosition(Vector3 worldPosition);  // enforces MapMinSeparation
    public bool ImposeJobOnCitizen(string mapId, string leaderId, Character citizen, Job job, CommercialBuilding building);
    // ProcessPendingBuildingClaims stays (OnNewDay driven)
    // AdoptExistingBuildings stays (called by CreateMapAtPosition when appropriate)
    // SaveKey = "CommunityTracker_Data"  (unchanged for save-file compat)
}

// Static registry — already exists on BiomeRegion (_allRegions) with
// GetRegionAtPosition / GetAdjacentRegions / ClearRegistry. Kept as-is
// after the rename; no separate RegionRegistry class is needed.
// Callers use: Region.GetRegionAtPosition(worldPos).

// New server-side singleton. ISaveable (persists dynamic zones via their parent Region).
public class WildernessZoneManager : MonoBehaviour {
    public static WildernessZoneManager Instance { get; }
    public WildernessZone SpawnZone(Vector3 pos, WildernessZoneDef def, Region parent = null);
    // If parent is null, auto-resolved via RegionRegistry.GetRegionAt(pos).
    // Rejects spawn if another IWorldZone center is within WorldSettingsData.MapMinSeparation.
}
```

### 5.5 New ScriptableObject

```csharp
[CreateAssetMenu(menuName = "World/WildernessZoneDef")]
public class WildernessZoneDef : ScriptableObject {
    public float DefaultRadius = 75f;
    public BiomeDefinition BiomeOverride;                 // null = inherit from Region
    public List<ScriptableZoneMotionStrategy> DefaultMotion;
    public HarvestableSeedingTable HarvestableSeedTable;  // phase 1 data scaffold
}
```

## 6. Data model changes

### 6.1 `WorldSettingsData` — new field

```csharp
[Header("Zone Placement")]
[Tooltip("Minimum world-unit distance between any two IWorldZone centers. 11 units = 1.67m (CLAUDE.md rule 32).")]
public float MapMinSeparation = 150f;
```

- Replaces `BuildingPlacementManager._nearbyMapJoinRadius`.
- Consumed by `BuildingPlacementManager`, `MapRegistry.CreateMapAtPosition`, `WildernessZoneManager.SpawnZone`, and `MacroSimulator` zone-motion clamp.

### 6.2 `CommunityData` — unchanged

Name and contents preserved. Only source of creation changes (no longer created by cluster promotion).

### 6.3 `CharacterMapTracker` — new NetworkVariable

```csharp
public NetworkVariable<FixedString64Bytes> CurrentRegionId = new NetworkVariable<FixedString64Bytes>(
    "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

Server updates it whenever the character's position enters a new region's bounds (via `RegionRegistry.GetRegionAt`). Updates are throttled to a 0.25s minimum interval to avoid thrash on boundary-hovering characters.

**Client-side display:** The NV carries only the `RegionId` string. For human-readable display ("You are in the Eastern Woods"), clients load a shared `RegionDefinition` SO keyed by `RegionId` at boot — the SO holds display name, biome reference, UI icon, etc. `Region` MonoBehaviours in the scene reference their matching `RegionDefinition`. This keeps the `Region` itself server-side while letting clients resolve the ID for UI purposes. `RegionDefinition` is added in step 6 alongside the `Region` class.

### 6.4 Save types

**`Region` save payload — extend existing `BiomeRegion` `ISaveable` state:**

The existing `BiomeRegion.CaptureState()` already serializes hibernated `WeatherFront`s (`WeatherFrontSnapshot[]`) and hibernation timing. Phase 1 adds `DynamicWildernessZones` to the same payload:

```csharp
[Serializable]
public class RegionSaveData {
    // EXISTING fields (kept — currently in BiomeRegion's private save type):
    public List<WeatherFrontSnapshot> HibernatedFronts;
    public bool IsHibernating;
    public double LastHibernationTime;

    // NEW:
    public List<WildernessZoneSaveData> DynamicWildernessZones = new();
    // Authored zones restored from scene prefab, not serialized here.
}
```

**`WildernessZoneSaveData` — new:**

```csharp
[Serializable]
public class WildernessZoneSaveData {
    public string ZoneId;
    public Vector3 Center;
    public float Radius;
    public string BiomeOverrideAssetPath;   // null = inherit
    public List<ResourcePoolEntry> Harvestables;
    public List<HibernatedNPCData> Wildlife;
    public List<string> MotionStrategyAssetPaths;
}
```

Back-compat: `RestoreState` reads the new shape. If an old save lacks `DynamicWildernessZones`, the field stays an empty list (default). No explicit legacy branch needed.

### 6.5 `MapRegistrySaveData` — renamed, shrunk

```csharp
[Serializable]
public class MapRegistrySaveData {
    public List<CommunityData> Communities = new();
    // RoamingClusterData + PendingClusters REMOVED
}
```

- `SaveKey = "CommunityTracker_Data"` kept unchanged for save-file backwards compatibility.
- `RestoreState` reads the new shape; if an old save has `PendingClusters`, it is discarded silently (one `Debug.Log` trace). This is a clean-cut migration — no legacy flag.

## 7. Deletions from `CommunityTracker` (→ `MapRegistry`)

- `EvaluatePopulations()` (daily cluster scan) — **deleted**
- `_pendingClusters` field — **deleted**
- `RoamingClusterData` class — **deleted**
- `PromoteToSettlement()` + internal NPC migration block — **deleted**
- `CommunityTier.RoamingCamp` auto-transition logic — **deleted** (enum value stays as an authored label)
- `CommunityTrackerSaveData.PendingClusters` — **deleted** (save-compat handled in `RestoreState`)

**Kept:**
- `CreateMapAtPosition(Vector3)` (recently added; now enforces `MapMinSeparation`)
- `AdoptExistingBuildings(MapController, CommunityData)`
- `GetCommunity`, `AddCommunity`, `GetAllCommunities`
- `ImposeJobOnCitizen`
- `ProcessPendingBuildingClaims`
- ISaveable integration

**Simplified:**
- `HandleNewDay()` previously called both `EvaluatePopulations` and `ProcessPendingBuildingClaims`. After the rip, it calls only the latter.

## 8. `MacroSimulator` catch-up extension

Add a new step (5) **Zone Motion**:

```csharp
foreach (WildernessZone zone in AllWildernessZones)
{
    Vector3 totalDelta = Vector3.zero;
    foreach (var strategy in zone.MotionStrategies)
        totalDelta += strategy.ComputeDailyDelta(zone, currentDay);

    Vector3 proposed = zone.transform.position + totalDelta * daysSinceLastTick;
    proposed = ClampBy MapMinSeparation(proposed);   // prevent zone-on-zone overlap
    zone.transform.position = proposed;
}
// Same pass for WeatherFront (stub — no strategies in Phase 1).
```

**When it runs:**
- **During active play:** `TimeManager.OnNewDay` fires `MacroSimulator.DailyTick()`. `daysSinceLastTick = 1`.
- **On map wake-up:** `MacroSimulator` runs its catch-up loop with `daysSinceLastTick = TimeManager.CurrentDay - zone.LastMotionDay`. Zones that were skipped during hibernation get their accumulated drift in one shot.

O(N) per day, iterating all zones globally. Negligible cost. With all zones defaulting to `StaticMotionStrategy`, the sum is zero and nothing moves — correct Phase 1 behavior.

## 9. `BuildingPlacementManager` integration

- Delete `_nearbyMapJoinRadius` field; read from `WorldSettingsData.MapMinSeparation`.
- `RegisterBuildingWithMap` flow stays (GetMapAtPosition → bounds fallback → join nearest within `MapMinSeparation` → `MapRegistry.CreateMapAtPosition`).
- `MapRegistry.CreateMapAtPosition` newly enforces the separation rule at the server level — rejects if another zone center is too close.

## 10. `CLAUDE.md` Rule 30 rewrite

Full replacement text:

> **30.** The game uses a Living World architecture based on Map Hibernation and Macro/Micro Simulation. The world is organized as a nested hierarchy: **`Region` (authored container) → { `MapController`, `WildernessZone`, `WeatherFront` }**. All three children implement `IWorldZone`. Before implementing any system that involves NPCs, resources, buildings, time, or map state, you must account for both simulation layers:
>
> - **Micro-Simulation** (Map active / player near zone): Real-time GOAP, NavMesh, live logistics, physical harvestables, NetworkObject presence. Runs for a `MapController` only when ≥ 1 player is present. Runs for a `WildernessZone` only for content streamed in within the player's spawn radius.
> - **Macro-Simulation** (Map hibernating / no player near zone): Maps freeze, NPCs serialize into `HibernatedNPCData` and despawn. `WildernessZone`s and `WeatherFront`s never host live GameObjects when no player is near — their state exists purely as data. On wake-up / player approach, `MacroSimulator` runs a catch-up loop in order: (1) Resource Pool Regeneration, (2) Inventory Yields, (3) Needs Decay, (4) Position Snap, (5) Zone Motion (apply accumulated daily deltas from each zone's `IZoneMotionStrategy` list). No live Unity systems during hibernation — all offline progress is pure math.
>
> **Key rules:**
> - Any new NPC stat, need, or behavior that changes over time must have an offline catch-up formula in `MacroSimulator`.
> - Any new resource or harvestable must be registered in `BiomeDefinition`. Runtime counts live as `ResourcePoolEntry` inside `CommunityData.ResourcePools` (map-attached) **or** `WildernessZone.Harvestables` (wilderness-attached). Never hardcode resource availability.
> - Any new job type must have a `JobYieldRecipe` entry in `JobYieldRegistry`. Biome-driven jobs must set `IsBiomeDriven = true`.
> - `TimeManager` is the single source of truth for all simulation math. Never use `Time.time` or `Time.deltaTime` for offline delta calculations — use `TimeManager.CurrentDay` + `CurrentTime01`.
> - **Maps are never created by NPC-cluster auto-promotion.** They are born via (a) scene authoring, (b) `BuildingPlacementManager` when a player places a building outside any existing map, or (c) future procedural generation. All dynamic creation routes through `MapRegistry.CreateMapAtPosition` and must respect `WorldSettingsData.MapMinSeparation`. Abandoned cities never release their spatial slot.
> - **`WildernessZone`s** are born via (a) scene authoring, (b) `WildernessZoneManager.SpawnZone` — callable from debug tools, quest scripts, or environmental systems (e.g., a `WeatherFront` spawning a temporary berry zone), or (c) future procedural generation. They hold `List<ResourcePoolEntry>` (harvestables) and `List<HibernatedNPCData>` (wildlife, future). Contents stream via `IStreamable` only when a player is within the zone's spawn radius. Zones can move via pluggable `IZoneMotionStrategy` SO assets (default `StaticMotionStrategy`).
> - **`IWorldZone`** is the shared abstraction for anything with spatial identity. Any new spatial entity type must implement it.
> - Always refer to `world-system/SKILL.md` before touching any map, NPC lifecycle, or simulation logic.

## 11. Phase-1 implementation order

Four sub-phases. Each row below is one commit.

| # | Step | Files touched | Behavior change? |
|---|---|---|---|
| **1a — Foundations** (additive) | | | |
| 1 | Add `IWorldZone`, `IZoneMotionStrategy`, `IStreamable` interfaces | 3 new files | None |
| 2 | Add `ScriptableZoneMotionStrategy` abstract base + `StaticMotionStrategy` SO + default asset under `Resources/Data/World/Motion/` | 2 new files + 1 asset | None |
| 3 | Add `MapMinSeparation` to `WorldSettingsData` | 1 field | None |
| **1b — Rip & rename** | | | |
| 4 | Rename `CommunityTracker` → `MapRegistry` + `CommunityTrackerSaveData` → `MapRegistrySaveData`. Keep `SaveKey = "CommunityTracker_Data"` for save compat | File rename + all `Instance` callsites | None (pure rename) |
| 5 | Rip `EvaluatePopulations`, `_pendingClusters`, `RoamingClusterData`, `PromoteToSettlement`, NPC migration block. Add restore-compat branch for old `PendingClusters` blob in saves | ~200 lines deleted, ~10 added | **NPC cluster auto-promotion stops firing** |
| **1c — Rename, extend, add** | | | |
| 6 | **Rename `BiomeRegion` → `Region`.** Move `Assets/Scripts/Weather/BiomeRegion.cs` → `Assets/Scripts/World/MapSystem/Region.cs` (move `.meta` together to preserve GUID). Update namespace `MWI.Weather` → `MWI.WorldSystem`. Update all 11 call-site files (`TerrainWeatherProcessor`, `GlobalWindController`, `MacroSimulator`, `BiomeDefinition`, `WeatherFront`, `CharacterTerrainEffects`, `SaveManager`, etc.) | 1 file moved + 11 files updated | **Class rename; no behavior change** |
| 7 | **Extend `Region`** with `IWorldZone` impl + `List<MapController> Maps` + `List<WildernessZone> WildernessZones` auto-discovered in `Awake` via `GetComponentsInChildren`. Extend existing save payload with `DynamicWildernessZones` | ~50 lines added to Region.cs | None (until scene use) |
| 8 | `WildernessZone.cs` (NetworkBehaviour, `ISaveable`, `IWorldZone`) + `WildernessZoneSaveData` | 2 new files | None |
| 9 | `WildernessZoneDef` SO + `HarvestableSeedingTable` helper | 2 new files | None |
| 10 | `WildernessZoneManager.cs` singleton + `SpawnZone(pos, def, parent=null)` with `MapMinSeparation` enforcement and auto-parent via `Region.GetRegionAtPosition` | 1 new file | None (until called) |
| **1d — Wire & integrate** | | | |
| 11 | `BuildingPlacementManager`: remove `_nearbyMapJoinRadius`, read from `WorldSettingsData.MapMinSeparation`. Enforce separation inside `MapRegistry.CreateMapAtPosition` | ~20 lines | **Wild-map spawn respects `MapMinSeparation` globally** |
| 12 | `CharacterMapTracker`: add `CurrentRegionId` NetworkVariable + server-side update on position change (0.25s throttle) | ~15 lines | **Clients know their current region** |
| 13 | `MacroSimulator`: daily iteration over all `WildernessZone` + `WeatherFront` motion strategies, sum deltas, clamp by `MapMinSeparation`, apply | ~40 lines | **Zones with non-static strategies start drifting** (no-op in Phase 1 since all zones default to `StaticMotion`) |
| 14 | Scene work: place one authored `Region` in the default test scene containing existing `MapController`(s) to exercise the new code path | Scene edit | Test coverage |

## 12. Narrative examples supported by the architecture

The following emergent mechanic composes from Phase 1 APIs + later-phase strategies. No further structural refactoring needed to enable it:

```
WeatherFront.OnPassThrough(Region)                              [Phase 3]
    └─► WildernessZoneManager.SpawnZone(pos, BerryZoneDef)      [Phase 1 — ready]
            └─► WildernessZone "Berries_xxxx" with Harvestables seeded
                    └─► Nearby wildlife records pulled in via
                        AttractionMotion strategy                [Phase 4]
                            └─► Zone's Wildlife list grows — animals
                                "adopted" into the zone
```

Each arrow is one strategy or one manager call.

## 13. Known risks & mitigations

| Risk | Mitigation |
|---|---|
| Existing saves use old `CommunityTrackerSaveData` shape | `RestoreState` reads new shape; old `PendingClusters` silently dropped. Dev branch, acceptable. |
| A `MapController` in the scene with no parent `Region` | Supported — `CurrentRegionId` is empty. No invariant violated. |
| `MapRegistry.CreateMapAtPosition` rejects a wild placement because of `MapMinSeparation` | `BuildingPlacementManager` falls back to "join nearest" path (current behavior). Placement never silently fails. |
| Harvestable prefab spawning doesn't exist yet | Explicit non-goal (Section 3). Data model lands in Phase 1; visible prefabs in Phase 2. |
| `MapId` / `ZoneId` NetworkVariable gap | Pre-existing issue with `MapController.MapId`; carried forward. Separate follow-up PR to convert to NetworkVariable. Flagged in build-system SKILL. |
| `CharacterMapTracker.CurrentRegionId` update thrashing (character hovering on a region boundary) | Add a 0.25s minimum update interval on the server-side position check. |
| `BiomeRegion` → `Region` rename touches 11 source files + scenes holding `BiomeRegion` components | Move `.cs` and `.meta` together to preserve the asset GUID so scene-serialized component references survive. Namespace change (`MWI.Weather` → `MWI.WorldSystem`) propagated via grep-and-replace; Unity will recompile and surface any missed callsite. Run full compile pass after step 6 before step 7 starts. |

## 14. Testing plan

**Unit / edit-mode tests:**
- `StaticMotionStrategy.ComputeDailyDelta(...)` returns `Vector3.zero` for any input.
- `Region.Awake()` auto-populates child lists (`Maps`, `WildernessZones`) from a fake transform hierarchy, while preserving existing WeatherFront spawn behavior.
- `Region.GetRegionAtPosition` (renamed from `BiomeRegion.GetRegionAtPosition`) returns correct region for in-bounds / null for out-of-bounds.
- `MapRegistry` `RestoreState` with legacy `CommunityTrackerSaveData` blob (containing `PendingClusters`) produces a valid `MapRegistry` state without crash and without the pending clusters.
- `WildernessZoneManager.SpawnZone` rejects spawn within `MapMinSeparation` of another zone.
- Existing `BiomeRegion` save roundtrip (now `Region`) — WeatherFront hibernation still works after rename.

**Integration / play-mode tests:**
- Place a building outside any map → `CreateMapAtPosition` is called, new `MapController` is born, separation rule enforced against other maps.
- Walk a test character across a `Region` boundary → `CharacterMapTracker.CurrentRegionId` updates on the server and replicates to the client.
- Save & reload after spawning a runtime `WildernessZone` → zone restores at the same position with its save data intact.
- Running one game day with all zones on `StaticMotion` → no zone positions change.

**Network matrix (all features):**
- Host ↔ Client: building placement, region transition, zone spawn visible on client.
- Client ↔ Client: region transition visible across clients.
- Host/Client ↔ NPC: NPCs don't break when their host map falls inside / outside a region.

## 15. Documentation updates

- `CLAUDE.md` Rule 30 — rewrite per Section 5.
- `.agent/skills/building_system/SKILL.md` — update "Building Placement & Save Persistence" section (replace `_nearbyMapJoinRadius` references with `MapMinSeparation`; note `MapRegistry` rename).
- `.agent/skills/world-system/SKILL.md` — replace cluster-promotion content with the new hierarchy (`Region`, `WildernessZone`, `WeatherFront`) and the `IWorldZone` / `IStreamable` / `IZoneMotionStrategy` abstractions.
- `.agent/skills/community-system/SKILL.md` — update to reflect `MapRegistry` rename and that `CommunityData` is no longer cluster-created.
- Wiki pages (`wiki/systems/world-*.md`) — handled separately via `/document-system` after code ships. Out of scope for this spec.

## 16. Out-of-scope follow-ups

- Convert `MapController.MapId` / `WildernessZone.ZoneId` to replicated `FixedString` NetworkVariables so clients can resolve dynamic maps. Currently affects any MapController created at runtime (including those from `MapRegistry.CreateMapAtPosition`).
- Live harvestable prefab spawning (Phase 2).
- `RandomDriftMotion` + other motion strategies (Phase 2–4).
- `WeatherFront` active behavior (Phase 3).
- Wildlife ecology — archetype spawn tables, `CharacterAnimal` streaming, owner-follow AI (Phase 3).

## 17. Change log

- 2026-04-21 — Initial design. Claude / Kevin.
- 2026-04-21 — Reconciled with existing `BiomeRegion` + `WeatherFront` in `Assets/Scripts/Weather/`. `Region` is now a **rename/extend** of `BiomeRegion` (Option A) rather than a new class. `WeatherFront` stays unchanged. Claude / Kevin.
- 2026-04-21 — Implementation complete across 25 commits on `feature/living-world-phase-1`. Post-implementation deviations (captured in ADR-0001 post-impl section): (1) `Region` upgraded from `MonoBehaviour` → `NetworkBehaviour` because NGO rejects `NetworkObject → non-NetworkObject` parenting; (2) building placement became region-aware; (3) `MapMinSeparation` became region-scoped; (4) `MapController.OnTriggerEnter`/`OnTriggerExit` now drive `CharacterMapTracker.CurrentMapID` (previously only door RPCs did); (5) interiors re-parent under exterior MapController via NGO `TrySetParent`; (6) wild maps strip `Biome`/`JobYields` so `VirtualResourceSupplier_*` children don't spawn on player outposts; (7) `CommunityData.SpawnPosition` added + `MapRegistry.RespawnDynamicMapsDeferred` calls `SpawnSavedBuildings` so save/load round-trips wild maps AND their buildings; (8) `UI_CharacterMapTrackerOverlay` added because NGO 2.10's `NetworkBehaviourEditor` only renders int/uint/long/float/bool/string/enum — `FixedString` and `Vector3` NetworkVariables show "Type not renderable". Claude / Kevin.
