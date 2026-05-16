---
type: system
title: "Building & Furniture"
tags: [building, furniture, world, tier-1]
created: 2026-04-18
updated: 2026-05-16
sources: []
related:
  - "[[world]]"
  - "[[items]]"
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
  - "[[ai]]"
  - "[[save-load]]"
  - "[[construction]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - world-system-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on:
  - "[[world]]"
  - "[[items]]"
  - "[[network]]"
  - "[[save-load]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
  - "[[ai]]"
  - "[[construction]]"
---

# Building & Furniture

## Summary
Nested spatial hierarchy: `Zone` (foundation with a `BoxCollider` + presence tracking) → `Room` (adds owners/residents + `FurnitureGrid`) → `ComplexRoom` (multi-room container) → `Building` (full structure with state machine, delivery zone, interior link). `CommercialBuilding` layers on top with a `BuildingTaskManager` (blackboard for GOAP workers) and a `BuildingLogisticsManager` (order queuing). Furniture is placed on a discrete grid via `FurnitureManager`/`FurnitureGrid` with occupancy state and community-gated placement permissions.

## Purpose
Give the world a single containment model that every gameplay system can hang off: "where am I?" (Zone), "whose place is this?" (Room), "what can I buy here?" (CommercialBuilding). Encode construction as a state machine so buildings can be scaffolded, contributed to, and become fully operational through gameplay (material contribution, community leader promotion).

## Responsibilities
- Tracking characters inside each zone via `OnTriggerEnter`/`Exit`.
- Enforcing ownership/residency per room.
- Managing furniture grids — discrete placement with collision check, occupancy, save round-trip.
- Running the `BuildingState` lifecycle (`Scaffold` → `UnderConstruction` → `Complete` → `Damaged` → `Demolished`).
- Material contribution and instant-build paths (`ContributeMaterial`, `BuildInstantly`).
- Linking exterior building footprints to interior maps (high in the sky at `y=5000`), with lazy spawn.
- Issuing `NetworkBuildingId` GUIDs so multiple shop-prefab instances stay distinct.
- Providing commercial infrastructure: `BuildingTaskManager` (blackboard) + `BuildingLogisticsManager` (order queues) for `CommercialBuilding` subclasses.
- Community-gated placement permissions via `BuildingPlacementManager`.

**Non-responsibilities**:
- Does **not** own economics or order fulfillment — see [[jobs-and-logistics]].
- Does **not** own shop customer queues or sale transactions — see [[shops]].
- Does **not** own world-level map hibernation or offset allocation — see [[world]].
- Does **not** own item data — see [[items]].

## Key classes / files

