# Building Default-Furniture Auto-Conversion — Design

- **Date:** 2026-05-01
- **Status:** Implemented (Tasks 1–5, 8–9 landed; Tasks 6–7 manual playtest in progress)
- **Author:** Silac (via Claude Opus 4.7)
- **Plan:** [2026-05-01-building-default-furniture-auto-conversion.md](../plans/2026-05-01-building-default-furniture-auto-conversion.md)
- **Implementation commits:** `344d92ee` → `09a9d208` (11 commits)

## Implementation deviations from spec

The implementation followed the design exactly, with two tightenings worth noting:
- **Room-walk loop boundary:** spec wrote the loop as `t != transform.parent` (which would let the building root itself match if it carries a `Room` component — and it does, via `Building : ComplexRoom : Room`). Code uses `t != transform` instead, so the building root is excluded from the search and furniture parented directly under the root yields `TargetRoom = null` per the spec's stated edge-case wording. (Caught in Task 3 review, fixed in `063eec0e`.)
- **Summary log condition:** spec said "fire only when `converted > 0`". Plan task 7.7 expected the log even when only `skipped > 0` (so the "nested plain-MonoBehaviour furniture preserved" case is observable in playtest). Code uses `if (converted > 0 || skipped > 0)`. (Caught in Task 4 review, fixed in `674a720c`.)

Both deviations are documented in the matching commit messages.

## Post-ship hotfix — DestroyImmediate (2026-05-01)

The shipped `ConvertNestedNetworkFurnitureToLayout` used plain async `Destroy()` to
remove the doomed children. This created a same-frame race with every Building
Instantiate→Spawn callsite (`MapController.SpawnSavedBuildings`,
`MapController.WakeUp`, `BuildingPlacementManager`): `Destroy` queues for end-of-frame,
but `NetworkObject.Spawn()` runs synchronously in the same frame, so the doomed
children were still physically alive in the hierarchy when NGO walked them.

Two symptoms:
1. **Silent functional bug:** `TrySpawnDefaultFurniture`'s dedup snapshot at
   `GetComponentsInChildren<Furniture>(includeInactive: true)` saw the doomed children
   as "already present" — every default-furniture slot was skipped and the building
   spawned empty.
2. **Client-join NRE:** the half-walked NetworkObject children left enough stale state
   in NGO's spawned-objects path to NRE at `NetworkObject.Serialize` (line 3172) the
   next time a client joined the host.

Fix: `Destroy(furniture.gameObject)` → `DestroyImmediate(furniture.gameObject)` at
`Assets/Scripts/World/Buildings/Building.cs` lines 667 + 707. Safe in this exact
context because (a) the child NOs have `IsSpawned == false` and are absent from
`SpawnedObjects`, (b) the destroyed object is a child GameObject, not the GameObject
whose Awake we're inside. The docstring at line 601-640 now explicitly documents why
`DestroyImmediate` is mandatory, and a memory entry
(`feedback_destroyimmediate_in_awake_strip.md`) captures the gotcha.

Companion safety net: `SpawnDefaultFurnitureSlot` now wraps its post-Spawn parenting
+ registration in a try/catch that **despawns** the just-Spawned NetworkObject if any
subsequent step throws — preventing a half-set-up NO from sitting in
`SpawnedObjectsList` and NRE'ing the next scene-sync.

## Problem

Authoring a building prefab today forces a painful split between **what
the level designer sees** and **what actually spawns at runtime**:

- NGO does **not** support nested `NetworkObject`s on prefabs that are
  `Spawn()`'d at runtime (only scene-authored NetworkObjects support
  nesting). A nested furniture `NetworkObject` baked inside a building
  prefab causes NGO to half-register the child during the parent's
  spawn, leaving a broken entry in `SpawnManager.SpawnedObjectsList`
  that NRE's during the next client scene-sync. This silently breaks
  remote-client joining (host-only testing never sees it). Documented
  on `2026-04-24` (Time Clock incident) and codified in the
  `_defaultFurnitureLayout` docstring on `CommercialBuilding`.
- The current workaround is the `_defaultFurnitureLayout`
  `List<DefaultFurnitureSlot>` Inspector field on `CommercialBuilding`:
  the level designer manually authors each slot (`FurnitureItemSO` +
  `LocalPosition` + `LocalEulerAngles` + `TargetRoom`). At runtime, the
  server's `OnNetworkSpawn` calls `TrySpawnDefaultFurniture` to
  Instantiate + `NetworkObject.Spawn()` each entry as a top-level NO,
  then re-parents under the building.
