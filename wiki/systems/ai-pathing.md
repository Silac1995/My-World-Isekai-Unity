---
type: system
title: "AI Pathing"
tags: [ai, navmesh, pathing, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[character-movement]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/Character/"
depends_on: ["[[ai]]", "[[character-movement]]"]
depended_on_by: ["[[ai]]"]
---

# AI Pathing

## Summary
Path diversification + unreachable-target blacklisting via `CharacterPathingMemory`. Each character remembers targets that failed pathing (e.g. blocked by a door, out of NavMesh) for a day; GOAP actions skip them on planning. Resets on `TimeManager` day change via `OnDestroy`.

## Key classes / files
- [CharacterPathingMemory.cs](../../Assets/Scripts/Character/CharacterPathingMemory.cs).

## Rules
- Use `character.PathingMemory` (via facade) — see [[character]] rule.
- Do **not** persist pathing memory across sessions.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/pathing-system/SKILL.md](../../.agent/skills/pathing-system/SKILL.md)
