---
type: system
title: "Building & Furniture"
tags: [building, furniture, world, tier-1]
created: 2026-04-18
updated: 2026-04-24
sources: []
related:
  - "[[world]]"
  - "[[items]]"
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
  - "[[ai]]"
  - "[[save-load]]"
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
| `Assets/Scripts/World/Buildings/Building.cs` | Building name/type, `BuildingState`, `_buildingZone` collider, `_deliveryZone`, contribute/instant-build methods. |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Adds `BuildingTaskManager` + `BuildingLogisticsManager` (consumed by [[jobs-and-logistics]] and [[shops]]). |

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
- `Building.CurrentState` (`BuildingState` enum).
- `Building.ContributeMaterial(ItemInstance, Character)` — gradual construction.
- `Building.BuildInstantly()` — debug/cheat/admin path.
- `Building.AttemptInstallFurniture(furniturePrefab, gridPosition)` — placement entry.
- `Building.BuildingId` — per-instance GUID.

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
- All persist via the world save (`MapSaveData`) and survive hibernation.

## Known gotchas / eg cases

- **Interior positions at y=5000** — cross-NavMesh transitions **must** use `CharacterMovement.ForceWarp`. `Warp` will fail.
- **Lazy interior spawn** — interior maps are not allocated until the first character attempts entry. Don't assume the interior exists before entry.
- **Every commercial building needs a `BuildingTaskManager`** — workers use blackboard tasks instead of heavy `Physics.OverlapBox` queries. Missing the manager = workers idle.
- **`BuildingId` is per-instance** — don't hardcode a shop prefab's ID; each spawn generates a new GUID.
- **Permission gate on placement** — community-level permissions run through `BuildingPlacementManager`. Non-owners can't place in non-public rooms.
- **Zone triggers fire on any collider** — characters enter/exit zones via `OnTriggerEnter/Exit`. Non-character triggers (like dropped items) must not pollute the `_charactersInside` set.

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
- 2026-04-24 — Known save-restore gotcha documented: `MapController.SpawnSavedBuildings` spawns the building's `NetworkObject` **before** reparenting it under the MapController transform, which can produce a half-spawned NO that later NRE's NGO's `NetworkObject.Serialize` during client-sync and silently breaks every join against a loaded save. Defensive purge in `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` masks the symptom; real fix is parent-before-spawn. Full write-up in [[network]] and [[save-load]]. — claude
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md)
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- `Assets/Scripts/World/Buildings/` (29 files).
- `Assets/Scripts/World/Furniture/` (5 files).
- Root [CLAUDE.md](../../CLAUDE.md) — World Scale Reference (11 Unity units = 1.67m).
- 2026-04-18 conversation with [[kevin]].
