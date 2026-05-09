# Combat Abilities System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add physical ability cast times, status effect suspend conditions, book-based ability learning, support ability infrastructure, and smarter AI ability selection.

**Architecture:** Six independent subsystems built in dependency order. Foundation stats come first (CombatCasting, SpellCasting rename), then ability cast time support, then status effect suspend/resume, then support ability interface + AI, and finally the book system. Each task produces compilable code.

**Tech Stack:** Unity 2022+, C#, Netcode for GameObjects, ScriptableObjects for data, pure C# classes for runtime instances, CharacterSystem MonoBehaviours.

**Spec:** `docs/superpowers/specs/2026-03-27-combat-abilities-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `Assets/Scripts/Character/CharacterStats/Tertiary Stats/SpellCasting.cs` | Renamed from CastingSpeed.cs — Dexterity-linked spell cast speed |
| `Assets/Scripts/Character/CharacterStats/Tertiary Stats/CombatCasting.cs` | New Agility-linked physical ability cast speed |
| `Assets/Scripts/StatusEffect/StatusEffectSuspendCondition.cs` | Serializable struct + ComparisonType enum for suspend conditions |
| `Assets/Scripts/Abilities/Data/IStatRestoreAbility.cs` | Interface for abilities with instant stat restores |
| `Assets/Scripts/Abilities/Data/StatRestoreEntry.cs` | Serializable struct for stat modifications |
| `Assets/Scripts/Abilities/Enums/AbilityPurpose.cs` | Enum: Offensive, Support |
| `Assets/Data/Item/BookSO.cs` | Book ScriptableObject extending MiscSO |
| `Assets/Scripts/Item/BookInstance.cs` | Book runtime instance extending MiscInstance |
| `Assets/Scripts/Character/CharacterBookKnowledge.cs` | CharacterSystem tracking reading progress |
| `Assets/Scripts/Character/CharacterActions/CharacterReadBookAction.cs` | Continuous reading action |

### Modified Files
| File | Changes |
|------|---------|
| `Assets/Scripts/StatusEffect/EnumStats.cs` | Rename CastingSpeed → SpellCasting, add CombatCasting |
| `Assets/Scripts/Character/CharacterStats/CharacterStats.cs` | SpellCasting rename, add CombatCasting property/create/recalc/GetBaseStat |
| `Assets/Scripts/Character/Race/RaceSO.cs` | Rename casting speed fields, add CombatCasting multiplier/offset |
| `Assets/Scripts/Abilities/Data/SpellSO.cs` | Add _instantCastThreshold, align formula to division-based |
| `Assets/Scripts/Abilities/Runtime/SpellInstance.cs` | Reference SpellCasting instead of CastingSpeed |
| `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs` | Add _baseCastTime, _instantCastThreshold, ComputeCastTime(), implement IStatRestoreAbility |
| `Assets/Scripts/Abilities/Runtime/PhysicalAbilityInstance.cs` | Add ComputeCastTime() |
| `Assets/Scripts/Character/CharacterActions/CharacterPhysicalAbilityAction.cs` | Cast time duration logic, process IStatRestoreAbility |
| `Assets/Scripts/Character/CharacterActions/CharacterSpellCastAction.cs` | Process IStatRestoreAbility |
| `Assets/Scripts/StatusEffect/StatusEffectInstance.cs` | Add virtual Suspend(), Resume() |
| `Assets/Scripts/StatusEffect/StatModifierEffectInstance.cs` | Override Suspend(), Resume(), add _isApplied guard |
| `Assets/Scripts/StatusEffect/PeriodicStatEffectInstance.cs` | Override Suspend(), Resume() |
| `Assets/Scripts/Character/CharacterStatusEffect.cs` | Add suspend condition fields, OnValidate() |
| `Assets/Scripts/Character/CharacterStatusEffectInstance.cs` | Add IsSuspended, anti-chatter guard, suspend/resume in Tick() |
| `Assets/Scripts/Abilities/Data/AbilitySO.cs` | Add _purpose (AbilityPurpose) |
| `Assets/Scripts/AI/CombatAILogic.cs` | Server-only resource-scanning support ability selection |
| `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs` | Filter methods by AbilityPurpose |

---

## Task 1: Rename CastingSpeed → SpellCasting + Add CombatCasting Stat

**Files:**
- Delete: `Assets/Scripts/Character/CharacterStats/Tertiary Stats/CastingSpeed.cs`
- Create: `Assets/Scripts/Character/CharacterStats/Tertiary Stats/SpellCasting.cs`
- Create: `Assets/Scripts/Character/CharacterStats/Tertiary Stats/CombatCasting.cs`
- Modify: `Assets/Scripts/StatusEffect/EnumStats.cs`
- Modify: `Assets/Scripts/Character/CharacterStats/CharacterStats.cs`
- Modify: `Assets/Scripts/Character/Race/RaceSO.cs`
- Modify: `Assets/Scripts/Abilities/Runtime/SpellInstance.cs:40`

- [ ] **Step 1: Update EnumStats.cs**

In `Assets/Scripts/StatusEffect/EnumStats.cs`, rename `CastingSpeed` to `SpellCasting` and add `CombatCasting`:

```csharp
// In the StatType enum, find line ~22:
// OLD: CastingSpeed,
// NEW:
SpellCasting,
CombatCasting,
```

- [ ] **Step 2: Create SpellCasting.cs (renamed from CastingSpeed)**

Create `Assets/Scripts/Character/CharacterStats/Tertiary Stats/SpellCasting.cs`:

```csharp
[System.Serializable]
public class SpellCasting : CharacterTertiaryStats
{
    public SpellCasting(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "SpellCasting";
    }
}
```

- [ ] **Step 3: Create CombatCasting.cs**

Create `Assets/Scripts/Character/CharacterStats/Tertiary Stats/CombatCasting.cs`:

```csharp
[System.Serializable]
public class CombatCasting : CharacterTertiaryStats
{
    public CombatCasting(CharacterStats characterStats, CharacterBaseStats linkedStat, float multiplier, float baseOffset = 0f, float minValue = 0f)
        : base(characterStats, linkedStat, multiplier, baseOffset, minValue)
    {
        statName = "CombatCasting";
    }
}
```

- [ ] **Step 4: Delete old CastingSpeed.cs**

Delete `Assets/Scripts/Character/CharacterStats/Tertiary Stats/CastingSpeed.cs` and its `.meta` file.

- [ ] **Step 5: Update RaceSO.cs**

In `Assets/Scripts/Character/Race/RaceSO.cs`, rename casting speed fields and add CombatCasting fields:

```csharp
// Rename existing fields (~lines 59-60):
// OLD: BaseCastingSpeedOffset, CastingSpeedMultiplier
// NEW:
[Header("SpellCasting")]
public float BaseSpellCastingOffset = 0f;
public float SpellCastingMultiplier = 0.1f;

