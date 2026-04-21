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

> ⚠️ **Pending Phase 1 refactor — see [[adr-0001-living-world-hierarchy-refactor]].**
> `CommunityTracker` is being renamed to `MapRegistry`. NPC-cluster auto-promotion (`EvaluatePopulations`, `PromoteToSettlement`, the `RoamingCamp → Settlement → EstablishedCity` lifecycle) is being deleted. `CommunityData` stays, but is no longer created by cluster detection — only by `MapController.EnsureCommunityData` or explicit `MapRegistry.CreateMapAtPosition` calls. Sections below describe the **pre-refactor** state.

## Summary
Social + territorial grouping of characters. Hierarchical (Kingdom > Duchy > Village); members identified by UUID. `CommunityTracker` runs a server heartbeat evaluating state promotions: Roaming Camp → Settlement → Established City → Abandoned City → Reclaimed. Triggers map birth via `WorldOffsetAllocator` + open-world stamping (physical chunk at cluster centroid).

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

## Sources
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md)
- [[world]] parent §6.
