---
name: dev-mode
description: Host-only god-mode developer tool. F3 toggle (editor/dev), /devmode on|off in chat (release). Modules: Spawn (click-to-spawn NPCs with full configuration), Select (click-to-select Characters + IDevAction plug-in; first action assigns building ownership), Game Speed (preset + custom Time.timeScale via GameSpeedController).
---

# Dev Mode System

## 1. Purpose

Dev Mode is a host-only, god-mode developer tool that layers a togglable admin panel and input-gate over the normal gameplay loop. It exists so developers — and the host of a multiplayer session when testing — can spawn NPCs, poke at state, and iterate on content without restarting the session or hacking around player-controlled input. The very first slice delivers the toggle/gate infrastructure, the chat command surface, and a single module: **Dev Spawn** (click-to-spawn fully configured NPCs). Future modules (freecam, item grant, teleport, time-of-day, Assign Job) plug into the same registry. Dev Mode never ships to clients and is explicitly locked behind build flags + a chat command in release builds.

## 2. Activation Rules

| Build / Context | Unlocked at Awake? | F3 Toggle | `/devmode on\|off` | Notes |
|---|---|---|---|---|
| Unity Editor | Yes | Yes | Yes | Always unlocked for developer iteration |
| `DEVELOPMENT_BUILD` | Yes | Yes | Yes | Unlocked automatically via `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| Release build | No | No (locked) | Yes (unlocks + enables) | Host must explicitly type `/devmode on` once to unlock the session |
| Client (any build) | N/A | No | Logs "host-only" and no-ops | Dev Mode is server-authoritative and host-only |

**Unlock vs. Enable:**
- *Unlock* is a session-level gate. Once unlocked, the host can freely toggle.
- *Enable* is the live panel state. `/devmode off` calls `Disable()` (keeps session unlocked). `Lock()` is a full teardown that re-locks the session.

## 3. Public API

`DevModeManager.Instance` — singleton, host-only. Clients never see it do anything.

**Properties**

| Property | Type | Meaning |
|---|---|---|
| `IsUnlocked` | `bool` | Session gate. True in editor/dev builds or after `/devmode on` in release. |
| `IsEnabled` | `bool` | Current panel state. True while the dev panel is open and input is suppressed. |
| `SuppressPlayerInput` | `static bool` | Global read used by `PlayerController` and `PlayerInteractionDetector` to suppress gameplay action inputs. Mirrors `IsEnabled` on the active instance. WASD movement is still allowed (god-mode flying); only right-click move, TAB target, Space attack, and E interact are blocked. |
| `GodModeMovementSpeed` | `static const float` | WASD movement speed (units/second) used by `PlayerController` while dev mode is active. Default `17f`. Edit the constant to tune. |

**Events**

| Event | Signature | When it fires |
|---|---|---|
| `OnDevModeChanged` | `Action<bool isEnabled>` | Whenever `IsEnabled` flips. All dev-mode modules subscribe to react (show/hide panel, clear ghost visuals, etc.). |

**Methods**

| Method | Behavior |
|---|---|
| `Unlock()` | Sets `IsUnlocked = true`. Called automatically in editor/dev builds; called by `/devmode on` in release. |
| `Lock()` | Full teardown. Forces `IsEnabled = false`, fires `OnDevModeChanged(false)`, sets `IsUnlocked = false`. Used when the host explicitly wants to re-lock the session. |
| `TryEnable()` | No-op on clients. On host: requires `IsUnlocked`, otherwise logs a warning. Sets `IsEnabled = true` and fires event. |
| `Disable()` | Sets `IsEnabled = false`, fires event. Keeps session unlocked — this is what `/devmode off` uses so the host doesn't have to re-unlock. |
| `TryToggle()` | Flips `IsEnabled` via `TryEnable()` / `Disable()`. Bound to F3. |

## 4. Chat Commands

Entry point: `DevChatCommands.Handle(string rawInput)`. Called from `UI_ChatBar` whenever a chat message starts with `/`.

| Command | Effect |
|---|---|
| `/devmode on` | Host-only. Calls `Unlock()` then `TryEnable()`. On a client, logs a host-only warning and swallows. |
| `/devmode off` | Host-only. Calls `Disable()` (does NOT re-lock). |
| Unknown `/command` | Logs a warning (`Unknown dev command`) and swallows — never crashes, never passes through to chat. |

## 5. Input Gating Contract

While `DevModeManager.IsEnabled == true`, the two input-reading components selectively suppress gameplay actions but keep movement live:

- `PlayerController.Update()` — reads WASD into `_inputDir` as usual, then **skips the gameplay-action block** (right-click move, TAB target, combat-command auto-assignment, Space attack) when `SuppressPlayerInput` is true. `_isCrouching` is also forced false. The character keeps moving via WASD; `Move()` swaps the speed argument to `DevModeManager.GodModeMovementSpeed` so god-mode flying feels brisk.
- `PlayerInteractionDetector.Update()` — early-outs after the proximity refresh. Nearby-target tracking still updates (so the prompt restores instantly when you exit dev mode), but the E-key press path is blocked.
- `CameraFollow.LateUpdate()` — also reads `SuppressPlayerInput` (out of necessity, not as a gate). When dev mode is on, the scroll-wheel zoom drops its upper clamp (`Mathf.Max(0f, ...)` instead of `Mathf.Clamp01`) and the offset interpolation switches to `Mathf.LerpUnclamped` so the camera can pull back without limit. Exiting dev mode re-applies the clamp; `_targetZoom` snaps back to `[0, 1]` and the camera smoothly returns to the normal zoom range.

`SuppressPlayerInput` is a **static** on `DevModeManager` so hot-path Updates don't dereference the instance every frame.

## 6. Panel & Module Registry Pattern

`DevModePanel` is the panel root, loaded lazily from `Resources/UI/DevModePanel` on first enable. It owns a `ContentRoot` Transform under which each module GameObject lives as a child.

**Registration is self-service** — there is no central `RegisterModule(...)` API. Each module:
1. Is a `MonoBehaviour` on a child of `ContentRoot` in the `DevModePanel` prefab.
2. Subscribes to `DevModeManager.OnDevModeChanged` in its own `OnEnable` / `Start`.
3. Unsubscribes in `OnDisable` / `OnDestroy`.
4. Shows/hides its own UI in response to the event.

**To add a new module:**

1. Create a new `MonoBehaviour` in `Assets/Scripts/Debug/DevMode/Modules/`.
2. Subscribe to `DevModeManager.OnDevModeChanged` and implement show/hide.
3. In the `DevModePanel` prefab, add a child GameObject under `ContentRoot` with your script attached.
4. Wire `[SerializeField]` refs (dropdowns, buttons, etc.).
5. Unsubscribe cleanly in `OnDisable` / `OnDestroy`.

No edit to `DevModeManager` or `DevModePanel` is required to add a module.

### Click arbitration

`DevModeManager` exposes a single-slot click consumer: `ActiveClickConsumer` (MonoBehaviour), `OnClickConsumerChanged` (event), `SetClickConsumer(x)`, `ClearClickConsumer(x)`. Armed dev modules MUST claim the slot when arming and release when disarming, and MUST gate their click loop on `ActiveClickConsumer == this`. Subscribing to `OnClickConsumerChanged` lets a module auto-disarm when another claims the slot — so arming Select flips Spawn off, and vice versa.

## 7. Dev Spawn Module Details

`DevSpawnModule` — the first shipping module. Lets the host click anywhere on the `Environment` layer to spawn fully configured NPCs, drop `ItemSO` instances, or instantiate `HarvestableSO` resource nodes (crops / trees / ore veins). Mode is selected by a **Character / Item / Harvestable sub-tab bar** at the top of the Spawn panel.

**Configuration UI**

The Spawn panel is a stack of: `SubTabBar` (Character / Item / Harvestable buttons) → active sub-panel (`CharacterSubPanel` or `ItemSubPanel` or `HarvestableSubPanel`) → shared `Label_Count` + `CountField` + `ArmedToggle`. The three sub-panels are toggled via `SetActive`; only one is visible at a time. Count and Armed live OUTSIDE the sub-panels because they apply to every mode.

**Character sub-tab fields** (`CharacterSubPanel`):

| Field | Widget |
|---|---|
| Race | TMP Dropdown |
| Prefab | TMP Dropdown (filtered by race) |
| Personality | TMP Dropdown |
| Behavioral Trait | TMP Dropdown |
| Combat Styles | Multi-entry row list (`DevSpawnRow` per entry — combat style dropdown + level input) |
| Skills | Multi-entry row list (`DevSpawnRow` per entry — skill dropdown + level input) |

**Item sub-tab fields** (`ItemSubPanel`):

| Field | Widget |
|---|---|
| Item | TMP Dropdown — every `ItemSO` under `Resources/Data/Item` sorted by `ItemName`. No sentinel; the sub-tab itself selects mode, so index 0 is a real item. |

**Harvestable sub-tab fields** (`HarvestableSubPanel`):

| Field | Widget |
|---|---|
| Harvestable | TMP Dropdown — every `HarvestableSO` (incl. `CropSO` and `TreeHarvestableSO` subclasses) found recursively under `Resources/Data/` via `Resources.LoadAll<HarvestableSO>("Data")`, sorted by `DisplayName`. Entries without a `HarvestablePrefab` are filtered out at load time (they can't be instantiated). Shipping entries: `Crop_Wheat`, `Crop_Flower`, `AppleTreeSO`, `HarvestableSO_OreNode`. |

**Ghost-placement UX (Harvestable sub-tab only).** When the user arms the Spawn toggle while the Harvestable sub-tab is active, `DevSpawnModule` spawns a stripped clone of the selected SO's `HarvestablePrefab` as a follow-cursor ghost — mirrors [CropPlacementManager](../../Assets/Scripts/Farming/CropPlacementManager.cs)'s UX so dev-spawned harvestables line up with the production crop placement grid. The ghost snaps to the nearest `TerrainCellGrid` cell of the `MapController` under the cursor, tints green on a valid grid cell / yellow off-grid (god-mode allows off-grid spawn). **LMB** confirms a single grid-snapped spawn at the ghost position then rebuilds the ghost for chain-placement; **RMB** cancels via disarm; **ESC** disarms via the global shortcut; **Space+LMB** shortcut still works and scatters N grid-snapped copies (each independently snapped to its containing cell). Switching off the Harvestable sub-tab, changing the dropdown selection, disarming, or closing dev mode all clear the ghost. Ghost interference is stripped via the canonical pattern: `NetworkObject.enabled = false` + `Rigidbody.isKinematic = true` + disable every `Collider` / `NavMeshObstacle` / `Harvestable` / `HarvestableNetSync` component recursively + move the whole tree onto the `Ignore Raycast` layer.

**Shared (always visible regardless of sub-tab):**

| Field | Widget |
|---|---|
| Count | TMP InputField (integer, default 1) |
| Armed | Toggle |

**Click flow**

1. Host clicks on an `Environment`-layer collider. Ray is cast from the mouse.
2. **Dispatch:** `SpawnAt(anchor)` reads `_activeSubTab`. If `Item`, the click routes to `SpawnItemBatch(anchor, _items[_itemDropdown.value])`. If `Harvestable`, the click routes to `SpawnHarvestableBatch(anchor, _harvestables[_harvestableDropdown.value])`. Otherwise the character path runs.
3. All three paths share the same scatter formula: for `N = count`, the scatter **radius = `4 * sqrt(N)` Unity units** (per project rule 32, 11 units = 1.67 m, so ~0.6 m per unit of radius). Individual offsets are random within the disk.
4. **Character path:** `SpawnManager.SpawnCharacter(...)` is invoked with the configured race/prefab/personality/trait/armed flag.
5. **Item path:** `SpawnManager.SpawnItem(item, pos)` is invoked per spawn. The dev-mode wrapper adds an explicit `NetworkManager.Singleton.IsServer` check before the loop (clearer error than SpawnManager's internal check) and wraps each per-spawn call in `try/catch` so one bad item doesn't abort the batch. No combat styles / skills / personality apply on the item path.
6. **Harvestable path:** two surfaces share the same per-instance spawn helper (`TryInstantiateHarvestable`): `Instantiate(so.HarvestablePrefab) → Harvestable.InitializeAtStage(so, startStage: int.MaxValue, startDepleted: false, cellX: -1, cellZ: -1) → NetworkObject.Spawn(true)`. `int.MaxValue` is clamped to `crop.DaysToMature` internally so crop-aware SOs spawn fully mature; `cellX = -1` means free-positioned (no cell coupling, no `FarmGrowthSystem.RegisterHarvestable` call, no plow / growth-tick dependency). Both surfaces grid-snap their target position via `SnapPositionToGrid` (resolves `MapController.GetMapAtPosition` → `TerrainCellGrid.WorldToGrid` → `GridToWorld`, falls back to raw XZ when off-grid). Surfaces: **(a)** `ConfirmHarvestableGhostSpawn` (single, called from the LMB-confirm in the armed Update loop — position is already grid-snapped on the ghost itself), and **(b)** `SpawnHarvestableBatch` (scatter, called from Space+LMB shortcut + the legacy armed-click path — per-instance grid snap inside the scatter loop). Each iteration is `try/catch`-wrapped. The instance is "wild scenery you can pick or destroy immediately" — every SO subtype (`HarvestableSO`, `CropSO`, `TreeHarvestableSO`) takes this same path, so a dev-spawned apple tree, wheat plant, or ore vein all spawn as mature, free-positioned, immediately-harvestable nodes.
7. Dev-mode extras (combat styles + levels, skills + levels) are passed to `SpawnManager` via a **server-only `Dictionary<ulong, PendingDevConfig>`** keyed on NetworkObjectId. Character path only.
8. `SpawnManager.ApplyDevExtras(...)` fires post-spawn on the server, applying combat styles via `CharacterCombat.UnlockCombatStyle(style, level)` and skills via the existing `CharacterSkills` API.

**Why a pending-config dict?** `SpawnCharacter` is an async network spawn — we don't have the instance yet when we configure it. The dict is populated on the main thread before spawn and drained in the spawn callback by NetworkObjectId.

**Item catalog caching.** `_items` is loaded once in `LoadCatalogs` (called from `Start`) via `Resources.LoadAll<ItemSO>("Data/Item")`, sorted alphabetically by `ItemName`, and never mutated at click time. Adding new `ItemSO` assets requires re-entering play mode for them to appear in the dropdown.

**Harvestable catalog caching.** `_harvestables` is loaded once in `LoadCatalogs` via `Resources.LoadAll<MWI.Interactables.HarvestableSO>("Data")` (recursive — picks up `CropSO` and `TreeHarvestableSO` subclasses via polymorphism), filtered to drop entries with a null `HarvestablePrefab`, then sorted alphabetically by `DisplayName`. Adding new `HarvestableSO` assets requires re-entering play mode for them to appear in the dropdown.

**Why no cell-coupling on the dev-mode harvestable path?** The user-facing crop placement flow (`CharacterAction_PlaceCrop` → `FarmGrowthSystem.SpawnHarvestableAt`) plows the cell, anchors to `(cellX, cellZ)`, and registers with `FarmGrowthSystem` so the daily tick advances growth. The dev-mode path intentionally skips that: dropping a crop in dev mode is for *testing harvest behavior*, not testing growth. Free-positioned + instant-mature gives "I want to pick this in 2 seconds" semantics — no plowing, no day-skip, no FarmGrowthSystem dependency. Use the production crop-plant flow when you need to test growth.

**Spawn panel layout contract — DO NOT REGRESS.** The Spawn panel uses Unity Auto Layout (nested VLG/HLG) end-to-end. Two contracts must hold or the layout collapses:

1. **Every direct child of `ContentRoot` and of `SpawnTab` that hosts another `LayoutGroup` must carry a `LayoutElement`.** `LayoutGroup` itself reports `flexibleHeight=-1` and a preferred height derived from its own children — when those children also stretch via anchors, the chain returns 0 and the parent VLG redistributes the empty space unpredictably (in the original Spawn-tab regression, the top `TabBar` ended up consuming the whole panel). The fix shipped in the prefab: add a `LayoutElement` with explicit `MinHeight` / `PreferredHeight` and `FlexibleHeight=0` on `TabBar` (36) and `SubTabBar` (32) so they stay thin, and `FlexibleHeight=1` on `CharacterSubPanel` / `ItemSubPanel` so the active one takes the remaining vertical space.
2. **`CharacterSubPanel` and `ItemSubPanel` must use top-stretch anchors `(0,1) → (1,1)` with pivot `(0.5, 1)`, NOT center-stretch `(0,0) → (1,1)`.** `SpawnTab`'s VLG runs with `ChildControlHeight=1` so it actively sets the children's heights. Center-stretch anchors with `SizeDelta (0,0)` make the panel report a rect height equal to the parent (chaos). Top-stretch with a real `SizeDelta.y` is what the VLG expects.

Adding a fourth sub-tab (e.g., "Furniture") = create a sibling `*SubPanel` GameObject following the ItemSubPanel / HarvestableSubPanel template (top-stretch anchors, VLG, LayoutElement with `FlexibleHeight=1`), add a button to `SubTabBar`, register it in `DevSpawnModule._*SubPanel` / `_*SubTabButton`, and extend the `SpawnSubTab` enum + `SpawnAt` dispatch. The canonical MCP-Roslyn recipe used for the 2026-05-18 Harvestable sub-tab: `PrefabUtility.LoadPrefabContents` → `Object.Instantiate(SubTab_Item, SubTabBar)` + `SetSiblingIndex(SubTab_Item.GetSiblingIndex() + 1)` → relabel TMP → `Object.Instantiate(ItemSubPanel, SpawnTab)` + sibling index + `SetActive(false)` → rename child `Label_Item` and `Dropdown_Item` to the new names → `new SerializedObject(spawnModule)` + `FindProperty(...).objectReferenceValue = ...` for each new SerializeField → `ApplyModifiedPropertiesWithoutUndo` → `PrefabUtility.SaveAsPrefabAsset` → `PrefabUtility.UnloadPrefabContents`.

### 7.1 Placement-aware sub-tab pattern (MANDATORY for every new dev-spawn surface that drops a world entity)

**Rule:** any future Spawn sub-tab that drops a *world entity that exists on the TerrainCellGrid* (Building, Furniture-in-world, NPC-in-formation, dynamic ore patch, weather marker, decal, …) **MUST** mirror the ghost-placement + grid-snap pattern enshrined by the 2026-05-18 Harvestable sub-tab. Click-and-spawn-at-raw-hit-point is only acceptable for non-spatial drops (Item: just falls; Character: pathfinds to walkable mesh). Anything that should "land on a specific cell" goes through ghost + grid snap. No exceptions for "dev tool, simple is fine" — the harvestable shipping pass proved the convention is small enough to mirror exactly.

**The seven canonical pieces** — replicate verbatim, not approximated:

1. **Per-SubTab serialized state on `DevSpawnModule`:**
   - `private GameObject _xxxGhost;` — the live ghost instance (null when inactive).
   - `private <SO type> _xxxGhostSO;` — the SO the current ghost was built from (so `HandleXxxDropdownChanged` can detect a swap).
   - `private Vector3 _xxxGhostSnappedPos;` — last grid-snapped XZ + raycast Y. The confirm-click path reads this directly so the ghost's visible position IS the spawn position.
   - `private bool _xxxGhostIsOnGrid;` + `int _xxxGhostCellX, _xxxGhostCellZ;` + `MapController _xxxGhostMap;` + `TerrainCellGrid _xxxGhostGrid;` — diagnostic state for the confirm log and validation.
   - `private bool _warnedNoCameraGhost, _warnedRayMissGhost;` — first-time-warn-once flags so the verbose log doesn't spam.

2. **Lifecycle hooks** (every one of these must clear the ghost; only the first two also build it):
   - `HandleArmedChanged(bool armed)`: `if (armed && _activeSubTab == SpawnSubTab.Xxx) EnsureXxxGhost(); else ClearXxxGhost();`
   - `Show<Xxx>SubTab()`: if armed, call `EnsureXxxGhost()` so a sub-tab switch re-spawns the ghost without re-toggling Armed.
   - `Show<Other>SubTab()` (every OTHER sub-tab show method): call `ClearXxxGhost()` so leaving the sub-tab destroys the ghost.
   - `HandleXxxDropdownChanged(int)`: if `_xxxGhost != null` call `EnsureXxxGhost()` to rebuild from the new SO.
   - `HandleDevModeChanged(bool enabled)`: if `!enabled` call `ClearXxxGhost()` (defensive — dev-mode-off should always nuke ghosts).
   - `OnDestroy` / `UnwireListeners`: call `ClearXxxGhost()` defensively.

3. **`EnsureXxxGhost()` builder** — pattern (factor into a private method):
   - `ClearXxxGhost()` first (always rebuild from scratch).
   - Read selected SO from dropdown + catalog list; null-check; null-check the SO's prefab.
   - `_xxxGhostSO = so; _xxxGhost = Instantiate(so.<PrefabField>); _xxxGhost.name = "Dev<Xxx>Ghost_" + so.Id;`
   - `DisableGhostInterference(_xxxGhost)` — see piece 6.
   - `TintGhost(_xxxGhost, 1f, 1f, 1f, 0.7f)` neutral until `UpdateXxxGhostPosition` runs.
   - Reset `_warnedNoCameraGhost = _warnedRayMissGhost = false`.
   - Call `UpdateXxxGhostPosition()` once so the ghost appears at the cursor on the very first frame (no flicker at world origin).

4. **`UpdateXxxGhostPosition()` per-frame mover** — called from `Update()` inside the Harvestable / Xxx sub-tab branch (NOT from `LateUpdate`; the raycast must run before the click handler):
   - Bail if `_xxxGhost == null` (defensive) or `Camera.main == null` (warn-once via `_warnedNoCameraGhost`).
   - `Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 500f, _environmentLayerMask)`. If miss, warn-once via `_warnedRayMissGhost` and leave the ghost at its last position.
   - Reset `_warnedRayMissGhost = false` once a valid hit returns.
   - Default `finalPos = hit.point; onGrid = false; cellX = cellZ = -1;`.
   - `var map = MapController.GetMapAtPosition(hit.point);` — null → off-grid (yellow tint, still spawnable in god mode).
   - If map: `EnsureGridInitialized(map);` (see piece 5), get `grid = map.GetComponent<TerrainCellGrid>()`. If `grid.WorldToGrid(hit.point, out cellX, out cellZ)` → `var snapped = grid.GridToWorld(cellX, cellZ); snapped.y = hit.point.y; finalPos = snapped; onGrid = true;`.
   - **CRITICAL: preserve `hit.point.y`, not `grid.GridToWorld(...).y`.** The grid plane Y is usually 0; the actual ground surface Y is on the raycast hit. Mixing them sinks ghosts into terrain or floats them above it.
   - Assign to `_xxxGhost.transform.position` + write back `_xxxGhostSnappedPos / _xxxGhostIsOnGrid / _xxxGhostMap / _xxxGhostGrid / _xxxGhostCellX / _xxxGhostCellZ`.
   - **Tint:** green `(0.6, 1, 0.6, 0.75)` when on grid, yellow `(1, 1, 0.4, 0.7)` when off grid. If the new entity type has stricter validation (footprint overlap, terrain type, ownership), tint red and block the confirm. For a building dev-spawn: green on grid AND footprint clear AND on valid terrain, yellow on grid but god-mode-acceptable (footprint overlap), red off grid AND/OR off-map (still spawnable but visually flagged).

5. **`EnsureGridInitialized(MapController map)` static helper** — already in [DevSpawnModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs). Copy-of-CropPlacementManager defensive bootstrap. **Do not** re-implement per-sub-tab; reuse the existing static method.

6. **`DisableGhostInterference(GameObject ghost)` static helper** — already in [DevSpawnModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs). When adding a new entity type, **extend the existing helper** to also disable the entity's specific scripts (e.g. for buildings: disable `Building`, `BuildingTaskManager`, `BuildingLogisticsManager`, every `Furniture` baked into the prefab, etc.) — otherwise the entity's `Awake → Update` poll will hammer on an unspawned NetworkObject and NPE. The pattern: every component on the entity that does NetworkVariable / NetworkObject lookups in its `Update` MUST be in the disable list. Don't trust "the NetworkObject is disabled so the NetVar reads will fail gracefully" — many of our components ToString-stringify the NetVar value into a log path that throws if the NGO sentinel is uninitialised.

7. **`ConfirmXxxGhostSpawn()` LMB-confirm + chain-rebuild path** — pattern:
   - Read `_xxxGhostSO`; null-check; validate via shared `Validate<Xxx>Spawn(so)` (server-only check, prefab non-null check).
   - Spawn at `_xxxGhostSnappedPos` via the shared `TryInstantiate<Xxx>(so, pos, parentMap, out instance)` helper — **passing the cached `_xxxGhostMap`** so the spawn re-parents under the MapController.
   - Compose a one-line log with the cell info (or "off-grid" / "free-positioned" / "off-grid + off-map (scene root)" depending on the path taken).
   - **Call `EnsureXxxGhost()` again at the end so the ghost rebuilds for the next placement** — this is the chain-spawn behavior that lets a dev drop multiple copies without re-arming. Mirrors `CropPlacementManager`'s implicit chain-rebuild minus the seed-consume gate.

8. **`TryInstantiate<Xxx>(so, pos, parentMap, out instance)` shared per-instance helper** — MUST re-parent the spawn under the `MapController`'s `NetworkObject`:
   ```csharp
   if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
   {
       netObj.Spawn(true);
       // CRITICAL: re-parent under the map's NetworkObject so the spawn lives inside the
       // map's hibernation scope. Without this the harvestable / building / NPC sits at
       // scene root, is ignored by MapController.Hibernate, and never serialises when the
       // map sleeps. worldPositionStays:true keeps the visual fixed across the re-parent.
       // Mirrors FarmGrowthSystem.SpawnHarvestableAt's final block verbatim.
       if (parentMap != null && parentMap.TryGetComponent<NetworkObject>(out var mapNetObj))
       {
           if (!netObj.TrySetParent(mapNetObj, worldPositionStays: true))
               Debug.LogWarning($"[DevSpawn] TrySetParent failed for '{so.name}' under map '{parentMap.name}' — falling back to scene root.");
       }
   }
   ```
   Pass `parentMap = null` to leave the spawn at scene root (off-map fallback). The scatter path threads the map through via `SnapPositionToGrid(scatteredPos, out MapController scatterMap)`. The ghost-confirm path reads `_xxxGhostMap` (already cached by `UpdateXxxGhostPosition`).

**The scatter path (Space+LMB) must ALSO grid-snap.** Don't let the global Space+LMB shortcut fall through to raw hit-point spawning when ghost-mode applies to the sub-tab. Per-instance call to `SnapPositionToGrid(scatteredPos)` inside the scatter loop — each scattered copy lands on its own cell. The 2026-05-18 Harvestable scatter path is the reference implementation; clone its shape exactly.

**For a future Building dev-spawn specifically** — the building's `BuildingSO.GridFootprintCells` (Vector2Int) defines its footprint; snap to the anchor cell and verify the full footprint is clear via the same `IsFootprintOccupied`-style predicate that `BuildingPlacementManager` uses for the player flow. Don't reinvent the validation — call into the existing predicates. The ghost should show the *full* footprint outline (BuildingPlacementManager's `_footprintOutlineLine` LineRenderer pattern), tinted to match grid-occupancy validity. Dev god mode still allows spawning a building that overlaps an existing one (red tint warning, no hard block) — that's the whole point of "dev mode bypasses validation". If you find yourself adding a hard block for "out of community range" or "tier requirement not met", you're recreating the player flow — stop and check the rule.

**Tracking:** new placement-aware sub-tabs are listed in `wiki/systems/dev-mode.md` Open Questions / TODO until shipped. Mark them shipped with a change-log entry + a sentence in this section if the rules ever need extending.

## Select Tab

Click-to-select for Characters + pluggable actions via the `IDevAction` interface.

### DevSelectionModule

Attached to the SelectTab GameObject in `DevModePanel.prefab`. Public API:

- `InteractableObject SelectedInteractable { get; }` — the currently selected interactable (any type), or null. Generalized from the original Character-only API.
- `Character SelectedCharacter { get; }` — back-compat convenience populated whenever the interactable resolves to a Character. Existing `IDevAction` consumers keep working unchanged.
- `event Action<InteractableObject> OnInteractableSelectionChanged` — fires on any interactable selection change. `DevInspectModule` subscribes here.
- `event Action OnSelectionChanged` — back-compat event; fires when `SelectedCharacter` changes.
- `void SetSelectedInteractable(InteractableObject)` / `void SetSelectedCharacter(Character)` — replaces the selection. The character overload routes through `SetSelectedInteractable` once it has resolved a `CharacterInteractable`.
- `void ClearSelection()` — sets selection to null.
- `bool TrySelectAtCursor(out string label)` — interior raycast against `_selectableLayerMask` (default `RigidBody + Furniture + Harvestable`). Backs the armed Select toggle and the global Ctrl+Click shortcut. `Harvestable` is included so any `Harvestable` (crop or wilderness) on layer index 15 — named `Harvestable` in the Tags & Layers list — can be picked. Wilderness prefabs (`Tree.prefab`, `Gatherable.prefab`) currently sit on the `Default` layer — move them onto `Harvestable` (or another layer in the mask) to make them selectable too.
- `bool TrySelectBuildingAtCursor(out string label)` — building raycast against `_buildingLayerMask` (default `Building`). Backs the global Alt+Click shortcut. Bypasses the interior pick when the user explicitly wants the building shell, even when furniture or characters sit along the same ray. **Special case (2026-05-08):** when the resolved `InteractableObject` is a `BuildingInteractable` (added 2026-05-06 by the cooperative construction loop, lives on the Building's root GameObject), walk up to its `Building` and call `SetSelectedBuilding(...)` instead of `SetSelectedInteractable(...)`. Without this branch, `OnBuildingSelectionChanged` would never fire and the Inspect tab would route to no `IInspectorView`. Any future `InteractableObject` subclass added directly on a Building's root GameObject must extend the same special case.

Selection is cleared automatically on `SceneManager.sceneUnloaded` and on `DevModeManager.OnDevModeChanged(false)` — prevents stale references.

**Click flow (dual mask, two entry points):** armed toggle (legacy) and Ctrl+Click both call `TrySelectAtCursor`, which raycasts the **interior** mask (`_selectableLayerMask`, default `RigidBody + Furniture + Harvestable`); Alt+Click calls `TrySelectBuildingAtCursor`, which raycasts the **building** mask (`_buildingLayerMask`, default `Building`). Both paths walk parents via `GetComponentInParent<InteractableObject>()` first, falling back to `GetComponentInParent<Character>()` when the collider's hierarchy has no interactable wrapper. The two-mask split exists because building shells physically enclose their interior contents — a single-pass raycast against `(RigidBody | Furniture | Building)` would always hit the building first, blocking selection of the chest, bed, or NPC inside. The serialized fields are `[FormerlySerializedAs("_characterLayerMask")] _selectableLayerMask` and `_buildingLayerMask`; both auto-default at runtime when left at zero, with `BuildMask` tolerating any layer name missing from Tags & Layers.

### IDevAction

Plug-in interface for Select-tab actions. Each action is a MonoBehaviour parented under the Select tab's ActionsContainer.

```csharp
public interface IDevAction
{
    string Label { get; }
    bool IsAvailable(DevSelectionModule sel);
    void Execute(DevSelectionModule sel);
}
```

Action recipe:
1. Create `MyDevAction : MonoBehaviour, IDevAction` under `Assets/Scripts/Debug/DevMode/Modules/Actions/`.
2. Hold `[SerializeField] DevSelectionModule _selection; [SerializeField] Button _button; [SerializeField] TMP_Text _buttonLabel;`.
3. In `Start`, wire the button click to `OnButtonClicked` and subscribe to `_selection.OnSelectionChanged` to refresh the button's interactable state via `IsAvailable`.
4. If the action needs to consume additional clicks (e.g., pick a second target), use `DevModeManager.SetClickConsumer(this)` while armed and the standard click-loop pattern.
5. Add the action GameObject as a child of the SelectTab's `ActionsContainer` in the prefab.

### DevActionAssignBuilding (first action)

Enabled when a Character is selected. On `Execute`, claims the click slot and waits for the next `LayerMask.GetMask("Building")` hit. Dispatches polymorphically:

- `CommercialBuilding` → `SetOwner(character, null)` (makes character the boss).
- `ResidentialBuilding` → `SetOwner(character)` (sets primary owner; character also becomes resident).

ESC cancels. Another module claiming the click slot also cancels. Selection is preserved after success so further actions can chain.

## Game Speed Module

`DevGameSpeedModule` — dev-side surface for the networked [`GameSpeedController`](../game-speed-controller/SKILL.md). Mirrors the production `UI_GameSpeedController` preset buttons (0× / 1× / 2× / 4× / 8×) and adds a free-form custom-speed input field so devs can stress-test slow-mo (0.25×) or extreme fast-forward (16×+) without rebuilding.

**Fields (`DevModePanel.prefab` → Speed tab content):**

| Field | Widget | Speed |
|---|---|---|
| Pause | `Button` | 0× |
| Normal | `Button` | 1× |
| Fast | `Button` | 2× |
| Super Fast | `Button` | 4× |
| Giga | `Button` | 8× |
| Custom value | `TMP_InputField` | any positive float (parsed with `InvariantCulture` so `0.5` works regardless of OS locale) |
| Apply Custom | `Button` | reads the input field |
| Current Speed | `TMP_Text` | read-only label, updated from `OnSpeedChanged` |
| Active color | `Color` (Inspector) | tint applied to the active preset button |
| Inactive color | `Color` (Inspector) | tint applied to the inactive preset buttons |

**Flow**

1. Every click (preset or Apply Custom) routes through `GameSpeedController.Instance.RequestSpeedChange(speed)`.
2. `RequestSpeedChange` handles the server-vs-client split internally: the host writes `_serverTimeScale.Value`, a client routes via `[Rpc(SendTo.Server)]`. The dev module makes no assumption about who is calling it — the controller decides.
3. The module subscribes to `OnSpeedChanged` in `OnEnable` and unsubscribes in `OnDisable`, so each tab activation reattaches the listener and refreshes the visual state from the current `Time.timeScale`.
4. **Fallback path:** when `GameSpeedController.Instance` is null (solo scene playback with no networked DayNightCycle prefab, or the prefab hasn't spawned yet), the module writes `Time.timeScale` directly and refreshes its own visuals. `RequestSpeedChange` itself does the same when `!IsSpawned`; the module's branch covers the case where the singleton doesn't exist at all.
5. Custom-value parsing is locale-safe (`InvariantCulture`) so `0.5`, `1.5`, `16` all work on every locale. Bad input logs a warning and leaves speed unchanged.
6. Speed clamps at `0` (no negative values, same contract as `GameSpeedController.RequestSpeedChange`).

**Why this lives in dev mode instead of always-on UI** — the production `UI_GameSpeedController` is part of the player HUD with a fixed 5-button vocabulary. The dev module exists so iteration can run outside that vocabulary (slow-mo for visual inspection, 16× for stress-testing tick catch-up), without polluting the player HUD or risking a player accidentally locking the session into a broken speed. Project rule #26 still holds: any animation inside the dev panel itself must use `Time.unscaledDeltaTime` so it keeps working at 0× / 8×.

**Prefab wiring**

Already wired in `Assets/Resources/UI/DevModePanel.prefab` (2026-05-13):
- `ContentRoot/TabBar/SpeedTabButton` — new tab button (label "Speed"), cloned from `TimeSkipTabButton`.
- `ContentRoot/SpeedTab` — content GameObject with `VerticalLayoutGroup` (cloned from `TimeSkipTab`); children: `Header`, `PauseButton`, `NormalButton`, `FastButton`, `SuperFastButton`, `GigaButton`, `Label_Custom`, `CustomField` (`TMP_InputField`, `ContentType.DecimalNumber`, placeholder "e.g. 0.25, 1.5, 16"), `ApplyCustomButton`, `CurrentSpeedLabel`.
- `DevGameSpeedModule` attached to `SpeedTab` with all `[SerializeField]` refs assigned.
- New `TabEntry` appended to `DevModePanel._tabs` so `DevModePanel.SwitchTab` picks it up automatically.

If you ever need to rebuild it from scratch (e.g. after a destructive prefab merge), the recipe is:

1. Clone `TimeSkipTabButton` under `ContentRoot/TabBar`, rename `SpeedTabButton`, change its label to "Speed".
2. Clone `TimeSkipTab` under `ContentRoot`, rename `SpeedTab`. Remove the cloned `DevTimeSkipModule` and delete its child UI.
3. Attach `DevGameSpeedModule` to the new `SpeedTab`.
4. Clone `TimeSkipTab/SkipButton` 6× → `PauseButton`, `NormalButton`, `FastButton`, `SuperFastButton`, `GigaButton`, `ApplyCustomButton`. Update each label.
5. Clone `TimeSkipTab/HoursInput` → `CustomField` (set `ContentType.DecimalNumber`).
6. Clone `TimeSkipTab/StatusLabel` 3× → `Header`, `Label_Custom`, `CurrentSpeedLabel`.
7. Wire all `[SerializeField]` refs on `DevGameSpeedModule`.
8. Append a new entry to `DevModePanel._tabs` with `TabButton = SpeedTabButton.Button`, `Content = SpeedTab`.

## 8. Integration Points

Files touched by the dev-mode slice:

| File | Role |
|---|---|
| `Assets/Scripts/Debug/DevMode/DevModeManager.cs` | Singleton — state, events, input gate read |
| `Assets/Scripts/Debug/DevMode/DevModePanel.cs` | Panel prefab root, lazy-loads from `Resources/UI/DevModePanel` |
| `Assets/Scripts/Debug/DevMode/DevChatCommands.cs` | `/devmode on\|off` parser; single `Handle(rawInput)` entry point |
| `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs` | Click-to-spawn module UI + click handler |
| `Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs` | Multi-entry row (combat style or skill + level) |
| `Assets/Scripts/Debug/DevMode/Modules/DevGameSpeedModule.cs` | Game-speed tab: preset buttons + custom value, routed through `GameSpeedController` |
| `Assets/Scripts/DayNightCycle/GameSpeedController.cs` | Server-authoritative `Time.timeScale` + day/time sync (consumed by the dev module via `RequestSpeedChange` / `OnSpeedChanged`) |
| `Assets/Resources/UI/DevModePanel.prefab` | Panel prefab with `ContentRoot` and modules as children |
| `Assets/Resources/UI/DevSpawnRow.prefab` | Reusable row prefab for combat style / skill lists |
| `Assets/Scripts/SpawnManager.cs` | Extended `SpawnCharacter` signature + `PendingDevConfig` dict + `ApplyDevExtras` server path |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | `UnlockCombatStyle(style, level)` overload used by dev mode |
| `Assets/Scripts/UI/UI_ChatBar.cs` | Routes `/`-prefixed messages to `DevChatCommands.Handle` |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Input gate — checks `SuppressPlayerInput` |
| `Assets/Scripts/Character/PlayerInteractionDetector.cs` | Input gate — early-out on `SuppressPlayerInput` |

## 9. Known Limitations

Explicit list of what this first slice does **not** cover. These are follow-up work, not bugs.

- **Personality IS applied server-side** on both networked and offline spawns (fixed post-review — `PendingDevConfig.Personality` now carries the dev-picked value into `InitializeSpawnedCharacter`). **Behavioral trait replication** to late-joining / non-host clients is still a known gap — the server applies the value on the spawned Character, but no dedicated `NetworkVariable` replicates it across all clients. A late-joiner sees defaults until the next full save round-trip. Follow-up slice.
- **Combat style live sync** — only the server applies styles via `UnlockCombatStyle(style, level)`. Reconnecting clients rebuild state from save data. A live `NetworkList<>`-based equivalent for combat styles is future work.
- **Skills do replicate** — `CharacterSkills` already owns `NetworkList<NetworkSkillSyncData>`, so skill levels granted by dev-mode propagate correctly to all clients. No additional work needed.
- **Jobs are deliberately excluded** from this slice. Assigning a job requires a `CommercialBuilding` workplace; it will ship as its own "Assign Job" module that lets the host target a building after spawning.
- **Freecam, sim-pause, invulnerability, item grant, teleport** — all future modules. None of them exist yet.
- **Client dev-mode** — out of scope. Dev Mode is host-only today. Giving clients any of this power needs a separate design pass for trust and replication.
- **Panel has no ScrollView** — plain `Transform` containers. Long combat-style / skill lists will overflow vertically. A scroll pass is deferred.
- **Prefab's TMP Dropdowns and InputFields use minimal default visuals** — no custom arrow sprite, no custom checkmark. Visual polish is deferred; functionality is the priority for this slice.
- **No visual selection indicator** — the selected character is shown by label only in the panel. Follow-up slice can add a world-space outline (shader-based per rule 25) or a UI marker.
- **No undo on ownership assignment** — the new owner replaces the previous via `SetOwner`'s existing semantics. No confirmation dialog.
- **Character-first flow only** — "click building first, then assign a character to it" is deferred.
- **No multi-character selection** — one at a time.
- **No exclude-self filter** — clicking on the host's own character selects it. Add a toggle if this becomes annoying.
- **Worker/resident/job actions** — deferred. Assign Building sets ownership only.
- **Item selection + actions** — not in this slice.

## 10. Extension Notes (Follow-Up Modules)

Planned modules, each to be added as a new child GameObject under `DevModePanel.ContentRoot` with its script subscribing to `OnDevModeChanged`:

| Module | Notes |
|---|---|
| Freecam | Detach camera from player, WASD + mouse look, speed slider. Must not interact with `Time.timeScale`. |
| NPC selection / edit | Click existing NPC -> panel showing needs, stats, inventory; edit live. |
| Item grant | Dropdown + count -> add directly to target inventory via `CharacterAction`. |
| Teleport | Click on map or enter coords -> teleport selected character. |
| Time-of-day slider | Drives `TimeManager.CurrentTime01` directly. |
| Assign Job | Select existing NPC, then click `CommercialBuilding` to assign. |

All of them follow the same contract: one MonoBehaviour per tab, self-register by subscribing to `DevModeManager.OnDevModeChanged`, unsubscribe in `OnDisable`/`OnDestroy`, no edits to `DevModeManager` or `DevModePanel` required.

## 11. Character City Founding Sub-Tab (2026-05-18)

`CharacterCityFoundingSubTab` — 11th sub-tab on `CharacterInspectorView` ("Founding" in the tab bar). Dedicated playtest harness for the Plan 4 city-founding loop (community creation → AB placement → tier-up → drifter migration → join requests) without authoring full content or waiting for the natural in-game cadence. Lives at `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterCityFoundingSubTab.cs`. Wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD` end-to-end.