[Header("CombatCasting")]
public float BaseCombatCastingOffset = 0f;
public float CombatCastingMultiplier = 0.1f;
```

- [ ] **Step 6: Update CharacterStats.cs — field, property, CreateStats, RecalculateTertiaryStats, GetBaseStat, ApplyRaceStats**

In `Assets/Scripts/Character/CharacterStats/CharacterStats.cs`:

1. Rename the field (~line 37) and property (~line 66):
```csharp
// Field (~line 37):
// OLD: [SerializeField] private CastingSpeed castingSpeed;
// NEW:
[SerializeField] private SpellCasting spellCasting;
[SerializeField] private CombatCasting combatCasting;

// Property (~line 66):
// OLD: public CastingSpeed CastingSpeed => castingSpeed;
// NEW:
public SpellCasting SpellCasting => spellCasting;
public CombatCasting CombatCasting => combatCasting;
```

2. In `CreateStats()` (~line 129):
```csharp
// OLD: castingSpeed = new CastingSpeed(this, dexterity, 1f);
// NEW:
spellCasting = new SpellCasting(this, dexterity, 1f);
combatCasting = new CombatCasting(this, agility, 1f);
```

3. In `RecalculateTertiaryStats()` (~line 201):
```csharp
// OLD: castingSpeed.UpdateFromLinkedStat();
// NEW:
spellCasting.UpdateFromLinkedStat();
combatCasting.UpdateFromLinkedStat();
```

4. In `GetBaseStat()` (~line 231):
```csharp
// OLD: StatType.CastingSpeed => castingSpeed,
// NEW:
StatType.SpellCasting => spellCasting,
StatType.CombatCasting => combatCasting,
```

5. In `ApplyRaceStats()` (~line 175):
```csharp
// OLD: CastingSpeed.UpdateScaling(race.CastingSpeedMultiplier, race.BaseCastingSpeedOffset);
// NEW:
spellCasting.UpdateScaling(race.SpellCastingMultiplier, race.BaseSpellCastingOffset);
combatCasting.UpdateScaling(race.CombatCastingMultiplier, race.BaseCombatCastingOffset);
```

- [ ] **Step 7: Update SpellInstance.cs**

In `Assets/Scripts/Abilities/Runtime/SpellInstance.cs` (~line 40):
```csharp
// OLD: float castingSpeed = _owner?.Stats?.CastingSpeed?.CurrentValue ?? 0f;
// NEW:
float castingSpeed = _owner?.Stats?.SpellCasting?.CurrentValue ?? 0f;
```

- [ ] **Step 8: Search for any remaining CastingSpeed references**

Run a project-wide search for `CastingSpeed` (case-sensitive) in all `.cs` files. Fix any remaining references. Common locations: UI scripts, tooltip code, save/load.

- [ ] **Step 9: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero compilation errors. All CastingSpeed references resolved.

- [ ] **Step 10: Commit**

```
feat: rename CastingSpeed to SpellCasting, add CombatCasting tertiary stat

Renames the Dexterity-linked CastingSpeed to SpellCasting for clarity.
Adds a new Agility-linked CombatCasting tertiary stat for physical
ability cast time reduction. Updates CharacterStats, RaceSO, EnumStats,
and all downstream references.
```

---

## Task 2: Physical Ability Cast Time + Spell Formula Alignment

**Files:**
- Modify: `Assets/Scripts/Abilities/Data/SpellSO.cs`
- Modify: `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs`
- Modify: `Assets/Scripts/Abilities/Runtime/PhysicalAbilityInstance.cs`
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterPhysicalAbilityAction.cs`

- [ ] **Step 1: Add _instantCastThreshold to SpellSO and align formula**

In `Assets/Scripts/Abilities/Data/SpellSO.cs`:

```csharp
// Add field after existing fields:
[SerializeField, Range(0f, 1f)]
[Tooltip("If reduced cast time falls to this fraction of base or below, ability becomes instant. Default 5%.")]
private float _instantCastThreshold = 0.05f;

public float InstantCastThreshold => _instantCastThreshold;
```

Replace the `ComputeCastTime` method (~lines 37-50) with the division-based formula.

> **WARNING:** The old formula uses `baseCastTime * (1 - castingSpeed * 0.01)` (linear reduction, 0-100 range input). The new formula uses `baseCastTime / (1 + spellCastingValue)` (asymptotic, raw stat value). All existing SpellSO assets will have different effective cast times after this change. A QA pass over all spell assets is recommended to retune `_baseCastTime` values.

```csharp
public float ComputeCastTime(float spellCastingValue)
{
    if (_baseCastTime <= 0f) return 0f;

    float reducedTime = _baseCastTime / (1f + spellCastingValue);

    if (reducedTime <= _baseCastTime * _instantCastThreshold)
        return 0f;

    return reducedTime;
}
```

- [ ] **Step 2: Add cast time fields and ComputeCastTime to PhysicalAbilitySO**

In `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs`, add after existing fields:

```csharp
[Header("Cast Time")]
[SerializeField]
[Tooltip("Base cast time in seconds. 0 = instant (no channel).")]
private float _baseCastTime = 0f;

[SerializeField, Range(0f, 1f)]
[Tooltip("If reduced cast time falls to this fraction of base or below, ability becomes instant. Default 10%.")]
private float _instantCastThreshold = 0.10f;

public float BaseCastTime => _baseCastTime;
public float InstantCastThreshold => _instantCastThreshold;

public float ComputeCastTime(float combatCastingValue)
{
    if (_baseCastTime <= 0f) return 0f;

    float reducedTime = _baseCastTime / (1f + combatCastingValue);

    if (reducedTime <= _baseCastTime * _instantCastThreshold)
        return 0f;

    return reducedTime;
}
```

- [ ] **Step 3: Add ComputeCastTime to PhysicalAbilityInstance**

In `Assets/Scripts/Abilities/Runtime/PhysicalAbilityInstance.cs`, add method:

