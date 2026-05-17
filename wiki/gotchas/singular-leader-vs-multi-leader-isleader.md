---
type: gotcha
title: "Singular `PrimaryLeader` getter vs multi-leader `IsLeader(Character)` check"
tags: [community, leader, auth, multi-leader, networking]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[Community.cs](../../Assets/Scripts/World/Community/Community.cs)"
  - "[singular-owner-vs-multi-owner-isowner.md](singular-owner-vs-multi-owner-isowner.md)"
related:
  - "[[world-community]]"
  - "[[character-community]]"
  - "[[singular-owner-vs-multi-owner-isowner]]"
status: open
confidence: high
---

# Singular `PrimaryLeader` getter vs multi-leader `IsLeader(Character)` check

## Summary
`Community` (the server-side group container) supports **multiple leaders** via
`public List<Character> leaders` (primary at index 0, secondaries 1..n).
`Community.PrimaryLeader` is a convenience getter that returns **only the first
entry** (`leaders[0]`). Using it for auth checks (`if (community.PrimaryLeader != character)`)
silently rejects every leader except the primary — including any secondary
leader added via `PromoteToSecondaryLeader` (the canonical multi-leader mutation
path that the admin console's Leaders tab in Plan 5 uses). The correct predicate
is `Community.IsLeader(Character)`, which checks the full `leaders` list.

## When this bites you
- Symptom: a freshly-promoted secondary leader can't open / use a feature gated
  by a "leader" check. The toast / log message says something like "Only the
  leader can …".
- Root cause: the auth gate is written as `community.PrimaryLeader == character`
  or `community.leaders[0] == character`. Both compare against the primary only.
- Reproduction: in Plan 5's admin console, promote a second character to
  secondary leader, switch control to that secondary, attempt the gated action.
  The primary works; the secondary is rejected.

## The fix
Replace `community.PrimaryLeader == character` / `community.leaders[0] == character`
with `community.IsLeader(character)` / `!community.IsLeader(character)`.

```csharp
// WRONG — singular primary check, blocks secondaries.
if (community.PrimaryLeader != character) { … reject … }

// RIGHT — multi-leader-aware, null-safe.
if (!community.IsLeader(character)) { … reject … }
```

`IsLeader(null)` returns false safely.

## Why this is sneaky
- `PrimaryLeader` returns a non-null `Character` for the founder, which makes
  the line *look* correct in code review — there's no obvious null-handling
  smell.
- Until Plan 5 ships, every community has exactly one leader (the founder), so
  `PrimaryLeader == character` and `IsLeader(character)` produce the same
  result on every test path. The bug stays latent until a second leader is
  promoted.
- The pattern compiles, runs, and the auth toast you authored is exactly the
  toast that fires — so the developer who hits it assumes the gate is "working
  correctly" and looks for the bug elsewhere (e.g. "did promotion actually
  save?").

## Where the canonical predicate lives
- `Community.IsLeader(Character)` at `Assets/Scripts/World/Community/Community.cs`
  (around line 51 post-migration) — `c != null && leaders.Contains(c)`.
  Reference-equality comparison (Character is a `MonoBehaviour`, identity is fine).
- The matching string-keyed predicate on the save side:
  `MWI.WorldSystem.CommunityData.IsLeader(string)` at
  `Assets/Scripts/World/MapSystem/MapRegistry.cs` (around line 625) —
  `LeaderIds.Contains(characterId)`.

## Audit list (post-migration)
The 2026-05-17 multi-leader migration (Plan 1 of City Founding) swept every
`.leader` callsite. Production code now uses `IsLeader(c)` exclusively:
- `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` (5 sites)
- `Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs:13`

Future leader-gated surfaces (Plans 4-5: admin console buttons, AB-furniture
interactions, join-request accept/decline, tier-up triggers) must use
`IsLeader(c)` from day one.

## Network safety
- `Community.leaders` is a plain `List<Character>` on a server-only class
  (Community is NOT a `NetworkBehaviour`).
- Clients that need to display leadership info pull through `MapRegistry.GetCommunity(mapId).LeaderIds`
  (a save-data field) or, for live state, through server-authoritative queries
  added by Plan 5.
- No replication channel is added by the migration itself — predicate swap only.
- Late-joiner repro: deferred to Plan 4/5 when the first client-visible
  leadership UI ships.

## Links
- [[world-community]]
- [[character-community]]
- [[singular-owner-vs-multi-owner-isowner]] — the sister gotcha that motivated
  this one (same pattern, different domain).

## Sources
- [Community.cs](../../Assets/Scripts/World/Community/Community.cs) — the source of truth.
- [singular-owner-vs-multi-owner-isowner.md](singular-owner-vs-multi-owner-isowner.md) — direct stylistic ancestor.
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §"Files Changes Summary" — the spec line that mandated this gotcha (line 1348).
- [docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md](../../docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md) §Task 8 — the implementation pass that swept the callsites.
