---
type: system
title: "Combat Engagement"
tags: [combat, engagement, formation, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[character-movement]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/BattleManager/"
depends_on: ["[[combat]]"]
depended_on_by: ["[[combat]]"]
---

# Combat Engagement

## Summary
Spatial sub-fight grouping. `CombatEngagementCoordinator` maintains a targeting graph, merges nearby fights, and splits overcrowded ones. Each `CombatEngagement` owns two `EngagementGroup`s (one per team), and each group owns a `CombatFormation` that positions role-based slots (front-line, flanker, back-line) organically.

## Key classes / files
- `CombatEngagementCoordinator.cs`
- `CombatEngagement.cs`, `EngagementGroup.cs`, `CombatFormation.cs`.

## Open questions
- [ ] No dedicated SKILL.md — covered partially in `combat_system` SKILL. Tracked in [[TODO-skills]].
- [ ] Formation weights per role — enumerate from code.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[combat]] and [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §1.
