---
type: system
title: "Character Terrain"
tags: [character, terrain, footstep, audio, tier-2, stub, post-merge]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[character]]", "[[terrain-and-weather]]", "[[kevin]]"]
status: planned
confidence: low
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterTerrain/"
depends_on: ["[[character]]", "[[terrain-and-weather]]"]
depended_on_by: ["[[character]]"]
---

# Character Terrain

> **⚠ STUB — code on feature branch.** `Assets/Scripts/Character/CharacterTerrain/` is empty on `multiplayyer`. The character-side terrain interaction layer (footstep resolver, visual effects) lives on `feature/character-archetype-system`. Tracked in [[TODO-post-merge]].

## Summary (provisional)
Character's reactions to the terrain cell beneath them: footstep audio resolution via `FootstepAudioResolver` + `FootstepAudioProfile`, visual effects (dust/splash/snow puff) via `CharacterTerrainEffects`.

## Responsibilities (provisional)
- Resolving the terrain cell under the character each step.
- Selecting an `AudioClip` from the matching `FootstepAudioProfile`.
- Triggering terrain-specific visual effects.

## Open questions
- [ ] Entire page — fill after feature branch merges.

## Change log
- 2026-04-19 — Stub created pre-merge. — Claude / [[kevin]]

## Sources
- Feature branch commits: `e1f99bb feat(terrain): add CharacterTerrainEffects, FootstepAudioResolver, FootstepAudioProfile`.
- [[terrain-and-weather]] parent.
