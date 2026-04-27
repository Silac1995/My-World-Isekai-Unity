---
type: system
title: "Save / Load"
tags: [save-load, persistence, network, tier-2]
created: 2026-04-19
updated: 2026-04-27
sources: []
related:
  - "[[character]]"
  - "[[world]]"
  - "[[network]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: save-persistence-specialist
secondary_agents:
  - character-system-specialist
  - world-system-specialist
owner_code_path: "Assets/Scripts/Core/SaveLoad/"
depends_on:
  - "[[character]]"
  - "[[world]]"
  - "[[network]]"
depended_on_by:
  - "[[character]]"
  - "[[world]]"
  - "[[party]]"
  - "[[items]]"
  - "[[jobs-and-logistics]]"
---

# Save / Load

## Summary
Two parallel persistence pipelines. The **character profile** is a portable local JSON that carries a character's full logical state (stats, needs, equipment, abilities, relations, blueprints) — loadable into Solo or Multiplayer sessions. The **world save** is per-session state (maps, hibernated NPCs, world items, community registries, map slots, time). Each character subsystem implements `ICharacterSaveData<T>` with a priority; `CharacterDataCoordinator` walks them in order on export/import. World systems implement `ISaveable` and round-trip through `SaveFileHandler` atomic I/O. Save triggers: bed checkpoint, portal gate return, manual save.

## Purpose
Decouple characters from any one session so a player's character can visit friends' worlds, take their stats home, and keep working alone offline. Let the living world hibernate in place, with [[world-macro-simulation]] filling the gap on wake-up.

## Architecture (sketch)

```
┌─────────── Portable character profile (local JSON) ────────────┐
│                                                                 │
│   CharacterProfileSaveData                                      │
│     ├── StatsSaveData      (priority: core)                     │
│     ├── NeedsSaveData                                           │
│     ├── SkillsSaveData                                          │
│     ├── EquipmentSaveData                                       │
│     ├── AbilitiesSaveData                                       │
│     ├── RelationSaveData                                        │
│     ├── BlueprintsSaveData                                      │
│     └── ... etc. (each subsystem contributes one)               │
│                                                                 │
│   Written by: CharacterDataCoordinator.Export(character)        │
│   Loaded by:  CharacterDataCoordinator.Import(character, data)  │
└─────────────────────────────────────────────────────────────────┘

┌─────────── World save (per-session) ───────────────────────────┐
│                                                                 │
│   WorldSaveData                                                 │
│     ├── MapSaveData[]        (per map — active + hibernated)    │
│     │     ├── HibernatedNPCData[]                               │
│     │     │     ├── flat fields (id, prefab, pos, needs, …)     │
│     │     │     └── ProfileData = CharacterProfileSaveData      │
│     │     │           (full coordinator blob, since 2026-04-26) │
│     │     ├── WorldItemSaveData[]   (dropped items)             │
│     │     ├── Resource pools                                    │
│     │     └── Last hibernation time                             │
│     ├── CommunityRegistry                                       │
│     ├── PartyRegistry                                           │
│     ├── WorldOffsetAllocator slots                              │
│     └── TimeManager snapshot                                    │
│                                                                 │
│   Written by: SaveManager on checkpoint / quit                  │
│   Atomic I/O via SaveFileHandler                                │
└─────────────────────────────────────────────────────────────────┘
```

## Responsibilities
- Exporting/importing character profiles via `ICharacterSaveData<T>` + `CharacterDataCoordinator` priority order.
- Persisting world state via `ISaveable` on each system + `WorldSaveManager` coordination.
- Atomic file I/O via `SaveFileHandler` (write-to-temp + rename to avoid corruption on crash).
- Triggering saves: bed checkpoint, portal gate return, manual, auto-save tick.
- Abandoned NPC flagging: characters whose owner left mid-session get marked for later reclaim.
- Supporting multiplayer profile transport: carrying a character into another host's session; stripping and returning on exit.

**Non-responsibilities**:
- Does **not** run macro-sim catch-up — that's [[world-macro-simulation]].
- Does **not** own NPC logic — pure data movement.

## Key classes / files (inferred paths — confirm during expansion)

