---
name: world-system
description: Architecture of the World System including Map Hibernation, Spatial Offsets, and Macro-Simulation (Offline Catch-Up) for scaling the game.
---

# WORLD SYSTEM ARCHITECTURE

## 1. Core Philosophy (Spatial Offset)
The World System avoids the overhead of Unity's `NetworkSceneManager` and additive scene loading. Instead, it uses a **Single-Scene Spatial Offset Architecture**.
*   **Regions / Maps** are massive quadrants physically separated by large distances (e.g., Map A at `x=0`, Map B at `x=10000`).
*   **Building Interiors** are placed high in the sky (`y=5000`) or deep underground.
*   **Transitions** between these areas are seamless and are handled by `CharacterMovement.Warp()`.

Because Maps are physically distant, Unity NGO's **Interest Management** naturally filters out network packets across Maps.

## 2. Map Hibernation (Performance)
To support thousands of NPCs and hundreds of Maps locally, we use Map Hibernation.
*   **Activation:** The `MapController` tracks active players. When `PlayerCount == 0`, the Map enters Hibernation.
*   **Phase 1 (Pause):** All NPCs on the Map are serialized into `HibernatedNPCData` (inside `MapSaveData`), extracting only pure logic (Stats, Schedule target, Inventory, Needs) and position. The actual heavy Unity `GameObject` (and `NavMeshAgent`, `Animator`, `NetworkObject`) is DESPAWNED and DESTROYED. The Map is visually dead.
*   **Phase 2 (Wake Up):** When the first player re-enters the dormant Map, the `MapController` immediately calls the `MacroSimulator`.

