---
type: system
title: "Character Profile"
tags: [character, personality, save-load, tier-2, stub]
created: 2026-04-19
updated: 2026-05-09
sources: []
related: ["[[character]]", "[[character-relation]]", "[[save-load]]", "[[host-player-uuid-timing-on-load]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents: ["save-persistence-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterProfile/"
depends_on: ["[[character]]", "[[character-traits]]"]
depended_on_by: ["[[character-relation]]", "[[save-load]]"]
---

# Character Profile

## Summary
Personality + portable profile data. Exposes `GetCompatibilityWith(Character other)` — the compatibility enum consumed by [[character-relation]] to filter opinion deltas (compatible ×1.5, incompatible ×0.5, inverted signs on conflicts). Also coordinates with `CharacterProfileSaveData` as the portable character file (local JSON) used by [[save-load]].

## Responsibilities
- Holding personality data (traits roll-up, disposition values).
- Computing compatibility vs another character.
- Integrating with `CharacterProfileSaveData` for portable save/load.

## Key classes / files
- `Assets/Scripts/Character/CharacterProfile/CharacterProfile.cs` (inferred).
- Works with `CharacterProfileSaveData` (see [[save-load]]).
- `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` — `ImportProfile(CharacterProfileSaveData)` is the single re-hydration entry point. Walks every `ICharacterSaveData<T>` provider in priority order. Server-only writes to `_character.NetworkCharacterId.Value`, gated by a previous-vs-new compare so unchanged GUIDs do not trigger spurious downstream events.

## Identity timing (host vs NPC)
The persistent `characterGuid` field on `CharacterProfileSaveData` is what makes a character "the same character" across save/load. Two arrival paths exist for it on the runtime `Character.NetworkCharacterId.Value`:

- **NPCs** — Pre-set BEFORE NGO spawn. `MapController.SpawnNPCsFromSnapshot` and `GameLauncher.SpawnPartyMembers` write `npcCharacter.NetworkCharacterId.Value = memberProfile.characterGuid` BEFORE calling `Spawn(true)`. `Character.OnNetworkSpawn`'s `if (NetworkCharacterId.Value.IsEmpty)` fresh-Guid fallback no-ops. `Character.OnCharacterSpawned` fires with the correct GUID.
- **Host's player Character** — Set AFTER NGO spawn. `Character.OnNetworkSpawn` fires first and assigns `NetworkCharacterId.Value = Guid.NewGuid()` (the host has no pre-spawn channel for the profile). `OnCharacterSpawned` fires with that fresh GUID. Only later, in `GameLauncher` Step 6, does `LoadAndImportProfile → CharacterDataCoordinator.ImportProfile` overwrite it with the saved profile GUID.

This asymmetry breaks any server-side resolver that calls `Character.FindByUUID(savedId)` between Steps 5 and 6 — the host appears un-found, and the pending-list subscription on `OnCharacterSpawned` never re-fires after the GUID changes. Mitigation: `CharacterDataCoordinator.ImportProfile` raises a separate `Character.OnCharacterIdReassigned` event when the GUID actually changes, and saved-data resolvers (`Building.RestoreOwnersFromSaveData`, `CommercialBuilding.RestoreEmployeesFromSaveData`) subscribe to BOTH events. See the gotcha at [[host-player-uuid-timing-on-load]] for the full trace.

## Open questions
- [ ] Exact compatibility scoring formula — SKILL describes outcomes, not math.
- [ ] What personality axes exist? (Openness, extroversion, etc., or custom enum?)
- [ ] No SKILL.md — tracked in [[TODO-skills]].

## Change log
- 2026-05-09 — Documented host-vs-NPC identity timing; added the new `Character.OnCharacterIdReassigned` event raised from `ImportProfile`. The previous-vs-new GUID compare in `ImportProfile` ensures NPCs (whose pre-spawn-set GUID matches the imported profile GUID) do not spuriously fire the event. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md) §2.
- [Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs](../../Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs)
- [Assets/Scripts/Core/GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs) — `LaunchSequence` Steps 4 → 5b → 5c → 6 ordering.
- [[character]] and [[character-relation]] parents.
- [[host-player-uuid-timing-on-load]] — gotcha that captures the timing window in detail.
