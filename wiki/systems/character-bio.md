---
type: system
title: "Character Bio"
tags: [character, identity, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[save-load]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterBio/"
depends_on: ["[[character]]"]
depended_on_by: ["[[save-load]]"]
---

# Character Bio

## Summary
Character identity data: name (first + last), gender, race, birthday, death date, biography notes. Used by dialogue placeholder tags (`[indexX].getName`), UI display, and save data.

## Responsibilities
- Holding identity fields with change events.
- Exposing `DisplayName`, `Gender`, `Race` accessors.
- Saving to character profile.

## Key classes / files
- `Assets/Scripts/Character/CharacterBio/CharacterBio.cs` (+ supporting data classes).

## Open questions
- [ ] Full field list — needs enumeration from code.
- [ ] Does race drive any mechanical effects, or purely cosmetic/flavor?

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- `Assets/Scripts/Character/CharacterBio/` (7 files).
- [[character]] parent.
