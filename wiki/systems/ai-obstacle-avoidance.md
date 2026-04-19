---
type: system
title: "AI Obstacle Avoidance"
tags: [ai, navmesh, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[character-movement]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/Behaviours/"
depends_on: ["[[ai]]"]
depended_on_by: ["[[ai]]"]
---

# AI Obstacle Avoidance

## Summary
Tight-space navigation. Uses `NavMeshAgent.avoidancePriority` + manual repulsion vectors in choke points, plus `CharacterPathingMemory` to bail on unreachable targets. Prevents the "stuck shuffle" when multiple NPCs claim the same tile.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/character-obstacle-avoidance/SKILL.md](../../.agent/skills/character-obstacle-avoidance/SKILL.md)
