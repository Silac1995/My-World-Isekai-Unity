---
type: system
title: "Character Archetype"
tags: [character, archetype, class, tier-2, stub, post-merge]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[visuals]]", "[[kevin]]"]
status: planned
confidence: low
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/Archetype/"
depends_on: ["[[character]]"]
depended_on_by: ["[[character]]"]
---

# Character Archetype

> **⚠ STUB — code on feature branch.** `Assets/Scripts/Character/Archetype/` is empty on `multiplayyer`. The archetype system (Warrior, Mage, etc. class blueprints) lives on `feature/character-archetype-system`. Tracked in [[TODO-post-merge]].

## Summary (provisional)
Class archetype blueprints (Warrior, Mage, Rogue, etc.) that seed a character's starting skills, stats, combat style, visuals, and default abilities. Precursor to the planned Spine 2D migration (see `project_spine2d_migration` memory).

## Responsibilities (provisional)
- Defining archetypes as ScriptableObjects.
- Seeding starting values on character creation.
- Integrating with [[visuals]] for sprite / body-part pack selection.

## Open questions
- [ ] Entire page — fill after feature branch merges.
- [ ] Exact scope: is this a character creator / class-select system, or a runtime archetype-swap tool?

## Change log
- 2026-04-19 — Stub created pre-merge. — Claude / [[kevin]]

## Sources
- Feature branch `feature/character-archetype-system`.
- Memory `project_visual_migration_order`.