```csharp
// PhysicalData property already exists in this file — do NOT add a duplicate.
// Just add the ComputeCastTime method:
public float ComputeCastTime()
{
    float combatCasting = _owner?.Stats?.CombatCasting?.Value ?? 0f;
    return PhysicalData.ComputeCastTime(combatCasting);
}
```

- [ ] **Step 4: Update CharacterPhysicalAbilityAction duration logic**

In `Assets/Scripts/Character/CharacterActions/CharacterPhysicalAbilityAction.cs`, update the constructor (~lines 14-28):

```csharp
// In the constructor, replace the Duration assignment:
// OLD: Duration = _ability.PhysicalData.AnimationDuration;  (or similar)
// NEW:
float castTime = _ability.ComputeCastTime();
if (castTime > 0f)
{
    // Channeled ability: duration = cast time
    Duration = castTime;
}
else
{
    // Instant: use animation duration (preserve current behavior)
    Duration = _ability.PhysicalData.AnimationDuration;
    // Try to get from animator if available
    if (character.CharacterAnimator != null)
    {
        float animDuration = character.CharacterAnimator.GetMeleeAttackDuration();
        if (animDuration > 0f) Duration = animDuration + 0.1f;
    }
}
```

- [ ] **Step 5: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors. Both SpellSO and PhysicalAbilitySO have aligned ComputeCastTime methods.

- [ ] **Step 6: Commit**

```
feat: add physical ability cast time with Agility scaling

Physical abilities now support _baseCastTime reduced by CombatCasting
(Agility). Per-ability _instantCastThreshold makes fast casters go
instant. SpellSO formula aligned to same division-based pattern.
```

---

## Task 3: Support Ability Infrastructure (AbilityPurpose + IStatRestoreAbility)

**Files:**
- Create: `Assets/Scripts/Abilities/Enums/AbilityPurpose.cs`
- Create: `Assets/Scripts/Abilities/Data/StatRestoreEntry.cs`
- Create: `Assets/Scripts/Abilities/Data/IStatRestoreAbility.cs`
- Modify: `Assets/Scripts/Abilities/Data/AbilitySO.cs`
- Modify: `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs`
- Modify: `Assets/Scripts/Abilities/Data/SpellSO.cs`
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterPhysicalAbilityAction.cs`
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterSpellCastAction.cs`

- [ ] **Step 1: Create AbilityPurpose enum**

Create `Assets/Scripts/Abilities/Enums/AbilityPurpose.cs`:

```csharp
public enum AbilityPurpose
{
    Offensive,
    Support
}
```

- [ ] **Step 2: Create StatRestoreEntry struct**

Create `Assets/Scripts/Abilities/Data/StatRestoreEntry.cs`:

```csharp
using System;
using UnityEngine;

[Serializable]
public struct StatRestoreEntry
{
    public StatType stat;
    [UnityEngine.Tooltip("Amount to restore. Negative values drain the stat.")]
    public float value;
    [UnityEngine.Tooltip("If true, value is a fraction of the stat's max (0-1 range).")]
    public bool isPercentage;
}
```

- [ ] **Step 3: Create IStatRestoreAbility interface**

Create `Assets/Scripts/Abilities/Data/IStatRestoreAbility.cs`:

```csharp
using System.Collections.Generic;

public interface IStatRestoreAbility
{
    IReadOnlyList<StatRestoreEntry> StatRestoresOnTarget { get; }
    IReadOnlyList<StatRestoreEntry> StatRestoresOnSelf { get; }
}
```

- [ ] **Step 4: Add _purpose to AbilitySO base**

In `Assets/Scripts/Abilities/Data/AbilitySO.cs`, add field and property:

```csharp
[Header("Purpose")]
[SerializeField] private AbilityPurpose _purpose = AbilityPurpose.Offensive;

public AbilityPurpose Purpose => _purpose;
```

- [ ] **Step 5: Implement IStatRestoreAbility on PhysicalAbilitySO**

In `Assets/Scripts/Abilities/Data/PhysicalAbilitySO.cs`:

1. Add `IStatRestoreAbility` to class declaration.
2. Add fields and properties:

```csharp
[Header("Stat Restores")]
[SerializeField] private List<StatRestoreEntry> _statRestoresOnTarget = new List<StatRestoreEntry>();
[SerializeField] private List<StatRestoreEntry> _statRestoresOnSelf = new List<StatRestoreEntry>();

public IReadOnlyList<StatRestoreEntry> StatRestoresOnTarget => _statRestoresOnTarget.AsReadOnly();
public IReadOnlyList<StatRestoreEntry> StatRestoresOnSelf => _statRestoresOnSelf.AsReadOnly();
```

- [ ] **Step 6: Implement IStatRestoreAbility on SpellSO**

In `Assets/Scripts/Abilities/Data/SpellSO.cs`:

1. Add `IStatRestoreAbility` to class declaration.
2. Add same fields and properties as Step 5.

- [ ] **Step 7: Add stat restore processing helper**

Create a static helper to avoid duplicating the processing logic in both action classes. Add to `StatRestoreEntry.cs`:

```csharp
public static class StatRestoreProcessor
{
    public static void ApplyRestores(IReadOnlyList<StatRestoreEntry> restores, Character target)
    {
        if (restores == null || target == null) return;

        var stats = target.Stats;
        foreach (var restore in restores)
        {
            var stat = stats.GetBaseStat(restore.stat);
            if (stat == null) continue;

            if (stat is CharacterPrimaryStats primaryStat)
            {
                if (restore.isPercentage)
                {
                    if (restore.value >= 0f)
                        primaryStat.IncreaseCurrentAmountPercent(restore.value);
                    else
                        primaryStat.DecreaseCurrentAmountPercent(Mathf.Abs(restore.value));
                }
                else
                {
                    if (restore.value >= 0f)
                        primaryStat.IncreaseCurrentAmount(restore.value);
                    else
                        primaryStat.DecreaseCurrentAmount(Mathf.Abs(restore.value));
                }
            }
        }
    }
}
```

- [ ] **Step 8: Process IStatRestoreAbility in CharacterPhysicalAbilityAction.OnApplyEffect()**

In `Assets/Scripts/Character/CharacterActions/CharacterPhysicalAbilityAction.cs`, inside `OnApplyEffect()` (~line 68), after existing status effect application logic:

