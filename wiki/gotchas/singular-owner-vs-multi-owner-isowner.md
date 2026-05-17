---
type: gotcha
title: "Singular `Owner` getter vs multi-owner `IsOwner(Character)` check"
tags: [building, owner, auth, multi-owner, networking]
created: 2026-05-17
updated: 2026-05-17
sources: []
related:
  - "[[commercial-building]]"
  - "[[management-panel-architecture]]"
  - "[[commercial-treasury]]"
status: mitigated
confidence: high
---

# Singular `Owner` getter vs multi-owner `IsOwner(Character)` check

## Summary
`Room` (the base of every `Building`, `CommercialBuilding`, `ResidentialBuilding`) supports **multiple owners** via a server-replicated `NetworkList<FixedString64Bytes> _ownerIds`. `CommercialBuilding.Owner` is a convenience getter that returns **only the first entry** (`_ownerIds[0]`). Using it for auth checks (`if (building.Owner != character)`) silently rejects every owner except the first — including any secondary owner added via `AddOwner` (the canonical multi-owner mutation path that the dev console's `[DEV] Add Owner` button and any future co-ownership flow use). The correct predicate is `Room.IsOwner(Character)`, which compares the character's UUID against the full `_ownerIds` list.

## When this bites you
- Symptom: a freshly-added co-owner can't open / use a furniture-driven feature gated by an owner check. The toast / log message says something like "Only the owner can …".
- Root cause: the auth gate is written as `building.Owner == character` or `building.Owner != character`. Both compare against a single owner.
- Reproduction: host a session, use the dev console's `[DEV] Add Owner` button to attach a second character as owner, switch control to that second character, try to use the feature. The first owner works, the second is rejected.

## The fix
Replace `building.Owner == character` / `building.Owner != character` with `building.IsOwner(character)` / `!building.IsOwner(character)`. The predicate lives on the `Room` base class and is inherited by every building subtype.

```csharp
// WRONG — singular owner check, blocks secondary owners.
if (!building.HasOwner || building.Owner != character) { … reject … }

// RIGHT — multi-owner-aware, network-safe, null-safe.
if (!building.IsOwner(character)) { … reject … }
```

The `!HasOwner` clause becomes redundant because `IsOwner(character)` returns `false` for any character when `_ownerIds` is empty (a building with no owners rejects everyone, which is the desired behaviour).

## Why this is sneaky
- `CommercialBuilding.Owner` returns a non-null `Character` for the first owner, which makes the line *look* correct in code review — there's no obvious null-handling smell.
- `HasOwner` returns `true` when the first owner is alive, which also looks correct.
- The bug only surfaces when `_ownerIds.Count > 1` — and until 2026-05-17 the only multi-owner path in production was rare. The dev console's `[DEV] Add Owner` exposes it routinely now, so any new owner-gated furniture written before this gotcha was documented likely has the bug latent.
- The pattern compiles, runs, and the toast you authored is exactly the toast that fires — so the developer who hits it assumes the gate is "working correctly" and looks for the bug elsewhere (e.g. "is the dev console adding the owner properly?").

## Where the canonical predicate lives
- `Room.IsOwner(Character)` at `Assets/Scripts/World/Buildings/Rooms/Room.cs:93` — `ContainsId(_ownerIds, character.CharacterId)`. Null-safe, compares UUIDs (not refs), works on server + client (the underlying `_ownerIds` is a `NetworkList` with server-write / everyone-read).
- Already used correctly by `CommercialBuilding.cs:1276` (internal authority check) and `ResidentialBuilding.cs:92`. **Mirror these when authoring a new owner-gated surface.**

## Audit list (post-fix)
The 2026-05-17 fix sweep touched only the two ManagementFurniture/Panel call sites. Audited but not buggy (null checks only — `building.Owner != null` is a "does any owner exist" predicate):
- `BuildingLogisticsManager.cs:902` and `:1131`
- `UI_CommercialBuildingDebugScript.cs:33`

If you add a NEW furniture or UI that needs to validate "is this character allowed to interact with this building?", grep for `building.Owner ==` / `building.Owner !=` in your new code and reject the diff — the answer is always `building.IsOwner(character)`.

## Network safety
- `_ownerIds` is `NetworkList<FixedString64Bytes>` — server-write / everyone-read (default permissions).
- Late-joiners receive the full owner list on subscribe (NetworkList delivers all entries to a fresh subscriber).
- Client-side `building.IsOwner(character)` reads the replicated list, so client-side pre-gates agree with server-side auth.
- No new replication channel is needed — every multi-owner gate fix is purely a predicate swap.

## Links
- [[commercial-building]]
- [[management-panel-architecture]]
- [[commercial-treasury]]

## Sources
- [Assets/Scripts/World/Buildings/Rooms/Room.cs](../../Assets/Scripts/World/Buildings/Rooms/Room.cs) — `IsOwner` (line 93), `_ownerIds` NetworkList (line 26).
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — singular `Owner` getter (line 201), `HasOwner` (line 1188), canonical `IsOwner` callsite (line 1276).
- [Assets/Scripts/World/Furniture/ManagementFurniture.cs](../../Assets/Scripts/World/Furniture/ManagementFurniture.cs) — the bug + fix (commit `520a2291` on worktree / `457b99cd` on multiplayyer).
- [Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs](../../Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs) — sibling defense-in-depth gate (same fix).
- 2026-05-17 conversation with [[kevin]] reporting the symptom after adding a second owner via the dev console.
