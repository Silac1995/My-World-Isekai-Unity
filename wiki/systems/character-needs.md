---
type: system
title: "Character Needs"
tags: [character, needs, ai, tier-2]
created: 2026-04-18
updated: 2026-04-26
sources: []
related:
  - "[[character]]"
  - "[[ai]]"
  - "[[world]]"
  - "[[shops]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents:
  - npc-ai-specialist
owner_code_path: "Assets/Scripts/Character/CharacterNeeds/"
depends_on:
  - "[[character]]"
depended_on_by:
  - "[[ai]]"
  - "[[world]]"
  - "[[items]]"
---

# Character Needs

## Summary
Per-character drives (Hunger, Social, Sleep, etc.) that decay over simulation time and feed GOAP state. Each need is a provider with a current value, decay rate, and thresholds. When a need crosses a threshold it fires events ([[ai]] reads these to select goals like "eat", "sleep", "socialize"). Needs are macro-simulation friendly — the `MacroSimulator` computes offline decay as pure math during map hibernation (see [[world]]).

## Purpose
Give every character a tractable set of drives that AI can plan against, without framelocking decay to Unity's Update. The same need definition runs during live ticks **and** during offline catch-up, ensuring the player returns to a coherent world where NPCs are hungrier / sleepier / lonelier in proportion to time away.

## Responsibilities
- Defining a need (`CharacterNeed` providers) — current value, min/max, decay per in-game day.
- Decaying needs on simulation tick (scaled by [[game-speed-controller]] — Simulation Time).
- Computing offline delta for the `MacroSimulator` catch-up pass.
- Firing threshold events (`OnNeedCritical`, `OnNeedSatisfied`) consumed by [[ai]].
- Satisfying needs via actions (eat, sleep, socialize, buy item) — restore or set to max.
- Providing a registry so new needs can be injected without modifying core code.

**Non-responsibilities**:
- Does **not** decide what action to take — see [[ai]] `GoapAction_*` handles resolution.
- Does **not** own shop buying logic — see [[shops]] for `NeedItem` resolution.
- Does **not** own stamina/mana/HP regeneration — that's [[character-stats]].

## Key classes / files

- `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs` — component on child GameObject.
- `Assets/Scripts/Character/CharacterNeeds/CharacterNeed.cs` — base need provider.
- Specialized subclasses per need type (Hunger, Social, Sleep, …).
- `Assets/Scripts/Character/CharacterNeeds/NeedProvider.cs` — registry pattern entry.
- Referenced by `HibernatedNPCData` for serialization.

## Public API

- `character.Needs.GetNeed<T>()` — typed getter.
- `need.CurrentValue`, `need.MaxValue`, `need.DecayPerDay`.
- `need.Satisfy(amount)` / `need.Set(value)`.
- `need.OnNeedCritical`, `need.OnNeedSatisfied` events.
- `CharacterNeeds.ComputeOfflineDecay(deltaDays)` — used by [[world]] macro-sim.

### NeedHunger (added 2026-04-26)

Phase-decay need (25 per `TimeManager.OnPhaseChanged`, 4× per in-game day → fully empty in 24 h).

- `IsStarving` — true when `CurrentValue == 0`.
- `OnStarvingChanged(bool)` — fired whenever the starving flag transitions.
- `OnValueChanged(float)` — fired on every decay or restore step.
- `IncreaseValue(float)` / `DecreaseValue(float)` — clamped to [0, MaxValue=100].
- `IsLow()` — true at or below 30.
- `TrySubscribeToPhase()` / `UnsubscribeFromPhase()` — defensive TimeManager subscription.
- `SetCooldown()` — rearms the GOAP activation cooldown after eating.

For procedural details (decay formula, GOAP resolver, macro-sim catch-up) see [.agent/skills/character_needs/SKILL.md](../../.agent/skills/character_needs/SKILL.md).

## Data flow

```
TimeManager.CurrentTime01 advances (scaled by GameSpeedController)
       │
       ▼
CharacterNeeds.Tick(delta)
       │
       ├── for each need: current -= rate * delta
       ├── fire OnNeedCritical if threshold crossed
       └── fire OnNeedSatisfied on restore
       │
       ▼
[[ai]] reads need state via GOAP state variables
       │
       ▼
Selects goal (Eat, Sleep, Socialize) → plans action chain
```

Offline (hibernation):
```
Map wakes up
       │
       ▼
MacroSimulator.CatchUp(deltaDays)
       │
       ▼
for each HibernatedNPCData:
       └── CharacterNeeds.ComputeOfflineDecay(deltaDays) — pure math, no Update
```

## Dependencies

### Upstream
- [[character]] — component on a child GameObject.
- [[game-speed-controller]] — ticks run in Simulation Time.

### Downstream
- [[ai]] — GOAP state variables derive from needs; BT social slot triggers on `WantsToSocialize`.
- [[world]] — macro-sim uses offline decay formula.

## State & persistence

- Current value, min/max, decay rate per need — saved on the character profile.
- Decay rates generally live on the need's asset/config, not per-character.
- `HibernatedNPCData` snapshots current values only; decay rate is recomputed from the definition.

## Known gotchas

- **Time base = Simulation Time** — use `Time.deltaTime * GameSpeedController.Scale` (or the managed tick). Never `Time.unscaledDeltaTime` for decay.
- **Offline formula must match online** — the macro-sim integrates over the time delta exactly as the online tick would. Divergence causes "NPC looks surprisingly hungry" on wake.
- **Satisfy saturates** — `Satisfy(x)` caps at `MaxValue`. Clamp on the way in, not on read.
- **Thresholds drive AI** — pick thresholds per need carefully; oscillation at the boundary churns GOAP replans.

## Open questions

- [ ] Full list of concrete needs — scan `CharacterNeeds/` for subclasses when expanding.
- [ ] Do any needs interact (e.g., low sleep caps max HP)? Probably yes in future — flag.

## Change log
- 2026-04-18 — Initial pass. — Claude / [[kevin]]
- 2026-04-26 — added NeedHunger (phase-tick decay, IsStarving event) + FoodSO consumable subtype + GoapAction_GoToFood/Eat — claude

## Sources
- [.agent/skills/character_needs/SKILL.md](../../.agent/skills/character_needs/SKILL.md)
- [.agent/skills/character_needs/examples/need_patterns.md](../../.agent/skills/character_needs/examples/need_patterns.md)
- [Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs](../../Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs)
- [Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs](../../Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs)
- [Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs](../../Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs)
- [Assets/Resources/Data/Item/FoodSO.cs](../../Assets/Resources/Data/Item/FoodSO.cs)
- [Assets/Scripts/Item/FoodInstance.cs](../../Assets/Scripts/Item/FoodInstance.cs)
- [[ai]] and [[world]] (parents-of-interest).