```csharp
// After existing status effect code, add:
if (_ability.Data is IStatRestoreAbility restorer)
{
    if (character.IsServer)
    {
        StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnTarget, _target);
        StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnSelf, character);
    }
}
```

- [ ] **Step 9: Process IStatRestoreAbility in CharacterSpellCastAction.OnApplyEffect()**

In `Assets/Scripts/Character/CharacterActions/CharacterSpellCastAction.cs`, inside `OnApplyEffect()` (~line 51), after existing damage/status logic:

```csharp
// After existing status effect code, add:
if (_spell.Data is IStatRestoreAbility restorer)
{
    if (character.IsServer)
    {
        StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnTarget, _target);
        StatRestoreProcessor.ApplyRestores(restorer.StatRestoresOnSelf, character);
    }
}
```

- [ ] **Step 10: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors. PhysicalAbilitySO and SpellSO inspector now show Stat Restores and Purpose fields.

- [ ] **Step 11: Commit**

```
feat: add support ability infrastructure

Introduces AbilityPurpose enum (Offensive/Support), IStatRestoreAbility
interface, and StatRestoreEntry for instant stat modifications. Physical
and Spell abilities can now heal, drain, or restore any stat on cast.
```

---

## Task 4: Status Effect Suspend Condition

**Files:**
- Create: `Assets/Scripts/StatusEffect/StatusEffectSuspendCondition.cs`
- Modify: `Assets/Scripts/StatusEffect/StatusEffect.cs`
- Modify: `Assets/Scripts/StatusEffect/StatModifierEffectInstance.cs`
- Modify: `Assets/Scripts/StatusEffect/PeriodicStatEffectInstance.cs`
- Modify: `Assets/Scripts/Character/CharacterStatusEffect.cs`
- Modify: `Assets/Scripts/Character/CharacterStatusEffectInstance.cs`

- [ ] **Step 1: Create StatusEffectSuspendCondition struct and ComparisonType enum**

Create `Assets/Scripts/StatusEffect/StatusEffectSuspendCondition.cs`:

```csharp
using System;
using UnityEngine;

public enum ComparisonType
{
    AboveOrEqual,
    BelowOrEqual
}

[Serializable]
public struct StatusEffectSuspendCondition
{
    [Tooltip("Which stat to monitor for the suspend condition.")]
    public StatType statType;

    [Tooltip("The threshold value to compare against.")]
    public float threshold;

    [Tooltip("If true, threshold is a fraction (0-1) of the stat's max. Only valid for Primary stats.")]
    public bool isPercentage;

    [Tooltip("When this comparison is true, the effect suspends.")]
    public ComparisonType comparison;

    public bool Evaluate(CharacterStats stats)
    {
        var stat = stats.GetBaseStat(statType);
        if (stat == null) return false;

        float currentValue;

        if (stat is CharacterPrimaryStats primaryStat)
        {
            if (isPercentage)
                currentValue = primaryStat.MaxValue > 0f
                    ? primaryStat.CurrentAmount / primaryStat.MaxValue
                    : 0f;
            else
                currentValue = primaryStat.CurrentAmount;
        }
        else
        {
            // Secondary/Tertiary: always use absolute value
            currentValue = stat.CurrentValue;
        }

        return comparison == ComparisonType.AboveOrEqual
            ? currentValue >= threshold
            : currentValue <= threshold;
    }
}
```

- [ ] **Step 2: Add virtual Suspend/Resume to StatusEffect base (StatusEffectInstance)**

In `Assets/Scripts/StatusEffect/StatusEffectInstance.cs` (the abstract base class for `StatModifierEffectInstance` and `PeriodicStatEffectInstance`). Currently has: `Apply()`, `Remove()`, `Tick(float)`.

Add:

```csharp
public virtual void Suspend() { }
public virtual void Resume() { }
```

- [ ] **Step 3: Implement Suspend/Resume on StatModifierEffectInstance**

In `Assets/Scripts/StatusEffect/StatModifierEffectInstance.cs`:

Add tracking field and override methods. The actual fields in this class are: `sourceEffect` (StatModifierEffect), `caster` (Character), `target` (Character), `modifiers` (List<StatsModifier>).

```csharp
private bool _isApplied = false;

// In existing Apply() method, wrap with guard at top:
public override void Apply()
{
    if (_isApplied) return;
    _isApplied = true;
    // ... keep existing apply logic unchanged (lines 31-43) ...
}

// In existing Remove() method, wrap with guard at top:
public override void Remove()
{
    if (!_isApplied) return;
    _isApplied = false;
    // ... keep existing remove logic unchanged (lines 47-58) ...
}

public override void Suspend()
{
    if (!_isApplied) return;
    _isApplied = false;
    // Same logic as Remove() — unapply modifiers without destroying instance
    if (target == null || target.Stats == null) return;
    foreach (var mod in modifiers)
    {
        var stat = target.Stats.GetBaseStat(mod.StatType);
        if (stat != null)
            stat.RemoveAllModifiersFromSource(this);
    }
    target.Stats.RecalculateTertiaryStats();
}

public override void Resume()
{
    if (_isApplied) return;
    _isApplied = true;
    // Same logic as Apply() — re-add modifiers
    if (target == null || target.Stats == null) return;
    foreach (var mod in modifiers)
    {
        var stat = target.Stats.GetBaseStat(mod.StatType);
        if (stat != null)
            stat.ApplyModifier(new StatModifier(mod.Value, this));
    }
    target.Stats.RecalculateTertiaryStats();
}
```

- [ ] **Step 4: Implement Suspend/Resume on PeriodicStatEffectInstance**

In `Assets/Scripts/StatusEffect/PeriodicStatEffectInstance.cs`:

```csharp
private bool _isSuspended = false;

public override void Suspend()
{
    _isSuspended = true;
    // Timer is NOT reset — it picks up where it left off on Resume
}

public override void Resume()
{
    _isSuspended = false;
}

// In existing Tick() method, add guard at the top of the processing logic:
// After timer accumulation, before applying the effect:
public override void Tick(float deltaTime)
{
    if (_isSuspended) return;
    // ... existing tick logic (damage/heal pulses) ...
}
```

- [ ] **Step 5: Update CharacterStatusEffect SO**

In `Assets/Scripts/Character/CharacterStatusEffect.cs`, add fields after `maxStacks`:

