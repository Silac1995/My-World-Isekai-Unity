---
type: system
title: "Character Progression"
tags: [character, progression, xp, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[combat]]", "[[character-stats]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterProgression/"
depends_on: ["[[character]]", "[[character-stats]]"]
depended_on_by: ["[[combat]]"]
---

# Character Progression

## Summary
Tracks combat level history and stat-point allocation. Consumers: combat XP from `TakeDamage` feeds `CharacterCombatLevel`; leveling up awards `_unassignedStatPoints` (default 5) and triggers an instant 30% MaxHP heal. Player allocates manually via UI; NPC auto-allocates evenly.

## Responsibilities
- Tracking `CurrentLevel`, `BaseExpYield`, `CombatLevelEntry` history.
- Awarding unspent points on level-up.
- Triggering instant heal.

## Key classes / files
- `Assets/Scripts/Character/CharacterProgression/CharacterCombatLevel.cs` (inferred).

## Open questions
- [ ] No SKILL.md — tracked in [[TODO-skills]].
- [ ] Is non-combat progression (crafting, social) tracked separately here or in [[character-skills]]?

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[combat]] SKILL §6.
- [[character]] parent.
