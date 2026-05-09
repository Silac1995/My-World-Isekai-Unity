---
type: system
title: "Character Stats"
tags: [character, stats, combat, tier-2]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[character-skills]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents:
  - combat-gameplay-architect
owner_code_path: "Assets/Scripts/Character/CharacterStats/"
depends_on:
  - "[[character]]"
depended_on_by:
  - "[[combat]]"
  - "[[character-needs]]"
  - "[[character-progression]]"
  - "[[ai]]"
---

# Character Stats

## Summary
Three-tier stat model. **Primary** stats are dynamic runtime values that change moment-to-moment (Health, Stamina, Mana, Initiative). **Secondary** stats are the six base characteristics the player/AI allocates unspent points into (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma). **Tertiary** stats are derived, read-only formulas over the secondary set (PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance, etc.). Combat reads tertiaries, stamina/mana gate abilities, Initiative drives the tick-ready check.

## Purpose
Keep the math that drives every numeric gameplay effect — damage, speed, hit chance, stamina cost, mana cost, ability gating — in one place with a clear separation between inputs (secondary), derived readouts (tertiary), and live moving values (primary).

## Responsibilities
- Holding primary runtime amounts with min/max and change events (`OnAmountChanged`).
- Holding secondary allocations (base value + allocated points).
- Computing tertiary stats as pure derived formulas.
- Auto-allocating secondary points for NPCs (`AutoAllocateStats`) evenly across the 6 stats.
- Exposing `SpendStatPoint(stat)` for player UI allocation.
- Saving secondary values to the character profile (tertiary is rederived on load).

**Non-responsibilities**:
- Does **not** compute combat damage — [[combat]] reads tertiary values into its formula.
- Does **not** own skills/crafting XP — see [[character-skills]].
- Does **not** award combat XP — see [[combat]] (`TakeDamage` centralizes XP awarding).

## Key classes / files

- `Assets/Scripts/Character/CharacterStats/CharacterStats.cs` — root component on a child GameObject.
- `Assets/Scripts/Character/CharacterStats/PrimaryStat.cs`, `SecondaryStat.cs`, `TertiaryStat.cs` — tiered classes.
- `Assets/Scripts/Character/CharacterStats/StatType.cs` — enum or string identifier.
- `Assets/Scripts/Character/CharacterStats/StatRestoreEntry.cs` — per-value restore contract used by abilities.
- `Assets/Scripts/Character/CharacterStats/StatRestoreProcessor.cs` — applies restores (from abilities, consumables).

## Public API

- `character.Stats.Health.CurrentAmount` / `MaxValue` / `OnAmountChanged`.
- `character.Stats.Initiative.CurrentAmount` — drives `CharacterCombat.IsReadyToAct`.
- `character.Stats.Strength.GetValue()` — secondary read.
- `character.Stats.PhysicalPower.GetValue()` — tertiary read (computed).
- `character.Stats.SpendStatPoint(StatType)` — player allocation.
- `character.Stats.AutoAllocateStats()` — NPC allocation at level-up.
- `StatRestoreProcessor.ApplyRestores(character, IEnumerable<StatRestoreEntry>)` — used by support abilities.

## Dependencies

### Upstream
- [[character]] — component on a child GameObject.

### Downstream
- [[combat]] — damage formula reads `PhysicalPower`, `CriticalHitChance`, `DodgeChance`; Initiative gates attacks; Stamina gates all attacks (basic + abilities); Mana gates spells.
- [[character-needs]] — needs can tick stamina/mana regen.
- [[character-progression]] — level-ups award `_unassignedStatPoints`, auto-heal 30% MaxHP.

## State & persistence

- Primary: live values saved as snapshots on character profile (current amount, max).
- Secondary: allocated values saved; unspent points saved.
- Tertiary: derived on load from secondary; never saved.

## Known gotchas

- **Stamina depletion applies Out of Breath** — permanent-duration status effect (see [[combat-status-effect]]), removed when stamina hits max. Requires `_outOfBreathEffect` assigned on `CharacterStatusManager`.
- **Level-up instant 30% heal** — side effect of `LevelUp()` combat progression. Don't gate encounters on "HP can't recover mid-fight" — it can.
- **NPC auto-allocate is evenly distributed** — no personality-driven weighting yet.
- **Tertiary stats have no setters** — attempting to write one is a design error; change the underlying secondary.

## Open questions

- [ ] Full list of tertiary formulas — not documented inline. Copy from `TertiaryStat.cs` when writing an expanded page.
- [ ] Is there a cap on allocated secondary points per level? Currently 5 per level by default (`_statPointsPerLevel`).

## Change log
- 2026-04-18 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/character-stats/SKILL.md](../../.agent/skills/character-stats/SKILL.md)
- [CharacterStats.cs](../../Assets/Scripts/Character/CharacterStats/CharacterStats.cs)
- [[character]] (parent)
- [[combat]] SKILL §5–§6.