```csharp
[Header("Suspend Condition")]
[SerializeField]
[Tooltip("Enable to suspend this effect when a stat threshold is met.")]
private bool _hasSuspendCondition = false;

[SerializeField]
private StatusEffectSuspendCondition _suspendCondition;

public bool HasSuspendCondition => _hasSuspendCondition;
public StatusEffectSuspendCondition SuspendCondition => _suspendCondition;

private void OnValidate()
{
    if (maxStacks < 1) maxStacks = 1;

    if (_hasSuspendCondition && _suspendCondition.isPercentage)
    {
        // Only Primary stats support percentage-based thresholds
        bool isPrimary = _suspendCondition.statType == StatType.Health
                      || _suspendCondition.statType == StatType.Mana
                      || _suspendCondition.statType == StatType.Stamina
                      || _suspendCondition.statType == StatType.Initiative;
        if (!isPrimary)
        {
            Debug.LogWarning($"[{statusEffectName}] isPercentage is only valid for Primary stats. Forcing to false.");
            _suspendCondition.isPercentage = false;
        }
    }
}
```

- [ ] **Step 6: Update CharacterStatusEffectInstance — add suspend/resume logic to Tick()**

In `Assets/Scripts/Character/CharacterStatusEffectInstance.cs`:

Add fields:

```csharp
private bool _isSuspended = false;
private float _suspendCheckTimer = 0f;
private const float SUSPEND_CHECK_INTERVAL = 1f;

public bool IsSuspended => _isSuspended;
```

Modify the `Tick(float deltaTime)` method (~line 87). **IMPORTANT:** Preserve the existing order: tick children FIRST, then check duration. The existing code ticks effects before decrementing duration.

```csharp
public bool Tick(float deltaTime)
{
    // 1. Evaluate suspend condition (once per second — anti-chatter)
    if (sourceAsset.HasSuspendCondition && target != null)
    {
        _suspendCheckTimer += deltaTime;
        if (_suspendCheckTimer >= SUSPEND_CHECK_INTERVAL)
        {
            _suspendCheckTimer = 0f;
            bool shouldSuspend = sourceAsset.SuspendCondition.Evaluate(target.Stats);

            if (shouldSuspend && !_isSuspended)
            {
                _isSuspended = true;
                foreach (var effect in statusEffectInstances)
                    effect.Suspend();
            }
            else if (!shouldSuspend && _isSuspended)
            {
                _isSuspended = false;
                foreach (var effect in statusEffectInstances)
                    effect.Resume();
            }
        }
    }

    // 2. Tick child effects (existing logic — only if NOT suspended)
    if (!_isSuspended)
    {
        foreach (var instance in statusEffectInstances)
            instance.Tick(deltaTime);
    }

    // 3. Duration ALWAYS decrements (existing logic, preserved order)
    if (isPermanent) return false;
    remainingDuration -= deltaTime;
    return remainingDuration <= 0;
}
```

- [ ] **Step 7: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors. CharacterStatusEffect inspector shows Suspend Condition toggle.

- [ ] **Step 8: Manual test**

Create a test status effect SO in the editor:
1. Create a new CharacterStatusEffect asset
2. Set duration = 30, add a PeriodicStatEffect that drains 5 HP/sec
3. Enable suspend condition: StatType = Health, threshold = 10, isPercentage = false, comparison = BelowOrEqual
4. Apply to a character in play mode
5. Verify: damage stops when HP hits 10, resumes if healed above 10, fully removed at 30s

- [ ] **Step 9: Commit**

```
feat: add data-driven status effect suspend conditions

Status effects can now suspend (pause applying) when a stat threshold
is met, while duration keeps ticking. Uses 1-second evaluation interval
to prevent oscillation. Supports percentage-based thresholds for Primary
stats and absolute values for all stat types.
```

---

## Task 5: AI Support Ability Selection

**Files:**
- Modify: `Assets/Scripts/AI/CombatAILogic.cs`
- Modify: `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`

- [ ] **Step 1: Add filter helpers to CharacterAbilities**

In `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`, add methods:

The AI helpers in Task 5 (`FindSlotForStat`, `FindSlotWithSelfRestore`) directly iterate `GetActiveSlot(i)` by index — no new helper methods are needed on `CharacterAbilities` for the AI. However, add a filtering method for UI use:

```csharp
/// <summary>
/// Returns all equipped active abilities matching the given purpose.
/// Useful for UI filtering (e.g., show only Support abilities).
/// </summary>
public List<AbilityInstance> GetEquippedAbilitiesByPurpose(AbilityPurpose purpose)
{
    var result = new List<AbilityInstance>();
    for (int i = 0; i < _activeSlots.Length; i++)
    {
        var slot = _activeSlots[i];
        if (slot != null && slot.Data.Purpose == purpose)
            result.Add(slot);
    }
    return result;
}
```

- [ ] **Step 2: Rewrite DecideAbilityOrAttack in CombatAILogic**

In `Assets/Scripts/AI/CombatAILogic.cs` (inside `namespace MWI.AI`), replace `DecideAbilityOrAttack()` (~lines 160-197).

**CRITICAL:** The field is `_self` (not `_character`). The return type is `Func<bool>` wrapping `_self.CharacterCombat.UseAbility(slotIndex, target)` or `_self.CharacterCombat.Attack()`. Must match existing calling convention.

