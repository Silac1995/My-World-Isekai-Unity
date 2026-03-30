
**Date:** 2026-03-27
**Status:** Draft
**Scope:** Physical/Spell ability cast times, tertiary stat renames, status effect suspend conditions, book-based learning, support abilities, AI support ability selection.

---

## 1. Tertiary Stat Changes

### 1.1 Rename: CastingSpeed → SpellCasting

The existing `CastingSpeed` tertiary stat is renamed to `SpellCasting` to clearly indicate it governs spell cast time reduction.

- **File rename:** `CastingSpeed.cs` → `SpellCasting.cs`
- **Linked stat:** Dexterity (unchanged)
- **Formula:** `BaseOffset + (Dexterity.CurrentValue * Multiplier)` (unchanged)
- All references update to `SpellCasting`:
  - `CharacterStats.cs`: property `CastingSpeed` → `SpellCasting`
  - `CharacterStats.GetBaseStat()`: switch case `StatType.CastingSpeed` → `StatType.SpellCasting`
  - `SpellSO.cs` and `Runtime/SpellInstance.cs`: stat reads update
  - `RaceSO.cs`: field names update
  - `EnumStats.cs`: `StatType.CastingSpeed` → `StatType.SpellCasting`
  - Any UI code referencing the old name

### 1.2 New Tertiary Stat: CombatCasting

A new tertiary stat governing physical ability cast time reduction.

- **New file:** `CombatCasting.cs` — follows the same `CharacterTertiaryStats` pattern
- **Linked stat:** Agility
- **Formula:** `BaseOffset + (Agility.CurrentValue * Multiplier)`
- **RaceSO:** Add `_combatCastingMultiplier` and `_combatCastingOffset` fields
- **CharacterStats.cs:**
  - New `CombatCasting` property
  - Instantiated in `CreateStats()`
  - `RecalculateTertiaryStats()` calls `CombatCasting.UpdateFromLinkedStat()`
  - `GetBaseStat()`: add `StatType.CombatCasting => combatCasting` case
- **StatType enum:** Add `CombatCasting` entry
- **EnumStats.cs:** Update to include `CombatCasting` in the tertiary section
- **Save/Load:** CombatCasting is tertiary (derived from Agility), so it does not need explicit persistence — it is recalculated on `RestoreState` via `RecalculateTertiaryStats()`

---

## 2. Physical Ability Cast Time

### 2.1 PhysicalAbilitySO Changes

Add cast time support to physical abilities, mirroring SpellSO's existing pattern:

- `float _baseCastTime` — default `0` (instant, no channel). If > 0, the character must channel before the ability fires.
- `float _instantCastThreshold` — range 0–1, default `0.10`. If the Agility-reduced cast time falls to this fraction of the base or below, the ability becomes instant.

New method:
```
float ComputeCastTime(float combatCastingValue)
    reducedTime = _baseCastTime / (1 + combatCastingValue)
    if (reducedTime <= _baseCastTime * _instantCastThreshold) return 0
    return reducedTime
```

### 2.2 SpellSO Changes

- Add `float _instantCastThreshold` — range 0–1, default `0.05`. Replaces the currently hardcoded 5% check in `ComputeCastTime()`.
- **Formula alignment:** `SpellSO.ComputeCastTime()` must use the same division-based formula as Physical abilities: `baseCastTime / (1 + spellCastingValue)`. If the existing implementation uses a different formula (e.g., `baseCastTime * (1 - reduction)`), it must be migrated to the division-based pattern. This ensures both ability types scale consistently: high stat values provide diminishing returns rather than hitting a hard floor.
- `ComputeCastTime()` updated to read `_instantCastThreshold` instead of a magic number.

### 2.3 Instance & Action Changes

**PhysicalAbilityInstance:**
- Add `ComputeCastTime()` that reads owner's `CombatCasting` stat value and calls `PhysicalAbilitySO.ComputeCastTime(value)`.

**CharacterPhysicalAbilityAction:**
- Duration logic:
  - Compute cast time via `_ability.ComputeCastTime()`
  - If cast time > 0: action duration = cast time. The ability effect resolves at the end of the channel.
  - If cast time = 0 (instant): action duration = `AnimationDuration`. The ability fires at the animation's apply point (current behavior preserved).
  - If cast time > animation duration: the Animator enters a dedicated looping "Channeling" state that plays until the cast timer completes, then transitions into the attack animation. This requires a `Channeling` bool parameter in the Animator Controller. If no channeling state exists for a given combat style, freeze on the last frame of the cast-start animation as a fallback.

