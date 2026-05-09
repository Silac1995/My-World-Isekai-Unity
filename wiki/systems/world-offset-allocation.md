---
type: system
title: "World Offset Allocation"
tags: [world, spatial, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[world]]", "[[network]]", "[[save-load]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: world-system-specialist
owner_code_path: "Assets/Scripts/World/"
depends_on: ["[[world]]"]
depended_on_by: ["[[world]]"]
---

# World Offset Allocation

## Summary
`WorldOffsetAllocator` assigns each map a **logical slot ID** and a persistent physical offset (e.g. 40,000 units on the X-axis). Unity NGO's interest management filters packets across these distances naturally. Slots are persisted via `WorldSaveManager`; freed slots enter a **lazy-recycle FreeList with a 30-day cooldown** to prevent stale saves from warping NPCs into the void. Abandoned cities retain their slot permanently.

## Key classes / files
- `Assets/Scripts/World/WorldOffsetAllocator.cs` (conceptual).

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[world]] §7.
