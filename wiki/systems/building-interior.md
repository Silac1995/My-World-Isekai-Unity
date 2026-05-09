---
type: system
title: "Building Interior"
tags: [building, interior, world, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-27
sources: []
related: ["[[building]]", "[[world]]", "[[character-movement]]", "[[character]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/World/MapSystem/"
depends_on: ["[[building]]", "[[world]]"]
depended_on_by: ["[[building]]", "[[character]]"]
---

# Building Interior

## Summary
Interior maps live high in the sky at `y=5000` (or deep underground). `BuildingInteriorDoor` links exterior footprint to interior map; `BuildingInteriorRegistry` lazy-spawns interiors on first entry. Every `Building` exposes a `NetworkBuildingId` that keys the registry — **scene-authored buildings derive a deterministic id from `scene name + world position` at spawn**, while runtime-placed buildings use `Guid.NewGuid()` and round-trip the value through `BuildingSaveData`. This keeps the same building bound to the same interior record across reloads.

## Cross-NavMesh rule
Entering an interior requires `CharacterMovement.ForceWarp` — disable agent, teleport, re-enable after 2 frames. `Warp` silently fails.

## Programmatic NPC entry / exit

NPCs can autonomously enter and leave any building's interior via two `CharacterAction` primitives:

- `[[character|CharacterEnterBuildingAction]](actor, Building)` — walks to and triggers the closest `BuildingInteriorDoor`.
- `[[character|CharacterLeaveInteriorAction]](actor)` — walks to and triggers the closest exit `MapTransitionDoor` on the current interior `MapController`.

Both delegate to the existing `door.Interact` → `CharacterMapTransitionAction` chain. The door retains full ownership of lock/key/rattle decisions.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-26 — documented programmatic NPC entry / exit via CharacterEnterBuildingAction & CharacterLeaveInteriorAction — claude
- 2026-04-27 — fix: scene-authored buildings now derive a deterministic `NetworkBuildingId` from `(scene name, world position)` at `OnNetworkSpawn` so the registry's interior record stays reachable across reloads. Runtime placement (`BuildingPlacementManager`) now sets `PlacedByCharacterId` *before* `Spawn()` so the discriminator is observable at spawn time. — claude
- 2026-04-27 — fix: `DoorLock` and `DoorHealth` now write `IsLocked` / `CurrentHealth` changes back into the matching `InteriorRecord` (write path was missing — only the read path existed). `OnNetworkSpawn` prefers the persisted record over `_startsLocked` / `_maxHealth`. `BuildingInteriorRegistry.RestoreState` calls new `DoorLock.ApplyLockState` + `DoorHealth.ApplyHealthState` helpers to retroactively fix exterior doors that spawned from the scene before restore ran. `RegisterInterior` snapshots live door state via `DoorLock.GetCurrentLockState` / `DoorHealth.GetCurrentHealth` so changes done before first entry aren't reverted to field defaults. — claude
- 2026-04-27 — hardening: exterior building doors were silently dropping out of the persistence path because their `_lockId` auto-derive could fail (Building.BuildingId not yet set, or door not parented to Building) which left them out of `DoorLock._registry`. Three layers of defense added: (1) `BuildingInteriorRegistry.RestoreState` ends with a scene-wide sweep (`ApplyDoorStateSceneWide`) that finds every spawned `DoorLock`/`DoorHealth` via `FindObjectsByType`, re-derives `lockId` from parent Building if empty, and applies the matching record state — defensive backup if the static-registry path missed any door; (2) `PersistLockState` / `PersistHealthState` retry `_lockId` derivation if it's empty at write time, so unlock/lock events can't lose state; (3) `GetCurrentLockState` / `GetCurrentHealth` fall back to a scene scan when the static registry has no entry for the lockId, so `RegisterInterior` always inherits the live exterior door's state. — claude
- 2026-04-27 — design rule: **interior `MapTransitionDoor`s no longer restore lock state from the record — they always spawn at the prefab `_startsLocked` default (unlocked).** The exterior `BuildingInteriorDoor` is the security-side door that round-trips its lock state; the interior side is treated as a one-way exit so a player who saved inside can never be trapped by a save-restored locked state. Implemented by gating both the read paths (`DoorLock.OnNetworkSpawn`, `DoorLock.ApplyLockState`, `BuildingInteriorRegistry.ApplyDoorStateSceneWide`) and the spawner's re-application loop on `GetComponentInParent<Building>() != null` — interior doors sit inside the spawned interior MapController which has no Building parent, so they fall through. Pair-sync still propagates lock changes between exterior and interior at runtime; the desync is only at spawn time. `DoorHealth` is unaffected (still restores). — claude
- 2026-04-27 — moved door state persistence out of `BuildingInteriorRegistry.InteriorRecord` into a dedicated `DoorStateRegistry : ISaveable` keyed by `lockId`. The previous coupling lost state for any door whose owning interior had not been visited yet (the record only existed after first entry). The new registry lazy-creates a record on the first lock/unlock or damage event, so unlock-and-save-without-entering now persists. The "interior always spawns unlocked" rule is reverted — both door types persist symmetrically. `DoorLock.OnNetworkSpawn` / `DoorHealth.OnNetworkSpawn` read from the new registry; `SetLockedStateWithSync` / `OnCurrentHealthChanged` write to it. `DoorStateRegistry.RestoreState` ends with a scene-wide sweep that catches any door not yet in its own static lookup. The legacy `IsLocked` / `DoorCurrentHealth` fields on `InteriorRecord` are kept for save-file backward compat but unused. The `DoorStateRegistry` GameObject lives in `GameScene` next to `BuildingInteriorRegistry`. — claude

## Sources
- [[world]] §4 + [[building]].
- [CharacterEnterBuildingAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs)
- [CharacterLeaveInteriorAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterLeaveInteriorAction.cs)
- [CharacterDoorTraversalAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterDoorTraversalAction.cs)
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md)
