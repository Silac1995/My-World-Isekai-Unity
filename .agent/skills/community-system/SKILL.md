---
name: community-system
description: Manages the social and territorial structure of NPCs, including hierarchy, membership, leadership, citizenship, and zones.
---

# Community System

> **Phase 1 refactor (ADR-0001):** `CommunityTracker` was renamed to `MapRegistry`. The cluster-driven auto-promotion lifecycle (`Roaming Camp → Settlement → Established City → Abandoned City`) has been removed — maps are now born only via scene authoring or `MapRegistry.CreateMapAtPosition` (building placement / future procedural). `CommunityData` itself is unchanged. Any reference below to `CommunityTracker.Instance.XXX` still works via `MapRegistry.Instance.XXX` (same API surface minus cluster methods). Save-file key `"CommunityTracker_Data"` is intentionally preserved for back-compat.

> **Multi-leader migration (2026-05-17):** `Community.leader` (singular `Character`) was replaced by `Community.leaders : List<Character>`. `SetLeader(Character)` was deleted. The canonical auth predicate is now `Community.IsLeader(Character)`. See "Common pitfalls" below and `wiki/gotchas/singular-leader-vs-multi-leader-isleader.md`.

The Community System handles the social grouping and territorial management of characters in the world. It allows for hierarchical structures (parent and sub-communities) and defines physical zones belonging to a community.

## When to use this skill
- When creating or dissolving a social group (village, guild, camp).
- When adding or removing a character from a community.
- When checking or changing community leadership.
- When granting or revoking citizenship.
- When establishing physical territories (Zones) for a community.
- When querying community hierarchy or membership.

## Public API reference

### Community (runtime entity — server-only, NOT NetworkBehaviour)

| Member | Signature | Notes |
|--------|-----------|-------|
| `leaders` | `List<Character>` | Full leader list. Index 0 = primary. |
| `PrimaryLeader` | `Character` (getter) | `leaders[0]`. Display only — never use for auth. |
| `SecondaryLeaders` | `List<Character>` (getter) | `leaders[1..n]`. |
| `IsLeader` | `bool IsLeader(Character c)` | **Canonical auth check.** Null-safe, full list. |
| `PromoteToSecondaryLeader` | `void (Character c)` | Adds c to `leaders` without displacing primary. |
| `DemoteFromLeadership` | `void (Character c)` | Removes c from `leaders`. |
| `TransferPrimaryLeadership` | `void (Character c)` | Moves c to index 0. |
| `Citizens` | `IReadOnlyList<Character>` (getter) | Members where `CharacterCommunity.Citizenship == this`. |
| `members` | `List<Character>` | All members (superset of Citizens). |
| `AddMember` | `void (Character c)` | — |
| `RemoveMember` | `void (Character c)` | — |
| `DeclareIndependence` | `void ()` | Detaches from parent community. |
| `ChangeLevel` | `void (CommunityLevel)` | — |

> **Deleted:** `SetLeader(Character)` — removed 2026-05-17. Use `PromoteToSecondaryLeader` / `TransferPrimaryLeadership` instead.

### CharacterCommunity (per-character adapter)

| Member | Signature | Notes |
|--------|-----------|-------|
| `CurrentCommunity` | `Community` (getter) | Transient membership. |
| `Citizenship` | `Community` (getter) | Sticky civic status (distinct from membership). |
| `SetCitizenship` | `void (Community c)` | Grant citizenship; implicitly renounces prior. |
| `RenounceCitizenship` | `void ()` | Explicit leave gesture. |
| `CheckAndCreateCommunity` | `void ()` | Founding gate — sole guard: not already a primary leader. |
| `IsLeaderOfCurrentCommunity` | `bool` (getter) | Delegates to `Community.IsLeader(this.Character)`. |

### CommunityManager (scripted / admin)
```csharp
Community CommunityManager.Instance.CreateNewCommunity(Character founder, string name)
Zone      CommunityManager.Instance.EstablishCommunityZone(community, pos, ZoneType, name)
```

### MapRegistry (server-only, save-layer)
```csharp
MapRegistry.Instance.ImposeJobOnCitizen(mapId, leaderId, citizen, job, building)
CommunityData MapRegistry.Instance.GetCommunity(mapId)  // CommunityData.LeaderIds : List<string>
```

## How to use it

### 1. Founding a Community
Founding is an autonomous process managed by the character's `CharacterCommunity` component.

**Requirement (post-migration):** The character must not already be the primary leader of a community.

> **Removed gates:** `canCreateCommunity` trait and ≥4-friend requirement were deleted in Tasks 3/4 of the 2026-05-17 migration. Do not restore them.

**Result:** A new community is created. If the founder already belongs to a community, the new one automatically becomes a sub-community of their current one.

