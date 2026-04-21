---
type: system
title: "World Community"
tags: [world, community, hierarchy, tier-2, stub]
created: 2026-04-19
updated: 2026-04-21
sources: []
related: ["[[world]]", "[[character-community]]", "[[character-relation]]", "[[adr-0001-living-world-hierarchy-refactor]]", "[[kevin]]"]
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

## Key classes / files
- `Assets/Scripts/World/Community/Community.cs` — the entity.
- `Assets/Scripts/World/Community/CommunityLevel.cs` — tier enum/state.
- `Assets/Scripts/World/Community/CommunityManager.cs` — registry.
- `CommunityTracker` — server heartbeat (location to verify).

## Character-side counterpart
See [[character-community]] for the per-character adapter that holds the founding gate.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-04-21 — Added pending-refactor notice pointing to [[adr-0001-living-world-hierarchy-refactor]]. — Claude / [[kevin]]
- 2026-04-21 — Refactor implemented. `CommunityTracker` renamed to `MapRegistry`, cluster-promotion deleted, wild-map save/load round-trip working. — Claude / [[kevin]]

## Sources
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md)
- [[world]] parent §6.
