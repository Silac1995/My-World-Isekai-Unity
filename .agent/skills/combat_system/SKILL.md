---
name: combat-system
description: Architecture, flow, and integration of the combat system (BattleManager, CharacterCombat, Initiative, Stats).
---

# Combat System

This skill details the architecture of the combat system in the project and the rules to follow when extending or debugging it. The combat system relies on concepts like **Initiative Ticks**, **Engagement Groups**, and strict role separation between the global Manager and local components.

## When to use this skill
- To add a new combat-related feature (e.g., AoE attack, fleeing, new combat buffs/debuffs).
- To interact with characters' attack delay (`Initiative`).
- In case of bugs where combat doesn't end, or a character is frozen without attacking.
- When adding or modifying combat-related statistics (in `CharacterStats`).

## How to use it

### 1. The BattleManager (Global Management)
The `BattleManager` is the supreme entity of a battle, usually instantiated when a clash begins. It strictly delegates its responsibilities to adhere to SOLID principles:
- **BattleTeams**: It always maintains two teams (Initiator vs Target). We do *not* support 3-team free-for-alls in a single instance.
- **BattleZoneController**: A delegated pure C# class that handles the physical terrain. It dynamically generates the boundary (`BoxCollider` isTrigger), pathfinding deterrent (`NavMeshModifierVolume`), and visual `LineRenderer` to mark the combat zone.
- **CombatEngagementCoordinator**: A delegated pure C# class that mathematically manages brawl "subgroups" (`_activeEngagements`). It computes spatial centers, merges nearby fights, and safely splits massive crowds to prevent actor overlapping.
- **Victory Condition**: The manager continuously polls `.IsTeamEliminated()` in its `Update()` loop. This physical guarantee ensures the battle definitively ends if an entire team is wiped out or silently despawned, rather than relying exclusively on volatile event triggers.
- **Robust Teardown**: Upon ending, the manager wraps `LeaveBattle` calls in a `try-catch` block to quarantine aggressive UI exceptions (like `PlayerUI` crashing) from aborting the shutdown script. It also unsubscribes all character events explicitly in `OnDestroy()` to prevent zombie memory leaks.
- **Tick System**: It is the `BattleManager` that sets the pace (`PerformBattleTick()`), and *not the Update method of each character*.

### 2. CharacterCombat (Local Logic)
This is the component every NPC/Player has in order to fight.
- **Combat Mode**: A character switches to "CombatMode" (and draws their weapon) if they intend to attack. There is a `COMBAT_MODE_TIMEOUT` (default 7 seconds).
- **Consumption & Initiative Tick**:
  - The `.IsReadyToAct` method checks if the Initiative (in Stats) is full.
  - The `.ConsumeInitiative()` method resets initiative to 0 after a successful attack.
  - The `.UpdateInitiativeTick(amount)` method is **called by the BattleManager** to fill the bar.
- **Action Intent & Execution (`CombatAILogic.cs`)**: Actions are no longer executed blindly by UI buttons or Behaviour Trees.
  - `SetActionIntent(Action, target)` logs what the character *intends* to do.
  - `CombatAILogic.Tick(target)` is the shared brain for both Players and NPCs. It handles all tactical pacing. It moves the character into valid strike range (evaluating `MeleeRange`, X-depth limits `< 1.5f`, and Z-alignment `<= 1.5f`) and ONLY calls `ExecuteAction` when perfectly positioned and `IsReadyToAct` is true.
  - While waiting for Initiative, `CombatAILogic` pulls random safe fallback points using `CombatTacticalPacer.GetTacticalDestination()`.
  - For standard hits, NPCs automatically pull intents when ready. Players strictly declare intents via `UI_CombatActionMenu`.

### 3. Weapons & Styles (3-Layer Architecture)

The system is split into three distinct layers to separate static data, runtime state, and fight mechanics.

#### A. Static Data (`WeaponSO` & `CombatStyleSO`)
- **`WeaponSO`**: Defines the item properties.
  - `WeaponCategory` (Melee vs Ranged).
  - `DamageType` (Slashing, Piercing, Blunt). *This is the primary source of damage type.*
  - Max stats (`MaxDurability`, `MaxSharpness`, `MagazineSize`).
- **`CombatStyleSO`**: Defines how the character fights.
  - **Hierarchy**: `MeleeCombatStyleSO` (hitbox-based) vs `RangedCombatStyleSO` (projectile-based).
  - **Ranged Subtypes**: `ChargingRangedCombatStyleSO` (Bow) vs `MagazineRangedCombatStyleSO` (Gun/Crossbow).
  - Defines `MeleeRange`, `ScalingStat`, and `KnockbackForce`.