### Why widgets instead of text

Existing `CharacterSubTab`s (Identity / Stats / Skills / Needs / AI / Combat / Social / Economy / Knowledge / Inventory) all output formatted text via the abstract `RenderContent(Character) → string`. The City Founding tab is interactive (buttons, input fields) so the 2026-05-18 commit virtualised `CharacterSubTab.Refresh` and `CharacterCityFoundingSubTab` overrides it directly to render programmatic UGUI widgets — mirroring the `BuildingConsoleManagementSubTab` pattern. `RenderContent` returns empty (satisfies the abstract base); the inherited `_content` TMP_Text is intentionally unwired on this variant.

Widgets parent under a `[SerializeField] private RectTransform _widgetRoot` field (wired to the inner `Viewport/Content` RectTransform that carries the inherited `VerticalLayoutGroup` + `ContentSizeFitter`). Falls back to the script's own transform when null so the sub-tab also works on a flat (non-scrolling) panel.

### Sections (top to bottom)

| Section | Visibility | What it does |
|---|---|---|
| DEV banner | always | Red banner — flags this surface as bypassing production gates |
| Refresh button | always | Re-runs `RebuildAll()`; useful for re-reading live state (treasury balance, member count) without triggering a mutator |
| Status header | always | Shows Character name, CurrentCommunity (name + level), Citizenship — color-coded |
| Create Community | `CurrentCommunity == null` | Optional name input (default "DebugCity"). Calls `CharacterCommunity.CreateCommunity(name)` — same path `Task_CreateCommunity` uses, so the founder auto-receives the AB blueprint |
| Ambition_FoundACity | `CharacterAmbition != null` | Loads `Resources/Data/Ambitions/Ambition_FoundACity` and calls `CharacterAmbition.SetAmbition(so)`. Adds a "Clear Ambition" button when one is already active |
| Community readout | `CurrentCommunity != null` | Diagnostic only: name / level / IsChartered / member count / leader count / primary leader / AB ref / AB treasury (CurrencyId.Default) |
| Force-Promote | `CurrentCommunity != null && AB != null` | −1/+1 tier buttons. Routes through `AdministrativeBuilding.DevForceChangeCommunityLevel(int delta)` — bypasses `Community.TryPromoteLevel`'s population / treasury / required-building gates |
| Grant Treasury | `CurrentCommunity != null && AB != null` | Designer-supplied amount input (default 1000). Calls `CommercialBuilding.DevForceCreditTreasury(int)` which resolves currency via enclosing map's NativeCurrency |
| Submit Join Request | `CurrentCommunity == null && Citizenship == null && ≥1 chartered AB in scene` | One row per candidate chartered AB. Click calls `AdministrativeBuilding.SubmitJoinRequestServerRpc(applicantNetId)` — identical to the drifter ↔ JoinRequestDesk path |
| Time | always | Live Day / Hour / Phase readout + count input + `[DEV] Force NewDay` button. Calls `MWI.Time.TimeManager.DevForceNewDay(count)`, which increments `CurrentDay` + fires `OnNewDay` once per day so `DrifterMigrationSystem` / `FarmGrowthSystem` / ambition deadlines see the same event volume |

