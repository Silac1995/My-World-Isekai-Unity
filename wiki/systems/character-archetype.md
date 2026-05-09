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

## Partial implementation (terrain-driven)

The [[terrain-and-weather]] Phase 1 work added one field to `CharacterArchetype`:

- `_defaultFootSurface : FootSurfaceType` — enum (`BareSkin`, `Hooves`, `Padded`, `Clawed`, `Scaled`). Used as fallback by [[character-terrain|FootstepAudioResolver]] when the character has no boots equipped. Different from the boot's `ItemMaterial` because creature feet are not items — a wolf's paws aren't "Leather."

See [[character-terrain]] for how this field is consumed.

## Open questions
- [ ] Entire page — fill out after full archetype system (class blueprints, starting skills/stats/visuals) is implemented.
- [ ] Exact scope: is this a character creator / class-select system, or a runtime archetype-swap tool?

## Change log
- 2026-04-19 — Stub created pre-merge. — Claude / [[kevin]]
- 2026-04-19 — Noted `_defaultFootSurface` field added by Phase 1 terrain/weather work. — Claude / [[kevin]]

## Sources
- Feature branch `feature/character-archetype-system`.
- [Assets/Scripts/Character/Archetype/FootSurfaceType.cs](../../Assets/Scripts/Character/Archetype/FootSurfaceType.cs) — enum added by terrain system.
- [[character-terrain]] — consumer.
- [[terrain-and-weather]] — parent system that introduced the field.
- Memory `project_visual_migration_order`.
