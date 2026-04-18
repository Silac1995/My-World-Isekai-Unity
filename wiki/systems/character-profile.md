---
type: system
title: "Character Profile"
tags: [character, personality, save-load, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[character-relation]]", "[[save-load]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents: ["save-persistence-specialist"]
owner_code_path: "Assets/Scripts/Character/CharacterProfile/"
depends_on: ["[[character]]", "[[character-traits]]"]
depended_on_by: ["[[character-relation]]", "[[save-load]]"]
---

# Character Profile

## Summary
Personality + portable profile data. Exposes `GetCompatibilityWith(Character other)` — the compatibility enum consumed by [[character-relation]] to filter opinion deltas (compatible ×1.5, incompatible ×0.5, inverted signs on conflicts). Also coordinates with `CharacterProfileSaveData` as the portable character file (local JSON) used by [[save-load]].

## Responsibilities
- Holding personality data (traits roll-up, disposition values).
- Computing compatibility vs another character.
- Integrating with `CharacterProfileSaveData` for portable save/load.

## Key classes / files
- `Assets/Scripts/Character/CharacterProfile/CharacterProfile.cs` (inferred).
- Works with `CharacterProfileSaveData` (see [[save-load]]).

## Open questions
- [ ] Exact compatibility scoring formula — SKILL describes outcomes, not math.
- [ ] What personality axes exist? (Openness, extroversion, etc., or custom enum?)
- [ ] No SKILL.md — tracked in [[TODO-skills]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md) §2.
- [[character]] and [[character-relation]] parents.