- For buildings with many furniture pieces (e.g. the Forge with crafting
  stations + shelves + tool storage + benches), this manual list is
  **tedious** and **lossy**: the prefab itself shows nothing — the
  designer has to delete/re-add furniture in Play Mode just to
  visualise positions, and re-typing positions back into the slot list
  is error-prone.

## Goal

Let the level designer author furniture **as visible children of the
building prefab**, exactly as they would in any non-network Unity
project, and have the runtime automatically convert those nested
network-bearing children into `_defaultFurnitureLayout` entries
**before** NGO sees them — preserving the existing top-level-spawn
behavior of `TrySpawnDefaultFurniture` while removing the authoring
friction.

After this change, the `_defaultFurnitureLayout` Inspector list remains
supported (legacy / opt-in for furniture authored outside the prefab),
but the recommended pattern for new prefabs is "drop the furniture
visually inside the prefab and forget about the list".

## Scope

### In

- Hoist the existing layout system from `CommercialBuilding` to
  `Building`:
  - `DefaultFurnitureSlot` nested type
  - `_defaultFurnitureLayout` `[SerializeField]` field +
    `_defaultFurnitureSpawned` flag
  - `TrySpawnDefaultFurniture()` and `SpawnDefaultFurnitureSlot()`
    methods
  - The `OnNetworkSpawn` call site that triggers them (currently in
    `CommercialBuilding.OnNetworkSpawn`)
- New method `Building.ConvertNestedNetworkFurnitureToLayout()`,
  invoked from `Building.Awake()` after the existing `_subRooms`
  auto-populate. Runs on every peer (server + clients).
- New `protected virtual void Building.OnDefaultFurnitureSpawned()`
  hook that `TrySpawnDefaultFurniture` calls at the end. Removes the
  existing rule #11 / #14 violation in
  `CommercialBuilding.TrySpawnDefaultFurniture` (`if (this is
  CraftingBuilding crafting) crafting.InvalidateCraftableCache();`):
  - `CommercialBuilding.OnDefaultFurnitureSpawned()` overrides to call
    `InvalidateStorageFurnitureCache()`.
  - `CraftingBuilding.OnDefaultFurnitureSpawned()` overrides further
    (`base.OnDefaultFurnitureSpawned()` + `InvalidateCraftableCache()`).
- Doc updates per project rules #28 / #29 / #29b:
  - `.agent/skills/<building/furniture skill folder>/SKILL.md` —
    document the new authoring pattern.
  - `wiki/systems/<matching page>` — bump `updated:` + changelog +
    refresh API surface section.

### Out

- No changes to `TrySpawnDefaultFurniture`'s spawn / parenting / grid
  registration logic itself. It already handles per-slot dedup by
  `FurnitureItemSO`, save-restore precedence, and `TargetRoom == null`
  fallback.
- No save schema changes. `FurnitureKey =
  "{ItemId}@{x:F2},{y:F2},{z:F2}"` keeps using the same `LocalPosition`
  values; this change just **derives them from the prefab hierarchy
  instead of the Inspector list**.
- No retroactive sweep of existing prefabs. Prefabs that already use
  the manual `_defaultFurnitureLayout` (Forge, Lumberyard, Shop, etc.)
  keep working unchanged. Migrating any of them to the new
  visual-children pattern is a separate, opt-in follow-up.
- No new `CharacterAction`. No NetworkVariable / NetworkList changes.
  No save-format changes.

## Architecture

### File map

