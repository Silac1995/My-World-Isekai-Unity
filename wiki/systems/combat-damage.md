---
type: system
title: "Combat Damage"
tags: [combat, damage, weapons, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[combat]]"
  - "[[items]]"
  - "[[character-stats]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/Character/CharacterCombat/"
depends_on:
  - "[[combat]]"
  - "[[items]]"
  - "[[character-stats]]"
depended_on_by:
  - "[[combat]]"
---

# Combat Damage

## Summary
Damage resolution flows from weapon data + combat style + character stats + target state. **Physical** damage: `PhysicalPower × Style.PhysicalPowerPercentage + Style.BaseDamage + (ScalingStatValue × Style.StatMultiplier)`, with ±30% variance. Damage type precedence: `WeaponSO.DamageType` first, fallback to `CombatStyleSO.DamageType` (usually Blunt for barehands). XP is awarded proportionally to HP depleted, not flat-per-hit, with level-difference scaling (up to +50% vs higher-level, down to −75% vs lower-level). Destructible objects receive damage via the `IDamageable` interface.

## Purpose
Make every hit feel grounded in the weapon, the style, the character's investments, and the target's circumstances — without a giant if/else chain. Keep damage data on `WeaponSO` and `CombatStyleSO` so balancing is a ScriptableObject edit, not a code change.

## Responsibilities
- Computing damage from weapon + style + stats at hit time.
- Applying variance (`Random.Range(0.7, 1.3)`).
- Dispatching damage to `Character.TakeDamage` or `IDamageable.TakeDamage`.
- Awarding XP proportional to HP removed (`hpBefore - hpAfter`).
- Applying level-difference multipliers (boost vs stronger, malus vs weaker).
- Applying kill bonus (+10% on killing blow).

**Non-responsibilities**:
- Does **not** own weapon data — see [[items]].
- Does **not** own stats — see [[character-stats]].
- Does **not** run knockback — see [[character-movement]] `ApplyKnockback`.
- Does **not** select damage type — precedence rule here reads from `WeaponSO` / `CombatStyleSO`.

## Damage types

**Physical**: `Blunt`, `Slashing`, `Piercing`.
**Magical**: `Fire`, `Ice`, `Lightning`, `Holy`, `Dark`.

Weapons use physical; spells typically use magical.

## Formula

```
baseDamage
  = PhysicalPower × Style.PhysicalPowerPercentage
  + Style.BaseDamage
  + (ScalingStatValue × Style.StatMultiplier)

finalDamage = baseDamage × Random.Range(0.7f, 1.3f)
```

`PhysicalPower` is tertiary — see [[character-stats]].
`ScalingStat` is per-style (e.g. Strength for heavy melee, Dexterity for light).

## XP formula

XP is awarded centrally inside `CharacterCombat.TakeDamage` (standard hits, DoTs, spells):

```
hpBefore, hpAfter = damage resolution
hpRemoved = hpBefore - hpAfter

proportion = hpRemoved / target.Stats.Health.MaxValue
xp = target.BaseExpYield × proportion

levelDiff = target.CombatLevel - attacker.CombatLevel
multiplier = clamp(
    levelDiff > 0 : 1 + min(0.50, levelDiff × 0.05) :         // boost up to +50% at +10 levels
    levelDiff < 0 : 1 + max(-0.75, levelDiff × 0.075) :       // malus down to −75% at −10 levels
    1
)

xp = xp × multiplier
xp = killingBlow ? xp × 1.10 : xp
```

Accumulated XP triggers `LevelUp()` — `CombatLevelEntry` logged to history, `_unassignedStatPoints` granted (default 5), **instant 30% MaxHP heal**.

## Destructibles — `IDamageable`

```csharp
public interface IDamageable {
    void TakeDamage(float damage, Character attacker);
    bool CanBeDamaged();
}
```

- `CombatStyleAttack.OnTriggerEnter`:
  1. Try to extract `Character` — if found, normal combat.
  2. Else try `IDamageable` on the collider GameObject — if `CanBeDamaged()`, apply `GetDamage() × Random.Range(0.7, 1.3)`.
- Doors implement via `DoorHealth` — see `door-lock-system`.

## Stamina cost

All basic attacks now consume stamina:
- **Melee**: `3 + PhysicalPower × 0.1`
- **Ranged**: flat `5`

Depleted stamina → **Out of Breath** status effect (see [[combat-status-effect]]): initiative fills slower, −70% physical damage. Removed automatically when stamina hits max.

## Known gotchas

- **DamageType precedence** — always check `WeaponSO.DamageType` first. Barehands fallback to `CombatStyleSO.DamageType`.
- **XP on near-kills** — the proportional formula prevents excessive XP when finishing an enemy with 1 HP left.
- **Kill bonus is compound** — applied on top of the level-difference multiplier, not replacing it.
- **Variance is per-hit** — each strike rolls its own 0.7–1.3. Ensemble variance over a fight is noticeable.
- **Destructibles must gate on `CanBeDamaged()`** — returning true during an invulnerability window mis-applies damage.

## Dependencies

### Upstream
- [[combat]] parent.
- [[items]] — `WeaponSO`, `CombatStyleSO`, `WeaponInstance`.
- [[character-stats]] — tertiary `PhysicalPower`, secondary scaling stat.
- [[character-progression]] (child of character) — level tracking.

### Downstream
- [[combat-status-effect]] — Out of Breath application.
- `door-lock-system` — `DoorHealth` consumer of `IDamageable`.

## State & persistence

- Per-character `CombatLevel`, `BaseExpYield`, unspent stat points — saved.
- `CombatLevelEntry` history — saved per character (combat progression log).

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §4, §6, §8, §10.
- [[combat]] parent.
- `Assets/Scripts/Combat/IDamageable.cs`.
