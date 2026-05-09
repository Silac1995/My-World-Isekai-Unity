# Living World Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rip the NPC-cluster auto-promotion pipeline, introduce `Region → { MapController, WildernessZone, WeatherFront }` hierarchy via the new `IWorldZone` interface, rename/extend `BiomeRegion` → `Region`, and land scaffolding for pluggable motion strategies + streaming content. Harvestable data model lands; live harvestable prefabs and wildlife ecology are deferred to later phases.

**Architecture:** `Region` becomes the top-level authored container (renamed from `BiomeRegion`, moved from `Weather/` to `World/MapSystem/`). `MapController`, `WildernessZone`, and `WeatherFront` all implement a new `IWorldZone` interface for uniform spatial queries. `CommunityTracker` is renamed to `MapRegistry` with cluster-promotion code deleted. A new `WildernessZoneManager` singleton spawns zones at runtime. `MacroSimulator` gains a 5th catch-up step (Zone Motion) evaluating pluggable `IZoneMotionStrategy` SO assets daily. A global `WorldSettingsData.MapMinSeparation` replaces the per-manager join-radius.

**Tech Stack:** Unity 2022 LTS, C#, Unity Netcode for GameObjects (NGO), Newtonsoft.Json (existing `ISaveable` pipeline), `NetworkVariable`, `ScriptableObject`. No test asmdef exists in this project — verification is via Unity Editor play-mode + MCP `console-get-logs` + `script-execute` smoke scripts. Task 15 covers a structured manual smoke-test matrix.

**Spec reference:** [docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md](../specs/2026-04-21-living-world-phase-1-design.md)
**ADR reference:** [wiki/decisions/adr-0001-living-world-hierarchy-refactor.md](../../../wiki/decisions/adr-0001-living-world-hierarchy-refactor.md)

**Deliberate deviation from writing-plans default:** no TDD cycle — the project has no test asmdef. Each task ends with a compile+console-log check via MCP and a commit. Task 15 batches end-to-end manual verification.

---

## File Structure

**New files (9):**

| Path | Responsibility |
|------|----------------|
| `Assets/Scripts/World/Zones/IWorldZone.cs` | Shared spatial-entity contract (ZoneId, Center, Radius, Contains, DistanceTo). |
| `Assets/Scripts/World/Zones/IZoneMotionStrategy.cs` | Pluggable daily-delta motion rule. |
| `Assets/Scripts/World/Zones/IStreamable.cs` | Contract for content that streams in/out of a `WildernessZone`. |
| `Assets/Scripts/World/Zones/ScriptableZoneMotionStrategy.cs` | Abstract SO base implementing `IZoneMotionStrategy` — editor-assignable. |
| `Assets/Scripts/World/Zones/StaticMotionStrategy.cs` | Default concrete SO — `ComputeDailyDelta => Vector3.zero`. |
| `Assets/Resources/Data/World/Motion/StaticMotion.asset` | Default `StaticMotionStrategy` asset, referenced by zones that don't drift. |
| `Assets/Scripts/World/Zones/WildernessZone.cs` | `NetworkBehaviour, ISaveable, IWorldZone`. Holds harvestables (phase 1) + future wildlife. |
| `Assets/Scripts/World/Zones/WildernessZoneSaveData.cs` | `[Serializable]` DTO for zone save/restore. |
| `Assets/Scripts/World/Zones/WildernessZoneDef.cs` | `ScriptableObject` — biome-override, radius, motion-strategy list, harvestable seed table. |
| `Assets/Scripts/World/Zones/WildernessZoneManager.cs` | Server-side singleton exposing `SpawnZone(pos, def, parent=null)`. |

**Moved + extended (1 → 1, rename):**

| From | To | Change |
|------|----|--------|
| `Assets/Scripts/Weather/BiomeRegion.cs` | `Assets/Scripts/World/MapSystem/Region.cs` | Class renamed `BiomeRegion` → `Region`; namespace `MWI.Weather` → `MWI.WorldSystem`; implements `IWorldZone`; adds `Maps` + `WildernessZones` lists auto-discovered in `Awake`; save payload extended with `DynamicWildernessZones`. **Move `.cs` + `.meta` together to preserve asset GUID**. |

**Renamed (1 → 1, rename within folder):**

| From | To | Change |
|------|----|--------|
| `Assets/Scripts/World/MapSystem/CommunityTracker.cs` | `Assets/Scripts/World/MapSystem/MapRegistry.cs` | Class renamed `CommunityTracker` → `MapRegistry`; `CommunityTrackerSaveData` → `MapRegistrySaveData`; `SaveKey = "CommunityTracker_Data"` **unchanged** for save compat. ~200 lines of cluster-promotion code deleted. |

**Edited files (existing):**

| Path | Change |
|------|--------|
| `Assets/Scripts/World/Data/WorldSettingsData.cs` | Add `MapMinSeparation` field; delete obsolete `SettlementMinPopulation` / other cluster-only fields. |
| `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` | Remove `_nearbyMapJoinRadius`; read from `WorldSettingsData.MapMinSeparation`. |
| `Assets/Scripts/Character/Components/CharacterMapTracker.cs` | Add `CurrentRegionId` NetworkVariable + server-side position-change update (0.25s throttle). |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | Add step 5 "Zone Motion" to the catch-up loop. |
| `Assets/Scripts/Weather/WeatherFront.cs` | Update `_parentRegion` field type + namespace reference (`BiomeRegion` → `Region`). |
| `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/Weather/GlobalWindController.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/Weather/WeatherFrontSnapshot.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/Weather/BiomeClimateProfile.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/World/Data/BiomeDefinition.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs` | Update `BiomeRegion` → `Region` references. |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | Update `BiomeRegion` → `Region` references. |
| `CLAUDE.md` | Rewrite Rule 30 per spec Section 10. |
| `.agent/skills/world-system/SKILL.md` | Replace cluster-promotion content; document `Region` / `WildernessZone` / `IWorldZone`. |
| `.agent/skills/building_system/SKILL.md` | Replace `_nearbyMapJoinRadius` references with `MapMinSeparation`. |
| `.agent/skills/community-system/SKILL.md` | Note `MapRegistry` rename + removal of cluster promotion. |

**Scene work:**

| Path | Change |
|------|--------|
| `Assets/Scenes/<default test scene>.unity` | Place one authored `Region` GameObject containing existing `MapController`s as children (identified in Task 14). |

---

## Pre-flight — References You Will Need

Keep these open in tabs before starting:

- [Assets/Scripts/World/MapSystem/CommunityTracker.cs](../../../Assets/Scripts/World/MapSystem/CommunityTracker.cs) — target of rename + rip. Read in full before Task 4.
- [Assets/Scripts/Weather/BiomeRegion.cs](../../../Assets/Scripts/Weather/BiomeRegion.cs) — target of rename + extend. Read in full before Task 6.
- [Assets/Scripts/World/MapSystem/MapController.cs](../../../Assets/Scripts/World/MapSystem/MapController.cs) — already implements spatial lookup via `_mapRegistry` static dict; `GetMapAtPosition`; `GetNearestExteriorMap` (added recently).
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — target of Zone Motion extension (Task 13).
- [Assets/Scripts/World/Buildings/BuildingPlacementManager.cs](../../../Assets/Scripts/World/Buildings/BuildingPlacementManager.cs) L16-35 — `_nearbyMapJoinRadius` field (removed in Task 11).
- [Assets/Scripts/Character/Components/CharacterMapTracker.cs](../../../Assets/Scripts/Character/Components/CharacterMapTracker.cs) — pattern for `NetworkVariable<FixedString128Bytes>` + server-side writes (Task 12).
- [Assets/Scripts/World/Data/WorldSettingsData.cs](../../../Assets/Scripts/World/Data/WorldSettingsData.cs) — ScriptableObject for global world settings (Task 3).

**CLAUDE.md rules that apply throughout:**
- Rule 15: private fields prefixed with `_`.
- Rule 16: unsubscribe from events and stop coroutines in `OnDestroy`.
- Rule 19: every networked feature must be validated against Host↔Client, Client↔Client, Host/Client↔NPC.
- Rule 22: any effect must go through `CharacterAction` (not applicable here — no gameplay actions touched).
- Rule 31: wrap fallible operations (I/O, deserialization, network callbacks) in `try/catch` + `Debug.LogException`.
- Rule 32: 11 Unity units = 1.67m — `MapMinSeparation = 150` units ≈ 22.7m between zone centers.

**Verification tools throughout:**
- `mcp__ai-game-developer__assets-refresh` — forces Unity recompile after `.cs` changes.
- `mcp__ai-game-developer__console-get-logs` with `logTypeFilter: "Error"` + `lastMinutes: 2` — detects compile errors / runtime exceptions.
- `mcp__ai-game-developer__script-execute` — run ad-hoc C# via Roslyn for lookup smoke tests.

---

## Task 1: Add Core Zone Interfaces

**Files:**
- Create: `Assets/Scripts/World/Zones/IWorldZone.cs`
- Create: `Assets/Scripts/World/Zones/IZoneMotionStrategy.cs`
- Create: `Assets/Scripts/World/Zones/IStreamable.cs`

