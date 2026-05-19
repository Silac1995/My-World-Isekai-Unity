---
type: system
title: "Character Relation"
tags: [character, social, relationship, compatibility, tier-2]
created: 2026-04-18
updated: 2026-05-19
sources: []
related:
  - "[[character]]"
  - "[[social]]"
  - "[[world]]"
  - "[[party]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-social-architect
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterRelation/"
depends_on:
  - "[[character]]"
  - "[[social]]"
depended_on_by:
  - "[[social]]"
  - "[[world]]"
  - "[[party]]"
  - "[[jobs-and-logistics]]"
  - "[[character-speech]]"
---

# Character Relation

## Summary
Bilateral opinion storage. Each character keeps a list of `Relationship` entries with other characters by ID; `AddRelationship(other)` on A automatically inserts the symmetric entry on B. Opinion deltas (`UpdateRelation(other, delta)`) are filtered by personality compatibility — compatible personalities multiply gains (×1.5) and mitigate conflicts (×0.5); incompatible ones halve gains and amplify conflicts. This is the long-term memory layer of [[social]].

## Purpose
Make social interactions **matter** over the long run. The same "+10 friendly gesture" has different consequences on different targets based on their personality match, producing believable NPC dynamics without hand-writing rules per relationship. Values feed community founding gates ([[world]]), party invitation likelihood, and shop reputation penalties ([[jobs-and-logistics]] expired orders).

## Responsibilities
- Storing `Relationship` entries keyed by other-character ID.
- Bilateral `AddRelationship` / `SetAsMet` — A→B always implies B→A.
- Filtering `UpdateRelation(other, delta)` through `CharacterProfile.GetCompatibilityWith(other)`.
- Exposing reads: `GetOpinion(other)`, `GetFriendCount()` (consumed by [[world]] community founding).
- Persisting the relationship list through the character profile.

**Non-responsibilities**:
- Does **not** run interactions — see [[character-interaction]].
- Does **not** define personality traits — those live in `CharacterProfile` (see [[character-profile]]).
- Does **not** own party membership — see [[party]].

## Key classes / files

- `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` — component.
- `Assets/Scripts/Character/CharacterRelation/Relationship.cs` — single bilateral bond entry.
- `CharacterProfile.GetCompatibilityWith(Character other)` — the filter. Location TBD in [[character-profile]] stub.

## Public API

- `character.CharacterRelation.SetAsMet(other)` — first acknowledgement; adds bilateral entry.
- `character.CharacterRelation.AddRelationship(other)` — alias/low-level.
- `character.CharacterRelation.UpdateRelation(other, delta)` — filtered opinion delta.
- `character.CharacterRelation.GetOpinion(other)` — current numeric value.
- `character.CharacterRelation.GetFriendCount()` — count of entries above a friendship threshold.
- `character.CharacterRelation.GetRelationshipWith(other)` — returns the `Relationship` entry (or null if none), used by consumers that need `KnowsName` / `HasMet` / opinion in one call.

### `Relationship` state

| Field | Type | Semantics |
|---|---|---|
| `Opinion` / `_relationValue` | `int` [-100, 100] | Compatibility-filtered numeric opinion. |
| `RelationshipType` | enum | Auto-derived from `Opinion`. |
| `HasMet` | `bool` | True once the two characters have been formally acknowledged via `SetAsMet`. Used for first-meeting gates and dialogue. |
| `KnowsName` | `bool` | **Separate from `HasMet`.** True once the local character has been told the other's name (introductions, signage, NPC dialogue). Drives the display-name fallback in [[character-speech]]: when a remote speaker's `KnowsName == false` for the local player, their speech bubble's name strip shows `"???"` instead of `Character.DisplayName`. Replicated via the existing `CharacterRelationSyncData` `NetworkList` path and persisted via the existing `RelationshipSaveEntry` save round-trip (with a dormant-resurrection path that also threads it). |

## Data flow — compatibility filter

```
A.CharacterRelation.UpdateRelation(B, +10)
       │
       ▼
B.CharacterProfile.GetCompatibilityWith(A) → enum { Compatible | Neutral | Incompatible }
       │
       ├── Compatible:   +10 × 1.5 = +15       (or -10 × 0.5 = -5  on conflict)
       ├── Neutral:      +10                   (or -10 on conflict)
       └── Incompatible: +10 × 0.5 = +5        (or -10 × 1.5 = -15 on conflict)
       │
       ▼
B._relationships[A.Id].opinion += filteredDelta
       │
       ▼
(Bilateral) A._relationships[B.Id].opinion += filteredDelta computed from A's side
```

## Dependencies

### Upstream
- [[character]] — component on a child GameObject.
- [[character-profile]] — compatibility filter lives there.

### Downstream
- [[social]] — every interaction action updates relations.
- [[world]] — community founding gate needs `GetFriendCount() >= 4`.
- [[party]] — invitations weighted by existing opinion (confirm).
- [[jobs-and-logistics]] — expired `BuyOrder` calls `UpdateRelation(client, negative)` for reputation penalty.

## State & persistence

- Saved to character profile: list of `Relationship` tuples `(otherCharacterId, opinion, lastUpdated)`.
- No offline decay currently (verify — could add "relationships fade if not interacted with").

## Known gotchas

- **Bilateral rule** — `AddRelationship` must add the symmetric entry. Forgetting this causes one-way memory: A remembers B but B doesn't remember A.
- **Compatibility ≠ opinion** — compatibility is a function of `CharacterProfile` (personality); opinion is a learned value. Don't conflate them when debugging odd behavior.
- **"Why did the player get fewer points than expected?"** → always check personality compatibility first. The filter is invisible to players and easy to miss.
- **`GetFriendCount` threshold** — exact threshold for "friend" must match [[world]]'s community gate (≥ 4 friends). Keep these in sync.

## Open questions

- [ ] Does opinion decay over time? Currently appears not to.
- [ ] Is there a hard cap on opinion magnitude? Needs confirmation.

## Change log
- 2026-05-19 — Added KnowsName bool (separate from HasMet) — drives speech-bubble "???" display when local player hasn't been told the speaker's name. — claude
- 2026-04-18 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md)
- [[social]] parent and [[character]] parent.
- `Assets/Scripts/Character/CharacterRelation/` (3 files).
