---
type: system
title: "Character Locations"
tags: [character, locations, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[world]]", "[[kevin]]"]
status: wip
confidence: low
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/"
depends_on: ["[[character]]"]
depended_on_by: ["[[ai]]"]
---

# Character Locations

## Summary
Per-character registry of named world locations this character cares about — home, workplace, social spot, favorite tavern. Consumed by [[ai]] when planning "go home to sleep", "travel to work", etc.

## Responsibilities
- Holding a dictionary of named locations.
- Resolving `GetLocation(name)` to a world position.

## Key classes / files
- [CharacterLocations.cs](../../Assets/Scripts/Character/CharacterLocations.cs).

## Open questions
- [ ] Full location-key enumeration — probably driven by the character's life circumstances.
- [ ] No SKILL.md — tracked in [[TODO-skills]].
- [ ] Saved or rebuilt? Confirm.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [CharacterLocations.cs](../../Assets/Scripts/Character/CharacterLocations.cs).
- [[character]] parent.