```csharp
private Func<bool> DecideAbilityOrAttack(Character target)
{
    var abilities = _self.CharacterAbilities;
    if (abilities == null)
        return () => _self.CharacterCombat.Attack();

    var stats = _self.Stats;

    // 1. Scan resource pools — find most urgent need
    float hpPercent = stats.Health.CurrentAmount / Mathf.Max(stats.Health.MaxValue, 1f);
    float staminaPercent = stats.Stamina.CurrentAmount / Mathf.Max(stats.Stamina.MaxValue, 1f);
    float manaPercent = stats.Mana.CurrentAmount / Mathf.Max(stats.Mana.MaxValue, 1f);

    StatType? urgentNeed = null;
    float urgentPercent = 1f;

    if (hpPercent < urgentPercent) { urgentNeed = StatType.Health; urgentPercent = hpPercent; }
    if (staminaPercent < urgentPercent) { urgentNeed = StatType.Stamina; urgentPercent = staminaPercent; }
    if (manaPercent < urgentPercent) { urgentNeed = StatType.Mana; urgentPercent = manaPercent; }

    bool isCritical = urgentPercent < 0.20f;
    bool isLow = urgentPercent < 0.40f;

    // 2. Try to find a support ability for the urgent need
    if (urgentNeed.HasValue && isLow)
    {
        float useChance = isCritical ? 0.80f : 0.30f;
        if (UnityEngine.Random.value <= useChance)
        {
            int supportSlot = FindSlotForStat(abilities, AbilityPurpose.Support, urgentNeed.Value, target);
            if (supportSlot >= 0)
            {
                var slot = abilities.GetActiveSlot(supportSlot);
                Character abilityTarget = (slot.Data.TargetType == AbilityTargetType.Self) ? _self : target;
                int idx = supportSlot;
                return () => _self.CharacterCombat.UseAbility(idx, abilityTarget);
            }

            if (isCritical)
            {
                int hybridSlot = FindSlotWithSelfRestore(abilities, urgentNeed.Value, target);
                if (hybridSlot >= 0)
                {
                    int idx = hybridSlot;
                    return () => _self.CharacterCombat.UseAbility(idx, target);
                }
            }
        }
    }

    // 3. Fallback: pick an offensive ability (30% chance) or basic attack
    for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
    {
        var slot = abilities.GetActiveSlot(i);
        if (slot == null || !slot.CanUse(target)) continue;
        if (slot.Data.Purpose != AbilityPurpose.Offensive) continue;

        if (UnityEngine.Random.value < 0.3f)
        {
            int slotIndex = i;
            return () => _self.CharacterCombat.UseAbility(slotIndex, target);
        }
    }

    return () => _self.CharacterCombat.Attack();
}

private int FindSlotForStat(CharacterAbilities abilities, AbilityPurpose purpose, StatType need, Character target)
{
    for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
    {
        var slot = abilities.GetActiveSlot(i);
        if (slot == null || !slot.CanUse(target)) continue;
        if (slot.Data.Purpose != purpose) continue;
        if (slot.Data is IStatRestoreAbility restorer)
        {
            foreach (var r in restorer.StatRestoresOnSelf)
                if (r.stat == need && r.value > 0f) return i;
            foreach (var r in restorer.StatRestoresOnTarget)
                if (r.stat == need && r.value > 0f) return i;
        }
    }
    return -1;
}

private int FindSlotWithSelfRestore(CharacterAbilities abilities, StatType need, Character target)
{
    for (int i = 0; i < CharacterAbilities.ACTIVE_SLOT_COUNT; i++)
    {
        var slot = abilities.GetActiveSlot(i);
        if (slot == null || !slot.CanUse(target)) continue;
        if (slot.Data is IStatRestoreAbility restorer)
        {
            foreach (var r in restorer.StatRestoresOnSelf)
                if (r.stat == need && r.value > 0f) return i;
        }
    }
    return -1;
}
```

- [ ] **Step 3: Ensure DecideAbilityOrAttack caller is server-gated**

In `CombatAILogic.Tick()`, verify that the call to `DecideAbilityOrAttack` is inside a server check. The existing code at ~line 60-67 should already be inside the `_autoDecideIntent` block. Add `_self.IsServer` guard if not present:

```csharp
// In Tick(), before calling DecideAbilityOrAttack:
if (_self.IsServer && _autoDecideIntent)
{
    Func<bool> chosenAction = DecideAbilityOrAttack(currentTarget);
    _self.CharacterCombat.SetActionIntent(chosenAction, currentTarget);
}
```

- [ ] **Step 4: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors.

- [ ] **Step 5: Commit**

```
feat: AI resource-scanning support ability selection

CombatAILogic now scans HP/Stamina/Mana pools for urgency and picks
matching Support abilities. Critical resources (< 20%) trigger 80%
chance, low (< 40%) triggers 30%. Falls back to offensive abilities
with self-restore, then basic attack. Server-only to prevent desync.
```

---

## Task 6: Book System — Data Layer (BookSO + BookInstance)

**Files:**
- Create: `Assets/Data/Item/BookSO.cs`
- Create: `Assets/Scripts/Item/BookInstance.cs`

- [ ] **Step 1: Create BookSO**

Create `Assets/Data/Item/BookSO.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBook", menuName = "Scriptable Objects/Item/Book")]
public class BookSO : MiscSO, IAbilitySource
{
    [Header("Book Content")]
    [SerializeField, TextArea(3, 10)]
    private List<string> _pages = new List<string>();

    [Header("Teaching")]
    [SerializeField]
    [Tooltip("Optional: the ability this book teaches when fully read.")]
    private AbilitySO _teachesAbility;

    [SerializeField]
    [Tooltip("Optional: the skill this book teaches when fully read.")]
    private SkillSO _teachesSkill;

    [Header("Reading")]
    [SerializeField]
    [Tooltip("Total reading progress required to complete the book.")]
    private float _readingDifficulty = 100f;

    [SerializeField]
    [Tooltip("If true, characters can write custom content into this book.")]
    private bool _isWritable = false;

    // Properties
    public IReadOnlyList<string> Pages => _pages.AsReadOnly();
    public AbilitySO TeachesAbility => _teachesAbility;
    public SkillSO TeachesSkill => _teachesSkill;
    public float ReadingDifficulty => _readingDifficulty;
    public bool IsWritable => _isWritable;
    public bool TeachesSomething => _teachesAbility != null || _teachesSkill != null;

    // ItemSO overrides
    public override System.Type InstanceType => typeof(BookInstance);
    public override ItemInstance CreateInstance() => new BookInstance(this);

    // IAbilitySource implementation
    public AbilitySO GetAbility() => _teachesAbility;
    public bool CanLearnFrom(Character learner)
    {
        if (_teachesAbility == null) return false;
        return !learner.CharacterAbilities.KnowsAbility(_teachesAbility);
    }
}
```

- [ ] **Step 2: Create BookInstance**