- `Assets/Scripts/Core/SaveLoad/SaveManager.cs` — top-level coordinator.
- `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs` — atomic I/O.
- `Assets/Scripts/Character/CharacterProfile/CharacterDataCoordinator.cs` — priority-ordered subsystem walker.
- `Assets/Scripts/Character/CharacterProfile/CharacterProfileSaveData.cs` — portable character container.
- `Assets/Scripts/World/MapSystem/MapSaveData.cs`, `HibernatedNPCData.cs`, `HibernatedItemData.cs` — world side.
- `ICharacterSaveData<T>`, `ISaveable` interfaces.

## Known gotchas / edge cases

- **Save-restore can leave half-spawned `NetworkObject`s** that kill client-join. Symptom: host throws `NullReferenceException` at `NetworkObject.Serialize` during `NetworkSceneManager.SynchronizeNetworkObjects` when a client tries to join — only with loaded worlds, fresh worlds are fine. Root cause sits in restore code paths that `Spawn()` a NetworkObject and then reparent it (e.g. `MapController.SpawnSavedBuildings` calls `bNet.Spawn()` **before** `bObj.transform.SetParent(this.transform)`). The internal `NetworkManagerOwner` field on the spawned NO can end up null — invisible via the public `NetworkObject.NetworkManager` property (which falls back to the singleton). A defensive purge in `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` (run from `ApprovalCheck`) invokes `Serialize` as a probe and removes any NRE-inducing entries before NGO's sync loop sees them — this lets joins succeed but doesn't remove the broken state, it just suppresses the symptom. See [[network]] for the full write-up and the canonical fix (parent before spawn).

## Open questions / TODO

- [ ] **Data-flow diagram needs concrete code verification** — the above sketch is based on SKILL docs + CLAUDE.md rule #20. Walk Assets/Scripts/Core/SaveLoad/ when expanding.
- [ ] How does the abandoned-NPC-reclaim flow work exactly? Mentioned in the save-persistence-specialist agent.
- [ ] Save version migration — is there a strategy for breaking schema changes? Worth adding now — adding TimeClock as a child of existing building prefabs AFTER saves were created silently poisoned those saves (buildings replay through `SpawnSavedBuildings` with a different authored child set).
- [ ] Exact priority ordering of `ICharacterSaveData<T>` providers — does priority dictate load order too, or just export?
- [ ] **Audit every `NetworkObject.Spawn()` + `SetParent` pair** in save-restore code — enforce the NGO-preferred parent-before-spawn order.
- [ ] **Verify `CharacterSkills` per-skill XP persistence end-to-end** once the skill system is feature-complete. Code path looks correct (`CharacterSkills.Serialize` captures `level + currentXP + totalXP`; the 4-arg `SkillInstance` ctor in `Deserialize` re-assigns all three). Deferred 2026-04-26 because the skill system itself is still in progress — Kevin will retest once it ships. If on retest values still don't restore, suspect post-Deserialize override (network sync from prefab default, OnNetworkSpawn re-init, or `RecalculateAllSkillBonuses` triggering an XP reset). See [CharacterSkills.cs:404-476](../../Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs).

