---
type: system
title: "World & Community"
tags: [world, map, community, macro-simulation, tier-1]
created: 2026-04-18
updated: 2026-04-27
sources: []
related:
  - "[[character]]"
  - "[[ai]]"
  - "[[jobs-and-logistics]]"
  - "[[building]]"
  - "[[save-load]]"
  - "[[network]]"
  - "[[terrain-and-weather]]"
  - "[[world-time-skip]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: world-system-specialist
secondary_agents:
  - save-persistence-specialist
  - network-specialist
owner_code_path: "Assets/Scripts/World/"
depends_on:
  - "[[save-load]]"
  - "[[network]]"
  - "[[ai]]"
depended_on_by:
  - "[[character]]"
  - "[[jobs-and-logistics]]"
  - "[[building]]"
  - "[[terrain-and-weather]]"
  - "[[dialogue]]"
---

# World & Community

## Summary
The "Living World" architecture. The game runs **many** maps (open-world regions, building interiors, dungeons, dynamic cities) inside a single Unity scene, separated by large spatial offsets so Unity NGO interest management filters traffic naturally. Each map runs in one of two modes. **Active** (at least one player present) — full real-time simulation with NavMesh, physics, NetworkObjects. **Hibernating** (zero players) — all NPCs, items, and mutable state are serialized into `HibernatedNPCData` / `MapSaveData`; the Unity GameObjects are despawned and destroyed. When a player re-enters, the `MacroSimulator` runs a catch-up math pass (resource pool regen → offline yields → needs decay → position snap), then re-spawns fresh prefabs with updated state.

## Purpose
Scale the game to thousands of NPCs across hundreds of maps on a single player's machine without the cost of keeping them all simulated. Let the world evolve organically while the player is elsewhere — crops grow, shops restock, communities expand — and be ready to welcome the player back with a coherent state at any moment.

## Responsibilities
- Allocating spatial slots per map (`WorldOffsetAllocator`) with persistent slot IDs.
- Tracking active players per map (`MapController`) and flipping hibernation state when `PlayerCount` hits zero.
- Serializing all NPC and item state on sleep (`HibernatedNPCData`, `HibernatedItemData`, `MapSaveData`).
- Running offline catch-up math on wake (`MacroSimulator`) — in strict order: resource regen → biome yields → needs decay → position snap.
- Despawning and re-spawning physical prefabs cleanly on state transitions.
- Orchestrating map-to-map transitions via `MapTransitionDoor` / `BuildingInteriorDoor` + `CharacterMapTracker` + `ScreenFadeManager`.
- Growing dynamic cities from NPC clustering via `CommunityTracker`: Roaming Camp → Settlement → Established City → Abandoned City → Reclaimed.
- Owning `Community` (groups of characters with hierarchy), `CommunityLevel`, `CommunityManager`, and biome-driven resource pools.
- Hosting `BiomeRegion` / `BiomeDefinition` — the data layer for what grows where and what jobs yield what.
- Keeping Character authority on `CharacterMapTracker.CurrentMapID` — the only source of truth for "which map am I on".

**Non-responsibilities**:
- Does **not** tick NPCs directly — see [[ai]].
- Does **not** own NPC save data schemas — see [[save-load]] (`ICharacterSaveData<T>` + `CharacterDataCoordinator`).
- Does **not** own building placement — see [[building]].

## Key classes / files

### Map & hibernation
| File | Role |
|------|------|
| `Assets/Scripts/World/MapSystem/MapController.cs` | Tracks active players on one map; flips hibernation; notified by transitions. |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | Offline catch-up math pass on wake-up. |
| `Assets/Scripts/World/MapSystem/MapSaveData.cs` | Serialized map state (NPCs, items, resources, last hibernation time). |
| `Assets/Scripts/World/WorldOffsetAllocator.cs` | Slot allocator; lazy recycle FreeList (30-day cooldown). |
| `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs`, `BuildingInteriorDoor.cs` | Door prefabs; initiate fade + `CharacterMapTransitionAction`. |
| `CharacterMapTracker` | Per-character; owns `CurrentMapID` `NetworkVariable`; fires `RequestTransitionServerRpc`. |
| `Assets/Scripts/UI/ScreenFadeManager.cs` | Real-time fade (unscaled) during transitions. |

