---
type: system
title: "Party"
tags: [party, social, group, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[social]]"
  - "[[ai]]"
  - "[[save-load]]"
  - "[[network]]"
  - "[[world]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-social-architect
secondary_agents:
  - character-system-specialist
  - save-persistence-specialist
owner_code_path: "Assets/Scripts/Character/CharacterParty/"
depends_on:
  - "[[character]]"
  - "[[social]]"
  - "[[network]]"
  - "[[save-load]]"
depended_on_by:
  - "[[ai]]"
  - "[[world]]"
---

# Party

## Summary
Small-scale character groups (2–8 members) where members stay together and follow a leader. A **party** is a tight traveling unit — distinct from **community** (the large social/territorial group documented in [[world]]). The architecture is a hybrid: `PartyData` (what the party is) + `PartyRegistry` (where to find it) + `CharacterParty` (per-character behaviour). Parties are server-authoritative; clients learn party state through `NetworkVariable`s and ClientRpcs. Party data uses `CharacterId` UUIDs, never direct `Character` references, so parties survive hibernation, disconnects, and map transitions.

## Purpose
Let 2–8 characters coordinate movement, formation, and map transitions as one unit. Both players and NPCs share the same `Character` class, so a party is always a unified code path — player-led, NPC-led, and mixed parties all work identically. Invitations flow through the same `CharacterInvitation` interaction pipeline as any other social exchange (see [[social]]).

## Responsibilities
- Creating, disbanding, and transferring leadership on parties.
- Tracking membership by UUID (survives hibernation/reconnect).
- Running leader-follow behaviour (`Strict` — follow leader; `Loose` — wander in territory).
- Gathering all members before map transitions so the party moves as a unit.
- Promoting a new leader automatically when the current leader is removed (`MemberIds[0]` wins).
- Announcing party events to clients (`OnJoinedParty`, `OnLeftParty`) via server-fire-plus-ClientRpc pattern.
- Serializing party state through [[save-load]] for both character profiles and world saves.

**Non-responsibilities**:
- Does **not** own invitation UI or the invitation interaction — see [[social]] `[[character-invitation]]`.
- Does **not** own relationship values — see [[social]] `[[character-relation]]`.
- Does **not** own community territory or city growth — see [[world]].

## Key classes / files

| File | Role |
|------|------|
| [CharacterParty.cs](../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs) | `CharacterSystem` on every character. Holds party membership state, syncs via 3 `NetworkVariable`s. |
| [PartyData.cs](../../Assets/Scripts/Character/CharacterParty/PartyData.cs) | Plain C# `[Serializable]` — identity + membership + follow mode + transient state. |
| [PartyRegistry.cs](../../Assets/Scripts/Character/CharacterParty/PartyRegistry.cs) | Static class — dual dictionaries (`PartyId → PartyData`, `CharacterId → PartyId`). |
| [PartyFollowMode.cs](../../Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs) | enum: `Strict` / `Loose`. |
| [PartyState.cs](../../Assets/Scripts/Character/CharacterParty/PartyState.cs) | enum: `Active` / `LeaderlessHold` / `Gathering` (transient; resets on load). |
| UI_PartyPanel, UI_PartyMemberSlot | `Assets/Scripts/UI/` — HUD. |
| BT follow nodes | `Assets/Scripts/AI/Behaviours/` — leader-follow branch. |

## Public API / entry points

Server-authoritative operations (call on server only):
- `CharacterParty.CreateParty(leader)` / `DissolveParty()`.
- `CharacterParty.Invite(target)` — routes through `CharacterInvitation`.
- `CharacterParty.Accept(partyData)` / `Decline()` — invitee side.
- `CharacterParty.Leave()`.
- `CharacterParty.SetFollowMode(PartyFollowMode)`.
- `CharacterParty.TransferLeadership(newLeader)`.

Read-only (any side):
- `CharacterParty.CurrentParty` — `PartyData` or null.
- `CharacterParty.IsLeader`, `IsInParty`, `IsGathering`.
- `PartyRegistry.GetParty(partyId)`, `GetPartyForCharacter(characterId)`.