### 2.4 Key Distinction

| Concept | Meaning |
|---------|---------|
| `AnimationDuration` | How long the attack animation takes (always plays) |
| `CastTime` | How long the character channels before the ability fires (can be 0) |

---

## 3. Status Effect Suspend Condition

### 3.1 New Struct: StatusEffectSuspendCondition

A serializable struct defining when a status effect should suspend (pause applying its effects).

**Location:** `Assets/Scripts/StatusEffect/StatusEffectSuspendCondition.cs`

**Fields:**
- `StatType statType` — which stat to monitor. **Constraint:** `isPercentage = true` is only valid for Primary stats (Health, Mana, Stamina, Initiative) which have a `MaxValue`. For Secondary/Tertiary stats, `isPercentage` must be false — enforce via `OnValidate()` on the parent SO, logging a warning if misconfigured.
- `float threshold` — the comparison value
- `bool isPercentage` — if true, threshold is a fraction of the stat's max value (0–1 range). If false, absolute value.
- `ComparisonType comparison` — when the condition is met, the effect suspends

### 3.2 New Enum: ComparisonType

- `AboveOrEqual` — suspend when stat >= threshold
- `BelowOrEqual` — suspend when stat <= threshold

### 3.3 CharacterStatusEffect SO Changes

- Add `bool _hasSuspendCondition` — toggle, default false. Controls inspector visibility.
- Add `StatusEffectSuspendCondition _suspendCondition` — the condition data.
- Add `OnValidate()` — clamp `maxStacks` minimum to 1 (cleanup of existing 0-or-1 ambiguity).
- New properties: `bool HasSuspendCondition`, `StatusEffectSuspendCondition SuspendCondition`.

### 3.4 CharacterStatusEffectInstance Runtime Changes

