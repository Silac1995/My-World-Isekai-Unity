---
type: system
title: "Character Locations"
tags: [character, locations, tier-2, stub]
created: 2026-04-19
updated: 2026-05-09
sources: []
related: ["[[character]]", "[[world]]", "[[building]]", "[[character-job]]", "[[host-player-uuid-timing-on-load]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/"
depends_on: ["[[character]]"]
depended_on_by: ["[[ai]]", "[[building]]"]
---

# Character Locations

## Summary
Per-character registry of named world locations this character cares about — home, workplace, social spot, favorite tavern. Consumed by [[ai]] when planning "go home to sleep", "travel to work", etc. Also holds the runtime `OwnedBuildings` mirror — the character-side reflection of the [[building]] ownership state.

## Responsibilities
- Holding a dictionary of named locations.
- Resolving `GetLocation(name)` to a world position.
- Mirroring building ownership: `RegisterOwnedBuilding(Building)` / `UnregisterOwnedBuilding(Building)` keep `OwnedBuildings` in sync with the corresponding `Room._ownerIds` NetworkList on each owned building.

## OwnedBuildings — derived, not persisted
`OwnedBuildings` is a **server-only runtime cache** on `CharacterLocations`. It is **NOT** serialized into `CharacterProfileSaveData` — instead, it is rebuilt on load as a side-effect of the building-side owner restore:

1. `MapController.SpawnSavedBuildings` / `WakeUp` runs `Building.RestoreOwnersFromSaveData(bSave.OwnerCharacterIds)` on every restored Building.
2. The base resolver's `BindRestoredOwner` hook (default impl + Residential/Commercial overrides) calls `owner.CharacterLocations.RegisterOwnedBuilding(this)` after pushing the UUID into `Room._ownerIds`.
3. The character-side mirror is therefore re-populated automatically when the building-side restoration succeeds.

Ownership is server-authoritative on `Room._ownerIds` (replicated `NetworkList<FixedString64Bytes>`); `OwnedBuildings` is a convenience cache that exists because some queries — permission gates, "go home" location resolution, schedule injection — want a character→buildings lookup without iterating every Building in the registry. Remote clients see an empty `OwnedBuildings` for their own character (the list is not networked). For client-side ownership queries use `building.IsOwner(character)` which reads the replicated `_ownerIds`.

## Key classes / files
- [CharacterLocations.cs](../../Assets/Scripts/Character/CharacterLocations.cs).

## Known gotchas / edge cases
- **Host's `OwnedBuildings` was empty after save/load on every reload** until 2026-05-09. Root cause was upstream — `Building.RestoreOwnersFromSaveData` couldn't find the host because the host's `CharacterId` hadn't been imported yet when the resolver ran. The mirror could only register buildings the building-side resolver had successfully bound, so when the building-side dropped the host, `OwnedBuildings` stayed empty too. Fixed by the dual-event subscription (`OnCharacterSpawned` + `OnCharacterIdReassigned`). See [[host-player-uuid-timing-on-load]].
- `OwnedBuildings` is not networked; non-host clients see empty for their own character. For ownership queries on clients, prefer `building.IsOwner(character)`.

## Open questions
- [ ] Full location-key enumeration — probably driven by the character's life circumstances.
- [ ] No SKILL.md — tracked in [[TODO-skills]].
- [ ] Should `OwnedBuildings` be replaced by a derived `BuildingManager.GetAllBuildings().Where(b => b.IsOwner(_character))` getter? The derived form would be consistent across host and clients without needing the runtime mirror at all.

## Change log
- 2026-05-09 — Documented `OwnedBuildings` as a server-only derived mirror of `Room._ownerIds`, NOT persisted. Resolves the prior open question "Saved or rebuilt? Confirm." — it is rebuilt as a side-effect of `Building.RestoreOwnersFromSaveData → BindRestoredOwner → RegisterOwnedBuilding`. Linked to [[host-player-uuid-timing-on-load]] which captures the timing trap that broke the host's mirror until same-day fix. Confidence bumped from `low` to `medium`. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [CharacterLocations.cs](../../Assets/Scripts/Character/CharacterLocations.cs).
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — "Ownership Sync Invariant" section.
- [[character]] parent.
- [[host-player-uuid-timing-on-load]].
