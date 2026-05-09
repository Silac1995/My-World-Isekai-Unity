---
type: system
title: "Combat Status Effect"
tags: [combat, status-effect, buff, debuff, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[character-stats]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/StatusEffect/"
depends_on: ["[[combat]]", "[[character-stats]]"]
depended_on_by: ["[[combat]]"]
---

# Combat Status Effect

## Summary
Timed modifications to a character's stats. Two families: `StatModifierEffect` (flat/percent changes to tertiary stats while active) and `PeriodicStatEffect` (tick-based — DoTs, HoTs, Out of Breath). Applied and cleared by `CharacterStatusManager`.

## Key classes / files
- [Assets/Scripts/StatusEffect/](../../Assets/Scripts/StatusEffect/)
  - `StatModifierEffect.cs`, `StatModifier.cs`
  - `PeriodicStatEffect.cs`, `PeriodicStatEffectInstance.cs`
  - `EnumStats.cs`

## Special cases
- **Out of Breath** — permanent-duration effect on stamina depletion; removed when stamina hits max. Requires `_outOfBreathEffect` assigned on `CharacterStatusManager`.

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/status-effect/SKILL.md](../../.agent/skills/status-effect/SKILL.md)
- [[combat]] and [[combat-damage]].
