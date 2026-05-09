---
type: system
title: "Player / AI Nav Switch"
tags: [ai, player, control, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[ai]]", "[[character]]", "[[character-movement]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: npc-ai-specialist
secondary_agents: ["character-system-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterControllers/"
depends_on: ["[[character]]"]
depended_on_by: ["[[ai]]"]
---

# Player / AI Nav Switch

## Summary
Switching a character between manual (player) control and AI (NPC) control without state loss. `Character.SwitchToPlayer` swaps controllers + rebinds `UI_PlayerHUD`; `Character.SwitchToNPC` reverts and re-enables NavMeshAgent. See [[character]] §4.

## Rules
- BT does **not** tick when controller is `PlayerController`.
- `ForceNextTick` after unfreezing to skip the 5-frame stagger.
- HUD binding happens inside `SwitchToPlayer`; if HUD missing, equipment notifications fail silently.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/player-ai-nav-switch/SKILL.md](../../.agent/skills/player-ai-nav-switch/SKILL.md)
- [[character]] parent §4.