## 3. Macro-Simulation (Catch-Up Math)
The `MacroSimulator` operates entirely off-screen when a Map wakes up.
*   It calculates the absolute time delta (`DeltaTime = CurrentTime - HibernationTime`).
*   It looks at each `HibernatedNPCData` and mathematically computes what the NPC *would* have done during that gap.
*   **Offline Needs Decay:** It calculates how much time has passed and subtracts that from the serialized `CharacterNeed.CurrentValue` (e.g., Hunger, Social), ensuring NPCs wake up with accurate stat depletion proportionate to the `DeltaTime`.
*   **Persistent Character Progression:** It handles extraction and injection of logic unique to the NPC (e.g. `CharacterBlueprints.UnlockedBuildingIds`) to ensure the city-building capabilities are consistent across Map Hibernation and Wake-Up phases.
*   **Offline City Growth:** Driven entirely by the `Community` Leader's `CharacterBlueprints` knowledge. The Simulator filters the `BuildingRegistry` by the Leader's known `UnlockedBuildingIds`, checks which are missing in the community, and spawns scaffold data respecting the `CommunityPriority`.
*   **Simulation vs Realtime:** Rather than simulating every frame of walking, the MacroSimulator just skips them to the end of their current scheduled task (e.g., if it's 8:00 AM, snap position to Blacksmith Forge).
*   Once simulated, the Server reinstantiates the Prefab at the new position, assigns the updated stats and blueprint data, and calls `Spawn()` to sync with the entering client.

## 3.1 Time Skip (player-initiated macro-sim loop)

The Time Skip path advances the in-game clock by N hours without running the live simulation, by hibernating the active map and running `MacroSimulator.SimulateOneHour` per hour. Coexists with `GameSpeedController` — that one scales `Time.timeScale` so live NPCs tick faster; this one freezes them entirely. Architecture details live in [[world-time-skip]].

*   **Triggers:** `/timeskip <hours>` chat command + `DevTimeSkipModule` dev-panel button (both pass `force: true` — admin override), and the bed flow + `UI_BedSleepPrompt` (consent path — `force: false`). **All routes go through `TimeSkipController.Instance.RequestSkip(hours, force)`** — server-only, returns `bool`.
*   **Multiplayer consent gate (`force: false`):** every connected player's `Character.IsSleeping` must be `true` before `RequestSkip` accepts. Reject otherwise. The auto-trigger watcher in `TimeSkipController.Update` (server-only) polls every connected player; when all `IsSleeping == true` AND at least one has `PendingSkipHours > 0`, it fires `RequestSkip(MIN(PendingSkipHours), force: false)` automatically — **closest target wins**, so nobody is dragged past their requested duration.
*   **Host force-override (`force: true`):** auto-EnterSleeps every connected player at the start of the coroutine. Bypasses the all-sleeping gate. Used by dev panel + chat command. Don't expose this to non-host gameplay paths or one player can override every other player's autonomy.
*   **Bed flow contract:** the bed `UseSlot` path raises `Character.IsSleeping = true`; the bed prompt UI sets `Character.PendingSkipHours = chosenHours` via `Character.SetPendingSkipHours(int)` (server-only — clients use a ServerRpc wrapper). On `Character.ExitSleep`, `PendingSkipHours` resets to 0 so a stale value can't auto-trigger the next session.
*   **Per-hour loop (server coroutine):** validate → freeze `Time.timeScale` → resolve `activeMap` + players → **pre-skip checkpoint save (waits for `SaveManager.CurrentState == Idle` before continuing)** → `HibernateForSkip` → for each hour (`AdvanceOneHour` → `SimulateOneHour` → `OnSkipHourTick` → abort check) → `WakeUpFromSkip` → `ExitSleep` for each player → restore `Time.timeScale`. The `_pendingSkipWake` flag on the active `MapController` makes `WakeUp()` skip the redundant single-pass catch-up over the same delta we just simulated hour-by-hour. **Cleanup is wrapped in try/catch** — a WakeUp exception logs but doesn't strand `IsSkipping = true`.
*   **Day-boundary gating:** any new step that integrates over `daysPassed` must run inside `if (crossedDayBoundary)` in `SimulateOneHour`, **not** the hour-grained block, or it will floor-to-0 every hour and silently no-op the entire skip. Hour-grained steps (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule`) run every hour because they're already scaled per-hour internally.
*   **Bed lifecycle:** `BedFurniture.UseSlot(slot, character)` → `Character.EnterSleep(anchor)` (server-only; snaps position+rotation, toggles NavMeshAgent via `ConfigureNavMesh(bool)`) → `Character.NetworkIsSleeping = true` → `PlayerController.Update()` early-out. `IsSleeping` and `PendingSkipHours` are NetworkVariables — replicate to all peers. Neither is persisted; reload always wakes the player.
*   **Pre-skip save** captures the world *before* the hibernate-skip-wake cycle. If anything goes wrong (exception, abort, crash), the player reverts to the pre-skip state on next load. The post-skip save still fires naturally via `SleepBehaviour.Exit` on the bed flow; dev paths rely solely on the pre-skip checkpoint.
*   **Virtual buildings caveat:** `MapController.Hibernate` skips any `Building` whose `NetworkObject` is shared with the MapController (the inherited `NetworkBehaviour.NetworkObject` getter walks up via `GetComponentInParent`). Despawning that NetworkObject would destroy the MapController itself — was the root cause of an early WakeUp `MissingReferenceException`. Do not remove the guard.
*   **Manual setup required** — see [[world-time-skip]] §"Manual setup checklist" for the Editor wiring (TimeSkipController on the GameManager GameObject, Time Skip tab in `DevModePanel.prefab`, `UI_TimeSkipOverlay.prefab`, `UI_BedSleepPrompt.prefab`).

## 4. Map Transitions
Transitions are standardized via `MapTransitionDoor` (exterior-to-exterior) and `BuildingInteriorDoor` (exterior-to-interior).
*   Players interact with a door. `CharacterMapTransitionAction` fades screen via `ScreenFadeManager`, then warps.
*   Cross-NavMesh teleports (building interiors at y=5000) **must** use `CharacterMovement.ForceWarp()`, not `Warp()`. ForceWarp disables the NavMeshAgent, teleports via `transform.position`, then re-enables the agent after 2 frames.
*   The `CharacterMapTracker` is invoked via `RequestTransitionServerRpc` (`FixedString128Bytes` for map IDs). The server resolves interior positions via `ResolveInteriorPosition()` (lazy-spawns if needed) and sends `WarpClientRpc` back if the position changed (ClientNetworkTransform is owner-authoritative).
*   The Server updates the `CurrentMapID` and notifies source/destination MapControllers via `MapController.NotifyPlayerTransition()` for hibernation handoff.
*   See `building-system` SKILL.md for full interior transition architecture.

## 5. Development Rules
*   **Do not rely on `FindObjectOfType`:** Maps hibernate, so GameObjects will completely disappear. Only search serialized data structures (like a hypothetical `WorldManager.HibernatedData`) if trying to locate an off-screen NPC.
*   **No Cross-Map Physics:** A projectile from Map A will never reach Map B. Do not attempt it. Any inter-map effects (e.g. economy shipments) must be calculated purely via Math in the `JobLogisticsManager`, not via physical objects driving across the emptiness.
*   **Character Maps are Authoritative:** Always rely on `CharacterMapTracker.CurrentMapID.Value` to know what map a character belongs to.

## 6. World Hierarchy (Phase 1 refactor — ADR-0001, implemented)
The world is organized as: **`Region` (authored container) → { `MapController`, `WildernessZone`, `WeatherFront` }**. All three implement `IWorldZone`.
*   **Region:** `NetworkBehaviour` + `ISaveable` placed in the scene. Holds a `BiomeDefinition`, `BiomeClimateProfile`, and a BoxCollider trigger. `[RequireComponent(NetworkObject)]` so scene-placed Regions auto-acquire a `NetworkObject` on scene load — required so child `MapController`/`WildernessZone` NetworkObjects can be NGO-parented via `TrySetParent`. Auto-discovers scene-child `MapController`s and `WildernessZone`s in `Awake` via `GetComponentsInChildren`. Runtime-spawned children register via `RegisterMap(MapController)` / `RegisterWildernessZone(WildernessZone)` — unregister counterparts on despawn. Clients learn their region via the `CharacterMapTracker.CurrentRegionId` NetworkVariable.
*   **WildernessZone:** `NetworkBehaviour` + `ISaveable`. Virtual-content region holding `List<ResourcePoolEntry>` (harvestables) + `List<HibernatedNPCData>` (wildlife, future). Contents stream via `IStreamable` only when a player is within spawn radius. Can move via pluggable `IZoneMotionStrategy` SO assets. Spawned either by scene authoring or via `WildernessZoneManager.SpawnZone(pos, def, parent)` — the manager uses `NetworkObject.TrySetParent` to attach the zone under the target Region's transform.
*   **WeatherFront:** Atmospheric region spawned by a parent `Region` on a timer (unchanged — formerly owned by `BiomeRegion`).

### 6.1 Map Birth (no more cluster auto-promotion)
Maps are **never** created by NPC-cluster auto-promotion (the old `CommunityTracker.PromoteToSettlement` path is removed). They are born via:
*   **(a) Scene authoring** — designer places a `MapController` inside a `Region` in the scene.
*   **(b) Building placement** — `BuildingPlacementManager` is **region-aware**: if the placement position falls inside a Region with no enclosing map, it calls `MapRegistry.CreateMapAtPosition(worldPosition)` to spawn a new wild map **inside that Region** rather than poaching a map from a neighbor. If the position is in the open world (no Region), it falls back to the legacy join-nearest-else-create flow using `MapController.GetNearestExteriorMap`.
*   **(c) Future procedural generation** — not implemented.

**MapControllers are elastic** — the `BoxCollider` adapts instead of rejecting placements (post-merge iteration, 2026-04-22):
*   `MapController.ClampBoundsToRegion(regionBounds)` — called by `MapRegistry.CreateMapAtPosition` right after `NetworkObject.Spawn`. Clamps the freshly-instantiated map's BoxCollider to the intersection of its prefab size and the Region's world-space bounds. **Shrink-to-fit** — small Regions produce smaller maps.
*   `MapController.ExpandBoundsToInclude(worldPoint, footprintSize, regionBounds)` — called by `BuildingPlacementManager.RegisterBuildingWithMap` when a placement falls NEAR (within `WorldSettingsData.MapMinSeparation`) an existing same-region map. Grows the BoxCollider to envelop the new building's footprint, clamped to Region bounds. **Expand-to-envelop** — communities stay contiguous, no micro-map proliferation.
*   `MapRegistry.FindNearestMapInRegion(position)` — returns the closest exterior map in the same Region within MinSep, or null. Placement prefers expansion; only spawns a new wild map when this returns null.
*   `WorldSettingsData.MapMinSeparation` is therefore a **soft threshold** (routes to expansion), NOT a hard rejection. The old `CreateMapAtPosition` rejection-on-MinSep branch and client-side fit/MinSep toasts were removed.
*   Wild maps strip `Biome`/`JobYields` on instantiation so `MapController.SpawnVirtualBuildings` short-circuits (no `VirtualResourceSupplier_*` children on small player outposts). Newly spawned maps `netObj.TrySetParent` under their target Region — valid because Region is a NetworkBehaviour.
*   **Known limitation:** `BoxCollider.center`/`size` are plain fields, not `NetworkVariable`s — resize is server-authoritative only. Clients see the prefab's default bounds until a future PR replicates them. Hibernation/placement/save-load are unaffected because they run server-side.

`MapRegistry` (renamed from `CommunityTracker`; `SaveKey = "CommunityTracker_Data"` preserved for save-file back-compat) holds the persistent `CommunityData` list (leaders, constructed buildings, resource pools, build permits, pending claims). It no longer runs population heartbeats — only `ProcessPendingBuildingClaims` on `TimeManager.OnNewDay`.

### 6.1a Save/Load round-trip for dynamic wild maps
`CommunityData.SpawnPosition` records the exact world-space position of dynamic maps. On world load, `MapRegistry.RestoreState` schedules `RespawnDynamicMapsDeferred` via `Invoke(..., 1.5f)` — the 1.5s delay lets scene-placed MapControllers finish `OnNetworkSpawn` and register in `_mapRegistry` first. The deferred pass iterates `_communities`, skips `IsPredefinedMap=true` entries and any `MapId` already live, and for the rest:
1. Instantiates `_mapControllerPrefab` at `SpawnPosition`
2. Clears `Biome`/`JobYields` (wild-map semantics)
3. `netObj.Spawn()`
4. Re-establishes Region parenting via `Region.GetRegionAtPosition` + `RegisterMap` + `TrySetParent`
5. Calls `mapController.SpawnSavedBuildings()` to bring back the buildings saved in `CommunityData.ConstructedBuildings`

Without step 5, `GameLauncher.cs:188-195` only calls `SpawnSavedBuildings` on predefined maps (iterated at world-load time, before our deferred respawn creates the wild ones).

### 6.1b CurrentMapID on trigger events
`MapController.OnTriggerEnter` / `OnTriggerExit` now update the entering/exiting character's `CharacterMapTracker.CurrentMapID` via `SetCurrentMap(MapId)` and `SetCurrentMap("")`. Original design assumed every map change went through a door RPC; wild maps on the same plane let players simply walk in, which requires trigger-driven tracker updates. The exit path only clears the tracker if it still matches THIS map (so a simultaneous enter/exit doesn't clobber the neighbor map's just-set value).

### 6.2 Zone Motion (MacroSimulator step 6)
Each `WildernessZone` has a `List<ScriptableZoneMotionStrategy>`. Default `StaticMotionStrategy` returns `Vector3.zero`. `MacroSimulator.TickZoneMotion(daysSinceLastTick)` sums per-zone deltas, clamps by `MapMinSeparation` (zone-vs-zone overlap prevention), and applies. Runs on map wake-up as step 6 of `SimulateCatchUp`. Phase 1 is a no-op until reactive strategies (`RandomDrift`, `AvoidWeatherFront`, `FollowResourceAbundance`, …) ship in later phases.

### 6.3 Identity & Legacy
*   **Building Identity:** Dynamic buildings generate a unique `NetworkBuildingId` (GUID) on spawn.
*   **Abandoned Cities:** Cities never truly dissolve — if population drops, the city hibernates and retains its slot permanently.
*   **Spawn channels for `WildernessZone`** include debug tools, quest scripts, and environmental systems (e.g. a `WeatherFront` spawning a temporary berry zone). All go through `WildernessZoneManager.SpawnZone`.

## 7. Offset Allocation (Instanced Cells)
While dynamic open-world content uses centroid-stamping, pure instanced content (like Dungeons, specific Buildings, or isolated narrative maps) uses the `WorldOffsetAllocator`'s physical spatial coordinates.
*   Slots are separated by a constant (e.g., 40,000 units on the X-axis).
*   The Allocator guarantees slot persistence via `WorldSaveManager`.
*   Unused slots are managed via a Lazy Recycling FreeList (30-day cooldown) to prevent stale saves from warping NPCs into the void.

## 8. Debugging & Visualization
*   **MapControllerDebugUI:** A UI component attached to a Canvas within the Map environment. It visualizes the real-time state of the Map (Active vs Hibernating), tracks the exact `ActivePlayers` list, and displays macro-simulation metrics from `HibernationData` (such as the number of hibernated NPCs and items, and the last saved simulation time). This is crucial for verifying that the transition between live simulation and macro-simulation works perfectly off-screen.