```csharp
// Entry point for founding (handles independent and sub-community cases)
characterCommunity.CheckAndCreateCommunity();
```

### 2. Leadership (multi-leader)

```csharp
// Auth check — ALWAYS use this, never PrimaryLeader == character
if (!community.IsLeader(character)) { return; /* reject */ }

// Promote a secondary leader
community.PromoteToSecondaryLeader(candidate);

// Transfer primary leadership
community.TransferPrimaryLeadership(newPrimary);

// Demote
community.DemoteFromLeadership(character);
```

### 3. Citizenship

Citizenship is the sticky civic status granted by a formal gesture (Plan 4 — Administrative Building or Join Request Desk). It is distinct from membership.

```csharp
// Grant
characterCommunity.SetCitizenship(community);

// Revoke
characterCommunity.RenounceCitizenship();

// Query — citizens of a community
IReadOnlyList<Character> citizens = community.Citizens;
```

Save round-trip: `CommunitySaveData.citizenshipMapId` (string map ID). The live reference is rebound after `MapRegistry` is ready (deferred late-bind, mirrors `communityMapId`).

### 4. Community Hierarchy
Communities can be nested infinitely to form complex social structures (Kingdoms > Duchies > Villages).

```csharp
parentCommunity.AddSubCommunity(subCommunity);
community.DeclareIndependence();
```

### 5. Community Assets & Building Ownership
`CommercialBuilding` is owned by an individual character; the community automatically tracks assets of its members. Owner privileges (bypassing schedules in `JobLogisticsManager`) remain with the individual, not the community.

### 6. Community Growth & Leader Blueprints
City growth is driven by the **primary leader's** `CharacterBlueprints.UnlockedBuildingIds`. The `MacroSimulator` reads `CommunityData.LeaderIds[0]` during hibernation to select the next building to construct (filtered by `CommunityPriority` in `WorldSettingsData.BuildingRegistry`). Future plans: secondary leaders' blueprints may contribute a merged union.

### 7. Imposing Jobs (leader authority)
```csharp
MapRegistry.Instance.ImposeJobOnCitizen(mapId, leaderId, citizen, job, building);
```

### 8. Managing Membership
```csharp
community.AddMember(newCharacter);
community.RemoveMember(leavingCharacter);
```

### 9. Territory (Zones)
```csharp
Zone campZone = CommunityManager.Instance.EstablishCommunityZone(
    community,
    spawnPosition,
    ZoneType.Camp,
    "Main Camp"
);
```

### 10. Community Levels
```csharp
community.ChangeLevel(CommunityLevel.Village);
```

## Architecture notes
- **`Community`**: Server-only data class (not a MonoBehaviour, not a NetworkBehaviour). Membership list, leader list, zone list, level.
- **`CommunityManager`**: Singleton MonoBehaviour — global registry + dynamic zone instantiator.
- **`CharacterCommunity`**: Character child component — wraps founding gate, membership ref, citizenship ref.
- **`CommunityData`** / **`MapRegistry`**: Persistent save-layer. `LeaderIds : List<string>` (UUIDs) is the save-safe representation of the leader list. Clients read leadership via `CommunityData`, never via a direct `Community` reference.
- **`Zone`**: Physical `BoxCollider` trigger in the world representing community territory.

## Common pitfalls
- **Multi-leader auth trap**: never use `Community.PrimaryLeader == character` or `leaders[0] == character` for auth. Use `Community.IsLeader(character)`. Until a second leader is promoted, both produce the same result — the bug is latent. See [`wiki/gotchas/singular-leader-vs-multi-leader-isleader.md`](../../wiki/gotchas/singular-leader-vs-multi-leader-isleader.md).
- **Founding gate**: `CheckAndCreateCommunity` only blocks if the character is already a primary leader. Old code that checks `canCreateCommunity` trait or friend count is stale.
- **`Community.Citizens` is a live scan**: do not call in a hot path; wrap in a cache if needed.
- **Clients cannot read `Community` directly**: `Community` is server-only. Push data to clients via `CommunityData` fields in the save-sync path.

## Change log
- (date unknown) — Initial skill file. Founding, hierarchy, membership, zones, leader blueprints documented.
- 2026-04-21 — Phase 1 refactor notice added (`CommunityTracker` → `MapRegistry`, cluster-promotion deleted).
- 2026-05-17 — Multi-leader migration: `leader` → `leaders : List<Character>`; `SetLeader` deleted; `IsLeader(c)`, `PromoteToSecondaryLeader`, `DemoteFromLeadership`, `TransferPrimaryLeadership` added. Citizenship (`SetCitizenship`, `RenounceCitizenship`, `Community.Citizens`). Founding gate simplified (no trait / 4-friends). Public API table added. Common pitfalls section updated.
