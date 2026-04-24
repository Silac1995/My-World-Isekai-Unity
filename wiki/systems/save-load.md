---
type: system
title: "Save / Load"
tags: [save-load, persistence, network, tier-2]
created: 2026-04-19
updated: 2026-04-24
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
│     │     ├── Alive Characters (as CharacterSaveData)           │
│     │     ├── HibernatedNPCData[]                               │
│     │     ├── HibernatedItemData[]                              │
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

## Change log
- 2026-04-24 — Added a Known gotchas section documenting the half-spawned-NetworkObject bug in save-restore paths (root cause + defensive purge). Cross-linked to [[network]]. — claude
- 2026-04-19 — Stub with architectural sketch. Full code walkthrough deferred — tier-2 per Kevin's plan. — Claude / [[kevin]]

## Sources
- [.agent/skills/save-load-system/SKILL.md](../../.agent/skills/save-load-system/SKILL.md)
- [.agent/skills/save-load-netcode/SKILL.md](../../.agent/skills/save-load-netcode/SKILL.md)
- [.claude/agents/save-persistence-specialist.md](../../.claude/agents/save-persistence-specialist.md)
- Root [CLAUDE.md](../../CLAUDE.md) rule #20.
- `Assets/Scripts/Core/SaveLoad/` (5 files).
