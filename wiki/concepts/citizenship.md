---
type: concept
title: "Citizenship"
tags: [community, character, city-founding, administrative-building]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs)"
  - "[CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs)"
related:
  - "[[character-community]]"
  - "[[world-community]]"
  - "[[administrative-building]]"
  - "[[found-a-city-ambition]]"
status: active
confidence: high
---

# Citizenship

## Summary
**Citizenship** is the sticky, formal "you belong to this city" relationship
between a `Character` and a `Community`. It is **distinct from membership**:
membership (`CharacterCommunity.CurrentCommunity`) is the community a character
is *currently in* (transient — can change every time they move maps);
citizenship (`CharacterCommunity.Citizenship`) is the community that has
**granted them civic status** (sticky — only changes on a deliberate gesture).

Membership is required to do things in a community's territory; citizenship is
required to access **civic privileges** (voting on tier-up, holding leadership,
appearing in the city's `Citizens` accessor, future tax/welfare hooks).

## Definition
A character holds citizenship in exactly one community at a time (v1 constraint —
no dual-citizenship). Citizenship is a server-side, save-persisted relationship
backed by `CharacterCommunity._citizenship : Community` (the live reference) and
`CommunitySaveData.citizenshipMapId : string` (the persisted map ID round-trip).
The `Community.Citizens` accessor returns the filtered view of `community.members`
where `member.CharacterCommunity.Citizenship == community`.

## Context
Citizenship was introduced in Plan 1 of the City Founding spec (2026-05-17).
The `_citizenship` field and its save round-trip exist in the codebase as of
Tasks 5 and 6 of the multi-leader migration. The gesture that *grants* citizenship
(founding via `AdministrativeBuilding.OnFinalize`, or acceptance via a
`JoinRequestDesk`) ships in Plan 4.

Systems that reference citizenship:
- [[character-community]] — owns the `_citizenship` field, `SetCitizenship`, `RenounceCitizenship`.
- [[world-community]] — `Community.Citizens` accessor reads citizenship back.

## Lifecycle

1. **Grant**: a character is granted citizenship when an
   `AdministrativeBuilding.OnFinalize` runs for an AB they founded (Plan 4a —
   landed 2026-05-17), or when a `JoinRequestDesk` accepts their join request
   (Plan 4c — pending).
2. **Hold**: the `_citizenship : Community` field on `CharacterCommunity` holds
   the reference. The matching map ID is round-tripped through
   `CommunitySaveData.citizenshipMapId`.
3. **Renounce**: the character calls `RenounceCitizenship` (UI-driven leave
   gesture, Plan 5) or the community dissolves. A second `SetCitizenship` call
   implicitly renounces the prior citizenship — no double-citizenship in v1.

## Related concepts
- [[world-community]] — the `Community` entity that citizenship points to.
- [[character-community]] — the per-character adapter that holds `_citizenship`.
- [[singular-leader-vs-multi-leader-isleader]] — leaders are always citizens; the predicate gotcha applies to leader-gated civic surfaces.

## Examples

Save semantics:
`CharacterCommunity` writes `data.citizenshipMapId` in `Serialize` by matching
`_citizenship.PrimaryLeader.CharacterId` against `CommunityData.IsLeader(...)`.
On load, the raw map ID lands in `_pendingCitizenshipMapId`; the live `Community`
reference is rebound when `MapRegistry` surfaces the matching `CommunityData`
(deferred late-bind pattern, mirrors `_pendingCommunityMapId`).

Legacy saves (no `citizenshipMapId` field) deserialize to an empty string and
result in `_citizenship = null`.

Membership vs citizenship contrast:
- A drifter who is a member of a city for a few days while looking for work is
  NOT a citizen until accepted. They appear in `community.members` but not in
  `community.Citizens`.
- A citizen who travels to another city for a season is a member of the host
  city (`CurrentCommunity = host`) but still a citizen of home
  (`Citizenship = home`).

## Open questions / TODO
- *Plan 5 — dual-citizenship?* Spec line 1431 hints v1 does NOT support
  it ("no double-citizenship in v1"). Future versions might allow it for
  marriage / alliance scenarios. `confidence: medium` because the dual-citizenship
  edge case is not yet confirmed closed.
- *Plan 4 — tier-up vote*: when a tier-up requires a vote, is the vote among
  `Community.Citizens` or `Community.members`? Spec defers; place-holder for
  Plan 4.

## Links
- [[character-community]]
- [[world-community]]
- [[singular-leader-vs-multi-leader-isleader]]

## Sources
- [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) — `_citizenship` field, `SetCitizenship`, `RenounceCitizenship`.
- [CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs) — `citizenshipMapId` round-trip.
- [Community.cs](../../Assets/Scripts/World/Community/Community.cs) — `Citizens` accessor (filtered view of members).
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §"`CharacterCommunity.Citizenship`" — design source.
- [docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md](../../docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md) — Plan 1 implementation.