### Community
| File | Role |
|------|------|
| `Assets/Scripts/World/Community/Community.cs` | Group of characters with hierarchy (parent + sub-communities); territory zones. |
| `Assets/Scripts/World/Community/CommunityLevel.cs` | `SmallGroup` / `Settlement` / `City` / etc. state machine tiers. |
| `Assets/Scripts/World/Community/CommunityManager.cs` | Global registry + lookup. |
| `Assets/Scripts/World/Community/CommunityTracker.cs` (conceptual, likely in same area) | Server heartbeat evaluating community promotion/demotion; triggers map birth. |
| `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` | **Character-side adapter.** `CheckAndCreateCommunity()`, founding gate (trait, 4 friends, not already a leader). |

### Biome / resources / zones
| File | Role |
|------|------|
| `Assets/Scripts/World/BiomeRegion.cs` | A biome-typed subregion within a map. |
| `Assets/Scripts/World/BiomeDefinition.cs` (ScriptableObject) | Per-biome resources, yields, climate profile. |
| `Assets/Scripts/World/Zones/` | Named zones within a map (work zones, social zones, restricted areas). |
| `Assets/Scripts/World/Data/` | Persistent world-data ScriptableObjects. |
| `Assets/Scripts/World/Jobs/JobYieldRegistry.cs` (conceptual) | Biome-driven job yield recipes consumed by the macro simulator. |

### Debug & visualization
| File | Role |
|------|------|
| `MapControllerDebugUI` | Canvas-based HUD for `ActivePlayers`, hibernation state, NPC count, last sim time. |

## Public API / entry points

Map lifecycle:
- `MapController.NotifyPlayerEntered(Character)` / `NotifyPlayerExited(Character)`.
- `MapController.IsHibernating` / `LastHibernationTime`.
- `MacroSimulator.CatchUp(MapSaveData data, float deltaDays)` — runs on wake.

Transitions:
- `CharacterMapTracker.RequestTransitionServerRpc(targetMapId)` — `FixedString128Bytes`.
- `CharacterMovement.Warp(position)` — same NavMesh.
- `CharacterMovement.ForceWarp(position)` — cross-NavMesh; disables agent, teleports via `transform.position`, re-enables after 2 frames.

Community:
- `characterCommunity.CheckAndCreateCommunity()` — gated by trait + friend count + not-already-leader.
- `Community.AddMember()` / `RemoveMember()`.
- `CommunityManager.GetCommunity(id)` / `GetCommunitiesOfCharacter(character)`.

## Data flow

Hibernation:
```
MapController.Update()
    │
    ├── _activePlayers.Count == 0? ──► HibernateMap()
    │     │
    │     ├── Serialize every NPC → HibernatedNPCData (stats, schedule target, inventory, needs, position)
    │     ├── Serialize every WorldItem → HibernatedItemData
    │     ├── Serialize resource pools
    │     ├── Write MapSaveData via SaveManager
    │     └── Despawn all NetworkObjects on the map
    │
    └── Map is now dead until first player re-enters
```

Wake-up & catch-up:
```
First player enters map
    │
    ▼
MapController.WakeUp()
    │
    ▼
MacroSimulator.CatchUp(delta)
    │
    1. Resource Pool Regeneration   (per CommunityData.ResourcePoolEntries)
    2. Inventory Yields via JobYieldRegistry + BiomeDefinition
    3. Needs Decay                  (Hunger, Social: linear over deltaTime)
    4. Offline City Growth          (Leader's CharacterBlueprints.UnlockedBuildingIds drives expansion)
    5. Schedule Snap                (skip NPCs to end of current scheduled task, position accordingly)
    │
    ▼
Re-instantiate NPC prefabs at updated positions with updated state + NetworkSpawn
```

Dynamic city birth:
```
CommunityTracker heartbeat (server)
    │
    ├── NPC cluster exceeds threshold for N days
    │
    ▼
Promote state: Roaming Camp → Settlement
    │
    ├── WorldOffsetAllocator.AllocateSlot()   (for save-data separation)
    └── Spawn MapController anchor at cluster centroid (NPCs roam freely, no warp)
```

## Dependencies

