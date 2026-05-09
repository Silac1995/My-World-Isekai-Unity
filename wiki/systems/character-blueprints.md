---
type: system
title: "Character Blueprints"
tags: [character, building, city-growth, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[world]]", "[[building]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents: ["world-system-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterBlueprints/"
depends_on: ["[[character]]", "[[building]]"]
depended_on_by: ["[[world]]"]
---

# Character Blueprints

## Summary
Tracks a character's `UnlockedBuildingIds` — the set of buildings they know how to construct. Drives [[world-macro-simulation]] offline city growth: the community leader's blueprints decide what scaffolds spawn in their city.

## Responsibilities
- Holding `UnlockedBuildingIds` set.
- Exposing unlock/check API.
- Extraction + injection by the macro simulator on hibernation/wake.

## Key classes / files
- `Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs`.

## Open questions
- [ ] No SKILL.md — tracked in [[TODO-skills]].
- [ ] Do players gain blueprints the same way as NPCs (rewards, purchases, quests)?

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/world-system/SKILL.md](../../.agent/skills/world-system/SKILL.md) §3.
- [[world]] parent.