### New `DevForce*` methods this sub-tab depends on

| Method | Located on | Behaviour |
|---|---|---|
| `AdministrativeBuilding.DevForceChangeCommunityLevel(int delta)` | `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs` | Host-only + `DevAssertHostAndDevMode`-gated. Clamps to `CommunityLevel` enum bounds. Calls `OwnerCommunity.ChangeLevel((CommunityLevel)clamped)` — fires the same change log as production tier-up |
| `CommercialBuilding.DevForceCreditTreasury(int amount)` | `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Host-only + `DevAssertHostAndDevMode`-gated. Resolves currency from `MapController.GetMapAtPosition(transform.position).NativeCurrency` (CurrencyId.Default fallback). Routes through the canonical `CreditTreasury(currency, amount, reason)` path |
| `MWI.Time.TimeManager.DevForceNewDay(int count = 1)` | `Assets/Scripts/DayNightCycle/TimeManager.cs` | Host-only (`NetworkManager.Singleton.IsServer`) + DevMode-gated. Bumps `CurrentDay` by `count` and fires `OnNewDay` once per day. Phase/hour untouched |

### Prefab wiring

`Assets/Resources/UI/DevModePanel.prefab` → `ContentRoot/InspectContent/Views/CharacterInspectorView`:
- `TabBar/Btn_CityFounding` — button cloned from `Btn_Inventory`, label changed to "Founding".
- `SubTabContents/CityFounding` — content GameObject cloned from `Inventory`. `InventorySubTab` component replaced by `CharacterCityFoundingSubTab`. Inherited `_content` TMP_Text removed (would render an empty band otherwise). The script's `_widgetRoot` field wired to `Viewport/Content` RectTransform.
- `CharacterInspectorView._subTabs` extended from 10 → 11 entries; the new entry is `(TabButton = Btn_CityFounding's Button, Content = CityFounding GO, Tab = CharacterCityFoundingSubTab MonoBehaviour)`.

### Authority + network model

Dev Mode is host-only (gated by `DevModeManager.TryEnable` IsServer check). Every mutator on this sub-tab either:
1. Calls a `DevForce*` method whose first line asserts host + DevMode and emits an audit log via the inherited `DevAssertHostAndDevMode`, OR
2. Calls a public POCO method on `CharacterCommunity` / `Community` / `CharacterAmbition` whose contract is "server-only by caller convention" — safe because dev mode runs on the server.

No new replication channels introduced. State changes flow through the existing paths (Community is a POCO, not a NetworkBehaviour; `OnNewDay` is a host-only event whose subscribers all run on the server; `SubmitJoinRequestServerRpc` already handles ApplicantNetId replication through `NetworkList<JoinRequest>`).

### Adding a new section

Append `BuildXxxSection(_bound);` to `RebuildAll()` and implement `BuildXxxSection(Character c)`. Use the inherited `MakeRow / MakeLabel / MakeHeader / MakeButton / MakeInput` helpers. Capture mutator targets into local variables before passing into lambdas (don't close over `_bound` directly — it can change between button-click rebuilds). End mutator callbacks with a `RebuildAll()` call so the readout reflects the new state immediately.

For state-mutating buttons that talk to a `NetworkBehaviour`, either call an existing `DevForce*` helper or add a new one — never poke the production path directly without the host + DevMode assertion.
