---
name: status-effect
description: Architecture and data structure for Character Status Effects, separating the visual/duration wrapper from the specific stat modifiers.
---

# Status Effect System

The Status Effect system uses ScriptableObjects to define buffs, debuffs, and states that can be applied to characters. It is split into a container (`CharacterStatusEffect`) handling duration and visuals, and the effects themselves (`StatusEffect`) handling the actual modifiers.

## When to use this skill
- When creating a new buff, debuff, or temporary state for a character.
- When modifying how status effects affect character stats or visuals.

## The Data-Driven Architecture

The system uses a composition approach using Unity ScriptableObjects to define effects, and a runtime manager on the character to handle their lifecycles.

### 1. CharacterStatusManager (The Controller)
The `CharacterStatusManager` is a `CharacterSystem` component attached to the character. It acts as the central hub for applying, ticking, and removing status effects.
**Rule:** Always apply or remove status effects through the `CharacterStatusManager` (using `ApplyEffect()` and `RemoveEffect()`), so that stats and lifecycles are correctly tracked.
- It instantiates a runtime `CharacterStatusEffectInstance` from the ScriptableObject asset.
- Handles the `Tick(deltaTime)` for all active effects and automatically removes them when their duration expires.
- Contains built-in logic for automatic state-based effects like Out-of-Combat Regeneration and Unconscious Recovery based on `CharacterCombat` and `Health` events.

### 2. CharacterStatusEffect (The Asset Wrapper)
The `CharacterStatusEffect` acts as the container or wrapper for one or more mechanical effects. It is a `ScriptableObject`.
**Rule:** Use `CharacterStatusEffect` to define the player-facing identity of the effect (Name, Icon, Description) and its lifecycle (Duration, Visual Effect).
- Contains a list of `StatusEffect` objects.
- `Duration` of 0 indicates a permanent effect.
- Read-only properties expose the data safely to the runtime system.

### 3. CharacterStatusEffectInstance (The Runtime Instance)
When an effect is applied, the manager creates a `CharacterStatusEffectInstance`.
- Tracks the specific remaining duration and the `caster` of the effect.
- Dispatches the `Apply()` and `Remove()` calls to the character's Stats.

### 4. StatusEffect (The Mechanic)
The `StatusEffect` is an abstract base class (`ScriptableObject`) representing the actual mechanical change (e.g., modifying stats).
**Rule:** Subclass `StatusEffect` to implement specific behaviors or stat modifications.
- Contains a generic `statusName` and a list of `StatsModifier`.

### Existing Components
- `CharacterStatusManager` -> The `Monobehaviour` tracking active effects and automatically handling out-of-combat/unconscious regeneration.
- `CharacterStatusEffect` -> Defines the duration, UI representation (icon, description), and visual effects (prefab), wrapping multiple underlying effects.
- `CharacterStatusEffectInstance` -> The runtime instance of the wrapper tracking time and source.
- `StatusEffect` -> The abstract base defining the mechanical parameter changes (`StatsModifier`).

## Suspend Condition System

Status effects can now **suspend** (pause their modifiers/ticks) when a character stat threshold is met, while the effect's **duration keeps ticking**. This is NOT removal — the effect pauses and can resume.

### Data Layer
- `StatusEffectSuspendCondition` (struct in `StatusEffectSuspendCondition.cs`): Defines `statType`, `threshold`, `isPercentage`, and `ComparisonType` (AboveOrEqual / BelowOrEqual).
- `CharacterStatusEffect` (SO): Has `_hasSuspendCondition` toggle and `_suspendCondition` field. `OnValidate()` enforces that `isPercentage` is only valid for Primary stats (Health/Mana/Stamina/Initiative).

### Runtime Layer
- `StatusEffectInstance` base class: Has `virtual Suspend()` and `virtual Resume()` methods.
- `StatModifierEffectInstance`: Tracks `_isApplied` state. `Suspend()` removes modifiers from stats. `Resume()` re-applies them. Guards prevent double-apply/remove.
- `PeriodicStatEffectInstance`: Tracks `_isSuspended` state. `Tick()` early-returns when suspended. Timer is NOT reset — picks up where it left off.
- `CharacterStatusEffectInstance.Tick()`: Evaluates suspend condition once per second (anti-chatter guard via `SUSPEND_CHECK_INTERVAL = 1f`). When suspended, child effects don't tick but duration always decrements.

### Key Rules
- Duration **always** ticks, even while suspended. The effect will expire normally.
- Anti-chatter: Conditions are only re-evaluated every 1 second to prevent rapid suspend/resume oscillation.
- `isPercentage` compares against `CurrentAmount / MaxValue` for Primary stats. Secondary/Tertiary stats always use absolute `CurrentValue`.
- Hardcoded effects (Out of Breath, Unconscious) use full removal behavior via `CharacterStatusManager`, NOT the suspend system.
