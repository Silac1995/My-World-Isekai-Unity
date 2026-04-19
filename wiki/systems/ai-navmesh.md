---
type: system
title: "AI NavMesh"
tags: [ai, navmesh, network, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[character-movement]]", "[[network]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
secondary_agents: ["network-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterMovement/"
depends_on: ["[[ai]]", "[[character-movement]]", "[[network]]"]
depended_on_by: ["[[ai]]"]
---

# AI NavMesh

## Summary
`NavMeshAgent` authority model + multiplayer concerns. Server owns pathfinding for NPCs; players use `ClientNetworkTransform` for owner-authoritative movement. Speed scales with combat mode (see [[character-movement]]).

## Key rules
- Host/server computes NPC pathing.
- Player movement is owner-authoritative; server validates.
- Cross-NavMesh teleports require `ForceWarp` (disable agent, warp via `transform.position`, re-enable).

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/navmesh-agent/SKILL.md](../../.agent/skills/navmesh-agent/SKILL.md)