| Path | Change |
| ---- | ------ |
| `Assets/Scripts/World/Buildings/Building.cs` | edit — receive ~140 hoisted lines (DefaultFurnitureSlot, `_defaultFurnitureLayout`, `_defaultFurnitureSpawned`, `TrySpawnDefaultFurniture`, `SpawnDefaultFurnitureSlot`); add `ConvertNestedNetworkFurnitureToLayout` (~50 lines); add `OnDefaultFurnitureSpawned` virtual hook; call conversion from `Awake()` and call `TrySpawnDefaultFurniture` from `OnNetworkSpawn` (server). |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | edit — remove the hoisted ~140 lines; add `OnDefaultFurnitureSpawned` override that calls `InvalidateStorageFurnitureCache()`; drop the `TrySpawnDefaultFurniture` call from its own `OnNetworkSpawn` since the base class now owns it. |
| `Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs` | edit — add `OnDefaultFurnitureSpawned` override that chains `base.OnDefaultFurnitureSpawned()` + `InvalidateCraftableCache()`. |
| `.agent/skills/building_system/SKILL.md` | edit — describe the new "author Furniture as nested children of the prefab" pattern; reference the in-Awake conversion. |
| `wiki/systems/building.md` | edit (or create the section if absent) — document the layout system at the `Building` level since it's no longer commercial-specific; bump `updated:` to 2026-05-01; append change-log entry. |
| `wiki/systems/commercial-building.md` | edit — bump `updated:`, append change-log entry pointing to `building.md` for the layout system, keep `OnDefaultFurnitureSpawned` override note. |

### Lifecycle

```
Server-side runtime placement of a building prefab
  via BuildingPlacementManager:
  1. Instantiate(buildingPrefab)
        ├─ Building.Awake()
        │    ├─ base.Awake() (ComplexRoom / Room / Zone init)
        │    ├─ _subRooms auto-populate (existing)
        │    └─ ConvertNestedNetworkFurnitureToLayout()   <-- NEW
        │         walks GetComponentsInChildren<Furniture>(true);
        │         for each child with a NetworkObject:
        │           - capture pose + TargetRoom
        │           - append to _defaultFurnitureLayout
        │           - Destroy(child.gameObject)
        └─ Building.OnNetworkSpawn (IsServer)
             └─ TrySpawnDefaultFurniture()                <-- now on Building
                  for each slot:
                    if no existing child with same ItemSO:
                      SpawnDefaultFurnitureSlot(slot)
                  OnDefaultFurnitureSpawned()             <-- virtual hook
                    CommercialBuilding override: InvalidateStorageFurnitureCache()
                    CraftingBuilding override: + InvalidateCraftableCache()

Client-side replication:
  NGO scene-sync re-instantiates the building prefab on the client
  -> Building.Awake() runs there too
  -> ConvertNestedNetworkFurnitureToLayout() destroys the same nested
     children locally
  -> client never sees the broken half-spawned NetworkObjects
  -> the server-spawned top-level furniture NetworkObjects replicate
     normally and AutoObjectParentSync re-parents them under the
     building, exactly as today.
```

### `ConvertNestedNetworkFurnitureToLayout` semantics

For each `Furniture` returned by
`GetComponentsInChildren<Furniture>(includeInactive: true)`:

0. **Edit-mode guard:** the entire method bails out early if
   `!Application.isPlaying`. Defensive against any future
   `[ExecuteAlways]` attribute or editor utility accidentally
   invoking it from edit mode.