## Change log
- 2026-04-27 — Fixed: in-hand carried item now persists. `HandsController` (a body-part visual `MonoBehaviour`, not a `CharacterSystem`) owns the gameplay-relevant `CarriedItem` (food/log/key/stone — distinct from the equipped weapon). It had no save contract, so reload silently lost the held item. Added `HandsSaveData` DTO and made `HandsController` implement `ICharacterSaveData<HandsSaveData>` at `LoadPriority = 35` (after `CharacterEquipment`'s 30 so the weapon slot is restored first; the restore bypasses `AreHandsFree()`). Reinforces that coordinator discovery is interface-based — non-`CharacterSystem` MonoBehaviours can and sometimes must persist. Bag inventory (`EquipmentSaveData.bagInventoryItems`) verified unchanged. Networking gap (carry visual not replicated to observers) flagged in [[character-equipment]] Open questions. — claude
- 2026-04-27 — Fixed: hibernated maps' NPCs + `WorldItem`s now survive save/load. `MapController._hibernationData` was in-memory only; `SaveManager` previously iterated `MapController.ActiveControllers` only — so any map a player had just left (e.g. exterior when they entered a building's interior) silently dropped its NPCs. Added `MapController.AllControllers` accessor; `SaveWorldAsync` / `SaveWorldDirectAsync` now snapshot every map, sourcing from `HibernationData` if hibernating, else `SnapshotActiveNPCs`. Buildings + storage furniture were already surviving via `MapRegistry.CommunityData` (an `ISaveable`) — only NPCs/items needed the new pass. — claude
- 2026-04-27 — Fixed: scene-authored buildings now derive a deterministic `NetworkBuildingId` from `MD5(scene name + world position)` at `OnNetworkSpawn`. Previously every reload generated a fresh `Guid.NewGuid()`, orphaning the `BuildingInteriorRegistry` record (records were saved keyed by the old GUID, the live building had a new GUID — door re-entry spawned a fresh interior instead of the saved one). Runtime-placed buildings keep `Guid.NewGuid()` (they round-trip via `BuildingSaveData`); discriminator is the `PlacedByCharacterId` field, which `BuildingPlacementManager` now sets *before* `Spawn()` so it's observable inside `Building.OnNetworkSpawn`. — claude
- 2026-04-27 — Fixed: door lock + health state now round-trip through save/load. `BuildingInteriorRegistry.InteriorRecord` had `IsLocked` and `DoorCurrentHealth` fields and a read path (`BuildingInteriorSpawner.SpawnInterior`) but no write path. Added: `DoorLock.SetLockedStateWithSync` + `DoorHealth.OnCurrentHealthChanged` persist into the record; new `OnNetworkSpawn` lookups prefer the persisted record over field defaults; `BuildingInteriorRegistry.RestoreState` calls new `ApplyLockState`/`ApplyHealthState` helpers to retroactively fix exterior doors that spawn before restore runs; `RegisterInterior` snapshots live door state via `GetCurrentLockState`/`GetCurrentHealth` so unlock-before-first-entry isn't reverted. — claude
- 2026-04-26 — Fixed: `CharacterCombatLevel` (character-progression XP, level history, unspent stat points) now persists. The system was a `CharacterSystem` with no `ICharacterSaveData<T>` contract, so the coordinator never saw it — both player and NPC progression reset to defaults on load. Added `CombatLevelSaveData` DTO, made `CharacterCombatLevel` implement `ICharacterSaveData<CombatLevelSaveData>` at priority 15 (between Stats and Skills). Restore is direct field assignment with no `LevelUp`/`SpendStatPoint` calls, so stat bonuses are not double-applied (CharacterStats already persists cumulative base values). Verified by Kevin same-day. **Deferred:** per-skill XP (`CharacterSkills`) round-trip cannot be verified yet — skill system still in progress. Open question logged above. — claude
- 2026-04-26 — Fixed: NPC stats/equipment (and every coordinator-driven subsystem) now survive save/load. Added `HibernatedNPCData.ProfileData` (`CharacterProfileSaveData`); `MapController.SnapshotActiveNPCs` and `Hibernate` populate it via `CharacterDataCoordinator.ExportProfile`, and `SpawnNPCsFromSnapshot` replays it via `ImportProfile` after `Spawn(true)`. Mirrors the party-NPC restore pattern in `GameLauncher.SpawnPartyMembers`. Backward-compatible (legacy saves fall back to flat fields). Pre-existing follow-up flagged: `SnapshotActiveNPCs` does not skip party NPCs of a player leader → potential double-spawn risk. — claude
- 2026-04-25 — Implemented WorldItem persistence. `MapSaveData.WorldItems` (`WorldItemSaveData` list) rides alongside `HibernatedNPCs` on each `MapSnapshot_{mapId}`. `MapController.SnapshotActiveNPCs` / `SpawnNPCsFromSnapshot` / `Hibernate` now also handle items; `WorldItem.SpawnWorldItem` reparents the GO under the containing map via new `MapController.GetAnyMapAtPosition`. — claude
- 2026-04-24 — Added a Known gotchas section documenting the half-spawned-NetworkObject bug in save-restore paths (root cause + defensive purge). Cross-linked to [[network]]. — claude
- 2026-04-19 — Stub with architectural sketch. Full code walkthrough deferred — tier-2 per Kevin's plan. — Claude / [[kevin]]

## Sources
- [.agent/skills/save-load-system/SKILL.md](../../.agent/skills/save-load-system/SKILL.md)
- [.agent/skills/save-load-netcode/SKILL.md](../../.agent/skills/save-load-netcode/SKILL.md)
- [.claude/agents/save-persistence-specialist.md](../../.claude/agents/save-persistence-specialist.md)
- Root [CLAUDE.md](../../CLAUDE.md) rule #20.
- `Assets/Scripts/Core/SaveLoad/` (5 files).