Events (fired server-side, mirrored client-side via ClientRpc):
- `OnJoinedParty(PartyData)`, `OnLeftParty()`, `OnLeaderChanged(newLeaderId)`, `OnMemberJoined(memberId)`, `OnMemberLeft(memberId)`, `OnStateChanged(newState)`.

NetworkVariables on `CharacterParty`:
- `_networkPartyId` (`FixedString64Bytes`).
- `_networkPartyState` (`byte` — `PartyState`).
- `_networkFollowMode` (`byte` — `PartyFollowMode`).

## Data flow

Invitation → join:
```
Leader calls CharacterParty.Invite(target)
       │
       ▼
CharacterInvitation creates InteractionInvitation
       │  (see social.md)
       ▼
Target receives invite, accepts
       │
       ▼
PartyData.AddMember(targetId)
       │
       ├── PartyRegistry.MapCharacterToParty(targetId, partyId)
       ├── _networkPartyId set on target's CharacterParty
       └── ClientRpc fires OnJoinedParty on all clients
```

Map transition while partied:
```
Any member reaches transition door
       │
       ▼
PartyData.State = Gathering
       │
       ▼
All members path toward transition
       │
       ├── all-gathered timeout? ──► leader transitions alone; others flagged
       └── all arrive          ──► transition together, server warps all on target map
```

Hibernation:
```
Map hibernates
       │
       ├── PartyData survives (stored by UUID, not prefab refs)
       ├── PartyState reset to Active on next load (transient)
       └── CharacterParty._networkPartyId re-applied when NPCs respawn
```

## Dependencies

### Upstream
- [[character]] — `CharacterParty` is a `CharacterSystem`.
- [[social]] — invitations flow through `CharacterInvitation` (same pipeline as dialogue, etc.).
- [[network]] — server-authoritative party operations; `NetworkVariable`s + ClientRpcs.
- [[save-load]] — party membership survives character profile round-trip.

### Downstream
- [[ai]] — BT follow branches read `CurrentParty.FollowMode` and `LeaderId`.
- [[world]] — transitions gather the party before flipping map state.

## State & persistence

- **Saved**: `PartyId`, `PartyName`, `LeaderId`, `MemberIds`, `FollowMode`.
- **Transient, not saved**: `PartyState` (resets to `Active` on load).
- **Character profile** round-trip: portable character profile carries `CurrentPartyId` reference; on session load, PartyRegistry re-binds if the party data is present in the world save.
- **World save**: all known parties serialize to the world save (Solo) or session save (Multi).

## Known gotchas / edge cases

- **UUIDs, not `Character` refs** — direct object references don't survive hibernation. Every party operation must use `CharacterId` strings.
- **Always call `PartyRegistry.MapCharacterToParty` / `UnmapCharacter`** whenever modifying `PartyData.MemberIds` — the reverse lookup desyncs silently otherwise.
- **Server authority** — never mutate `PartyData` from a client path. Route through ServerRpc.
- **`CharacterParty` lives on every character** — not just party members. Mirrors the pattern of `CharacterCombat`, `CharacterJob`.
- **Auto-promote leader** — removing the current leader auto-promotes `MemberIds[0]`. If the list is empty, the party auto-dissolves.
- **Gathering deadlock** — if a member can't reach the transition (stuck, incapacitated), the party times out and the leader proceeds alone. Stuck members are flagged in Open questions below.

## Open questions / TODO

- [ ] Exact timeout for gathering-before-transition — needs verification from code. Flag for review.
- [ ] What happens to a NPC-led party when the NPC enters hibernation? Does the party itself hibernate or stay "alive" on the active map?

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/party-system/SKILL.md](../../.agent/skills/party-system/SKILL.md)
- [.agent/skills/character_invitation/SKILL.md](../../.agent/skills/character_invitation/SKILL.md)
- [.claude/agents/character-social-architect.md](../../.claude/agents/character-social-architect.md)
- [CharacterParty.cs](../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs)
- [PartyData.cs](../../Assets/Scripts/Character/CharacterParty/PartyData.cs)
- [PartyRegistry.cs](../../Assets/Scripts/Character/CharacterParty/PartyRegistry.cs)
- 2026-04-18 conversation with [[kevin]].
