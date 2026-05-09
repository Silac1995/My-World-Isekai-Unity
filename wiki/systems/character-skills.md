---
type: system
title: "Character Skills"
tags: [character, skills, progression, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[character-mentorship]]", "[[jobs-and-logistics]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterSkills/"
depends_on: ["[[character]]"]
depended_on_by: ["[[character-mentorship]]", "[[jobs-and-logistics]]"]
---

# Character Skills

## Summary
Profession / crafting skills (distinct from combat [[combat-abilities]]). Each skill has a tier that gates what NPCs can do in a job (e.g. Blacksmith requires `SmithingTier ≥ n`). XP is awarded via activity (crafting, harvesting, lessons). The specialized overlay for combat weapon expertise lives in `CombatStyleExpertise` on [[combat]].

## Responsibilities
- Holding per-skill XP and derived tier.
- Awarding XP on activity (crafting, harvesting, mentorship).
- Gating job employment (`JobCrafter` requires minimum tier).

## Key classes / files
- `Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs` + `SkillSO.cs` (ScriptableObject definitions).

## Open questions
- [ ] Full skill enum — needs enumeration. Confidence: medium until listed.
- [ ] Interaction with [[character-mentorship]] — teacher skill tier caps student rate?

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/character-skills/SKILL.md](../../.agent/skills/character-skills/SKILL.md)
- [[character]] parent.
