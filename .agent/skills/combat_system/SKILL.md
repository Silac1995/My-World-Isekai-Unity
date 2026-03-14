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
The `BattleManager` is the supreme entity of a battle, usually instantiated when a clash begins.
- **BattleTeams**: It always maintains two teams (Initiator vs Target). We do *not* support 3-team free-for-alls in a single instance.
- **CombatEngagement**: NEW SYSTEM. The BattleManager manages brawl "subgroups" (e.g., the Warrior hits the Mage, while the Archer hits the Rogue within the same battle). Handled by the internal `_activeEngagements` list.
- **BattleZone**: A physical zone (`BoxCollider` isTrigger) and pathfinding volume (`NavMeshModifierVolume`) is dynamically generated at the center of the initial clash to mark the terrain.
- **Tick System**: It is the `BattleManager` that sets the pace (`PerformBattleTick()`), and *not the Update method of each character*.
- **Performance**: Always guard expensive debug logic (e.g., `UpdateDebugEngagements`) with `#if UNITY_EDITOR` to ensure optimal performance in production builds.

### 2. CharacterCombat (Local Logic)
This is the component every NPC/Player has in order to fight.
- **Combat Mode**: A character switches to "CombatMode" (and draws their weapon) if they intend to attack. There is a `COMBAT_MODE_TIMEOUT` (default 7 seconds).
- **Consumption & Initiative Tick**:
  - The `.IsReadyToAct` method checks if the Initiative (in Stats) is full.
  - The `.ConsumeInitiative()` method resets initiative to 0 after a successful attack.
  - The `.UpdateInitiativeTick(amount)` method is **called by the BattleManager** to fill the bar.
- **Attack(target)**: Dynamic choice. 
  - If the character has a `RangedCombatStyleSO` equipped AND the target is beyond `MeleeRange`, it performs a `RangedAttack()`.
  - Otherwise, it performs a `MeleeAttack()`.
  - Note: Ranged weapons fallback to Melee at close range (e.g., hitting someone with a bow).

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

## Tips & Troubleshooting
- **A character never attacks**: 
  - Verify that the `BattleManager` is properly calling `.UpdateInitiativeTick()`.
  - Check `WeaponInstance.CanFire()`. A magazine-based weapon won't fire if empty.
- **Ranged attack accuracy**: Projectiles travel in a straight line towards the target's position at the moment of firing. They do not "home in" on the target.