1. **Skip** if the component is on the building root itself (defensive;
   shouldn't happen but cheap).
2. **Skip** if the furniture has **no `NetworkObject` component** — this
   is the "baked plain-MonoBehaviour furniture" case the existing
   docstring relies on (e.g. a non-networked TimeClock once its NO was
   stripped). NGO is happy with these as nested children, and
   `TrySpawnDefaultFurniture` already de-dups against them via the
   `existingItemSOs` HashSet. Leave them in place.
3. **Validate** `furniture.FurnitureItemSO != null`. If null, log a
   warning ("nested furniture with NetworkObject but no
   FurnitureItemSO — destroyed without conversion") and `Destroy` the
   GameObject anyway (it's already broken from NGO's perspective if it
   has a nested NO).
4. Build a new `DefaultFurnitureSlot`:
   - `ItemSO = furniture.FurnitureItemSO`
   - `LocalPosition = transform.InverseTransformPoint(furniture.transform.position)`
   - `LocalEulerAngles = (Quaternion.Inverse(transform.rotation) * furniture.transform.rotation).eulerAngles`
   - `TargetRoom`: walk up `furniture.transform.parent` chain, take the
     **first** `Room` component found (could be a nested sub-room, the
     main `Room_Main`, or the building itself if the furniture was
     parented directly under the root — in that last case the slot's
     `TargetRoom` stays null, matching today's "warn + parent under
     root without grid registration" fallback).
5. **Dedup against existing serialized slots**: if
   `_defaultFurnitureLayout` already contains an entry with the same
   `ItemSO` reference, **the converted child wins** — replace the
   manual entry with the prefab-derived slot. Log this so the user
   notices they have redundant authoring (the manual entry was probably
   leftover). All mutations to `_defaultFurnitureLayout` happen
   **in-memory only** on the live instance — never written back to the
   prefab asset (the `Application.isPlaying` guard in step 0 prevents
   any edit-mode invocation, so Unity's serialization system never
   sees the runtime mutation).
6. `Destroy(furniture.gameObject)`.
7. Single summary `Debug.Log` per building when conversion happened
   ("converted N nested NetworkObject furniture children to
   `_defaultFurnitureLayout`"). One log per building lifetime, runs in
   `Awake()` not in a hot path — exempt from rule #34's "gate every
   `Debug.Log` in a hot path" requirement. (If a generic
   `BuildingDebug.VerboseLifecycle` toggle exists at implementation
   time, gate behind it; otherwise unconditional is acceptable here.)

### Idempotence

Re-entry into `Awake` (rare; e.g. a future domain reload during a
session) finds zero remaining Furniture children with NetworkObjects
(they were destroyed first time) and is a no-op. The
`_defaultFurnitureSpawned` flag still guards `TrySpawnDefaultFurniture`
from double-spawn.

### Save / restore interaction

- Persisted Furniture children (the save-restore path repopulates them
  before `OnNetworkSpawn`) are **not** affected: they have no nested
  NetworkObject during serialization (they were spawned as top-level
  NOs and re-parented), so the conversion in Awake finds nothing to
  destroy. `TrySpawnDefaultFurniture` then sees the restored Furniture
  via `existingItemSOs` and skips re-spawning, exactly as today.
- The save schema risk previously documented in
  `feedback_layout_position_save_schema.md` is **not new** — repositioning
  a slot's `LocalPosition` between save and load silently drops storage
  contents because `FurnitureKey` no longer matches. This change just
  shifts the editing surface from "the Inspector list" to "the prefab's
  child transforms": moving a Furniture child in the prefab now has the
  same effect as moving a slot's `LocalPosition` did before. Same
  failure mode, same severity, new place to be careful. The summary log
  added in step 7 above gives at least a paper trail when conversion
  happens.

## Edge cases

| Case | Behavior |
| ---- | -------- |
| Prefab has 0 nested Furniture children | Conversion is a no-op. `TrySpawnDefaultFurniture` runs against whatever's in the manual `_defaultFurnitureLayout` (legacy path), unchanged. |
| Prefab has nested Furniture, none with `NetworkObject` | All preserved as-is. `TrySpawnDefaultFurniture` de-dups against them via `existingItemSOs`. |
| Prefab has nested Furniture with `NetworkObject` but no `FurnitureItemSO` (broken authoring) | Log warning, destroy the child, do not append to layout. Prefer "fail loudly" over silently shipping broken nested NOs. |
| Prefab has both a manual `_defaultFurnitureLayout` entry and a nested child for the same `ItemSO` | Converted child replaces the manual slot in-memory. Logged. |
| Furniture child parented directly under building root, no Room ancestor | `TargetRoom = null`. Existing fallback path warns and parents under root without grid registration. |
| Building is **scene-authored** (not runtime-spawned) and has nested Furniture children with NetworkObjects | Conversion still runs. NGO would have supported the nested NOs natively for scene objects, so we lose the "scene-object lifecycle" efficiency. In exchange, behavior becomes uniform across scene-authored and runtime-placed buildings: the same `_defaultFurnitureLayout` re-spawn path on both. Net cost: one extra `Spawn()` per furniture at scene load on the server. Negligible at the scales we ship. |
| Furniture child has a `Furniture` component but no derived behaviour set up in `FurnitureItemSO.InstalledFurniturePrefab` | `TrySpawnDefaultFurniture` already logs a warning and skips that slot. No new behavior. |

## Risks

### Unity serialization migration of `_defaultFurnitureLayout`

Hoisting `_defaultFurnitureLayout` from `CommercialBuilding` to
`Building` changes the declaring class of a `[SerializeField]` private
field. Unity *usually* preserves serialized data when a field is
promoted up the hierarchy with the same name and type, but it's not
guaranteed across all Unity versions / serialization-mode combinations.

**Mitigation:**
- After implementation, manually open every prefab that currently has
  authored `_defaultFurnitureLayout` entries (Forge, Lumberyard, Shop,
  ClothingShop, TransporterBuilding, CommercialBuilding_prefab) and
  verify the slots still display the same `ItemSO` / `LocalPosition` /
  `LocalEulerAngles` / `TargetRoom` values they had before.
- Also check any **scene-authored** building instances dragged
  directly into a scene (rather than placed via
  `BuildingPlacementManager`) — their serialized `_defaultFurnitureLayout`
  data lives on the scene file, not the prefab asset, and is subject
  to the same hierarchy-promotion serialization concern.
- If anything dropped, add `[FormerlySerializedAs("_defaultFurnitureLayout")]`
  to the new declaration. (No-op if Unity preserved the data.)

### Save schema (already-known)

Already covered in "Save / restore interaction" above. Same risk as
today, just relocated. Worth re-mentioning in the SKILL.md and wiki
page so the new authoring pattern carries the warning.

### Editor-only "destroy in Awake" footgun

`Destroy()` in `Awake()` is legal at runtime and in Play Mode, but
calling it accidentally in *edit mode* (e.g. via a `[ExecuteAlways]`
attribute or an editor utility) would mutate the prefab. The new method
must run only at runtime.

**Mitigation:** the conversion method bails out early if
`!Application.isPlaying`. Defensive but cheap.

## Validation plan

After implementation, verify these scenarios manually:

1. **Existing manual-layout building (e.g. Forge with current
   `_defaultFurnitureLayout` entries, no nested NO Furniture
   children):** Place via `BuildingPlacementManager`. Verify all
   furniture spawns at the correct positions, the building works as
   today (workers craft, storage works, transporter pickups work).
   Verify a remote client joins and sees the same furniture.
2. **New visual-authored building (test prefab with nested Furniture
   children that have NetworkObjects):** Place via
   `BuildingPlacementManager`. Verify children are destroyed in Awake
   (no warnings from NGO about nested NOs in console), then re-spawn
   in the same world position via `TrySpawnDefaultFurniture`. Verify
   remote-client join works.
3. **Save / load round-trip:** Place a building with stocked
   `StorageFurniture` (chest with items). Save. Reload. Verify chest
   contents are restored. (Sanity check that the `FurnitureKey` path
   still works because we use the same `LocalPosition` values.)
4. **Conversion idempotence:** Trigger a domain reload during a session
   (Edit > Project Settings > Editor > Enter Play Mode Settings, or
   force a script recompile with the game running). Verify the
   building's furniture is intact and no duplicates appear.
5. **Mixed authoring (one prefab with both nested children and manual
   slots for different ItemSOs):** Both should spawn.
6. **Mixed authoring with overlap (nested child + manual slot for the
   same ItemSO):** One spawn, with the nested child winning. Log
   message visible.
7. **Building with nested Furniture but no NetworkObject (e.g. legacy
   plain-MonoBehaviour TimeClock):** Stays as a child untouched after
   Awake. Building works.

## Source rule references

- Rule #11 (Liskov substitution) and #14 (depend on abstractions, not
  concrete classes) — `OnDefaultFurnitureSpawned` virtual hook removes
  the `if (this is CraftingBuilding crafting)` cast.
- Rule #18 / #19 (network architecture) — conversion runs on every
  peer's `Awake()` so each peer destroys its own copy of the broken
  nested NOs locally; `TrySpawnDefaultFurniture` stays server-only.
- Rule #28 (SKILL.md updated) and #29b (wiki page updated) — included
  in scope.
- Rule #31 (defensive coding) — `try/catch` already exists around
  `SpawnDefaultFurnitureSlot` in `TrySpawnDefaultFurniture`; the new
  conversion method adds null/validation checks per child.
- Rule #34 — conversion runs once per building in `Awake`, not in a hot
  path. No allocations beyond the `_defaultFurnitureLayout.Add` (which
  is a one-time event per building lifetime).
- Memory: `feedback_no_nested_networkobject_in_runtime_spawned_prefab`
  (the original NGO half-spawn problem this design works around) and
  `feedback_layout_position_save_schema` (save schema gotcha now
  applicable to prefab child positions too).
