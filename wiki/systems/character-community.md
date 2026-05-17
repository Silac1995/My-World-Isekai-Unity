---
type: system
title: "Character Community (adapter)"
tags: [character, community, world, tier-2, citizenship, multi-leader]
created: 2026-04-19
updated: 2026-05-17
sources: []
related:
  - "[[world]]"
  - "[[world-community]]"
  - "[[character-relation]]"
  - "[[character-traits]]"
  - "[[citizenship]]"
  - "[[singular-leader-vs-multi-leader-isleader]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterCommunity/"
depends_on: ["[[character]]", "[[world]]", "[[world-community]]"]
depended_on_by: ["[[world]]"]
---

# Character Community (adapter)

## Summary
**Character-side adapter** to [[world-community]]. Holds the founding gate
(`CheckAndCreateCommunity`), the current-membership reference
(`CurrentCommunity`), the citizenship reference (`Citizenship`), and leadership
flags (is this character a leader of their community?). The actual community
entities (`Community`, `CommunityLevel`, `CommunityManager`) live under
`Assets/Scripts/World/Community/` (see [[world-community]]).

## Purpose
Decouple the `Character` from direct `Community` class imports. Every
character-side community operation (found, join, leave, check leadership,
check citizenship, serialize to profile save) routes through this adapter.
This keeps `Character.cs` free of community logic and allows the world-side
`Community` to evolve independently.

## Responsibilities
- Founding gate: `CheckAndCreateCommunity` (sole guard post-migration: "not already a primary leader" — trait + 4-friends gates removed in Task 3/4).
- Current community membership reference (`CurrentCommunity : Community`).
- Citizenship reference (`Citizenship : Community`) — sticky civic status.
- Leadership query forwarding (`IsLeaderOfCurrentCommunity`).
- Serialization to `CommunitySaveData` (profile save pipeline).

## Key classes / files
- [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) — the adapter.
- [CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs) — the save struct (`communityMapId`, `citizenshipMapId`).
- Counterpart: `Assets/Scripts/World/Community/` — `Community.cs`, `CommunityLevel.cs`, `CommunityManager.cs` (see [[world-community]]).

## Public API / entry points

### Membership
```csharp
Community CurrentCommunity { get; }         // the community the character is currently a member of
void      CheckAndCreateCommunity()         // founding gate — sole guard: not already a primary leader
```

### Citizenship
```csharp
Community Citizenship { get; }              // sticky civic membership (distinct from CurrentCommunity)
void      SetCitizenship(Community c)       // grant civic status; implicitly renounces prior citizenship
void      RenounceCitizenship()             // explicit leave gesture
```

> See [[citizenship]] for the full lifecycle (grant → hold → renounce) and the
> membership-vs-citizenship distinction.

### Leadership (delegation)
```csharp
bool IsLeaderOfCurrentCommunity { get; }   // delegates to Community.IsLeader(this.Character)
```

> **Auth predicate rule:** internal calls use `Community.IsLeader(character)`, not
> `Community.PrimaryLeader == character`. See [[singular-leader-vs-multi-leader-isleader]].

## Citizenship
`CharacterCommunity._citizenship` holds the live `Community` reference.
`CommunitySaveData.citizenshipMapId` is the persisted form (string map ID).

On save: `Serialize` looks up the `CommunityData` whose `LeaderIds` contains the
citizenship community's primary leader ID, then writes that map ID.

On load: the raw map ID lands in `_pendingCitizenshipMapId`; the live `Community`
reference is rebound when `MapRegistry` surfaces the matching `CommunityData`
(deferred late-bind pattern, mirrors `_pendingCommunityMapId`).

Legacy saves (no `citizenshipMapId` field) deserialize to an empty string →
`_citizenship = null`.

## Data flow
1. Character founds a community → `CheckAndCreateCommunity` → `CommunityManager.CreateNewCommunity` → `CurrentCommunity` set.
2. Citizenship granted (Plan 4, via `AdministrativeBuilding.OnFinalize` or `JoinRequestDesk`) → `SetCitizenship(community)` → `_citizenship` set.
3. Profile save: `CommunitySaveData.communityMapId` + `CommunitySaveData.citizenshipMapId` written to JSON.
4. Profile load: pending IDs held; late-bound when `MapRegistry` confirms matching `CommunityData`.

## Dependencies (upstream / downstream)
- **Upstream:** [[character]] (lives on a character's child GameObject), [[world-community]] (wraps `Community` and `CommunityManager`).
- **Downstream:** profile save pipeline (`ICharacterSaveData<CommunitySaveData>`), [[world-community]] (`Community.Citizens` reads citizenship back).

## State & persistence
- `_currentCommunity : Community` — live reference (server-only, not serialized directly).
- `_citizenship : Community` — live reference (server-only).
- `_pendingCommunityMapId : string` — late-bind buffer on load.
- `_pendingCitizenshipMapId : string` — late-bind buffer on load.
- `CommunitySaveData.communityMapId` — persisted membership map ID.
- `CommunitySaveData.citizenshipMapId` — persisted citizenship map ID (added Task 6, 2026-05-17).

## Known gotchas / edge cases
- **Multi-leader predicate** — the adapter's `IsLeaderOfCurrentCommunity` must delegate to `Community.IsLeader(character)`, never read `Community.PrimaryLeader` for auth. See [[singular-leader-vs-multi-leader-isleader]].
- **Founding gate stripped** — `CheckAndCreateCommunity` no longer requires `canCreateCommunity` trait or ≥4 friends (removed Tasks 3/4, 2026-05-17). Any code that still checks these gates in the call chain is stale.
- **Citizenship is server-side only** — `_citizenship` is on `Community` (not a `NetworkBehaviour`). Client-visible citizenship state must be pushed via `CommunityData.citizenshipMapId` in the save-sync path.
- **Double late-bind** — both `_pendingCommunityMapId` and `_pendingCitizenshipMapId` are resolved in the same `OnMapRegistryReady` callback. If `MapRegistry` is not yet ready at profile load time, citizenship will be null until the callback fires.

## Open questions / TODO
- Plan 4: `AdministrativeBuilding.OnFinalize` must call `SetCitizenship` on the founder — not yet implemented.
- Q4 from the original stub (character-side adapter vs world-side entity split): confirmed by implementation — the split is intentional and correct.
- No dedicated SKILL.md for `character-community` alone — tracked as a future task (currently documented under the umbrella `community-system` SKILL.md).

## Change log
- 2026-04-19 — Stub. Q4 inference noted. — Claude / [[kevin]]
- 2026-05-17 — Added Citizenship section (`_citizenship`, `SetCitizenship`, `RenounceCitizenship`); `CommunitySaveData.citizenshipMapId` round-trip documented; founding gate simplified (no trait / 4-friends); multi-leader predicate note; full 10-section template applied; Q4 resolved. — claude

## Sources
- [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) — primary implementation.
- [CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs) — `communityMapId`, `citizenshipMapId`.
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md) — procedural how-to.
- File inspection 2026-04-18 — [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) (1 file) vs `Assets/Scripts/World/Community/` (3 files).
