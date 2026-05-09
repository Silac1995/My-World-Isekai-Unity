---
type: system
title: "Character Book Knowledge"
tags: [character, books, abilities, learning, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[items]]", "[[combat]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/"
depends_on: ["[[character]]", "[[items]]"]
depended_on_by: ["[[combat]]"]
---

# Character Book Knowledge

## Summary
Tracks which books a character has read. Books implement `IAbilitySource`, letting the book act as a "teacher" that progresses ability learning without a live [[character-mentorship]] session. Consumed by [[combat-abilities]].

## Responsibilities
- Storing read-state per book (fully read, in progress).
- Exposing learning tick for books (alternate to mentorship).
- Saving to character profile.

## Key classes / files
- [CharacterBookKnowledge.cs](../../Assets/Scripts/Character/CharacterBookKnowledge.cs).

## Open questions
- [ ] No SKILL.md — tracked in [[TODO-skills]].
- [ ] Does reading a book consume a resource (energy/time) or is it purely gated by `IsFree`?

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [[combat]] SKILL §9.H.
- [[character]] parent.