### Hierarchy
| File | Role |
|------|------|
| `Assets/Scripts/World/Buildings/Zone.cs` | Foundation — `BoxCollider` + `_charactersInside` tracking. |
| `Assets/Scripts/World/Buildings/Room.cs` | Adds `_roomOwners`, `_roomResidents`, `FurnitureManager`, `FurnitureGrid`. |
| `Assets/Scripts/World/Buildings/ComplexRoom.cs` | Multi-room container; `GetRoomAt(Vector3)`, `FindAvailableFurniture()`. |
| `Assets/Scripts/World/Buildings/Building.cs` | Building name/type, `BuildingState`, `_buildingZone` collider, `_deliveryZone`, contribute/instant-build methods. Each prefab carries a `[SerializeField] BuildingSO _blueprint` — the single source of truth for `PrefabId` / `BuildingName` / `BuildingType` / `ConstructionRequirements` / `DefaultFurnitureLayout`. |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Adds `BuildingTaskManager` + `BuildingLogisticsManager` (consumed by [[jobs-and-logistics]] and [[shops]]). |
| `Assets/Scripts/World/Data/BuildingSO.cs` | Base ScriptableObject blueprint (`namespace MWI.WorldSystem`). One asset per building type under `Assets/Resources/Data/Buildings/`. Holds `PrefabId`, `BuildingName`, `Icon`, `BuildingPrefab`, `InteriorPrefab`, `CommunityPriority`, `BuildingType`, `ConstructionRequirements`, `DefaultFurnitureLayout`. Replaces the legacy inline `BuildingRegistryEntry` + the 5 duplicated SerializeFields previously on every Building prefab. |
| `Assets/Scripts/World/Data/BuildingCommercialSO.cs` | `BuildingSO` subclass adding `int BaseTreasury` (currency seed credited once at construction-complete via [[commercial-building#Treasury seed flow]]). |

### Furniture
| File | Role |
|------|------|
| `Assets/Scripts/World/Furniture/FurnitureManager.cs` | Per-room registry of placed furniture. |
| `Assets/Scripts/World/Furniture/FurnitureGrid.cs` | Discrete grid; occupancy check; save/load. |
| `Assets/Scripts/World/Furniture/FurnitureItem.cs` (or similar) | Runtime furniture instance. |
| `Assets/Scripts/World/Furniture/BuildingPlacementManager.cs` | Community permission gate for placement requests. |

### Interiors
| File | Role |
|------|------|
| `BuildingInteriorDoor.cs` (in `MapSystem/`) | Links exterior footprint to interior map at y=5000. |
| `BuildingInteriorRegistry.cs` (conceptual) | Lazy-spawn of interior maps on first entry. |

## Public API / entry points

Zone:
- `Zone.GetRandomPointInZone()` — NavMesh-valid random point.
- `Zone._charactersInside` — HashSet of currently-inside characters.

Room:
- `Room.IsOwnedBy(Character)`, `Room.IsResident(Character)`.
- `Room.FurnitureManager` / `Room.Grid`.

Building:
- `Building._blueprint` — `[SerializeField] BuildingSO` per prefab. **Single source of truth** for `PrefabId` (saved string key — preserved verbatim across the 2026-05-16 migration so zero save migration was required), `BuildingName`, `BuildingType`, `ConstructionRequirements`, `DefaultFurnitureLayout`. The legacy duplicated SerializeFields (`_prefabId`, `buildingName`, `_buildingType`, `_constructionRequirements`, `_defaultFurnitureLayout`) have been removed; all accessors read through `_blueprint`. `BuildingCommercialSO` subclasses also expose `BaseTreasury` (see [[commercial-building#Treasury seed flow]]). `WorldSettingsData.Blueprints : List<BuildingSO>` is the live registry; the legacy `[Obsolete] BuildingRegistry` lives alongside for one cycle.
- `Building.CurrentState` (`BuildingState` enum).
- `Building.ContributeMaterial(ItemSO, int)` — server-side ledger increment for construction (called from `CharacterAction_FinishConstruction.OnTick`).
- `Building.BuildInstantly()` — debug/cheat/admin path.
- `Building.AttemptInstallFurniture(furniturePrefab, gridPosition)` — placement entry.
- `Building.BuildingId` — per-instance GUID.

**Construction loop API (added 2026-05-06 — see [[construction]]):**
- `Building.ConstructionProgress` — `NetworkVariable<float>` (Read=Everyone, Write=Server). UI meter; updates only when delta > 0.001f.
- `Building.DeliveredMaterials` — `NetworkList<DeliveredMaterialEntry>`. Per-requirement-index delivered counts.
- `Building.IsUnderConstruction` — `_currentState.Value == UnderConstruction`.
- `Building.BuildingZone` — public accessor for the footprint collider (also the construction drop zone).
- `Building.ComputeProgress()` — server-only progress recompute from `ContributedMaterials` against requirements.
- `Building.Finalize()` — server-only state-flip-first finalization (state → `Complete`, visual swap, default-furniture spawn, leftover eviction). **Note:** shadows `object.Finalize` (the GC finalizer hook); declared `public new void Finalize()`. The GC slot is unaffected — Building has no `~Building()` destructor.
- `Building.EvictLeftoversToPerimeter()` — server-only, repositions leftover `WorldItem`s outside `_buildingZone` after completion.
- `Building.GetPhysicalItemsInCollider(Collider, List<WorldItem>)` — refactored sibling of `GetPhysicalItemsInZone`. Caller-supplied buffer for Rule #34 (zero per-tick alloc).
- `_constructionVisualRoot` / `_completedVisualRoot` — `[SerializeField] GameObject` toggled by `HandleStateChanged`.

Commercial:
- `CommercialBuilding.TaskManager.ClaimBestTask<T>()` — GOAP workers pull tasks.
- `CommercialBuilding.LogisticsManager` — see [[jobs-and-logistics]].
- `CommercialBuilding.AskForJob(Character)` — employment entry.

Furniture:
- `FurnitureGrid.CanPlace(prefab, cell)` / `Place(...)` / `Remove(...)`.

## Data flow

Construction:
```
Admin / community action drops a scaffold
       │
       ▼
Building state = Scaffold
       │
       ▼
Character.ContributeMaterial(item) — loops until recipe met
       │
       ├── UnderConstruction
       │
       ▼
Recipe satisfied ──► Complete
       │
       ├── Opens interior via BuildingInteriorDoor (lazy-spawn map if needed)
       └── Registers in MapController + CommunityManager
```

Furniture placement:
```
Player/NPC selects furniture to place
       │
       ▼
BuildingPlacementManager.CheckPermission(character, room, furniture)
       │
       ▼
FurnitureGrid.CanPlace(furniturePrefab, cell)?
       │
       ▼
FurnitureManager.Place(instance) ──► serialize cell occupancy
```

## Dependencies

### Upstream
- [[world]] — buildings live on maps; interior maps link via `BuildingInteriorDoor`; `NetworkBuildingId` keeps instances distinct.
- [[items]] — material contributions are `ItemInstance`s; furniture prefabs reference item data.
- [[network]] — buildings are `NetworkBehaviour`s; server-authoritative state.
- [[save-load]] — `Building`, `FurnitureGrid`, room owners/residents all serialize to map save data.

### Downstream
- [[jobs-and-logistics]] — `BuildingTaskManager` + `BuildingLogisticsManager` power the job/logistics layer.
- [[shops]] — `ShopBuilding : CommercialBuilding`.
- [[ai]] — workers pull tasks via `TaskManager.ClaimBestTask<T>()`; residents and owners affect schedule/GOAP.

## State & persistence

- Per-building: name, type, `BuildingId`, `CurrentState`, contributed materials, owners/residents, interior map ID.
- Per-room: owners, residents, `FurnitureGrid` occupancy snapshot.
- Per-furniture: grid cell, rotation, state (e.g. door locked?), `ItemInstance` reference if applicable.
- `BuildingSaveData.PlacedFurnitures` (2026-05-13) — runtime-placed furniture roster (anything not in `_defaultFurnitureLayout`). Without this list, every player/NPC-placed chest / crafting station / etc. silently disappeared on save→load because `Building.TrySpawnDefaultFurniture` only re-instantiates default-layout slots. Restored before `StorageFurnitures` slot-content restore by `MapController.RestorePlacedFurnitureForBuilding`, so per-storage key lookup finds the fresh instances. See building_system SKILL `Placed-furniture roster` section for the full save/restore contract.
- **Save-restore ordering invariant (2026-05-13):** `MapController.ApplyDynamicSaveDataToBuilding` MUST call `Building.RestoreFromSaveData` BEFORE the per-furniture restore steps (`RestorePlacedFurnitureForBuilding`, `RestoreStorageFurnitureContents`, `RestoreCashierContents`, ShopBuilding hooks). The state-flip inside `RestoreFromSaveData` triggers the manual post-Complete cascade (`TrySpawnDefaultFurniture` + NavMesh carve) when `_isStarted == false` — without that ordering, the cascade fires AFTER the content-restore steps have already walked a furniture-less building and silently no-op'd. See [[save-restore-state-flip-no-subscriber]].
- All persist via the world save (`MapSaveData`) and survive hibernation.

## Known gotchas / eg cases

- **Interior positions at y=5000** — cross-NavMesh transitions **must** use `CharacterMovement.ForceWarp`. `Warp` will fail.
- **Lazy interior spawn** — interior maps are not allocated until the first character attempts entry. Don't assume the interior exists before entry.
- **Every commercial building needs a `BuildingTaskManager`** — workers use blackboard tasks instead of heavy `Physics.OverlapBox` queries. Missing the manager = workers idle.
- **`BuildingId` is per-instance** — don't hardcode a shop prefab's ID; each spawn generates a new GUID.
- **Permission gate on placement** — community-level permissions run through `BuildingPlacementManager`. Non-owners can't place in non-public rooms.
- **Zone triggers fire on any collider** — characters enter/exit zones via `OnTriggerEnter/Exit`. Non-character triggers (like dropped items) must not pollute the `_charactersInside` set.

## Default furniture layout

`_defaultFurnitureLayout : List<DefaultFurnitureSlot>` is a `[SerializeField]` on
`Building` that drives server-only spawning of authored furniture on first
`OnNetworkSpawn`. The system was hoisted from `[[commercial-building]]` to `Building`
on 2026-05-01 so any subclass benefits.

**Two authoring modes:**

- **Visual (recommended):** drop the Furniture prefab as a nested child of the
  building prefab, in the room hierarchy you want. `Building.Awake()` calls
  `ConvertNestedNetworkFurnitureToLayout()` on every peer — capturing each
  network-bearing child's pose + nearest `Room` ancestor into a slot, then
  destroying the child so NGO never half-spawns the nested NetworkObject.
- **Manual (legacy / opt-in):** populate the Inspector list directly. If both
  a manual slot and a nested child target the same `ItemSO`, the nested child
  wins.

**Subclass extension:** `OnDefaultFurnitureSpawned()` is a `protected virtual`
hook fired at the tail of `TrySpawnDefaultFurniture` when the layout had entries
to process. `CommercialBuilding` overrides to invalidate the storage furniture
cache; `CraftingBuilding` chains base + `InvalidateCraftableCache()`.

**Save schema gotcha:** slot `LocalPosition` feeds `FurnitureKey` for
`StorageFurniture` save/restore. Repositioning a slot (or a nested Furniture
child) between save and load silently drops storage contents — treat the layout
poses as part of the on-disk schema once a build ships with stocked storages.
See also [[building#Known gotchas / eg cases]].

See [building_system SKILL.md](../../.agent/skills/building_system/SKILL.md)
for procedural authoring details.

## Open questions / TODO

- [ ] Exact list of `BuildingTask` subclasses — tracked in [[jobs-and-logistics]].
- [ ] `BuildingPlacementManager` location not directly verified — path inferred from SKILL + agent.
- [ ] No SKILL.md covering `FurnitureGrid` specifically — it's inside `building_system` SKILL but could warrant its own.

## Child sub-pages (to be written in Batch 2)

- [[building-hierarchy]] — Zone / Room / ComplexRoom / Building classes.
- [[building-state]] — Scaffold → UnderConstruction → Complete → Damaged → Demolished state machine.
- [[furniture-grid]] — discrete placement, occupancy, save format.
- [[commercial-building]] — `BuildingTaskManager`, `BuildingLogisticsManager`, employment.
- [[building-interior]] — lazy-spawn interior maps, door linking, y=5000 offset.
- [[building-placement-manager]] — community permission gate.

## Change log
- 2026-05-16 — BuildingSO blueprint introduced. Replaces inline BuildingRegistryEntry + duplicated prefab fields. PrefabId strings preserved verbatim (zero save migration). — claude
- 2026-05-13 (later) — Fixed the second half of the save-load storage bug: **prefab-authored default furniture** (e.g. Lumberyard's crate at `_defaultFurnitureLayout[0]`) was also vanishing on load, not just runtime-placed. Same subscription-timing race that bit `BuildInstantly` (see [[buildinstantly-pre-start-lifecycle-race]]): `MapController.SpawnSavedBuildings` → `Spawn()` → `OnNetworkSpawn` auto-derives `_currentState.Value = UnderConstruction` for any prefab with non-empty `_constructionRequirements` and `_spawnAsComplete=false`. Then `ApplyDynamicSaveDataToBuilding → RestoreFromSaveData` writes `_currentState.Value = Complete`, **but Start() (where the `OnValueChanged += HandleStateChanged` subscription lives) hasn't run yet** — so the post-Complete cascade (`TrySpawnDefaultFurniture`, `ConfigureNavMeshObstacles`, `OnConstructionComplete`) silently no-ops. Default furniture never spawns; storage CONTENTS restore then finds zero live storages and silently skips them too. Fix: `Building.RestoreFromSaveData` now manually invokes `TrySpawnDefaultFurniture()` + `ConfigureNavMeshObstacles()` after the state-flip when `_isStarted == false`; idempotent via `_defaultFurnitureSpawned`. `MapController.ApplyDynamicSaveDataToBuilding` reordered so `RestoreFromSaveData` runs FIRST — owners/employees → state-flip-with-cascade → placed-furniture → storage contents → cashier → shop hooks. Save-restore can't use the coroutine-defer pattern BuildInstantly uses because the synchronous content-restore steps need furniture live in the same call. See [[save-restore-state-flip-no-subscriber]] in gotchas. — claude
- 2026-05-13 — Player/NPC-placed furniture now persists across save/load. Added `BuildingSaveData.PlacedFurnitures : List<PlacedFurnitureSaveEntry>` capturing every Furniture under the building that isn't matched by `_defaultFurnitureLayout` (dedup via new `Building.IsDefaultLayoutFurniture(furniture)` helper — same ItemSO + LocalPosition within 0.05u). Restore wired in `MapController.RestorePlacedFurnitureForBuilding`, called from `ApplyDynamicSaveDataToBuilding` BEFORE `RestoreStorageFurnitureContents` so storage contents bind to the freshly-spawned instances. Server-only, gated to `Complete` state, mirrors `Building.SpawnDefaultFurnitureSlot` (Instantiate → `NetworkObject.Spawn()` → parent under building root → `FurnitureManager.RegisterSpawnedFurnitureUnchecked` on MainRoom). Both refresh paths (`SnapshotActiveBuildings`, `Hibernate`) copy the new field — same hazard that bit `ConstructionProgress` / `DeliveredMaterials` on 2026-05-07. Pre-fix symptom: storages placed in HarvestingBuilding / CraftingBuilding / FarmingBuilding silently vanished on every load. ShopBuilding appeared to work only because SellShelves are nested-NO prefab children absorbed into `_defaultFurnitureLayout` at Awake by `ConvertNestedNetworkFurnitureToLayout` — Shop's runtime player placements had the same bug. — claude
- 2026-05-09 (later) — Closed the host-player UUID timing window. `Building.RestoreOwnersFromSaveData` and `CommercialBuilding.RestoreEmployeesFromSaveData` now subscribe to **both** `Character.OnCharacterSpawned` (NPC path — UUID correct at spawn) and a new `Character.OnCharacterIdReassigned` (host player path — UUID arrives via `ImportProfile` AFTER spawn). Previously the host's player Character spawned with a fresh `Guid.NewGuid()` in GameLauncher Step 4, the saved-owner resolver fired in Step 5b with the wrong GUID, and the persistent profile GUID only landed in Step 6 — by which point nothing was listening. Net result: host-owned buildings now restore for the host on load, matching the NPC behaviour. Player jobs already worked because `CharacterJob.Deserialize` runs character-side from `ImportProfile` and binds via BuildingId. — claude
- 2026-05-09 — Owner save/load made symmetric across every Building subclass. Owner restoration was previously only wired for `CommercialBuilding` — `ResidentialBuilding` and base `Building` saved their `OwnerCharacterIds` but silently dropped them on load. The pending+`OnCharacterSpawned` resolver moved to base `Building` (`RestoreOwnersFromSaveData` + virtual `BindRestoredOwner` hook); `CommercialBuilding` keeps the employee-specific half (`RestoreEmployeesFromSaveData`). Same fix removed the `SnapshotActiveBuildings` filter that skipped scene-authored ("preplaced") buildings, and replaced the bare "skip if already in scene" branch in `SpawnSavedBuildings` with an overlay branch that applies the saved dynamic state to the existing instance via the new `ApplyDynamicSaveDataToBuilding` helper. Result: assigning an owner to either a runtime-placed Tavern, a runtime-placed House, or a scene-authored Inn now persists across save/load. See [[save-load]] for the full restoration ordering. — claude
- 2026-05-06 — added construction loop (visual swap via `_constructionVisualRoot` / `_completedVisualRoot`, server-only `ConstructionSiteScanner` 2 Hz observational scan, `ConstructionProgress` + `DeliveredMaterials` NetworkVariables, `Building.Finalize()` state-flip-first method, `EvictLeftoversToPerimeter()`, `GetPhysicalItemsInCollider` refactor, `BuildingSaveData` extension with `ConstructionProgress` + `DeliveredMaterials` round-trip). Owner-only finalize via `BuildingInteractable` + `CharacterAction_FinishConstruction`. Default furniture spawn now deferred until `Complete`. See new [[construction]] page for the full architecture. — claude
- 2026-05-01 — Hoisted `_defaultFurnitureLayout` system from `[[commercial-building]]` to `Building` (every subclass now benefits). Added `ConvertNestedNetworkFurnitureToLayout()` Awake-time stripper so designers can author Furniture as nested prefab children. Replaced `is CraftingBuilding` cast with `OnDefaultFurnitureSpawned()` virtual hook. New `## Default furniture layout` section added. — claude
- 2026-04-24 — Known save-restore gotcha documented: `MapController.SpawnSavedBuildings` spawns the building's `NetworkObject` **before** reparenting it under the MapController transform, which can produce a half-spawned NO that later NRE's NGO's `NetworkObject.Serialize` during client-sync and silently breaks every join against a loaded save. Defensive purge in `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` masks the symptom; real fix is parent-before-spawn. Full write-up in [[network]] and [[save-load]]. — claude
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md)
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- `Assets/Scripts/World/Buildings/` (29 files + `Construction/` subfolder for the loop).
- `Assets/Scripts/World/Furniture/` (5 files).
- [docs/superpowers/specs/2026-05-06-building-construction-loop-design.md](../../docs/superpowers/specs/2026-05-06-building-construction-loop-design.md) — Phase 1 construction-loop design spec.
- Root [CLAUDE.md](../../CLAUDE.md) — World Scale Reference (11 Unity units = 1.67m).
- 2026-04-18 conversation with [[kevin]].
