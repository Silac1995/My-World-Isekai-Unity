---
description: Architecture, flow, and integration of the combat system (BattleManager, CharacterCombat, Initiative, Stats).
---

# Combat System Skill

This skill details the architecture of the combat system in the project and the rules to follow when extending or debugging it. The combat system relies on concepts like **Initiative Ticks**, **Engagement Groups**, and strict role separation between the global Manager and local components.

## When to use this skill
- To add a new combat-related feature (e.g., AoE attack, fleeing, new combat buffs/debuffs).
- To interact with characters' attack delay (`Initiative`).
- In case of bugs where combat doesn't end, or a character is frozen without attacking.
- When adding or modifying combat-related statistics (in `CharacterStats`).

## Architecture & How to use it

### 1. The BattleManager (Global Management)
The `BattleManager` is the supreme entity of a battle, usually instantiated when a clash begins.
- **BattleTeams**: It always maintains two teams (Initiator vs Target). We do *not* support 3-team free-for-alls in a single instance.
- **CombatEngagement**: NEW SYSTEM. The BattleManager manages brawl "subgroups" (e.g., the Warrior hits the Mage, while the Archer hits the Rogue within the same battle). Handled by the internal `_activeEngagements` list.
- **BattleZone**: A physical zone (`BoxCollider` isTrigger) and pathfinding volume (`NavMeshModifierVolume`) is dynamically generated at the center of the initial clash to mark the terrain.
- **Tick System**: It is the `BattleManager` that sets the pace (`PerformBattleTick()`), and *not the Update method of each character*.

### 2. CharacterCombat (Local Logic)
This is the component every NPC/Player has in order to fight.
- **Combat Mode**: A character switches to "CombatMode" (and draws their weapon) if they intend to attack. There is a `COMBAT_MODE_TIMEOUT` (default 7 seconds).
- **Consumption & Initiative Tick**:
  - The `.IsReadyToAct` method checks if the Initiative (in Stats) is full.
  - The `.ConsumeInitiative()` method resets initiative to 0 after a successful attack.
  - The `.UpdateInitiativeTick(amount)` method is **called by the BattleManager** to fill the bar.
- **Attack()**: Dynamic choice. If the target is within ranged weapon reach (according to `RangedCombatStyleSO.MeleeRange`), it performs a `RangedAttack()`. Otherwise, it chooses `MeleeAttack()`. These actions are sent to the global `CharacterActions` system.

### 3. Combat Styles (`CombatStyleSO`)
The bridge between State (`CharacterStats`) and Spatial Logic (`CharacterCombat`).
- **Statistical Data**: Defines which stat makes the attack stronger (`ScalingStat`, `StatMultiplier`). Ex: The dagger scales with Dexterity rather than Strength.
- **Range and Animations**: Contains the weapon range (`MeleeRange`) and especially **the dynamic animation controller** assigned according to the mastery level (`StyleLevelData.CombatController`).
- These variables are polled on the fly by `CharacterCombat` at the moment of attacking.

### 4. CharacterStats (Stat Distribution)
Combat massively relies on `CharacterStats`. It is critical to respect its architecture:
- **Primary Stats**: Dynamic (Health, Stamina, Mana, **Initiative**). 
  - *Note: Initiative has a default base of "0".*
- **Secondary Stats**: Base characteristics (Strength, Agility, Dexterity, Intelligence, Endurance, Charisma).
- **Tertiary Stats**: Derived from secondary ones (PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance, etc.). These stats are checked during pure damage calculations.

## Tips & Troubleshooting
- **A character never attacks**: Verify that the `BattleManager` is properly calling `.UpdateInitiativeTick()` on this character. If the combat zone "forgot" them in `_allParticipants`, Initiative will stay at 0 forever.
- **Combat never ends**: The `_isBattleEnded` flag often depends on the survival of entire teams. Ensure callbacks are firing upon a participant's death.
