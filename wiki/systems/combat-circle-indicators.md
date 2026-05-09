---
type: system
title: "Combat Circle Indicators"
tags: [combat, visuals, decal, tier-3, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[visuals]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/BattleManager/"
depends_on: ["[[combat]]"]
depended_on_by: []
---

# Combat Circle Indicators

## Summary
Visual-only, owner-local feature. When the local player joins a battle, colored decals appear under every participant — **blue** for allies, **red** for enemies. Colors are relative to the viewer's team. Uses URP `DecalProjector` with shared materials (no per-instance clones), per-instance opacity via `fadeFactor`. All fade coroutines use `Time.unscaledDeltaTime` (UI-class visual per rule #26).

## Key classes / files
- [BattleGroundCircle.cs](../../Assets/Scripts/BattleManager/BattleGroundCircle.cs) — one `DecalProjector` per participant; fade-in / dim / cleanup.
- [BattleCircleManager.cs](../../Assets/Scripts/BattleManager/BattleCircleManager.cs) — `CharacterSystem`; only active for `IsOwner`; subscribes to battle events.

## Rendering rule
Requires Decal Renderer Feature on the URP renderer assets.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §12.