#### B. Runtime State (`WeaponInstance`)
Every equipped weapon has a specialized instance class to track its wear and tear.
- **`MeleeWeaponInstance`**: Tracks `Sharpness`. High sharpness grants bonuses (impl. pending), low sharpness might require sharpening at a forge.
- **`RangedWeaponInstance`**:
  - `ChargingWeaponInstance`: Tracks `ChargeProgress`. Must be 100% to fire.
  - `MagazineWeaponInstance`: Tracks `CurrentAmmo`. Requires a `Reload()` action when empty.

#### C. Combat Actions (`CharacterAction`)
The actual implementation of the attack.
- **`CharacterMeleeAttackAction`**: Triggers animator, spawns a `CombatStyleAttack` (hitbox) via Animation Event.
- **`CharacterRangedAttackAction`**: Spawns a `Projectile` towards the target.

> [!NOTE]
> All combat actions must override **`ShouldPlayGenericActionAnimation`** to return **`false`**. This prevents the generic "busy" animation from overriding the specific `MeleeAttack` or `RangedAttack` triggers managed by the `CharacterAnimator`.

### 4. Damage Resolution Rules
1. **Damage Type**: Always check `WeaponSO.DamageType` first. If no weapon is equipped, use `CombatStyleSO.DamageType` (fallback for barehands, usually Blunt).
2. **Formula**: `PhysicalPower (from Stats) * Style.PhysicalPowerPercentage + Style.BaseDamage + (ScalingStatValue * Style.StatMultiplier)`.
3. **Projectiles**: Use the `Projectile.cs` script. They are physical objects (`Rigidbody`) that apply damage and knockback on `OnTriggerEnter`.

### 5. CharacterStats (Stat Distribution)
Combat massively relies on `CharacterStats`. It is critical to respect its architecture:
- **Primary Stats**: Dynamic (Health, Stamina, Mana, **Initiative**). 
- **Secondary Stats**: Base characteristics (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma).
- **Tertiary Stats**: Derived from secondary ones (PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance, etc.).

### 6. Combat Progression (XP & Leveling)
Combat directly drives character progression via the `CharacterCombatLevel` component (a `CharacterSystem`).
- **Centralized XP**: Experience is strictly awarded inside `CharacterCombat.TakeDamage()` to centralize standard hits, DoTs, and spells.
- **Proportional EXP Acquisition**: Instead of flat per-hit XP, a target yields EXP proportionally to the exact amount of HP depleted relative to their MaxHP. Each character has a `BaseExpYield` (e.g., 50). Stripping 10% of their MAX HP instantly rewards 10% of their `BaseExpYield`. The system explicitly calculates `hpBefore - hpAfter` to prevent rewarding excessive EXP when executing an enemy with 1 HP left.
- **Kill Bonus**: A minor +10% yield bonus is awarded for landing the killing blow.
- **Dynamic Balancing (`CalculateCombatExp`)**:
  - **Boost**: Hitting a target with a *higher* level grants up to a **+50%** multiplier (caps at 10 level difference).
  - **Malus**: Hitting a target with a *lower* level implies a penalty up to **-75%** multiplier (caps at 10 level difference).
- **Leveling Up**: Accumulating enough XP (scaling by 50 per level) automatically triggers `LevelUp()`. This logs a `CombatLevelEntry` to history, grants `_statPointsPerLevel` (default 5) as `_unassignedStatPoints` for the player/AI to distribute later, and **instantly heals the character for 30% of their Max HP**.
  - **Player Allocation**: Manual via `SpendStatPoint()` in UI. Only Secondary Stats (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma) are directly upgradeable.
  - **NPC Allocation**: Handled internally via `AutoAllocateStats()`. They randomly reinvest all unspent attribute points evenly across the 6 core Secondary Stats to scale dynamically with the player.

### 7. Targeting & Visual Feedback
- **Click-to-Target**: Handled via the UI layer (`UI_PlayerTargeting`) which directly commands the logical `CharacterVisual.SetLookTarget(transform)`.
- **Shader-Driven Target Indicator**: Active target UI elements (like the crosshair ring) must dynamically lerp their colors (Green -> Yellow -> Red) based on the target's missing health (`Stats.Health.OnAmountChanged`). Obeying the strict **Shader-First** rule, this must be pushed exclusively through `Material.SetFloat("_HealthPercent")` on a custom unlit UI shader to avoid CPU color manipulation and prevent Canvas batching breaks.

## Tips & Troubleshooting
- **A character never attacks**: 
  - Verify that the `BattleManager` is properly calling `.UpdateInitiativeTick()`.
  - Check `WeaponInstance.CanFire()`. A magazine-based weapon won't fire if empty.
- **Ranged attack accuracy**: Projectiles travel in a straight line towards the target's position at the moment of firing. They do not "home in" on the target.