Create `Assets/Scripts/Item/BookInstance.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BookInstance : MiscInstance
{
    // Identity
    [SerializeField] private string _instanceUid;
    [SerializeField] private string _contentId;

    // Custom book state (for writable books)
    [SerializeField] private List<string> _customPages = new List<string>();
    [SerializeField] private string _customTeachesAbilityId;
    [SerializeField] private string _customTeachesSkillId;
    [SerializeField] private string _authorName;

    // Cached SO references (resolved lazily from IDs)
    [NonSerialized] private AbilitySO _customTeachesAbility;
    [NonSerialized] private SkillSO _customTeachesSkill;
    [NonSerialized] private bool _customRefsResolved;

    public BookInstance(BookSO bookSO) : base(bookSO)
    {
        _instanceUid = Guid.NewGuid().ToString();
        _contentId = bookSO.name; // Pre-authored: contentId = SO asset name
    }

    // Identity
    public string InstanceUid => _instanceUid;
    public string ContentId => _contentId;

    // Resolved properties (custom overrides SO)
    public BookSO BookData => (BookSO)_itemSO;

    public IReadOnlyList<string> Pages =>
        _customPages.Count > 0 ? _customPages.AsReadOnly() : BookData.Pages;

    public AbilitySO TeachesAbility
    {
        get
        {
            ResolveCustomRefs();
            return _customTeachesAbility ?? BookData.TeachesAbility;
        }
    }

    public SkillSO TeachesSkill
    {
        get
        {
            ResolveCustomRefs();
            return _customTeachesSkill ?? BookData.TeachesSkill;
        }
    }

    public string AuthorName => _authorName;
    public bool IsCustomBook => _customPages.Count > 0 || !string.IsNullOrEmpty(_authorName);
    public bool TeachesSomething => TeachesAbility != null || TeachesSkill != null;
    public float ReadingDifficulty => BookData.ReadingDifficulty;

    // Writing
    public void FinalizeWriting(string authorName, List<string> pages,
        AbilitySO teachesAbility = null, SkillSO teachesSkill = null)
    {
        _authorName = authorName;
        _customPages = new List<string>(pages);
        _customTeachesAbility = teachesAbility;
        _customTeachesSkill = teachesSkill;
        _customTeachesAbilityId = teachesAbility != null ? teachesAbility.AbilityId : null;
        _customTeachesSkillId = teachesSkill != null ? teachesSkill.SkillID : null;
        _contentId = Guid.NewGuid().ToString(); // Custom books get unique contentId
        _customRefsResolved = true;
    }

    private void ResolveCustomRefs()
    {
        if (_customRefsResolved) return;
        _customRefsResolved = true;

        if (!string.IsNullOrEmpty(_customTeachesAbilityId))
            _customTeachesAbility = Resources.Load<AbilitySO>($"Data/Abilities/{_customTeachesAbilityId}");
        if (!string.IsNullOrEmpty(_customTeachesSkillId))
            _customTeachesSkill = Resources.Load<SkillSO>($"Data/Skills/{_customTeachesSkillId}");
    }
}
```

Note: Adapt `_itemSO` field name to whatever the actual base class uses for its SO reference.

- [ ] **Step 3: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors. Can create BookSO assets in Unity editor via Create menu.

- [ ] **Step 4: Commit**

```
feat: add BookSO and BookInstance for book items

BookSO extends MiscSO with pages, teaching (ability/skill), reading
difficulty, and writable flag. BookInstance tracks dual identity
(instanceUid for item, contentId for reading progress), supports
custom written content with lazy network reference resolution.
```

---

## Task 7: Book System — CharacterBookKnowledge + ReadBookAction

**Files:**
- Create: `Assets/Scripts/Character/CharacterBookKnowledge.cs`
- Create: `Assets/Scripts/Character/CharacterActions/CharacterReadBookAction.cs`

- [ ] **Step 1: Create BookReadingEntry struct and CharacterBookKnowledge**

Create `Assets/Scripts/Character/CharacterBookKnowledge.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BookReadingEntry
{
    public string contentId;
    public string bookTitle;
    public float currentProgress;
    public float requiredProgress;
    public bool completed;
}

public class CharacterBookKnowledge : CharacterSystem, ISaveable
{
    [SerializeField] private List<BookReadingEntry> _readingLog = new List<BookReadingEntry>();

    // Active reading state
    private CharacterReadBookAction _activeReadingAction;

    private const float BASE_READING_RATE = 10f;
    private const float INTELLIGENCE_COEFFICIENT = 0.05f;

    public IReadOnlyList<BookReadingEntry> ReadingLog => _readingLog.AsReadOnly();
    public bool IsReading => _activeReadingAction != null;

    public BookReadingEntry GetOrCreateEntry(BookInstance book)
    {
        var entry = _readingLog.Find(e => e.contentId == book.ContentId);
        if (entry == null)
        {
            entry = new BookReadingEntry
            {
                contentId = book.ContentId,
                bookTitle = book.ItemName,
                currentProgress = 0f,
                requiredProgress = book.ReadingDifficulty,
                completed = false
            };
            _readingLog.Add(entry);
        }
        return entry;
    }

    public float GetReadingSpeed()
    {
        float intelligence = _character.Stats.Intelligence.Value;
        return BASE_READING_RATE * (1f + intelligence * INTELLIGENCE_COEFFICIENT);
    }

    public bool AddProgress(BookInstance book, float amount)
    {
        var entry = GetOrCreateEntry(book);
        if (entry.completed) return true;

        entry.currentProgress = Mathf.Min(entry.currentProgress + amount, entry.requiredProgress);

        if (entry.currentProgress >= entry.requiredProgress)
        {
            entry.completed = true;
            OnBookCompleted(book);
            return true;
        }
        return false;
    }

    public bool IsCompleted(string contentId)
    {
        var entry = _readingLog.Find(e => e.contentId == contentId);
        return entry != null && entry.completed;
    }

    public float GetProgress(string contentId)
    {
        var entry = _readingLog.Find(e => e.contentId == contentId);
        if (entry == null) return 0f;
        return entry.requiredProgress > 0f ? entry.currentProgress / entry.requiredProgress : 1f;
    }

    private void OnBookCompleted(BookInstance book)
    {
        if (book.TeachesAbility != null && _character.CharacterAbilities != null)
        {
            if (!_character.CharacterAbilities.KnowsAbility(book.TeachesAbility))
            {
                _character.CharacterAbilities.LearnAbility(book.TeachesAbility);
                Debug.Log($"[BookKnowledge] {_character.CharacterName} learned ability: {book.TeachesAbility.AbilityName} from reading.");
            }
        }

        if (book.TeachesSkill != null)
        {
            // Integrate with CharacterSkills system
            Debug.Log($"[BookKnowledge] {_character.CharacterName} learned skill: {book.TeachesSkill.SkillName} from reading.");
            // TODO: Call CharacterSkills.LearnSkill() or equivalent
        }
    }

    // === Reading Tick (called from Update) ===
    public void SetActiveReading(CharacterReadBookAction action) => _activeReadingAction = action;
    public void ClearActiveReading() => _activeReadingAction = null;

    private void Update()
    {
        if (_activeReadingAction != null)
        {
            _activeReadingAction.TickReading(Time.deltaTime);
        }
    }

    // === ISaveable ===
    public string SaveKey => "BookKnowledge";

    public object CaptureState()
    {
        return _readingLog;
    }

    public void RestoreState(object state)
    {
        if (state is List<BookReadingEntry> savedLog)
            _readingLog = savedLog;
    }
}
```

