---
description: Architecture, progression, and interdependencies of the Character Stats system (Primary, Secondary, and Tertiary stats).
---
# Character Stats System

The `CharacterStats` system is the numerical backbone of characters in the project. It handles health, mana, stamina, core attributes, and derived combat properties. All changes to stats properly propagate down a dependency chain, ensuring modifications to base attributes are automatically reflected in derived capabilities.

## Stat Categories

The system divides stats into three categories:

1. **Secondary Stats** (`CharacterStrength`, `CharacterAgility`, etc.)
   - The root building blocks.
   - Assigned initially from `RaceSO` defaults.
   - Core progression points: these are the stats you manually upgrade when assigning level-up points via `SpendStatPoint()`.
2. **Primary Stats** (`CharacterHealth`, `CharacterStamina`, `CharacterMana`, `CharacterInitiative`)
   - Resource pools (with a `CurrentAmount` and `MaxAmount`).
   - Except for Initiative, the `MaxAmount` is derived from a linked Secondary Stat.
3. **Tertiary Stats** (`PhysicalPower`, `CastingSpeed`, etc.)
   - Purely derived combat numeric values.
   - Completely dependent on a specific Secondary Stat and a formula containing multipliers and offsets provided by `RaceSO`.

## Stat Dependencies Matrix

When a Secondary Stat is increased, it directly impacts specific Primary and Tertiary stats. The central aggregator `CharacterStats.cs` listens for changes to Secondary Stats via the `OnValueChanged` event and cascades a recalculation to these dependents.

### 1. Strength
- **Impacts Tertiary:** `PhysicalPower`
- **Role:** Pure physical scaling. Governs raw melee and physical ability damage output.

### 2. Agility
- **Impacts Tertiary:** `Speed`, `DodgeChance`, `MoveSpeed`
- **Role:** General body movement. Modifies animation/turn speed, evasion mechanics against physical attacks, and actual NavMesh traversal velocity.

### 3. Dexterity
- **Impacts Tertiary:** `Accuracy`, `CastingSpeed`, `CriticalHitChance`
- **Role:** Fine motor skills. Modifies hit likelihood, how fast spells and intricate abilities cast, and the chance to strike weak points.

### 4. Intelligence
- **Impacts Primary:** `Mana` (Max Mana Pool)
- **Impacts Tertiary:** `MagicalPower`, `ManaRegenRate`
- **Role:** Mental capacity. Determines magical output, the depth of the mana pool, and how quickly it refills.

### 5. Endurance
- **Impacts Primary:** `Health` (Max HP), `Stamina` (Max Stamina Pool)
- **Impacts Tertiary:** `StaminaRegenRate`
- **Role:** Physical resilience. Essential for staying alive and performing continuous physical actions (combat maneuvers, running).

### 6. Charisma
- **Impacts:** No direct combat numeric dependents (currently).
- **Role:** Reserved for social systems, trade pricing, interaction exchanges, and community/faction standings.

### Initiative (Outlier Primary)
- **Impacts:** None.
- **Role:** Uses a fixed base from `RaceSO` to determine turn order in combat. Doesn't possess a `CurrentAmount` reservoir like Health or Mana.

## Stat Calculation & RaceSO

While `CharacterStats.cs` links the metrics, the actual conversion rates (multipliers and base offsets) are governed by the `RaceSO` scriptable object. 
When `ApplyRaceStats(RaceSO race)` is called:
1. All Secondary stats receive flat base values from the race.
2. All Primary & Tertiary stats receive scaling multipliers and offsets.
3. The formulas look generally like this:
   `New Max = BaseOffset + (LinkedSecondaryStat.CurrentValue * Multiplier)`

## Code Standards & Event Handling

- **Memory Management:** Every `CharacterTertiaryStats` and `CharacterPrimaryStats` derives from `CharacterBaseStats`. They all feature `OnValueChanged` events. Inside `CharacterStats.cs`, any listener applied (like `_onSecondaryStatChanged`) **must** be securely unsubscribed within `OnDestroy()` to avoid memory leaks if the gameObject is removed.
- **Dynamic Percentage Handling:** When Primary stats (like Health) change their `MaxAmount` (because Endurance leveled up), their `CurrentAmount` automatically scales keeping the exact same percentage (e.g., if you had 50% HP, you still have 50% HP out of the new max pool).
- **Stat Modifier Support:** Every stat supports a `StatModifier` wrapper for buffs and debuffs. Added modifiers dynamically recalculate `CurrentValue` against `BaseValue`, without permanently losing the root `BaseValue`.
