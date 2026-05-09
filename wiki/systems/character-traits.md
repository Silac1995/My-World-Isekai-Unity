---
type: system
title: "Character Traits"
tags: [character, traits, personality, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[character-profile]]", "[[world]]", "[[kevin]]"]
status: wip
confidence: medium
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterTraits/"
depends_on: ["[[character]]"]
depended_on_by: ["[[world]]", "[[character-profile]]"]
---

# Character Traits

## Summary
Enumerated behavioural traits that modify character decisions and unlock gates. A trait can flip a flag (`canCreateCommunity`), modulate GOAP action costs, or bias AI choice. Traits contribute to the personality profile consumed by [[character-relation]] compatibility math (via [[character-profile]]).

## Responsibilities
- Holding a set of typed traits per character.
- Exposing trait-driven gates (e.g. `canCreateCommunity` read by [[world]] community founding).
- Modulating GOAP action cost per SKILL.

## Key classes / files
- `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` + `TraitSO.cs`.
- See [.agent/skills/character-traits/examples/trait_usage.md](../../.agent/skills/character-traits/examples/trait_usage.md).

## Open questions
- [ ] Full trait enum and their effects — needs enumeration.
- [ ] How traits bias GOAP cost — formula.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/character-traits/SKILL.md](../../.agent/skills/character-traits/SKILL.md)
- [[character]] parent.