Note: `CharacterSystem` is the project's base MonoBehaviour for per-character systems. Adapt `_character` access pattern to match what `CharacterSystem` provides.

- [ ] **Step 2: Create CharacterReadBookAction**

Create `Assets/Scripts/Character/CharacterActions/CharacterReadBookAction.cs`:

```csharp
using UnityEngine;

public class CharacterReadBookAction : CharacterAction
{
    private readonly BookInstance _book;
    private readonly CharacterBookKnowledge _bookKnowledge;
    private bool _isCompleted;

    public override string ActionName => $"Reading {_book.ItemName}";

    public CharacterReadBookAction(Character character, BookInstance book) : base(character)
    {
        _book = book;
        _bookKnowledge = character.CharacterBookKnowledge;
        Duration = float.MaxValue; // Continuous until cancelled or completed
        _isCompleted = false;
    }

    public override bool CanExecute()
    {
        return _book != null
            && _bookKnowledge != null
            && !_bookKnowledge.IsCompleted(_book.ContentId);
    }

    public override void OnStart()
    {
        // Register with CharacterBookKnowledge so its Update() ticks us
        _bookKnowledge.SetActiveReading(this);
        // TODO: Open book UI, show pages and progress bar
        Debug.Log($"[ReadBook] {character.CharacterName} starts reading '{_book.ItemName}' " +
                  $"(Progress: {_bookKnowledge.GetProgress(_book.ContentId):P0})");
    }

    public void TickReading(float deltaTime)
    {
        if (_isCompleted) return;

        float readingSpeed = _bookKnowledge.GetReadingSpeed();
        bool completed = _bookKnowledge.AddProgress(_book, readingSpeed * deltaTime);

        if (completed)
        {
            _isCompleted = true;
            OnApplyEffect();
            Finish();
        }
    }

    public override void OnApplyEffect()
    {
        // Learning is handled by CharacterBookKnowledge.OnBookCompleted()
        Debug.Log($"[ReadBook] {character.CharacterName} finished reading '{_book.ItemName}'");
    }

    public override void OnCancel()
    {
        // Unregister from tick
        _bookKnowledge.ClearActiveReading();
        // Progress is already saved in CharacterBookKnowledge — nothing lost
        Debug.Log($"[ReadBook] {character.CharacterName} stopped reading '{_book.ItemName}' " +
                  $"(Progress: {_bookKnowledge.GetProgress(_book.ContentId):P0})");
    }
}
```

- [ ] **Step 3: Add CharacterBookKnowledge reference to Character**

In the main `Character.cs` class, add the component reference following the existing pattern for `CharacterAbilities`, `CharacterMentorship`, etc.:

```csharp
[SerializeField] private CharacterBookKnowledge _characterBookKnowledge;
public CharacterBookKnowledge CharacterBookKnowledge => _characterBookKnowledge;
```

- [ ] **Step 4: Compile and verify**

Run: Unity compilation via `assets-refresh` MCP tool.
Expected: Zero errors.

- [ ] **Step 5: Commit**

```
feat: add CharacterBookKnowledge and CharacterReadBookAction

CharacterBookKnowledge tracks reading progress per book (by contentId),
with Intelligence-based reading speed. CharacterReadBookAction is a
continuous action that ticks progress until cancelled or completed.
Book copies share progress via contentId.
```

---

## Task 8: Update SKILL.md Files

**Files:**
- Modify: `.agent/skills/combat_system/SKILL.md`
- Modify: `.agent/skills/status-effect/SKILL.md` (if exists, otherwise look for the status effect skill)
- Create or modify: `.agent/skills/item-system/SKILL.md` (add book system section)

- [ ] **Step 1: Update combat system SKILL.md**

Add sections for:
- Physical ability cast time (CombatCasting stat, ComputeCastTime, instant cast threshold)
- Support abilities (AbilityPurpose, IStatRestoreAbility, StatRestoreEntry)
- AI support ability selection (resource scanning, urgency thresholds)
- Rename CastingSpeed → SpellCasting, new CombatCasting stat

- [ ] **Step 2: Update status effect SKILL.md**

Add section for:
- Suspend condition system (StatusEffectSuspendCondition, ComparisonType)
- Suspend/resume behavior on StatusEffectInstance subclasses
- Anti-chatter guard (1-second evaluation)
- Hardcoded vs data-driven effects distinction

- [ ] **Step 3: Update item system SKILL.md**

Add section for:
- BookSO / BookInstance architecture
- Dual identity system (instanceUid vs contentId)
- CharacterBookKnowledge reading progress tracking
- CharacterReadBookAction continuous action pattern
- Writable books and network serialization strategy

- [ ] **Step 4: Update character stats SKILL.md (if exists)**

Add:
- SpellCasting (renamed from CastingSpeed) — Dexterity-linked
- CombatCasting (new) — Agility-linked

- [ ] **Step 5: Commit**

```
docs: update SKILL.md files for combat abilities system

Documents physical ability cast times, status effect suspend conditions,
book system, support ability infrastructure, and AI improvements.
```

---

## Dependency Order

```
Task 1 (Stats) ──► Task 2 (Cast Time) ──► Task 3 (Support) ──► Task 5 (AI)
                                                                     │
Task 4 (Status Suspend) ◄── independent                             │
                                                                     │
Task 6 (Book Data) ──► Task 7 (Book Knowledge) ◄────────────────────┘
                                                                     │
Task 8 (SKILL.md) ◄─────────────────────────── all tasks complete ───┘
```

Tasks 1→2→3→5 are sequential (each depends on the previous).
Task 4 is independent — can be done in parallel with Tasks 2-3.
Tasks 6→7 are sequential but independent from Tasks 1-5.
Task 8 runs last after everything compiles.