- [ ] **Step 1: Create `IWorldZone.cs`**

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Shared contract for any spatial entity in the world hierarchy.
    /// Implemented by Region, MapController, WildernessZone, and (future) WeatherFront.
    /// Used for uniform spatial queries (containment, distance, separation checks).
    /// </summary>
    public interface IWorldZone
    {
        /// <summary>Stable string identifier. Survives save/load.</summary>
        string ZoneId { get; }

        /// <summary>World-space center of the zone. For zones with a BoxCollider, this is typically the collider bounds center.</summary>
        Vector3 Center { get; }

        /// <summary>Effective radius in Unity units. Used for separation checks and streaming.</summary>
        float Radius { get; }

        /// <summary>True if the given world position is inside the zone's bounds.</summary>
        bool Contains(Vector3 worldPos);

        /// <summary>Distance from the zone's surface to the given world position. Returns 0 if inside.</summary>
        float DistanceTo(Vector3 worldPos);
    }
}
```

- [ ] **Step 2: Create `IZoneMotionStrategy.cs`**

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Pluggable motion rule for a world zone. Evaluated daily by MacroSimulator.
    /// Implementations are typically ScriptableZoneMotionStrategy SO assets so they can be editor-assigned.
    /// </summary>
    public interface IZoneMotionStrategy
    {
        /// <summary>
        /// Returns the desired XZ world delta for this zone on the given day.
        /// Return Vector3.zero for static behavior. MacroSimulator sums deltas across strategies and clamps by MapMinSeparation.
        /// </summary>
        Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
    }
}
```

- [ ] **Step 3: Create `IStreamable.cs`**

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Contract for content that materializes in and out of a WildernessZone based on player proximity.
    /// Phase 1 is data-only — live prefab instantiation implementations land in Phase 2 for harvestables and Phase 3 for wildlife.
    /// </summary>
    public interface IStreamable
    {
        string Id { get; }
        Vector3 WorldPosition { get; }

        /// <summary>Server-only. Instantiate the live representation at the given position.</summary>
        GameObject MaterializeAt(Vector3 pos);

        /// <summary>Server-only. Capture live state back into the record and destroy the GameObject.</summary>
        void SnapshotAndRelease(GameObject live);
    }
}
```

- [ ] **Step 4: Compile + verify no errors**

Use MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2 maxEntries=30
```
Expected: no new errors beyond the pre-existing project-path warning.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Zones/
git commit -m "feat(world): add IWorldZone, IZoneMotionStrategy, IStreamable interfaces"
```

---

## Task 2: Add `ScriptableZoneMotionStrategy` + `StaticMotionStrategy` + default asset

**Files:**
- Create: `Assets/Scripts/World/Zones/ScriptableZoneMotionStrategy.cs`
- Create: `Assets/Scripts/World/Zones/StaticMotionStrategy.cs`
- Create: `Assets/Resources/Data/World/Motion/StaticMotion.asset`

- [ ] **Step 1: Create `ScriptableZoneMotionStrategy.cs`**

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Abstract ScriptableObject base for zone-motion strategies. Editor-assignable on WildernessZone and WeatherFront.
    /// Concrete subclasses implement ComputeDailyDelta.
    /// </summary>
    public abstract class ScriptableZoneMotionStrategy : ScriptableObject, IZoneMotionStrategy
    {
        public abstract Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay);
    }
}
```

- [ ] **Step 2: Create `StaticMotionStrategy.cs`**

```csharp
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Default motion strategy — zero daily delta. Zones with this strategy (and no others) never move.
    /// </summary>
    [CreateAssetMenu(fileName = "StaticMotion", menuName = "MWI/World/Motion/Static", order = 0)]
    public class StaticMotionStrategy : ScriptableZoneMotionStrategy
    {
        public override Vector3 ComputeDailyDelta(IWorldZone zone, int currentDay) => Vector3.zero;
    }
}
```

- [ ] **Step 3: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```
Expected: clean.

- [ ] **Step 4: Create the default `StaticMotion.asset`**

In the Unity Project window: right-click `Assets/Resources/Data/World/` → create folder `Motion` if it doesn't exist → inside, right-click → `Create > MWI > World > Motion > Static`. Name the asset `StaticMotion`. This produces `Assets/Resources/Data/World/Motion/StaticMotion.asset` + `.meta`.

Alternatively via MCP:
```
mcp__ai-game-developer__assets-create-folder parentFolder=Assets/Resources/Data/World destinationName=Motion
# Then use gameobject/object tooling to instantiate a StaticMotionStrategy SO.
```

If MCP can't create SO assets directly, do it manually in the editor. Verify the file exists:
```
mcp__ai-game-developer__assets-find filter="t:StaticMotionStrategy"
```
Expected: one result at `Assets/Resources/Data/World/Motion/StaticMotion.asset`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Zones/ScriptableZoneMotionStrategy.cs
git add Assets/Scripts/World/Zones/StaticMotionStrategy.cs
git add Assets/Resources/Data/World/Motion/
git commit -m "feat(world): add ScriptableZoneMotionStrategy + StaticMotionStrategy default"
```

---

## Task 3: Add `MapMinSeparation` to `WorldSettingsData`; delete obsolete cluster fields

**Files:**
- Modify: `Assets/Scripts/World/Data/WorldSettingsData.cs`

- [ ] **Step 1: Read the current `WorldSettingsData.cs` fully to identify obsolete fields**

Look for the `[Header("Community Tracker: Settlement Promotion")]` block and related fields (`SettlementMinPopulation`, `SettlementStableDays`, etc.). These are cluster-promotion only — they get deleted in Task 5 as part of the rip, but for now **leave them in place** so the current code still compiles. Task 5 removes them after `PromoteToSettlement` is deleted.

- [ ] **Step 2: Add `MapMinSeparation` field near the existing "Proximity" header**

In `WorldSettingsData.cs`, find the `[Header("Community Tracker: Proximity")]` block and add a new header block above or below it:

```csharp
[Header("Zone Placement")]
[Tooltip("Minimum world-unit distance between any two IWorldZone centers. Enforced at building placement AND procedural zone spawning. 11 units = 1.67m (see CLAUDE.md rule 32). Default 150 units ≈ 22.7m.")]
public float MapMinSeparation = 150f;
```

Place it right below `ProximityChunkSize`. Preserve the existing value for `ProximityChunkSize` — it's still used by many places and not being deleted.

- [ ] **Step 3: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```
Expected: clean.

- [ ] **Step 4: Verify the field shows up on the asset**

The `WorldSettingsData` asset lives at `Assets/Resources/Data/World/WorldSettingsData.asset`. Open it in the Inspector — the new "Zone Placement" section should appear with `MapMinSeparation = 150`. No manual set required; the field defaults to 150.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Data/WorldSettingsData.cs
git commit -m "feat(world): add MapMinSeparation to WorldSettingsData"
```

---

## Task 4: Rename `CommunityTracker` → `MapRegistry` (pure refactor, no behavior change)

**Files:**
- Move + rename: `Assets/Scripts/World/MapSystem/CommunityTracker.cs` → `Assets/Scripts/World/MapSystem/MapRegistry.cs`
- Edit: all callsites (grep in Step 3 below).

**This task is a mechanical rename. Task 5 does the rip.** Splitting keeps commits reviewable.

- [ ] **Step 1: Rename the class file on disk (preserve `.meta`)**

```bash
git mv Assets/Scripts/World/MapSystem/CommunityTracker.cs Assets/Scripts/World/MapSystem/MapRegistry.cs
git mv Assets/Scripts/World/MapSystem/CommunityTracker.cs.meta Assets/Scripts/World/MapSystem/MapRegistry.cs.meta
```

Using `git mv` preserves the `.meta` GUID so Unity's asset tracking stays intact.

- [ ] **Step 2: Inside `MapRegistry.cs`, rename the class declarations**

Find and replace **within that one file only**:
- `public class CommunityTracker` → `public class MapRegistry`
- `[Serializable] public class CommunityTrackerSaveData` → `[Serializable] public class MapRegistrySaveData`
- All internal `CommunityTracker.Instance` self-references (if any) → `MapRegistry.Instance`

**Keep `SaveKey = "CommunityTracker_Data"` unchanged** — this is intentional, for save-file backwards compatibility.

Keep the `PendingClusters` / `RoamingClusterData` references in place — Task 5 deletes them, not this task.

- [ ] **Step 3: Find all callsites and update them**

```
mcp__ai-game-developer__assets-refresh  # regenerate the AssetDatabase
```

Then use Grep:
```
Grep pattern="CommunityTracker" glob="*.cs" output_mode="files_with_matches"
```

Expected callsites (each needs `CommunityTracker` → `MapRegistry` except the SaveKey string constant):
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — multiple `CommunityTracker.Instance`
- `Assets/Scripts/World/MapSystem/MapController.cs` — multiple `CommunityTracker.Instance`
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs` — likely `CommunityTracker.Instance`
- `Assets/Scripts/Character/**/*.cs` — grep confirms

For each file: replace bare `CommunityTracker` with `MapRegistry`. **Do NOT touch the string literal `"CommunityTracker_Data"`** used as `SaveKey`.

Do the same for any `CommunityTrackerSaveData` → `MapRegistrySaveData` type references.

- [ ] **Step 4: Compile + fix any residual errors**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2 maxEntries=50
```
Expected: clean. If errors remain, `Grep pattern="CommunityTracker"` again to catch misses.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(world): rename CommunityTracker to MapRegistry (SaveKey preserved for compat)"
```

---

## Task 5: Rip NPC-cluster auto-promotion from `MapRegistry`

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs`
- Modify: `Assets/Scripts/World/Data/WorldSettingsData.cs` (delete obsolete cluster fields)

- [ ] **Step 1: Delete the cluster detection + promotion methods from `MapRegistry.cs`**

