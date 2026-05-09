---
type: system
title: "Combat Abilities"
tags: [combat, abilities, spells, passives, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[combat]]"
  - "[[character-stats]]"
  - "[[character-mentorship]]"
  - "[[items]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: combat-gameplay-architect
owner_code_path: "Assets/Scripts/Abilities/"
depends_on:
  - "[[combat]]"
  - "[[character]]"
  - "[[character-stats]]"
  - "[[items]]"
depended_on_by:
  - "[[combat]]"
---

# Combat Abilities

## Summary
Three ability families — **physical** (weapon-bound, Stamina-cost, no cooldown), **spell** (weapon-independent, Mana-cost, cooldown + cast time), **passive** (9 trigger conditions, event-driven reactions). Each has a `SO` base and a runtime `Instance` that tracks cooldown / charge / trigger state. Characters have 6 active slots + 4 passive slots on `CharacterAbilities`. Execution follows the same Owner-predict → Server-validate → Broadcast RPC pattern as basic attacks.

## Purpose
Make abilities a first-class, data-driven layer on top of basic combat without coupling them to specific weapons, stats, or AI decision logic. Let players and NPCs use the same ability mechanics; let support abilities (heals, buffs) slot into the same resource-cost / cast-time / cooldown model as offensive ones.

## Responsibilities
- Defining abilities as ScriptableObjects (`AbilitySO` hierarchy).
- Holding runtime ability state (`AbilityInstance` subclasses).
- Gating use: `CanUse()` checks resources, cooldown, weapon match, trigger conditions.
- Executing through `CharacterPhysicalAbilityAction` / `CharacterSpellCastAction`.
- Running passive trigger evaluation server-side only.
- Driving AI choice via `DecideAbilityOrAttack` resource-scanning heuristics.
- Supporting stat restores via `IStatRestoreAbility`.

**Non-responsibilities**:
- Does **not** teach abilities — see [[character-mentorship]] and books (`IAbilitySource`).
- Does **not** own basic attacks — see [[combat]] / [[combat-damage]].

## Ability types

### `PhysicalAbilitySO`
- **Weapon-bound** — requires specific `WeaponType` equipped.
- **Stamina cost**; **no cooldown**.
- Optional cast time via `_baseCastTime` — reduced by `CombatCasting` (Agility-linked). Default threshold 10% → becomes instant below threshold.
- Learning from two different CombatStyles merges availability when any matching weapon is equipped.

### `SpellSO`
- **Weapon-independent**.
- **Mana cost + cooldown + cast time**.
- Cast time scales with `SpellCasting` (Dexterity-linked) via `baseCastTime / (1 + spellCastingValue)`. Per-ability `_instantCastThreshold` (default 5%).

### `PassiveAbilitySO`
- **Event-triggered** via one of **9 conditions**:
  - `OnDamageTaken`, `OnCriticalHitDealt`, `OnKill`, `OnDodge`, `OnBattleStart`,
  - `OnInitiativeFull`, `OnAllyDamaged`, `OnLowHPThreshold`, `OnStatusEffectApplied`.
- Trigger chance + internal cooldown + reaction effects.

### Support abilities — `IStatRestoreAbility`

Both `PhysicalAbilitySO` and `SpellSO` can implement:
- `AbilityPurpose` enum: `Offensive | Support`.
- `StatRestoreEntry { stat, value, isPercentage }`.
- Lists: `StatRestoresOnTarget`, `StatRestoresOnSelf`.
- Applied by `StatRestoreProcessor.ApplyRestores()` in `OnApplyEffect` (server-side).

## Key classes / files

- `Assets/Scripts/Abilities/Data/AbilitySO.cs` — base.
- `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs`, `SpellSO.cs`, `PassiveAbilitySO.cs`.
- `Assets/Scripts/Abilities/Runtime/` — `PhysicalAbilityInstance`, `SpellInstance`, `PassiveAbilityInstance`.
- `Assets/Scripts/Abilities/Learning/` — `IAbilitySource`, learning progression.
- `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs` — 6 active + 4 passive slots.
- `Assets/Scripts/Character/CharacterActions/` — `CharacterPhysicalAbilityAction`, `CharacterSpellCastAction`.

## Public API

- `character.CharacterAbilities.LearnAbility(AbilitySO)`.
- `character.CharacterAbilities.EquipActive(slot, instance)` / `EquipPassive(slot, instance)`.
- `character.CharacterCombat.UseAbility(slotIndex, target)` — entry; Owner-predict → Server-validate → Broadcast.
- `abilityInstance.CanUse()`, `.TryTrigger()` (passives).

## Execution flow

```
UseAbility(slot, target)
       │
       ▼
instance.CanUse()?
       │   (stamina/mana, cooldown, weapon match, trigger chance)
       │
       ▼
Owner-predicts animation
       │
       ▼
Server validates
       │
       ▼
Broadcast RPC
       │
       ▼
CharacterPhysicalAbilityAction / CharacterSpellCastAction
       │
       ├── Physical: consume stamina in OnStart; spawn hitbox via animation events.
       └── Spell:    consume mana in OnStart; apply effect in OnApplyEffect after cast time.
                     On interrupt (OnCancel): refund mana.
       │
       ▼
Apply stat restores (if IStatRestoreAbility) — server-side
```

Passive trigger evaluation (server-only):
```
Event fires (e.g. OnDamageTaken in CharacterCombat.TakeDamage)
       │
       ▼
for each equipped passive slot:
       │
       ├── trigger condition matches?
       ├── cooldown elapsed?
       └── chance roll passes?
             │
             └── execute reaction effect
```

## AI integration

`CombatAILogic.DecideAbilityOrAttack`:
1. Scan HP/Stamina/Mana for urgency.
2. If a resource is critical (<20%): 80% chance to use matching Support ability.
3. If low (<40%): 30% chance.
4. `FindSlotForStat(needed)` locates Support abilities; `FindSlotWithSelfRestore` finds offensive+self-heal.
5. Else 30% chance offensive ability, else basic attack.
6. Server-only (`_self.IsServer`) to prevent client desync.

## Known gotchas

- **Passives are server-only** — client-side trigger evaluation would desync. Always gate on `IsServer`.
- **`AbilityPurpose` drives AI** — Support abilities won't be selected by offensive branches even if usable. Tag correctly.
- **Cast-time thresholds** — if `(1 + casting) × threshold >= baseCastTime`, the ability becomes instant. Predictable but easy to miss when tuning.
- **Must override `ShouldPlayGenericActionAnimation => false`** on ability `CharacterAction`s.
- **Teaching vs equipping** — learning an ability adds it to known list; equipping moves an instance into an active/passive slot. Only equipped passives fire.

## Dependencies

### Upstream
- [[combat]] parent.
- [[character]] — hosts `CharacterAbilities`.
- [[character-stats]] — reads Stamina, Mana, Dexterity (spell casting), Agility (combat casting).
- [[items]] — `WeaponType` gate for physical abilities.

### Downstream
- [[combat-damage]] — ability hitboxes apply damage via the same pipeline.
- [[character-mentorship]] — `AbilitySO` branch teaches abilities.
- `character-book-knowledge` — books implement `IAbilitySource`.

## State & persistence

- Known abilities + equipped slots + passive slot assignments — saved on character profile.
- Runtime state (`_remainingCooldown`, passive internal cooldowns) transient; reset on load.

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) §9.A–H.
- [[combat]] parent.
- `Assets/Scripts/Abilities/`.
