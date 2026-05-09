---
type: system
title: "Combat Styles (data layer)"
tags: [combat, items, data, scriptable-object, tier-2, stub]
created: 2026-04-19
updated: 2026-04-19
sources: []
related: ["[[combat]]", "[[items]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Resources/Data/CombatStyle/"
depends_on: ["[[combat]]", "[[items]]"]
depended_on_by: ["[[combat]]"]
---

# Combat Styles (data layer)

## Summary
ScriptableObject hierarchy defining **how** a character fights. `MeleeCombatStyleSO` (hitbox-based) vs `RangedCombatStyleSO` (projectile-based). Ranged splits into `ChargingRangedCombatStyleSO` (bow) vs `MagazineRangedCombatStyleSO` (gun/crossbow). Each defines `MeleeRange`, `ScalingStat`, `KnockbackForce`, `DamageType`. **Data-only, no runtime logic** per Kevin's Q10 directive.

## Where the files live
- `Assets/Resources/Data/CombatStyle/*.cs` (e.g. `BarehandsStyleSO.cs`).
- `Assets/Prefabs/CombatStyles/` (prefab variants including `CS_BasePrefab`).
- `Assets/Scripts/CombatStyles/` — **empty placeholder folder** (see [[combat]] Open questions — decide to remove or populate).

## Key classes / files
- Base: `CombatStyleSO` (parent).
- Subclasses: `MeleeCombatStyleSO`, `RangedCombatStyleSO`, `ChargingRangedCombatStyleSO`, `MagazineRangedCombatStyleSO`.

## Rule (from Kevin's Q10)
**If runtime logic is ever added here, STOP and flag.** This layer is purely data/ScriptableObjects. Runtime behaviour lives in [[combat]] actions and [[items]] `WeaponInstance`.

## Change log
- 2026-04-19 — Stub. Confirmed data-only per Q10. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §3.A.
- 2026-04-18 conversation with [[kevin]] (Q10).
