---
type: system
title: "Visuals"
tags: [visuals, sprites, animation, spine, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[character]]"
  - "[[items]]"
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents: []
owner_code_path: "Assets/Scripts/Character/"
depends_on:
  - "[[character]]"
depended_on_by:
  - "[[character]]"
  - "[[combat]]"
---

# Visuals

## Summary
2D sprites in a 3D environment (project rule #17). Visual abstraction happens via `ICharacterVisual` (the interface every visual backend implements), `IAnimationLayering`, `ICharacterPartCustomization`, and `IBoneAttachment`. Current backend: Unity sprite-based body-parts controller (layered sprites, animator clips). **Planned migration**: Spine 2D (queued behind archetype work — see memory `project_spine2d_migration.md`). Because save data is visual-system-agnostic, the migration does not require touching character persistence.

## Purpose
Keep gameplay logic blind to the rendering backend. Character subsystems call `ICharacterVisual.SetLookTarget`, `PlayAnimation`, `SetPartColor` — the implementation can be sprite-swap today, Spine tomorrow, 3D skinned mesh later, without code churn outside the visual module.

## Responsibilities
- Rendering character sprites / skeletons / particles.
- Applying per-`ItemInstance` color injection via Material Property Block (project rule #25: shader-first, no batching break).
- Animation sync across the network (owner animation + observer replay).
- Body-part customization (clothing layers, hair, face, ...).
- Bone attachment for weapons, accessories, bag sockets.
- Blink, facial expressions, ambient idle cycles.

**Non-responsibilities**:
- Does **not** own character save data — visuals rebuild from the character profile on load.
- Does **not** own combat damage calc — just visual feedback (target indicator, damage numbers).
- Does **not** own UI — see [[player-ui]].

## Components (rough map — to expand)

| Layer | Scripts |
|---|---|
| Sprite / body-part | `CharacterBodyPartsController/` (13 files) |
| Animation | `CharacterAnimator.cs` (root), `AnimationSync/` (2 files) |
| Facial / ambient | `CharacterBlink.cs` |
| Speech bubbles | [[character-speech]] |
| Gender / race visual | `CharacterGender/` |

## Shader-first rule (project rule #25)

Dynamic visual feedback (target indicators, health bars, fade, damage flashes) **must** use Material Property Blocks + shaders. `Image.fillAmount`, `Graphic.color`, and sprite vertex manipulation are forbidden on hot paths — they break batching and cost CPU-to-GPU transfers. Example: the combat target indicator lerps Green→Yellow→Red via `Material.SetFloat("_HealthPercent")` on a custom unlit UI shader.

## Open questions / TODO

- [ ] **Spine 2D migration plan** — own page under `wiki/projects/`. Not yet created.
- [ ] Exact list of `ICharacterVisual` implementers + contract surface.
- [ ] Palette swapping (LUT) vs tint — project rule #25 recommends LUT for customization. Confirm usage.
- [ ] Networked visual state — what syncs (blink? facial?) vs what's local cosmetic only?

## Change log
- 2026-04-19 — Stub. Confidence medium — Spine migration reshapes this page's scope. — Claude / [[kevin]]

## Sources
- [.agent/skills/character_visuals/SKILL.md](../../.agent/skills/character_visuals/SKILL.md)
- [.agent/skills/spine-unity/SKILL.md](../../.agent/skills/spine-unity/SKILL.md) — migration target.
- Memory `project_spine2d_migration`, `project_visual_migration_order`.
- Root [CLAUDE.md](../../CLAUDE.md) rules #17, #25.
