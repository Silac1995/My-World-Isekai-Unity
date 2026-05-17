---
type: system
title: "World Community"
tags: [world, community, hierarchy, tier-2, multi-leader, citizenship]
created: 2026-04-19
updated: 2026-05-17
sources: []
related:
  - "[[world]]"
  - "[[character-community]]"
  - "[[character-relation]]"
  - "[[adr-0001-living-world-hierarchy-refactor]]"
  - "[[citizenship]]"
  - "[[singular-leader-vs-multi-leader-isleader]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/Community/"
depends_on: ["[[world]]"]
depended_on_by: ["[[world]]", "[[jobs-and-logistics]]"]
---

# World Community

> **Phase 1 refactor complete — see [[adr-0001-living-world-hierarchy-refactor]].**
> `CommunityTracker` is now `MapRegistry`. NPC-cluster auto-promotion (`EvaluatePopulations`, `PromoteToSettlement`, the `RoamingCamp → Settlement → EstablishedCity` lifecycle) was deleted. `CommunityData` stays; entries are created only by `MapController.EnsureCommunityData` (scene-authored maps) or `MapRegistry.CreateMapAtPosition` (wild maps from building placement). `SaveKey = "CommunityTracker_Data"` is intentionally preserved for save-file back-compat. Sections below describe the **post-refactor** state.

## Summary
Social + territorial grouping of characters. Hierarchical (Kingdom > Duchy > Village); members identified by UUID. `MapRegistry` (renamed from `CommunityTracker`) holds the persistent `CommunityData` list — leaders, constructed buildings, resource pools, build permits, pending claims — and exposes `CreateMapAtPosition` (server-only wild-map birth triggered by out-of-map building placement), `AdoptExistingBuildings`, and `ImposeJobOnCitizen` for leader authority. No cluster heartbeat; no auto-promotion. Map birth is explicit (scene authoring or placement-driven).

## Purpose
`Community` is the authoritative server-side entity for any social group that occupies territory. It owns the membership list, the leadership list, the zone list, and the aggregate state that drives the Macro Simulator (offline city growth, job yields, resource pool regeneration). Every other system that needs to know "who belongs here" or "who leads here" queries `Community` directly or its save-data mirror `CommunityData` in `MapRegistry`.

## Responsibilities
- Membership tracking (`members : List<Character>`, `AddMember`, `RemoveMember`).
- Multi-leader tracking (`leaders : List<Character>`, `IsLeader(Character)`, `PromoteToSecondaryLeader`, `DemoteFromLeadership`, `TransferPrimaryLeadership`).
- Citizenship filtering (`Citizens` — members where `CharacterCommunity.Citizenship == this`).
- Hierarchical nesting (parent ↔ sub-community, `DeclareIndependence`).
- Zone ownership (`zones : List<Zone>`).
- Level / tier state (`CommunityLevel`, `ChangeLevel`).
- Aggregate state persistence (serialised into `CommunityData` via `MapRegistry`).

## Key classes / files
- `Assets/Scripts/World/Community/Community.cs` — the runtime entity (NOT a NetworkBehaviour).
- `Assets/Scripts/World/Community/CommunityLevel.cs` — tier enum.
- `Assets/Scripts/World/Community/CommunityManager.cs` — in-scene registry / zone instantiator.
- `Assets/Scripts/World/MapSystem/MapRegistry.cs` — persistent `CommunityData` store; `LeaderIds`, `IsLeader(string)`.
- `Assets/Scripts/World/MapSystem/CommunityData.cs` — serialisable save struct.

## Public API / entry points

### Leadership (multi-leader)
```csharp
// Read
List<Character>   community.leaders              // full list; index 0 = primary
Character         community.PrimaryLeader         // leaders[0] convenience getter — use for display only
List<Character>   community.SecondaryLeaders      // leaders[1..n]
bool              community.IsLeader(Character c) // CANONICAL auth check — null-safe, full list

// Mutate (server-only)
void community.PromoteToSecondaryLeader(Character c)  // adds to leaders; does not displace primary
void community.DemoteFromLeadership(Character c)       // removes from leaders
void community.TransferPrimaryLeadership(Character c)  // moves c to index 0
// SetLeader(Character) — DELETED in 2026-05-17 migration; replaced by the above three.
```

> **Auth predicate rule:** always use `IsLeader(c)`. Never use `PrimaryLeader == c` or
> `leaders[0] == c` for auth — see [[singular-leader-vs-multi-leader-isleader]].

### Membership
```csharp
void community.AddMember(Character c)
void community.RemoveMember(Character c)
List<Character> community.members  // all members; read-only outside Community
```

### Citizenship
```csharp
// Filtered accessor — members where CharacterCommunity.Citizenship == this community
IReadOnlyList<Character> community.Citizens
```
`Citizens` is a derived view, not a stored list — recomputed on access. See [[citizenship]] for lifecycle.