Delete the following in full:
- `EvaluatePopulations()` method (~60 lines)
- `PromoteToSettlement(CommunityData comm, int currentDay)` method (~100 lines)
- The `_pendingClusters` private field
- The `RoamingClusterData` class declaration (~5 lines; it's at the top of the file)
- Any `CurrentDailyPopulation` / `_pendingClusters` reset logic inside `EvaluatePopulations`

Also modify `HandleNewDay()`:

**Before:**
```csharp
private void HandleNewDay()
{
    if (!NetworkManager.Singleton.IsServer) return;
    EvaluatePopulations();
    ProcessPendingBuildingClaims();
}
```

**After:**
```csharp
private void HandleNewDay()
{
    if (!NetworkManager.Singleton.IsServer) return;
    ProcessPendingBuildingClaims();
}
```

- [ ] **Step 2: Shrink `MapRegistrySaveData`**

**Before:**
```csharp
[Serializable]
public class MapRegistrySaveData
{
    public List<CommunityData> Communities = new List<CommunityData>();
    public List<RoamingClusterData> PendingClusters = new List<RoamingClusterData>();
}
```

**After:**
```csharp
[Serializable]
public class MapRegistrySaveData
{
    public List<CommunityData> Communities = new List<CommunityData>();
}
```

- [ ] **Step 3: Add back-compat handling in `RestoreState`**

The save format changed. Old saves will still contain a `PendingClusters` field blob in the JSON payload. Newtonsoft.Json silently drops unknown fields by default, so no explicit code is needed — but add a one-line log to document it:

Find the `RestoreState(object state)` method. At the start, after the cast to `MapRegistrySaveData` (or after the `JsonConvert.DeserializeObject<MapRegistrySaveData>(...)` call if that's how it's structured), add:

```csharp
// Legacy CommunityTrackerSaveData may have contained PendingClusters;
// those are silently discarded by JSON deserialization. Log so this is traceable.
Debug.Log($"<color=cyan>[MapRegistry:RestoreState]</color> Restored {restored.Communities.Count} communities. Legacy cluster data (if any) discarded.");
```

- [ ] **Step 4: Delete obsolete cluster fields from `WorldSettingsData.cs`**

Remove the entire `[Header("Community Tracker: Settlement Promotion")]` section and any fields it introduced (`SettlementMinPopulation`, `SettlementStableDays`, `SettlementDissolveThreshold`, `SettlementDroppedBelowGrace`, etc. — delete all fields under that header; keep fields under `[Header("Community Tracker: Proximity")]`).

Grep first to confirm which fields exist:
```
Grep pattern="Settlement|RoamingCamp" path="Assets/Scripts/World/Data/WorldSettingsData.cs" output_mode="content" -n=true
```

Delete every field that's exclusively about settlement promotion. Keep `ProximityChunkSize` — it's still used elsewhere.

If any callsite references one of the deleted fields, the compile will fail. Search:
```
Grep pattern="SettlementMinPopulation|SettlementStableDays|SettlementDissolveThreshold" glob="*.cs" output_mode="files_with_matches"
```
Expected: only the (now-deleted) `EvaluatePopulations` / `PromoteToSettlement` in `MapRegistry.cs`. No other callers should exist. If they do, that means the rip is incomplete — investigate.

- [ ] **Step 5: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2 maxEntries=50
```
Expected: clean.

- [ ] **Step 6: Smoke-test — enter play mode and watch the console**

```
mcp__ai-game-developer__editor-application-set-state (set playmode on)
```
Wait ~5 seconds.
```
mcp__ai-game-developer__console-get-logs logTypeFilter=Exception lastMinutes=1
mcp__ai-game-developer__editor-application-set-state (set playmode off)
```
Expected: no exceptions related to `MapRegistry`, `EvaluatePopulations`, or `RoamingClusterData`. The game should boot and run; NPCs won't auto-form communities anymore, which is intentional.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(world): rip NPC-cluster auto-promotion from MapRegistry

- Delete EvaluatePopulations, PromoteToSettlement, RoamingClusterData, _pendingClusters
- Remove PendingClusters from MapRegistrySaveData (legacy JSON fields silently discarded on load)
- Remove obsolete Settlement* fields from WorldSettingsData
- HandleNewDay now calls only ProcessPendingBuildingClaims"
```

---

## Task 6: Rename `BiomeRegion` → `Region`; move file; update 11 callsites

**Files:**
- Move: `Assets/Scripts/Weather/BiomeRegion.cs` → `Assets/Scripts/World/MapSystem/Region.cs` (+ `.meta`)
- Edit callsites (list below)

**This is a pure mechanical rename + move. Task 7 does the feature extension.** Split for reviewability.

- [ ] **Step 1: Ensure the destination folder exists**

```bash
ls Assets/Scripts/World/MapSystem/
```
Expected: contains `MapController.cs`, `MapRegistry.cs`, `MacroSimulator.cs`, etc. Good.

- [ ] **Step 2: Move the file preserving `.meta`**

```bash
git mv Assets/Scripts/Weather/BiomeRegion.cs Assets/Scripts/World/MapSystem/Region.cs
git mv Assets/Scripts/Weather/BiomeRegion.cs.meta Assets/Scripts/World/MapSystem/Region.cs.meta
```

The `.meta` file contains the asset GUID. Moving it alongside the `.cs` preserves that GUID so scene-serialized component references survive.

- [ ] **Step 3: Inside `Region.cs`, rename the namespace and class**

Find and replace **within Region.cs only**:
- `namespace MWI.Weather` → `namespace MWI.WorldSystem`
- `public class BiomeRegion` → `public class Region`
- `private static List<BiomeRegion> _allRegions` → `private static List<Region> _allRegions`
- `public static BiomeRegion GetRegionAtPosition` → `public static Region GetRegionAtPosition`
- `public static List<BiomeRegion> GetAdjacentRegions(BiomeRegion region)` → `public static List<Region> GetAdjacentRegions(Region region)`
- Any other internal `BiomeRegion` type references within the file

**Do NOT change the serialized field names** (e.g. `_regionId`, `_biomeDefinition`) — Unity's serialization tracks fields by name, and changing them would wipe existing scene data.

Keep the `[RequireComponent(typeof(BoxCollider))]` attribute as-is.

- [ ] **Step 4: Find all callsites outside Region.cs**

```
Grep pattern="BiomeRegion" glob="*.cs" output_mode="files_with_matches"
```

Expected ~10 files beyond `Region.cs` itself:
- `Assets/Scripts/Terrain/TerrainWeatherProcessor.cs`
- `Assets/Scripts/Weather/GlobalWindController.cs`
- `Assets/Scripts/Weather/WeatherFront.cs`
- `Assets/Scripts/Weather/WeatherFrontSnapshot.cs`
- `Assets/Scripts/Weather/BiomeClimateProfile.cs`
- `Assets/Scripts/World/MapSystem/MacroSimulator.cs`
- `Assets/Scripts/World/Data/BiomeDefinition.cs`
- `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs`
- `Assets/Scripts/Core/SaveLoad/SaveManager.cs`

For each file:
1. Replace type references `BiomeRegion` → `Region`.
2. If the file has `using MWI.Weather;` and **only** uses `BiomeRegion` from that namespace, change to `using MWI.WorldSystem;` (or remove the using if `Region` is in the same namespace).
3. If the file uses other `MWI.Weather` types in addition, keep `using MWI.Weather;` AND add `using MWI.WorldSystem;` so the new `Region` resolves.

Special case — `Assets/Scripts/Weather/WeatherFront.cs`:
```csharp
// BEFORE
private BiomeRegion _parentRegion;

public void Initialize(BiomeRegion parent, WeatherType type, ...)

// AFTER
private Region _parentRegion;

public void Initialize(Region parent, WeatherType type, ...)
```
And add `using MWI.WorldSystem;` at the top if not present.

- [ ] **Step 5: Compile + fix stragglers**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2 maxEntries=50
```

If errors remain, re-grep `BiomeRegion` — any bare reference still in the codebase means a missed update. Fix and retry until clean.

- [ ] **Step 6: Scene reference verification**

Since we moved `.cs` + `.meta` together, scene-serialized components pointing to the old `BiomeRegion` script should auto-resolve to the new `Region` script (same GUID). Verify:
```
mcp__ai-game-developer__gameobject-find name="*"  # list scene hierarchy
```
(or open the main test scene in the Unity Editor and check that any `Region` / former `BiomeRegion` components still show inspector UI — no "Missing (Mono Script)" warnings).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(world): rename BiomeRegion to Region, move to World/MapSystem/

- File moved with .meta GUID preserved (scene refs intact)
- Namespace MWI.Weather -> MWI.WorldSystem
- 10 call-site files updated
- No behavior change (feature extension in next task)"
```

---

## Task 7: Extend `Region` with `IWorldZone` + `Maps` + `WildernessZones` auto-discovery

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/Region.cs`

- [ ] **Step 1: Make `Region` implement `IWorldZone`**

At the top of the file:

**Before:**
```csharp
public class Region : MonoBehaviour, ISaveable
{
```

**After:**
```csharp
public class Region : MonoBehaviour, IWorldZone, ISaveable
{
```

- [ ] **Step 2: Add new fields and properties for child tracking**

Add inside the class, near the existing private fields:

```csharp
// --- NEW: Child MapController and WildernessZone tracking (Phase 1) ---
private readonly List<MapController> _maps = new List<MapController>();
private readonly List<WildernessZone> _wildernessZones = new List<WildernessZone>();
public IReadOnlyList<MapController> Maps => _maps;
public IReadOnlyList<WildernessZone> WildernessZones => _wildernessZones;
```

Don't forget `using` statements if needed — `List<>` needs `using System.Collections.Generic;` (should already be present).

- [ ] **Step 3: Auto-discover children in `Awake`**

Find the existing `Awake()` method. Extend it:

**Before (likely):**
```csharp
private void Awake()
{
    _bounds = GetComponent<BoxCollider>();
    _bounds.isTrigger = true;
    _allRegions.Add(this);
}
```

**After:**
```csharp
private void Awake()
{
    _bounds = GetComponent<BoxCollider>();
    _bounds.isTrigger = true;
    _allRegions.Add(this);

    // Auto-discover child MapControllers and WildernessZones.
    // Dynamic additions (via WildernessZoneManager) call RegisterWildernessZone / UnregisterWildernessZone directly.
    _maps.AddRange(GetComponentsInChildren<MapController>(includeInactive: true));
    _wildernessZones.AddRange(GetComponentsInChildren<WildernessZone>(includeInactive: true));
    Debug.Log($"<color=cyan>[Region:Awake]</color> '{_regionId}' discovered {_maps.Count} maps, {_wildernessZones.Count} wilderness zones.");
}
```

- [ ] **Step 4: Add dynamic-zone register/unregister helpers**

Add these public methods so `WildernessZoneManager` (Task 10) can attach spawned zones:

```csharp
/// <summary>Called by WildernessZoneManager when a zone is spawned inside this region at runtime.</summary>
public void RegisterWildernessZone(WildernessZone zone)
{
    if (zone == null || _wildernessZones.Contains(zone)) return;
    _wildernessZones.Add(zone);
}

/// <summary>Called on zone despawn to keep the list clean.</summary>
public void UnregisterWildernessZone(WildernessZone zone)
{
    if (zone == null) return;
    _wildernessZones.Remove(zone);
}
```

- [ ] **Step 5: Implement `IWorldZone` members**

Add to the class (as a new region labeled "IWorldZone"):

```csharp
// --- IWorldZone ---
public string ZoneId => _regionId;
public Vector3 Center => _bounds != null ? _bounds.bounds.center : transform.position;
public float Radius => _bounds != null ? _bounds.bounds.extents.magnitude : 0f;

public bool Contains(Vector3 worldPos)
    => _bounds != null && _bounds.bounds.Contains(worldPos);

public float DistanceTo(Vector3 worldPos)
{
    if (_bounds == null) return Vector3.Distance(transform.position, worldPos);
    Vector3 closest = _bounds.ClosestPoint(worldPos);
    return Vector3.Distance(closest, worldPos);
}
```

- [ ] **Step 6: Extend the save payload with `DynamicWildernessZones`**

Find the existing `CaptureState()` / `RestoreState(object)` methods. The existing save payload type (whatever it's called — likely an internal struct or `BiomeRegionSaveData`) needs one new field.

If the save type is declared inside `Region.cs`, add:

```csharp
[Serializable]
private class RegionSaveData
{
    // EXISTING fields (keep their names exactly — Newtonsoft serializes by name):
    public List<WeatherFrontSnapshot> HibernatedFronts = new List<WeatherFrontSnapshot>();
    public bool IsHibernating;
    public double LastHibernationTime;

    // NEW:
    public List<WildernessZoneSaveData> DynamicWildernessZones = new List<WildernessZoneSaveData>();
}
```

If the existing save type has a different name (e.g. `BiomeRegionSaveData`), **rename it to `RegionSaveData`** (keep the JSON field names the same so old saves load cleanly) and add the new field.

Populate + restore inside the existing methods:

**In `CaptureState()` — add after existing fields are populated:**
```csharp
// Dynamic wilderness zones (authored ones restore from scene, not here)
state.DynamicWildernessZones = new List<WildernessZoneSaveData>();
foreach (var zone in _wildernessZones)
{
    if (zone == null || !zone.IsDynamicallySpawned) continue;
    state.DynamicWildernessZones.Add(zone.CaptureZoneState());
}
```

**In `RestoreState(object state)` — add after existing restoration, before the method returns:**
```csharp
// Respawn dynamic wilderness zones via the manager
if (data.DynamicWildernessZones != null && WildernessZoneManager.Instance != null)
{
    foreach (var zoneData in data.DynamicWildernessZones)
    {
        try
        {
            WildernessZoneManager.Instance.RestoreZone(zoneData, this);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
```

**NOTE:** `WildernessZone.IsDynamicallySpawned`, `WildernessZone.CaptureZoneState()`, and `WildernessZoneManager.RestoreZone(...)` are defined in Tasks 8 and 10. This task will **not compile** until Task 8 lands. That's fine — we commit this task's changes and the repo becomes compiling again after Task 10. If the interim broken compile is not acceptable in your workflow, you may defer the save-payload edit (Step 6) until Task 10 and commit Tasks 8 and 9 in between.

If you prefer a continuously-compiling repo: leave Step 6 as a stub pointing to a `TODO(task-10)` comment, implement Tasks 8-10, then come back and fill it in as part of Task 10's commit. **Document explicitly whichever order you pick in the commit message.**

- [ ] **Step 7: Compile (may fail until Task 8-10 land — see Step 6 note)**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```

If errors are only the expected `WildernessZone` / `WildernessZoneSaveData` / `WildernessZoneManager` symbol-not-found, proceed. All other errors must be fixed before commit.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/MapSystem/Region.cs
git commit -m "feat(world): extend Region with IWorldZone + Maps/WildernessZones lists

- Implement IWorldZone (ZoneId, Center, Radius, Contains, DistanceTo)
- Auto-discover child MapControllers and WildernessZones in Awake
- RegisterWildernessZone/UnregisterWildernessZone for runtime-spawned zones
- Extend save payload with DynamicWildernessZones (restored via WildernessZoneManager)

Note: depends on Tasks 8-10 for WildernessZone* symbols. Repo may not
compile until Task 10; saved wilderness state restored via Task 10's
WildernessZoneManager.RestoreZone."
```

---

## Task 8: Create `WildernessZone` + `WildernessZoneSaveData`

**Files:**
- Create: `Assets/Scripts/World/Zones/WildernessZone.cs`
- Create: `Assets/Scripts/World/Zones/WildernessZoneSaveData.cs`

- [ ] **Step 1: Create `WildernessZoneSaveData.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Serializable snapshot of a WildernessZone for save/load.
    /// Holds enough data to respawn the zone with its harvestable + future wildlife contents.
    /// </summary>
    [Serializable]
    public class WildernessZoneSaveData
    {
        public string ZoneId;
        public Vector3 Center;
        public float Radius;
        /// <summary>Resources path to a BiomeDefinition override, or null to inherit from the parent Region.</summary>
        public string BiomeOverrideAssetPath;
        public List<ResourcePoolEntry> Harvestables = new List<ResourcePoolEntry>();
        /// <summary>Wildlife records (empty in Phase 1; populated once animal ecology ships).</summary>
        public List<HibernatedNPCData> Wildlife = new List<HibernatedNPCData>();
        /// <summary>Resources paths to ScriptableZoneMotionStrategy assets driving this zone.</summary>
        public List<string> MotionStrategyAssetPaths = new List<string>();
        public bool IsDynamicallySpawned;
    }
}
```

- [ ] **Step 2: Create `WildernessZone.cs`**

```csharp
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    /// <summary>
    /// A virtual-content region inside a Region. Holds harvestables and future wildlife records
    /// that stream in only when a player is within spawn radius. Can move via pluggable
    /// IZoneMotionStrategy ScriptableObject assets evaluated daily by MacroSimulator.
    /// </summary>
    public class WildernessZone : NetworkBehaviour, IWorldZone, ISaveable
    {
        [Header("Identity")]
        [SerializeField] private string _zoneId;
        [SerializeField] private float _radius = 75f;

        [Header("Parent")]
        [SerializeField] private Region _parentRegion;  // null allowed — zone can exist outside any region

        [Header("Contents (data-layer)")]
        [SerializeField] private List<ResourcePoolEntry> _harvestables = new List<ResourcePoolEntry>();
        [SerializeField] private List<HibernatedNPCData> _wildlife = new List<HibernatedNPCData>();

        [Header("Motion")]
        [SerializeField] private List<ScriptableZoneMotionStrategy> _motionStrategies = new List<ScriptableZoneMotionStrategy>();

        [Header("Lifecycle")]
        [SerializeField] private bool _isDynamicallySpawned;

        /// <summary>True if this zone was spawned at runtime (not placed in the scene prefab).</summary>
        public bool IsDynamicallySpawned => _isDynamicallySpawned;
        public Region ParentRegion => _parentRegion;
        public List<ResourcePoolEntry> Harvestables => _harvestables;
        public List<HibernatedNPCData> Wildlife => _wildlife;
        public IReadOnlyList<ScriptableZoneMotionStrategy> MotionStrategies => _motionStrategies;

        // --- IWorldZone ---
        public string ZoneId => _zoneId;
        public Vector3 Center => transform.position;
        public float Radius => _radius;
        public bool Contains(Vector3 worldPos)
            => (worldPos - transform.position).sqrMagnitude <= _radius * _radius;
        public float DistanceTo(Vector3 worldPos)
        {
            float dist = Vector3.Distance(worldPos, transform.position);
            return Mathf.Max(0f, dist - _radius);
        }

        // --- Server-side init ---
        /// <summary>Called by WildernessZoneManager.SpawnZone after the NetworkObject is spawned.</summary>
        public void InitializeAsDynamic(string zoneId, float radius, Region parent,
            List<ScriptableZoneMotionStrategy> motionStrategies,
            List<ResourcePoolEntry> seededHarvestables)
        {
            if (!IsServer)
            {
                Debug.LogError("<color=red>[WildernessZone:InitializeAsDynamic]</color> Must be called on server.");
                return;
            }
            _zoneId = zoneId;
            _radius = radius;
            _parentRegion = parent;
            _motionStrategies = motionStrategies != null
                ? new List<ScriptableZoneMotionStrategy>(motionStrategies)
                : new List<ScriptableZoneMotionStrategy>();
            _harvestables = seededHarvestables != null
                ? new List<ResourcePoolEntry>(seededHarvestables)
                : new List<ResourcePoolEntry>();
            _isDynamicallySpawned = true;
            parent?.RegisterWildernessZone(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _parentRegion?.UnregisterWildernessZone(this);
        }

        // --- Save ---
        public string SaveKey => string.IsNullOrEmpty(_zoneId) ? null : $"WildernessZone_{_zoneId}";

        /// <summary>
        /// Captures the zone's current state for serialization by its parent Region.
        /// This is called by Region.CaptureState for dynamic zones only (authored zones restore from scene).
        /// </summary>
        public WildernessZoneSaveData CaptureZoneState()
        {
            var data = new WildernessZoneSaveData
            {
                ZoneId = _zoneId,
                Center = transform.position,
                Radius = _radius,
                IsDynamicallySpawned = _isDynamicallySpawned,
                Harvestables = new List<ResourcePoolEntry>(_harvestables),
                Wildlife = new List<HibernatedNPCData>(_wildlife),
            };

            foreach (var strategy in _motionStrategies)
            {
                if (strategy == null) continue;
                // Resources path (strip "Assets/Resources/" prefix and ".asset" suffix at callsite if needed).
                // For simplicity, store the asset name; WildernessZoneManager resolves via Resources.Load.
                data.MotionStrategyAssetPaths.Add(strategy.name);
            }

            return data;
        }

        // Boilerplate ISaveable (individual zone saves go through Region aggregation, so these are no-ops).
        public object CaptureState() => null;
        public void RestoreState(object state) { }
    }
}
```

- [ ] **Step 3: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```
Expected: clean. If errors reference `ResourcePoolEntry` or `HibernatedNPCData`, ensure the `using` namespace for each resolves. Grep for their declarations:
```
Grep pattern="class ResourcePoolEntry|class HibernatedNPCData" glob="*.cs" output_mode="files_with_matches"
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Zones/WildernessZone.cs Assets/Scripts/World/Zones/WildernessZoneSaveData.cs
git commit -m "feat(world): add WildernessZone (NetworkBehaviour) + WildernessZoneSaveData"
```

---

## Task 9: Create `WildernessZoneDef` SO + `HarvestableSeedingTable`

**Files:**
- Create: `Assets/Scripts/World/Zones/WildernessZoneDef.cs`
- Create: `Assets/Scripts/World/Zones/HarvestableSeedingTable.cs`

- [ ] **Step 1: Create `HarvestableSeedingTable.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Simple data container mapping harvestable ResourceIds to their initial pool sizes
    /// when a new wilderness zone is spawned. Used by WildernessZoneDef.
    /// </summary>
    [Serializable]
    public class HarvestableSeedingTable
    {
        [Serializable]
        public struct Entry
        {
            public string ResourceId;
            public float InitialAmount;
            public float MaxAmount;
        }

        public List<Entry> Entries = new List<Entry>();

        /// <summary>Produces a list of ResourcePoolEntry structs suitable for WildernessZone._harvestables.</summary>
        public List<ResourcePoolEntry> BuildInitialPool(int currentDay)
        {
            var pool = new List<ResourcePoolEntry>();
            foreach (var entry in Entries)
            {
                if (string.IsNullOrEmpty(entry.ResourceId)) continue;
                pool.Add(new ResourcePoolEntry
                {
                    ResourceId = entry.ResourceId,
                    CurrentAmount = entry.InitialAmount,
                    MaxAmount = entry.MaxAmount,
                    LastHarvestedDay = currentDay,
                });
            }
            return pool;
        }
    }
}
```

- [ ] **Step 2: Create `WildernessZoneDef.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Designer-authored template for spawning a WildernessZone. Passed into
    /// WildernessZoneManager.SpawnZone(pos, def, parent) to produce a configured zone.
    /// </summary>
    [CreateAssetMenu(fileName = "NewWildernessZoneDef", menuName = "MWI/World/WildernessZoneDef", order = 10)]
    public class WildernessZoneDef : ScriptableObject
    {
        [Tooltip("Default radius in Unity units (11 units = 1.67m). Typical: 75 units ≈ 11.4m = 1 chunk.")]
        public float DefaultRadius = 75f;

        [Tooltip("Optional biome override. Null = inherit from parent Region's DefaultBiome.")]
        public BiomeDefinition BiomeOverride;

        [Tooltip("Motion strategies applied daily by MacroSimulator. Leave empty to default to StaticMotion.")]
        public List<ScriptableZoneMotionStrategy> DefaultMotion = new List<ScriptableZoneMotionStrategy>();

        [Tooltip("Optional table seeding initial harvestable pool entries.")]
        public HarvestableSeedingTable HarvestableSeedTable;
    }
}
```

- [ ] **Step 3: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Zones/WildernessZoneDef.cs Assets/Scripts/World/Zones/HarvestableSeedingTable.cs
git commit -m "feat(world): add WildernessZoneDef SO + HarvestableSeedingTable"
```

---

## Task 10: Create `WildernessZoneManager` + `SpawnZone` + `RestoreZone`

**Files:**
- Create: `Assets/Scripts/World/Zones/WildernessZoneManager.cs`

- [ ] **Step 1: Create `WildernessZoneManager.cs`**

```csharp
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Server-side singleton that spawns and restores WildernessZones at runtime.
    /// Enforces WorldSettingsData.MapMinSeparation across all IWorldZone centers.
    /// </summary>
    public class WildernessZoneManager : MonoBehaviour
    {
        public static WildernessZoneManager Instance { get; private set; }

        [SerializeField] private WorldSettingsData _settings;
        [SerializeField] private GameObject _wildernessZonePrefab;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            }
            if (_wildernessZonePrefab == null)
            {
                _wildernessZonePrefab = Resources.Load<GameObject>("Prefabs/World/WildernessZone");
            }
        }

        /// <summary>
        /// Server-only. Spawns a new WildernessZone at the given position using the provided def.
        /// If parent is null, the parent Region is auto-resolved via Region.GetRegionAtPosition(pos).
        /// Rejects the spawn if another IWorldZone center is within MapMinSeparation.
        /// </summary>
        /// <returns>The new WildernessZone, or null on failure.</returns>
        public WildernessZone SpawnZone(Vector3 pos, WildernessZoneDef def, Region parent = null)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Must run on the server.");
                return null;
            }
            if (def == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> WildernessZoneDef is null.");
                return null;
            }
            if (_wildernessZonePrefab == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> WildernessZone prefab not assigned or found at Resources/Prefabs/World/WildernessZone.");
                return null;
            }

            // Separation check
            float minSep = _settings != null ? _settings.MapMinSeparation : 150f;
            if (IsTooCloseToExistingZone(pos, minSep, out string conflictId))
            {
                Debug.LogWarning($"<color=yellow>[WildernessZoneManager:SpawnZone]</color> Rejected spawn at {pos}: within {minSep} units of existing zone '{conflictId}'.");
                return null;
            }

            parent = parent ?? Region.GetRegionAtPosition(pos);

            GameObject obj;
            try
            {
                obj = Instantiate(_wildernessZonePrefab, pos, Quaternion.identity);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }

            var zone = obj.GetComponent<WildernessZone>();
            if (zone == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Prefab missing WildernessZone component.");
                Destroy(obj);
                return null;
            }

            var netObj = obj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("<color=red>[WildernessZoneManager:SpawnZone]</color> Prefab missing NetworkObject component.");
                Destroy(obj);
                return null;
            }

            try
            {
                netObj.Spawn();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Destroy(obj);
                return null;
            }

            string zoneId = $"Wild_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
            var initialPool = def.HarvestableSeedTable != null
                ? def.HarvestableSeedTable.BuildInitialPool(currentDay)
                : new List<ResourcePoolEntry>();

            zone.InitializeAsDynamic(zoneId, def.DefaultRadius, parent, def.DefaultMotion, initialPool);

            Debug.Log($"<color=magenta>[WildernessZoneManager:SpawnZone]</color> Spawned '{zoneId}' at {pos} (parent={parent?.ZoneId ?? "<none>"}, radius={def.DefaultRadius}).");
            return zone;
        }

        /// <summary>
        /// Server-only. Restores a WildernessZone from save data under the given parent Region.
        /// Called by Region.RestoreState during save-load.
        /// </summary>
        public WildernessZone RestoreZone(WildernessZoneSaveData data, Region parent)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return null;
            if (data == null || _wildernessZonePrefab == null) return null;

            GameObject obj = Instantiate(_wildernessZonePrefab, data.Center, Quaternion.identity);
            var zone = obj.GetComponent<WildernessZone>();
            var netObj = obj.GetComponent<NetworkObject>();
            if (zone == null || netObj == null)
            {
                Destroy(obj);
                return null;
            }

            netObj.Spawn();

            // Re-resolve motion strategies from Resources paths (stored as asset names)
            var motion = new List<ScriptableZoneMotionStrategy>();
            foreach (string name in data.MotionStrategyAssetPaths)
            {
                var strat = Resources.Load<ScriptableZoneMotionStrategy>($"Data/World/Motion/{name}");
                if (strat != null) motion.Add(strat);
            }

            zone.InitializeAsDynamic(data.ZoneId, data.Radius, parent, motion, data.Harvestables);

            // Re-apply wildlife records (empty in Phase 1)
            zone.Wildlife.Clear();
            zone.Wildlife.AddRange(data.Wildlife);

            Debug.Log($"<color=green>[WildernessZoneManager:RestoreZone]</color> Restored '{data.ZoneId}' under region '{parent?.ZoneId ?? "<none>"}'.");
            return zone;
        }

        // --- Internal ---

        private bool IsTooCloseToExistingZone(Vector3 pos, float minSep, out string conflictId)
        {
            conflictId = null;
            float sqr = minSep * minSep;

            // Check MapControllers (exteriors only)
            var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in allMaps)
            {
                if (m == null || m.Type == MapType.Interior) continue;
                if ((m.transform.position - pos).sqrMagnitude < sqr)
                {
                    conflictId = m.MapId;
                    return true;
                }
            }

            // Check other WildernessZones
            var allZones = UnityEngine.Object.FindObjectsByType<WildernessZone>(FindObjectsSortMode.None);
            foreach (var z in allZones)
            {
                if (z == null) continue;
                if ((z.transform.position - pos).sqrMagnitude < sqr)
                {
                    conflictId = z.ZoneId;
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 2: Create the WildernessZone prefab**

The manager needs a prefab at `Resources/Prefabs/World/WildernessZone`. In the Unity Editor:

1. Ensure folder `Assets/Resources/Prefabs/World/` exists (create if missing).
2. In the scene hierarchy, create a new empty GameObject named `WildernessZone`.
3. Add these components: `NetworkObject` (from NGO), `WildernessZone` (the one you just created).
4. On the `NetworkObject`: leave "AutoObjectParent Sync" enabled. In a later step Unity will assign a GlobalObjectIdHash; that's expected.
5. Drag it into `Assets/Resources/Prefabs/World/` to create the prefab. Delete the scene instance.
6. Verify the prefab path exists:
```
mcp__ai-game-developer__assets-find filter="WildernessZone t:GameObject"
```

- [ ] **Step 3: Register the WildernessZone prefab with `NetworkManager`**

NGO requires spawnable prefabs to be listed in a `NetworkPrefabsList`. Find the existing `NetworkPrefabsList` asset used by this project:
```
Grep pattern="NetworkPrefabsList|NetworkConfig" glob="*.cs" output_mode="files_with_matches"
```
Or inspect the `NetworkManager` GameObject in the main scene — its Network Prefabs List reference points to an asset under `Assets/`. Add the new `WildernessZone` prefab to that list via the Inspector.

If the project uses automatic NetworkPrefab registration (there's a `DefaultNetworkPrefabs.asset` that auto-adds all prefabs with `NetworkObject`), then this step is free — just verify by reopening the asset.

- [ ] **Step 4: Attach `WildernessZoneManager` to the world bootstrap scene**

The manager is a singleton `MonoBehaviour`. It needs to live in a scene that loads at boot. The simplest slot is alongside existing singletons like `CommunityTracker` / `MapRegistry`, `TimeManager`, `SaveManager`.

Find the GameObject hosting those (likely in `Assets/Scenes/Main.unity` or similar). Add a new child named `WildernessZoneManager` with the `WildernessZoneManager` component attached. In the Inspector, assign:
- `_settings` → drag in `Assets/Resources/Data/World/WorldSettingsData.asset`
- `_wildernessZonePrefab` → drag in `Assets/Resources/Prefabs/World/WildernessZone.prefab`

(The `Start()` method falls back to `Resources.Load` if not set, but explicit refs are faster and more explicit.)

- [ ] **Step 5: Go back to Region.cs and remove any Step 6 TODO stubs from Task 7**

If you deferred the save-payload edit in Task 7 Step 6, apply it now — `WildernessZone.IsDynamicallySpawned`, `WildernessZone.CaptureZoneState()`, and `WildernessZoneManager.RestoreZone(...)` all exist.

- [ ] **Step 6: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```
Expected: clean.

- [ ] **Step 7: Smoke-test — SpawnZone from the console**

Using MCP `script-execute` to trigger a runtime spawn:

```csharp
// Ad-hoc test: spawn a zone at world origin and verify it registers
using UnityEngine;
using MWI.WorldSystem;

public static class TestSpawnZone
{
    public static string Run()
    {
        if (WildernessZoneManager.Instance == null) return "manager null";
        var def = ScriptableObject.CreateInstance<WildernessZoneDef>();
        def.DefaultRadius = 50f;
        var zone = WildernessZoneManager.Instance.SpawnZone(new Vector3(0, 0, 0), def, null);
        return zone != null
            ? $"spawned {zone.ZoneId} radius={zone.Radius}"
            : "spawn failed";
    }
}
```

Run this from the Unity Test Runner or via `mcp__ai-game-developer__script-execute` while play-mode is active. Expected: `spawned Wild_<guid8> radius=50`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/World/Zones/WildernessZoneManager.cs
git add Assets/Resources/Prefabs/World/WildernessZone.prefab
# plus any scene edit
git commit -m "feat(world): add WildernessZoneManager with SpawnZone + RestoreZone

- Server-authoritative zone spawning with MapMinSeparation enforcement
- Auto-resolves parent Region via Region.GetRegionAtPosition
- Restores dynamic zones on save-load via Region aggregate payload
- Companion WildernessZone prefab under Resources/Prefabs/World/"
```

---

## Task 11: Wire `BuildingPlacementManager` to read `MapMinSeparation`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs` (enforce separation inside `CreateMapAtPosition`)

- [ ] **Step 1: Remove `_nearbyMapJoinRadius` from `BuildingPlacementManager.cs`**

Find:
```csharp
[Tooltip("When placing outside any existing MapController, the building will join the nearest exterior map within this radius (Unity units). If no map is within this radius, a new wild map is spawned centered on the placement.")]
[SerializeField] private float _nearbyMapJoinRadius = 150f;
```
Delete those two lines (attribute + field).

- [ ] **Step 2: Replace usages with `_settings.MapMinSeparation`**

Inside `RegisterBuildingWithMap`, the current code reads `_nearbyMapJoinRadius`. Replace with `_settings.MapMinSeparation`:

```csharp
// Before:
MapController nearest = MapController.GetNearestExteriorMap(worldPosition, _nearbyMapJoinRadius);
// After:
float minSep = _settings != null ? _settings.MapMinSeparation : 150f;
MapController nearest = MapController.GetNearestExteriorMap(worldPosition, minSep);
```

Same replacement for any log messages mentioning `_nearbyMapJoinRadius`.

- [ ] **Step 3: Add separation check inside `MapRegistry.CreateMapAtPosition`**

Find the existing `CreateMapAtPosition(Vector3 worldPosition)` method (previously added). At the start, after the `IsServer` check, add:

```csharp
// Enforce MapMinSeparation — reject if another zone center is too close.
if (_settings != null)
{
    float minSep = _settings.MapMinSeparation;
    float minSqr = minSep * minSep;

    // Check other MapControllers
    var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
    foreach (var m in allMaps)
    {
        if (m == null || m.Type == MapType.Interior) continue;
        if ((m.transform.position - worldPosition).sqrMagnitude < minSqr)
        {
            Debug.LogWarning($"<color=yellow>[MapRegistry:CreateMapAtPosition]</color> Rejected map at {worldPosition}: within {minSep} units of map '{m.MapId}'.");
            return null;
        }
    }

    // Check WildernessZones
    var allZones = UnityEngine.Object.FindObjectsByType<WildernessZone>(FindObjectsSortMode.None);
    foreach (var z in allZones)
    {
        if (z == null) continue;
        if ((z.transform.position - worldPosition).sqrMagnitude < minSqr)
        {
            Debug.LogWarning($"<color=yellow>[MapRegistry:CreateMapAtPosition]</color> Rejected map at {worldPosition}: within {minSep} units of zone '{z.ZoneId}'.");
            return null;
        }
    }
}
```

Ensure `MapRegistry` has a `[SerializeField] private WorldSettingsData _settings;` field and populates it in `Start` (or add if missing, mirroring `WildernessZoneManager`).

- [ ] **Step 4: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingPlacementManager.cs Assets/Scripts/World/MapSystem/MapRegistry.cs
git commit -m "feat(world): BuildingPlacementManager reads MapMinSeparation; MapRegistry.CreateMapAtPosition enforces it"
```

---

## Task 12: Add `CharacterMapTracker.CurrentRegionId` NetworkVariable

**Files:**
- Modify: `Assets/Scripts/Character/Components/CharacterMapTracker.cs`

- [ ] **Step 1: Add the NetworkVariable field**

Find the existing `HomeMapId` declaration and add below it:

```csharp
public NetworkVariable<FixedString64Bytes> CurrentRegionId = new NetworkVariable<FixedString64Bytes>(
    "",
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);
```

- [ ] **Step 2: Add server-side update with throttle**

Add private state + update logic. Add these private fields:
```csharp
private float _nextRegionCheckTime = 0f;
private const float RegionCheckIntervalSeconds = 0.25f;
```

In `Update()` (or add one if not present), add at the end:

```csharp
if (!IsServer) return;
if (Time.unscaledTime < _nextRegionCheckTime) return;
_nextRegionCheckTime = Time.unscaledTime + RegionCheckIntervalSeconds;

try
{
    var region = MWI.WorldSystem.Region.GetRegionAtPosition(transform.position);
    string id = region != null ? region.ZoneId : "";
    if (CurrentRegionId.Value.ToString() != id)
    {
        CurrentRegionId.Value = new FixedString64Bytes(id);
    }
}
catch (Exception e)
{
    Debug.LogException(e);
}
```

**CLAUDE.md rule 26:** UI must use unscaled time, but this is not UI — it's a server-side poll. `Time.unscaledTime` keeps the 0.25s cadence stable regardless of `GameSpeedController` scale, which is intentional: we don't want region detection to speed up/down with simulation.

Add `using System;` if not already at the top.

- [ ] **Step 3: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Components/CharacterMapTracker.cs
git commit -m "feat(character): add CurrentRegionId NetworkVariable with server-side 0.25s polling"
```

---

## Task 13: Extend `MacroSimulator` with Zone Motion daily catch-up step

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs`

- [ ] **Step 1: Identify the catch-up pipeline entry point**

Read the current `MacroSimulator.cs`. Find whatever method runs the daily/wake-up loop. The spec's Section 8 calls it the "catch-up loop" — in code, it's typically named `CatchUp(...)`, `SimulateOfflineDelta(...)`, or invoked from `MapController.WakeUp`.

Take note of:
- Signature (does it receive `deltaDays`?)
- Where steps 1-4 (resource regen, yields, needs decay, position snap) live
- Whether there's a matching `TimeManager.OnNewDay` subscription that advances live zones during play

- [ ] **Step 2: Add the Zone Motion tick method**

Add a new private method to `MacroSimulator`:

```csharp
/// <summary>
/// Step 5 of the catch-up loop. Iterates all WildernessZones and applies accumulated motion deltas
/// from their IZoneMotionStrategy lists. Clamps each proposed position by WorldSettingsData.MapMinSeparation
/// to prevent zone-on-zone overlap.
/// </summary>
private void TickZoneMotion(int daysSinceLastTick)
{
    if (daysSinceLastTick <= 0) return;

    int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
    float minSep = _settings != null ? _settings.MapMinSeparation : 150f;
    float minSqr = minSep * minSep;

    var zones = UnityEngine.Object.FindObjectsByType<WildernessZone>(FindObjectsSortMode.None);
    foreach (var zone in zones)
    {
        if (zone == null || zone.MotionStrategies == null || zone.MotionStrategies.Count == 0) continue;

        Vector3 totalDelta = Vector3.zero;
        foreach (var strategy in zone.MotionStrategies)
        {
            if (strategy == null) continue;
            try
            {
                totalDelta += strategy.ComputeDailyDelta(zone, currentDay);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        if (totalDelta == Vector3.zero) continue;

        Vector3 proposed = zone.transform.position + totalDelta * daysSinceLastTick;

        // Clamp: if proposed position is within minSep of any other zone (not self), skip this update
        bool blocked = false;
        foreach (var other in zones)
        {
            if (other == null || other == zone) continue;
            if ((other.transform.position - proposed).sqrMagnitude < minSqr)
            {
                blocked = true;
                break;
            }
        }

        if (!blocked)
        {
            zone.transform.position = proposed;
        }
    }
}
```

Ensure `MacroSimulator` has `[SerializeField] private WorldSettingsData _settings;`. Add if missing, mirroring the other world-system singletons.

- [ ] **Step 3: Wire the tick into the existing catch-up flow**

After step 4 (Position Snap), call `TickZoneMotion(deltaDays)`:

```csharp
// Step 5: Zone Motion (NEW — Phase 1)
TickZoneMotion(deltaDays);
```

Also subscribe to `TimeManager.OnNewDay` so zones drift during active play. In `MacroSimulator.Start()` or equivalent:

```csharp
if (TimeManager.Instance != null)
{
    TimeManager.Instance.OnNewDay += HandleNewDayForMotion;
}
```

And:

```csharp
private void HandleNewDayForMotion()
{
    if (!NetworkManager.Singleton.IsServer) return;
    TickZoneMotion(1);
}
```

Don't forget **rule 16** — unsubscribe in `OnDestroy`:

```csharp
private void OnDestroy()
{
    if (TimeManager.Instance != null)
        TimeManager.Instance.OnNewDay -= HandleNewDayForMotion;
}
```

- [ ] **Step 4: Compile**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=2
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MacroSimulator.cs
git commit -m "feat(world): add Zone Motion step 5 to MacroSimulator catch-up loop

Iterates all WildernessZones, evaluates IZoneMotionStrategy deltas,
applies clamped by MapMinSeparation. Runs on TimeManager.OnNewDay
(active play) and on map wake-up (catch-up with daysSinceLastTick).
No-op in Phase 1 since all zones default to StaticMotion."
```

---

## Task 14: Scene work — place an authored `Region` around existing `MapController`s

**Files:**
- Modify: the main test scene (likely `Assets/Scenes/Main.unity` or whatever the primary scene is)

- [ ] **Step 1: Identify the main scene**

```
mcp__ai-game-developer__scene-list-opened
```
Expected: one scene active. If it's a lobby/splash scene rather than the gameplay scene, switch to the one holding the test `MapController`(s):
```
mcp__ai-game-developer__assets-find filter="t:Scene"
```
Pick the scene where existing `MapController`s live (the one used for solo/multiplayer testing).

- [ ] **Step 2: Check the current scene root structure**

```
mcp__ai-game-developer__scene-get-data
```
Note which GameObjects host `MapController`(s). They likely sit at the scene root or under a "World" parent.

- [ ] **Step 3: Create the authored `Region` GameObject**

Using MCP or the Unity Editor:
1. Create an empty GameObject at scene root named `Region_TestArea`.
2. Add `BoxCollider` component — size it to enclose all existing `MapController` bounds plus some margin. The easiest way: take the min/max world positions of all existing map triggers and size the box ≈1.2× that envelope.
3. Add `Region` component (from `MWI.WorldSystem`). In the Inspector:
   - `_regionId` = `"TestArea"`
   - `_biomeDefinition` = drag in an existing `BiomeDefinition` asset (whichever biome is used for the default test environment — ask the user if unclear)
   - `_climateProfile` = drag in the matching `BiomeClimateProfile`
   - `_weatherFrontPrefab` = existing `WeatherFront` prefab (copy from any currently-placed `BiomeRegion` in the scene, or from `Assets/Resources/Prefabs/Weather/WeatherFront.prefab` if that's where it lives)
4. Re-parent the existing `MapController` GameObject(s) under `Region_TestArea` (drag in Unity Hierarchy).

- [ ] **Step 4: Save the scene**

```
mcp__ai-game-developer__scene-save
```

- [ ] **Step 5: Enter play mode and verify**

```
mcp__ai-game-developer__editor-application-set-state  # set playmode on
```
Wait 3-5 seconds. Then:
```
mcp__ai-game-developer__console-get-logs logTypeFilter=Log lastMinutes=1 maxEntries=30
```
Expected log lines:
- `[Region:Awake] 'TestArea' discovered N maps, 0 wilderness zones.` (N ≥ 1)
- No errors.

Walk a test character into and out of the Region bounds (use existing test tools or move the character via console). The `CharacterMapTracker.CurrentRegionId` should flip from `""` → `"TestArea"` on entry and back to `""` on exit.

If you have dev-mode tools, add an overlay showing the player's `CurrentRegionId` for live verification. Otherwise use `script-execute` to read it:
```csharp
public static class TestRegionId
{
    public static string Run()
    {
        var players = UnityEngine.Object.FindObjectsByType<MWI.Character>(UnityEngine.FindObjectsSortMode.None);
        foreach (var p in players)
        {
            var tracker = p.GetComponentInChildren<CharacterMapTracker>();
            if (tracker != null)
                return $"{p.name}: {tracker.CurrentRegionId.Value}";
        }
        return "no characters";
    }
}
```

- [ ] **Step 6: Exit play mode and commit**

```
mcp__ai-game-developer__editor-application-set-state  # playmode off
```

```bash
git add Assets/Scenes/
git commit -m "scene: add authored Region_TestArea enclosing existing MapControllers

Exercises the new Region -> MapController hierarchy and CharacterMapTracker.CurrentRegionId
NetworkVariable flow."
```

---

## Task 15: End-to-end smoke-test matrix (manual verification)

**Files:** none modified. This task is manual + MCP verification.

No commit for this task unless you fix bugs found along the way (each bug fix is its own commit).

- [ ] **Check 1: Compile clean across all phases**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs logTypeFilter=Error lastMinutes=5
mcp__ai-game-developer__console-get-logs logTypeFilter=Exception lastMinutes=5
```
Expected: no errors or exceptions related to `Region`, `WildernessZone`, `MapRegistry`, `BiomeRegion`, `CommunityTracker`.

- [ ] **Check 2: Solo play-mode boot**

Enter play-mode. After 10 seconds, retrieve logs:
- Expected `[Region:Awake]` line for `TestArea`.
- Expected `[MapRegistry:RestoreState]` log if a save exists.
- Expected **no** `CommunityTracker` log lines (the rip is done).

- [ ] **Check 3: Building placement joins existing map**

In play-mode, use the building placement UI to place a building inside an existing `MapController`'s bounds. Expected: building parents to the map, `BuildingSaveData` added to `CommunityData.ConstructedBuildings`. Verify via logs or by saving and reloading.

- [ ] **Check 4: Building placement creates wild map outside any map**

Walk the player ~250 units away from any existing `MapController`. Place a building. Expected logs:
- `[BuildingPlacementManager:Register] No nearby map within 150 units. Creating a new wild map at ...`
- `[MapRegistry:CreateMapAtPosition] Wild map 'Wild_xxxxxxxx' spawned ...`

- [ ] **Check 5: `MapMinSeparation` rejection**

Place a building 50 units away from the first wild map (within `MapMinSeparation = 150`). Expected: `BuildingPlacementManager` joins the existing wild map rather than creating a new one. No `CreateMapAtPosition` log.

- [ ] **Check 6: Region transition updates `CurrentRegionId`**

Use `script-execute` or a dev overlay to read the player's `CurrentRegionId` while walking in and out of `Region_TestArea`. Expected: `"TestArea"` when inside, `""` outside.

- [ ] **Check 7: `WildernessZoneManager.SpawnZone` rejects within `MapMinSeparation`**

Run the test script:
```csharp
using UnityEngine;
using MWI.WorldSystem;
public static class TestSeparationReject
{
    public static string Run()
    {
        if (WildernessZoneManager.Instance == null) return "manager null";
        var def = ScriptableObject.CreateInstance<WildernessZoneDef>();
        def.DefaultRadius = 50f;

        // Pick a position near an existing MapController
        var maps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        if (maps.Length == 0) return "no maps to test against";
        Vector3 closePos = maps[0].transform.position + Vector3.right * 10f; // 10 units away — well within 150

        var zone = WildernessZoneManager.Instance.SpawnZone(closePos, def, null);
        return zone == null ? "correctly rejected" : "BUG: accepted spawn too close";
    }
}
```
Expected: `"correctly rejected"` plus a `<yellow>` warning log.

- [ ] **Check 8: Save + reload round-trip**

In play-mode, spawn a `WildernessZone` via the test script above (at a valid position, far from existing zones). Save the game. Exit play-mode. Re-enter play-mode (triggers load). Verify via logs:
- `[WildernessZoneManager:RestoreZone] Restored 'Wild_xxxxxxxx'` line appears.
- The zone is again findable via `Region.WildernessZones`.

- [ ] **Check 9: Zone Motion is a no-op in Phase 1**

In play-mode, force `TimeManager.OnNewDay` to fire (or advance real time past a day boundary). Verify `WildernessZone.transform.position` is unchanged for all zones. (All zones default to `StaticMotion`, so no drift is expected.)

- [ ] **Check 10: Network matrix (Host ↔ Client)**

If a second machine or a second editor instance is available:
1. Host the session on machine A; connect machine B as client.
2. On Host, place a building far from any map → creates wild map. Verify the wild map appears on the Client.
3. On Host, walk a character into `Region_TestArea` → `CurrentRegionId` on Client's copy of the character shows `"TestArea"` (read via `script-execute` on client).
4. Any desyncs are blockers — log them and investigate before shipping.

- [ ] **If all 10 checks pass:** Phase 1 code is shippable. Proceed to Task 16.

---

## Task 16: Update `CLAUDE.md` Rule 30 + `.agent/skills/` documents

**Files:**
- Modify: `CLAUDE.md`
- Modify: `.agent/skills/world-system/SKILL.md`
- Modify: `.agent/skills/building_system/SKILL.md`
- Modify: `.agent/skills/community-system/SKILL.md`

- [ ] **Step 1: Rewrite `CLAUDE.md` Rule 30**

Open `CLAUDE.md` and find the existing Rule 30 block (starts with `## World System & Simulation`). Replace the body of Rule 30 with the text from the Phase 1 spec Section 10. Keep the `## World System & Simulation` header and rule numbering intact — only replace rule 30's content.

(The exact replacement text is in `docs/superpowers/specs/2026-04-21-living-world-phase-1-design.md` Section 10.)

- [ ] **Step 2: Update `.agent/skills/world-system/SKILL.md`**

Open the file. Replace content describing cluster-promotion (`EvaluatePopulations`, `PromoteToSettlement`, `RoamingCluster → Settlement → EstablishedCity` transitions) with:
- `Region` hierarchy (`Region → { MapController, WildernessZone, WeatherFront }`)
- `IWorldZone` / `IZoneMotionStrategy` / `IStreamable` abstractions
- `WildernessZoneManager.SpawnZone` as the runtime entry point
- Zone Motion as step 5 of `MacroSimulator` catch-up
- `MapMinSeparation` in `WorldSettingsData` as the global separation constraint

Keep the file tight — this is procedural content, not architectural. Link to `wiki/systems/world-biome-region.md` and `wiki/decisions/adr-0001-living-world-hierarchy-refactor.md` for architecture.

- [ ] **Step 3: Update `.agent/skills/building_system/SKILL.md`**

Find the "Building Placement & Save Persistence" section. Update:
- Replace every `_nearbyMapJoinRadius` reference with `WorldSettingsData.MapMinSeparation`.
- Note that `CommunityTracker` has been renamed to `MapRegistry` (one line at the top of that section).
- Cross-reference the ADR.

- [ ] **Step 4: Update `.agent/skills/community-system/SKILL.md`**

Add a notice at the top:
> **Pending Phase 1 refactor status:** `CommunityTracker` has been renamed to `MapRegistry`; NPC-cluster auto-promotion (`EvaluatePopulations`, `PromoteToSettlement`) has been deleted. `CommunityData` stays as-is. See [ADR-0001](../../wiki/decisions/adr-0001-living-world-hierarchy-refactor.md) for rationale.

Update any procedural content that relied on the removed methods.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md .agent/skills/
git commit -m "docs: update CLAUDE.md Rule 30 + world/building/community SKILLs for Phase 1

- Rule 30 rewritten: Region hierarchy, IWorldZone abstraction, Zone Motion step 5
- world-system SKILL: replace cluster-promotion content with new hierarchy
- building-system SKILL: MapMinSeparation replaces _nearbyMapJoinRadius
- community-system SKILL: MapRegistry rename note"
```

---

## Self-Review

### Spec coverage check

| Spec section | Covered by | Status |
|---|---|---|
| 2. Goals | Tasks 1-16 collectively | ✓ |
| 3. Non-goals | Explicitly avoided (no live harvestable prefabs, no wildlife ecology, no reactive motion, no WeatherFront changes) | ✓ |
| 4. World hierarchy | Tasks 6-7 (Region rename+extend), Task 8 (WildernessZone), Task 14 (scene work) | ✓ |
| 4.1 Relationship to existing code | Tasks 6-7 handle the rename + extend explicitly | ✓ |
| 5.1 Interfaces | Task 1 | ✓ |
| 5.2 MonoBehaviours | Tasks 6-7 (Region), Task 8 (WildernessZone); WeatherFront explicitly untouched | ✓ |
| 5.3 Motion strategy base + default | Task 2 | ✓ |
| 5.4 Managers and registries | Task 4 (MapRegistry rename), Task 10 (WildernessZoneManager); Region.GetRegionAtPosition stays as existing static | ✓ |
| 5.5 WildernessZoneDef SO | Task 9 | ✓ |
| 6.1 MapMinSeparation field | Task 3 | ✓ |
| 6.2 CommunityData unchanged | No task needed (absence of change) | ✓ |
| 6.3 CurrentRegionId NetworkVariable | Task 12 | ✓ |
| 6.4 Save types | Task 7 (Region extended), Task 8 (WildernessZoneSaveData) | ✓ |
| 6.5 MapRegistrySaveData rename + shrink | Task 4 (rename), Task 5 (shrink) | ✓ |
| 7. Deletions | Task 5 | ✓ |
| 8. MacroSimulator extension | Task 13 | ✓ |
| 9. BuildingPlacementManager integration | Task 11 | ✓ |
| 10. CLAUDE.md Rule 30 rewrite | Task 16 | ✓ |
| 11. Phase-1 implementation order | Mapped 1:1 to Tasks 1-14 | ✓ |
| 12. Narrative examples | Not an implementation concern (composition of future-phase APIs) | N/A |
| 13. Known risks | Acknowledged in task notes (rename scope, Region compile-order dependency in Task 7) | ✓ |
| 14. Testing plan | Task 15 (smoke matrix) covers integration checks; unit tests deferred due to no asmdef | ✓ (with deliberate deviation) |
| 15. Documentation updates | Task 16 | ✓ |
| 16. Out-of-scope follow-ups | Explicitly deferred (not in plan) | ✓ |

**No spec gaps.**

### Placeholder scan

- "TBD" / "TODO" / "implement later" / "fill in details" — none in final plan (the Task 7 Step 6 note explicitly names the dependency on Task 10 and gives two concrete options; it is not a vague placeholder).
- "Add appropriate error handling" — none; every fallible step (Instantiate, Spawn, JSON parse) has an explicit `try/catch` per rule 31.
- "Write tests for the above" — not used; testing is Task 15 with a concrete 10-check matrix.
- "Similar to Task N" — not used; each task has complete code blocks.

### Type & symbol consistency

- `WildernessZone.InitializeAsDynamic(string zoneId, float radius, Region parent, List<ScriptableZoneMotionStrategy> motionStrategies, List<ResourcePoolEntry> seededHarvestables)` — referenced the same way in Task 8 (declaration), Task 10 Step 1 (`SpawnZone` callsite), and Task 10 Step 1 (`RestoreZone` callsite). ✓
- `WildernessZone.IsDynamicallySpawned` / `CaptureZoneState()` / `Harvestables` / `Wildlife` / `MotionStrategies` — declared in Task 8; referenced in Task 7 Step 6 and Task 10. ✓
- `WildernessZoneManager.Instance` / `SpawnZone` / `RestoreZone` — declared in Task 10; referenced in Task 7 Step 6 (`RestoreZone` callsite). ✓
- `Region.GetRegionAtPosition` / `RegisterWildernessZone` / `UnregisterWildernessZone` — declared in Tasks 6/7; referenced in Tasks 8, 10, 12. ✓
- `WorldSettingsData.MapMinSeparation` — declared Task 3; referenced Tasks 10, 11, 13. ✓
- `MapRegistry.CreateMapAtPosition` — renamed in Task 4 (from `CommunityTracker.CreateMapAtPosition`); enforced-at Task 11. ✓
- `CharacterMapTracker.CurrentRegionId` — declared Task 12; referenced Task 14 & 15 smoke tests. ✓
- `HibernatedNPCData`, `ResourcePoolEntry`, `WeatherFrontSnapshot` — existing types, confirmed present in codebase via earlier Grep. ✓

### Known compile-order caveat

Task 7 introduces references to `WildernessZone.IsDynamicallySpawned`, `WildernessZone.CaptureZoneState()`, and `WildernessZoneManager.RestoreZone(...)` — all declared in Tasks 8 and 10. Task 7 Step 6 explicitly documents this and offers two strategies: (a) commit Task 7 with a temporary non-compiling state that resolves after Task 10, or (b) defer the save-payload edit until Task 10. Either is acceptable; the plan calls out the choice explicitly. This is **not** a plan failure — the dependency is signposted and has a concrete resolution.

---

## Execution handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-21-living-world-phase-1.md`.**

Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration on a dedicated worktree.
2. **Inline Execution** — execute tasks in this session with checkpoints for review at commit boundaries.

Which approach?
