---
type: system
title: "World Community"
tags: [world, community, hierarchy, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[character-community]]", "[[character-relation]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/Community/"
depends_on: ["[[world]]"]
depended_on_by: ["[[world]]", "[[jobs-and-logistics]]"]
---

# World Community

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

## Sources
- [.agent/skills/community-system/SKILL.md](../../.agent/skills/community-system/SKILL.md)
- [[world]] parent §6.