- Add `bool IsSuspended` — read-only property tracking current suspend state.
- **Anti-chatter guard:** Suspend condition is evaluated **once per second** (aligned with `PeriodicStatEffectInstance`'s tick timer), NOT every frame. This prevents rapid oscillation when a stat hovers near the threshold, avoiding per-frame modifier add/remove churn and stat flickering in UI.
- **Tick() behavior:**
  1. Duration **always** decrements, regardless of suspended state.
  2. Once per second: evaluate suspend condition against target's current stats.
  3. If condition met and not already suspended → set `IsSuspended = true`, call `Suspend()` on all child `StatusEffectInstance` objects.
  4. If condition no longer met and currently suspended → set `IsSuspended = false`, call `Resume()` on all children.
  5. If duration expired → full `Remove()` as usual.

### 3.5 StatusEffectInstance Changes

**StatusEffectInstance base class:**
- Add `virtual void Suspend() {}` — default no-op. Subclasses override with real behavior.
- Add `virtual void Resume() {}` — default no-op.
- This allows `CharacterStatusEffectInstance.Tick()` to call `Suspend()`/`Resume()` polymorphically on the `List<StatusEffectInstance>` without type-checking casts.

**StatModifierEffectInstance:**
- Add `Suspend()` — removes modifiers from stats (same logic as `Remove()`) but does NOT destroy the instance.
- Add `Resume()` — re-applies modifiers (same logic as `Apply()`).
- Add `bool _isApplied` — prevents double-apply or double-remove.

**PeriodicStatEffectInstance:**
- Add `Suspend()` — pauses ticking (skips damage/heal pulses while suspended).
- Add `Resume()` — resumes ticking. Timer does NOT reset — picks up where it left off.

### 3.6 Hardcoded System Effects (Unchanged)

The following effects remain hardcoded in `CharacterStatusManager` and do NOT use the suspend system:
- **Out of Breath** — full removal when stamina reaches 100%
- **Unconscious Recovery** — full removal when health reaches 30%
- **Out of Combat Regeneration** — full removal when health reaches 50%

These are system-level mechanics with specific game rules, not data-driven content.

### 3.7 Behavior Example: Poisoned

- Duration: 10 seconds. Threshold: Health ≤ 10 HP (absolute, BelowOrEqual).
- t=0s: Poison applied, ticking damage.
- t=5s: Character drops to 10 HP → poison **suspends** (no more damage ticks).
- t=5s–7s: Duration keeps counting. Poison suspended.
- t=7s: Ally heals character to 15 HP → poison **resumes** ticking damage.
- t=10s: Duration expires → poison fully removed.

---

## 4. Book System

### 4.1 BookSO — Extends MiscSO, Implements IAbilitySource

**Location:** `Assets/Scripts/Item/BookSO.cs` (or `Assets/Data/Item/BookSO.cs` to follow existing SO patterns)

**Fields:**
- `List<string> _pages` — default text content (always present, every book is readable)
- `AbilitySO _teachesAbility` — optional (null = does not teach an ability)
- `SkillSO _teachesSkill` — optional (null = does not teach a skill)
- `float _readingDifficulty` — total reading progress required to "complete" the book (e.g., 100 for simple, 500 for complex tome)
- `bool _isWritable` — if true, this SO serves as a blank book template that characters can write into

**Required overrides (from ItemSO):**
- `CreateInstance()` → returns `new BookInstance(this)` (NOT `MiscInstance`)
- `InstanceType` → returns `typeof(BookInstance)`
- Without these overrides, books would instantiate as `MiscInstance` at runtime, losing all book-specific state.

**IAbilitySource implementation:**
- `GetAbility()` → returns `_teachesAbility`
- `CanLearnFrom(Character learner)` → returns false if ability is null or character already knows it

### 4.2 BookInstance — Extends MiscInstance

**Two layers of identity:**

1. `string _instanceUid` (GUID) — unique per physical book object. For networking, inventory tracking, save/load. Generated on creation.
2. `string _contentId` (string) — identifies the content, not the physical copy.
   - **Pre-authored books:** `_contentId` = `BookSO` asset name/ID. All copies share this.
   - **Custom/written books:** `_contentId` = generated GUID, assigned when the author finishes writing.

**Runtime state for custom books:**
- `List<string> _customPages` — player-written content (overrides SO pages when non-empty)
- `AbilitySO _customTeachesAbility` — set by the writer, overrides SO
- `SkillSO _customTeachesSkill` — set by the writer, overrides SO
- `string _authorName` — who wrote it

**Property resolution (fallback pattern):**
- `Pages` → `_customPages.Count > 0 ? _customPages : _bookSO.Pages`
- `TeachesAbility` → `_customTeachesAbility ?? _bookSO.TeachesAbility`
- `TeachesSkill` → `_customTeachesSkill ?? _bookSO.TeachesSkill`

### 4.3 CharacterBookKnowledge — New CharacterSystem

**Location:** `Assets/Scripts/Character/CharacterBookKnowledge.cs`

Tracks reading progress per book content across sessions.

**Data structure:**
```
[Serializable]
BookReadingEntry {
    string contentId      // matches BookInstance._contentId
    string bookTitle      // display name for UI/save readability
    float currentProgress // 0 to requiredProgress
    float requiredProgress // from BookSO._readingDifficulty
    bool completed        // true when fully read
}
```

**Storage:** `List<BookReadingEntry> _readingLog`

**Key methods:**
- `GetOrCreateEntry(BookInstance book)` — finds existing entry by `_contentId` or creates new one at progress 0.
- `AddProgress(BookInstance book, float amount)` — increments progress. On completion: triggers learning if the book teaches something, marks entry as completed.
- `IsCompleted(string contentId)` — quick check if a book's content has been fully read.
- `GetProgress(string contentId)` — returns current progress fraction (0–1).

**Reading speed:** Intelligence-based. `readingSpeed = baseRate * (1 + intelligence * coefficient)`. Exact values tuned in `RaceSO` or a global config.

**Persistence:** Implements `ISaveable` — reading progress survives save/load.

**Copy behavior:** Character reads Book A (copy 1), reaches 40%. Loses it. Finds Book A (copy 2) → same `_contentId` → resumes at 40%.

**Event cleanup (CLAUDE.md Rule 16):** If `CharacterBookKnowledge` subscribes to any events (e.g., stat change listeners, reading UI callbacks), all subscriptions must be cleaned up in `OnDestroy()`.

### 4.4 CharacterReadBookAction — Extends CharacterAction

**Continuous action integration:** The existing `CharacterAction` base uses `float Duration` for scheduling. For continuous actions, set `Duration = float.MaxValue`. The action controller ticks `OnApplyEffect()` based on progress completion (checked each frame by `CharacterBookKnowledge`), not duration expiry. When reading completes or is cancelled, the action calls `Finish()` explicitly. This pattern can be reused for future continuous actions.

- **Not fixed-duration.** Continuous action that runs until cancelled or book completed.
- Each tick: `CharacterBookKnowledge.AddProgress(book, readingSpeed * deltaTime)`
- `OnCancel()`: progress already saved in `CharacterBookKnowledge` — nothing lost.
- `OnApplyEffect()`: fires when progress reaches 100%.
  - If teaches ability → `CharacterAbilities.LearnAbility(ability)`
  - If teaches skill → corresponding skill learning method
  - If pure text book → just marks as completed (no mechanical effect)
- Opens the book UI to display pages. If the book teaches something, a progress bar visible in UI.

### 4.5 Writing Books

Characters can write into writable books (`BookSO._isWritable = true`):
- A write action populates `BookInstance._customPages`, `_customTeachesAbility`, `_customTeachesSkill`, and `_authorName`.
- The `_contentId` is generated as a GUID when writing is finalized.
- Players write via UI. NPCs could theoretically author books too (future expansion).

### 4.6 Network Serialization

Custom book content must sync over the network when traded, dropped, or inspected by other players:
- **SO references** (`_customTeachesAbility`, `_customTeachesSkill`): Serialized by asset name/ID string. Resolved on the receiving client via `Resources.Load()` from `Resources/Data/Abilities` or equivalent path.
- **Custom pages** (`_customPages`): Synced **lazily** — content is transmitted when a player opens the book to read, not on spawn. This avoids bandwidth cost for books sitting in inventories or on the ground.
- **`_contentId` and `_authorName`**: Synced eagerly as small strings alongside the item's standard network data.
- **`_instanceUid`**: Part of the base item serialization, synced with the item itself.

---

## 5. Support Abilities & AbilityPurpose

### 5.1 AbilityPurpose Enum

New enum on `AbilitySO` base class:

```
AbilityPurpose { Offensive, Support }
```

- `_purpose` field on `AbilitySO` — every ability declares its intent.
- Used by AI, UI filtering, and designer validation.

### 5.2 StatRestoreEntry — Instant Stat Modifications

New serializable struct for immediate stat changes on ability use:

```
[Serializable]
StatRestoreEntry {
    StatType stat       // Health, Mana, Stamina, etc.
    float value         // amount to restore (negative for drains)
    bool isPercentage   // if true, value is fraction of stat's max
}
```

**NOT added to AbilitySO base** — this would bloat PassiveAbilitySO which has its own reaction dispatch path. Instead, use an interface:

**New interface: `IStatRestoreAbility`**
```
IStatRestoreAbility {
    IReadOnlyList<StatRestoreEntry> StatRestoresOnTarget { get; }
    IReadOnlyList<StatRestoreEntry> StatRestoresOnSelf { get; }
}
```

- Implemented by `PhysicalAbilitySO` and `SpellSO` only.
- `PassiveAbilitySO` does NOT implement it — passives use their existing `ReactionEffects` system.
- Action classes (`CharacterPhysicalAbilityAction`, `CharacterSpellCastAction`) check `ability.Data is IStatRestoreAbility restorer` to process stat restores.

**`AbilityPurpose _purpose`** IS added to `AbilitySO` base — all three types benefit from the offensive/support tag (passives can be tagged too for UI filtering).

**Execution order in OnApplyEffect():**
1. Damage (hitbox/projectile — existing)
2. Stat restores (new — immediate HP/Mana/Stamina changes, via `IStatRestoreAbility`)
3. Status effects (existing — duration-based buffs/debuffs)

### 5.3 Ability Composition Example

**"Vampiric Strike"** (Physical, Offensive):
- Deals melee damage (hitbox, damage multiplier)
- `_statRestoresOnSelf`: `{ Health, 0.10, isPercentage: true }` (heals 10% max HP)
- `_statusEffectsOnTarget`: Applies "Weakened" debuff

**"Battle Meditation"** (Physical, Support):
- `_baseCastTime`: 2.0 (channels for 2 seconds, reduced by Agility)
- `_statRestoresOnSelf`: `{ Stamina, 0.30, isPercentage: true }` (restores 30% max Stamina)
- `_statusEffectsOnSelf`: Applies "Focused" buff (+accuracy)

**"Healing Light"** (Spell, Support):
- `_manaCost`: 25
- `_targetType`: SingleAlly
- `_statRestoresOnTarget`: `{ Health, 50, isPercentage: false }` (heals 50 flat HP)

---

## 6. AI Support Ability Selection

### 6.1 CombatAILogic Changes

Replace the current heuristic with a priority-based resource scan.

**Network authority:** `DecideAbilityOrAttack` must only execute on the server (host). The caller must gate with `IsServer` before invoking. This prevents client/server desync from independent random rolls.

**Support ability selection (`DecideAbilityOrAttack`):**

1. **Scan resource pools** — evaluate HP, Stamina, Mana as percentage of max.
2. **Urgency thresholds:**
   - Critical (< 20%): Highest priority. Actively seek a support ability for this stat.
   - Low (< 40%): Medium priority. Use support ability if available and no combat pressure.
   - Fine (>= 40%): No support needed for this stat.
3. **Match support ability to need:**
   - First scan equipped slots tagged `AbilityPurpose.Support` — these are primary candidates.
   - Also scan `Offensive` abilities that have `_statRestoresOnSelf` entries (e.g., Vampiric Strike heals on hit). These are secondary candidates when a resource is Critical and no pure Support ability is available.
   - For each candidate, check via `IStatRestoreAbility` — does `_statRestoresOnSelf` or `_statRestoresOnTarget` address the most urgent need?
     - HP restore abilities prioritized when Health is critical.
     - Stamina restore when Stamina is critical (risk of Out of Breath).
     - Mana restore when Mana is critical and character relies on spells.
4. **Decision weight:**
   - If any resource is Critical → 80% chance to use matching support ability (if available).
   - If any resource is Low → 30% chance.
   - Otherwise → use offensive ability or basic attack.
5. **Ally support:** If ability targets allies (`SingleAlly`, `AllAllies`), scan nearby teammates for critical resources. Prioritize self if both self and ally are critical.

### 6.2 Fallback

If no matching support ability is equipped or all are on cooldown / insufficient resources → fall back to offensive ability or basic attack (existing logic).

---

## 7. File Impact Summary

### New Files
| File | Description |
|------|-------------|
| `CombatCasting.cs` | New tertiary stat (Agility-linked) |
| `StatusEffectSuspendCondition.cs` | Serializable struct for suspend conditions |
| `ComparisonType.cs` | Enum: AboveOrEqual, BelowOrEqual |
| `IStatRestoreAbility.cs` | Interface for abilities with instant stat restores |
| `StatRestoreEntry.cs` | Serializable struct for instant stat changes |
| `AbilityPurpose.cs` | Enum: Offensive, Support |
| `BookSO.cs` | Book ScriptableObject extending MiscSO |
| `BookInstance.cs` | Book runtime instance extending MiscInstance |
| `CharacterBookKnowledge.cs` | CharacterSystem tracking reading progress |
| `CharacterReadBookAction.cs` | Continuous reading action |

### Modified Files
| File | Changes |
|------|---------|
| `CastingSpeed.cs` | Rename to `SpellCasting.cs` |
| `CharacterStats.cs` | Add `CombatCasting` property, create/recalculate calls, `SpellCasting` rename, update `GetBaseStat()` switch |
| `RaceSO.cs` | Add `CombatCasting` multiplier/offset fields |
| `EnumStats.cs` | Rename `CastingSpeed` → `SpellCasting`, add `CombatCasting` to StatType |
| `PhysicalAbilitySO.cs` | Add `_baseCastTime`, `_instantCastThreshold`, `ComputeCastTime()`, implement `IStatRestoreAbility` |
| `SpellSO.cs` | Add `_instantCastThreshold` field, align `ComputeCastTime()` formula, implement `IStatRestoreAbility` |
| `PhysicalAbilityInstance.cs` | Add `ComputeCastTime()` |
| `CharacterPhysicalAbilityAction.cs` | Duration logic for cast time vs animation, process `IStatRestoreAbility` |
| `CharacterSpellCastAction.cs` | Reference `SpellCasting` instead of `CastingSpeed`, process `IStatRestoreAbility` |
| `CharacterStatusEffect.cs` | Add suspend condition fields, `OnValidate()` for maxStacks |
| `CharacterStatusEffectInstance.cs` | Add `IsSuspended`, anti-chatter guard, suspend/resume logic in `Tick()` |
| `StatusEffectInstance.cs` (base) | Add `virtual Suspend()`, `virtual Resume()` |
| `StatModifierEffectInstance.cs` | Override `Suspend()`, `Resume()`, add `_isApplied` guard |
| `PeriodicStatEffectInstance.cs` | Override `Suspend()`, `Resume()` |
| `AbilitySO.cs` | Add `_purpose` (AbilityPurpose) |
| `CombatAILogic.cs` | Server-only resource-scanning support ability selection |
| `CharacterAbilities.cs` | Filter methods by `AbilityPurpose` |
