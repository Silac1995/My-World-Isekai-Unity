---
type: system
title: "Combat AI Logic"
tags: [combat, ai, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[ai]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
secondary_agents: ["npc-ai-specialist"]
owner_code_path: "Assets/Scripts/AI/"
depends_on: ["[[combat]]", "[[character-movement]]"]
depended_on_by: ["[[combat]]", "[[ai]]"]
---

# Combat AI Logic

## Summary
Pure-C# shared brain for both players and NPCs in combat. One tick function runs intent → movement into range → execute. Uses `CharacterCombat.PlannedTarget` as the override for coordinator-assigned targets, so player click/TAB selection is honored by AI movement.

## Phases
1. **Decide intent** — NPC auto-decides (offensive / support via `DecideAbilityOrAttack`); player declares via `UI_CombatActionMenu`.
2. **Move into range + execute** — evaluates `MeleeRange`, X-depth limits, `Z-alignment ≤ 1.6f`. Only calls `ExecuteAction` when perfectly positioned and `IsReadyToAct`.
3. **Tactical pacing** — `CombatTacticalPacer` for step-back, unengaged follow, random safe fallback.

## Key classes / files
- [CombatAILogic.cs](../../Assets/Scripts/AI/CombatAILogic.cs)
- [CombatTacticalPacer.cs](../../Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs)

## See parent
Full flow in [[combat]] and [[combat-abilities]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §2.