### Hierarchy
```csharp
void community.DeclareIndependence()
void parentCommunity.AddSubCommunity(Community sub)
```

### Level
```csharp
void community.ChangeLevel(CommunityLevel level)
```

### CommunityManager (scripted / admin)
```csharp
Community CommunityManager.Instance.CreateNewCommunity(Character founder, string name)
Zone      CommunityManager.Instance.EstablishCommunityZone(community, pos, ZoneType, name)
```

### MapRegistry (server-only, save-layer)
```csharp
MapRegistry.Instance.ImposeJobOnCitizen(mapId, leaderId, citizen, job, building)
```

## Data flow
1. Player or NPC triggers a founding gesture → `CharacterCommunity.CheckAndCreateCommunity` → `CommunityManager.CreateNewCommunity` → runtime `Community` object born.
2. `MapRegistry.EnsureCommunityData` / `CreateMapAtPosition` creates/updates a `CommunityData` entry (persisted).
3. `MacroSimulator` reads `CommunityData.LeaderIds` (string UUIDs) to drive offline growth — never touches the runtime `Community` object directly.
4. Live leader-gated actions call `Community.IsLeader(character)` on the server; the save-layer equivalent is `CommunityData.IsLeader(characterId)`.
5. Client-side leadership display reads `MapRegistry.GetCommunity(mapId).LeaderIds` (replicated via the NGO save-sync path, not a direct `Community` reference).

## Dependencies (upstream / downstream)
- **Upstream:** [[world]] (region / map context), [[character]] (members are Characters).
- **Downstream:** [[jobs-and-logistics]] (leader blueprints drive macro-sim build selection), [[character-community]] (per-character adapter), Plan 4 `AdministrativeBuilding` (grants citizenship, promotes leaders).

## State & persistence
- Runtime state lives in the `Community` object (server only, not a `NetworkBehaviour`).
- Persistent state lives in `CommunityData` serialised by `MapRegistry` under the save key `"CommunityTracker_Data"` (intentionally preserved for back-compat).
- **Citizenship**: `Community.Citizens` returns members filtered by `CharacterCommunity.Citizenship == this`. The backing field is on `CharacterCommunity._citizenship`; the persisted form is `CommunitySaveData.citizenshipMapId`. See [[citizenship]] for the full lifecycle.
- **Leader IDs on save**: `CommunityData.LeaderIds : List<string>` (UUIDs). Written by `MapRegistry` whenever `Community.leaders` changes. Read by `MacroSimulator` during hibernation.

## Known gotchas / edge cases
- **Multi-leader auth trap** — see [[singular-leader-vs-multi-leader-isleader]]. Never use `PrimaryLeader == c` for an auth check; use `IsLeader(c)`.
- **`Community` is NOT a `NetworkBehaviour`** — it is server-only. Clients must receive leadership info via `CommunityData.LeaderIds` through the save-sync path, not by direct field access.
- `CommunityManager.CreateNewCommunity` bypasses all founding requirements (trait, friends). Use only for scripted/admin creation; player-facing founding always goes through `CharacterCommunity.CheckAndCreateCommunity`.
- The `Citizens` accessor recomputes on every call (linear scan of `members`). Do not call it in a hot path.

## Open questions / TODO
- Plan 4: `AdministrativeBuilding.OnFinalize` must call `SetCitizenship` on the founder — not yet implemented.
- Plan 5: admin console Leaders tab (`PromoteToSecondaryLeader`, `DemoteFromLeadership`) — not yet implemented.
- `Community.Citizens` performance: if member counts grow to >200, wrap in an explicit-invalidation cache.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-21 — Added pending-refactor notice pointing to [[adr-0001-living-world-hierarchy-refactor]]. — Claude / [[kevin]]
- 2026-04-21 — Refactor implemented. `CommunityTracker` renamed to `MapRegistry`, cluster-promotion deleted, wild-map save/load round-trip working. — Claude / [[kevin]]
- 2026-05-17 — Multi-leader migration: `leader` field → `leaders : List<Character>`; `IsLeader(c)` is canonical auth predicate; `SetLeader` deleted; `PromoteToSecondaryLeader` / `DemoteFromLeadership` / `TransferPrimaryLeadership` added. Citizenship (`Citizens` accessor, [[citizenship]] concept page). Public API, Data flow, State & persistence, Gotchas, Open questions sections added. — claude

## Sources
- [Assets/Scripts/World/Community/Community.cs](../../Assets/Scripts/World/Community/Community.cs) — primary implementation.
- [Assets/Scripts/World/MapSystem/MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) — `CommunityData`, `LeaderIds`, `ImposeJobOnCitizen`.
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md) — procedural how-to.
- [[world]] parent §6.