### Upstream
- [[save-load]] — `MapSaveData`, `HibernatedNPCData`, `HibernatedItemData`, world save manager.
- [[network]] — interest management makes spatial offset work; `NetworkBehaviour` for MapController + doors.
- [[ai]] — macro simulator reads schedules and needs; BT/GOAP pause during hibernation.

### Downstream
- [[character]] — `CharacterMapTracker` lives on the character; hibernation snapshots the character's logical state.
- [[jobs-and-logistics]] — biome-driven `JobYieldRecipe`s consumed during catch-up; shops restock based on `BuildingLogisticsManager`.
- [[building]] — city growth spawns building scaffolds; `NetworkBuildingId` GUIDs link each instance to its interior slot.
- [[terrain-and-weather]] — biome definition drives weather, vegetation, and footstep surfaces (code on feature branch).

## State & persistence

Per-map:
- `MapSaveData` — active NPCs (as live prefabs on disk reference), hibernated NPC snapshots, items, resource pool entries, last hibernation time.
- Slot ID persistence — `WorldOffsetAllocator` guarantees `CommunityData.SlotId` stays stable across sessions.

Global:
- `WorldSaveManager` coordinates map + community + offset state.
- Predefined maps register via `SaveManager.RegisterPredefinedMaps()` on boot; dynamic maps register via `CommunityTracker` promotions.

Single source of truth:
- `TimeManager.CurrentDay` + `CurrentTime01` — used by all catch-up math. Never `Time.time` / `Time.deltaTime` for offline deltas.

## Known gotchas / edge cases

- **`FindObjectOfType` is unsafe** — maps hibernate, so their GameObjects disappear. Always query serialized data or `MapController` registries.
- **Cross-map physics don't exist** — projectiles, shipments, AI paths **never** cross maps. Inter-map effects are pure math in `JobLogisticsManager`.
- **`ForceWarp` vs `Warp`** — cross-NavMesh transitions **must** use `ForceWarp` (disable + teleport + re-enable). `Warp` breaks.
- **Abandoned cities keep their slot forever** — they hibernate at 0 CPU cost but never release the spatial offset. The `WorldOffsetAllocator` FreeList excludes them.
- **Unique building IDs** — every `Building` generates a `NetworkBuildingId` GUID on spawn so multiple instances of the same shop prefab can link to distinct interior maps.
- **Character map authority** — always read `CharacterMapTracker.CurrentMapID.Value`. Never infer from position alone.
- **Character-community vs world-community** — `CharacterCommunity.cs` is the per-character **adapter** (founding gate, leadership flags). The actual system (`Community`, `CommunityLevel`, `CommunityManager`) lives under `World/Community/`. Both share membership data via character IDs.

## Open questions / TODO

- [ ] `CommunityTracker.cs` — location inferred from SKILL docs; confirm exact path and responsibility split vs `CommunityManager`.
- [ ] No SKILL.md for `world-map`, `world-zones`, `world-data`. Tracked in [[TODO-skills]].
- [ ] `BiomeRegion.cs` / `BiomeClimateProfile.cs` / `WeatherFront.cs` lie at the border of World and [[terrain-and-weather]] — see that stub for the split.

## Child sub-pages (to be written in Batch 2)

- [[world-map-hibernation]] — MapController, MapSaveData, hibernation lifecycle.
- [[world-macro-simulation]] — MacroSimulator, catch-up order, formulas.
- [[world-community]] — Community hierarchy, `CommunityTracker`, promotion state machine.
- [[world-biome-region]] — BiomeRegion, BiomeDefinition, resource pools.
- [[world-offset-allocation]] — WorldOffsetAllocator, slot FreeList.
- [[world-map-transitions]] — doors, `CharacterMapTracker`, warp rules.

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/world-system/SKILL.md](../../.agent/skills/world-system/SKILL.md)
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md)
- [.claude/agents/world-system-specialist.md](../../.claude/agents/world-system-specialist.md)
- `Assets/Scripts/World/MapSystem/` source tree.
- `Assets/Scripts/World/Community/` source tree.
- Root [CLAUDE.md](../../CLAUDE.md) — World System & Simulation section.
- 2026-04-18 conversation with [[kevin]].
