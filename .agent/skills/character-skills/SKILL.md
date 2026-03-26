---
name: character-skills
description: Architecture, progression, and interdependencies of the Character Skills system (Professions, XP, and Stat bonuses).
---

# Character Skills System

The Character Skills system governs the non-combat proficiencies and professions of a character. It manages skill progression (XP/Level), proficiency calculation (scaling with stats), and passive stat bonuses granted at specific milestones.

## When to use this skill
- When implementing new professions or gathering/crafting skills.
- When modifying how character statistics influence non-combat efficiency.
- When integrating skills with the Jobs or Mentorship systems.

## Core Architecture

### 1. The Hub: [CharacterSkills.cs](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs)
A `CharacterSystem` (`NetworkBehaviour`) component attached to the `Character` GameObject.
- **Responsibility**: Manages the collection of `SkillInstance` objects, maps them for quick lookup, and handles the application of passive stat bonuses.
- **Key Methods**:
    - `GainXP(SkillSO skill, int amount)`: The entry point for adding experience. Automatically learns the skill if not already present.
    - `GetSkillProficiency(SkillSO skill)`: Returns the final efficiency score used by other systems (e.g., crafting quality).
    - `RecalculateAllSkillBonuses()`: Sums up all `LevelBonuses` from all skills and applies them as `StatModifier` to `CharacterBaseStats`.

### 2. Data Model: [SkillSO](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/Assets/Scripts/Character/CharacterSkills/SkillSO.cs)
The static definition of a skill.
- **StatInfluences**: A list of `SkillStatScaling` defining which `SecondaryStatType` (Strength, Agility, etc.) boosts this skill's proficiency.
- **LevelBonuses**: A list of `SkillLevelBonus` defining passive stat points granted when reaching specific level thresholds.

### 3. Runtime State: [SkillInstance](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/Assets/Scripts/Character/CharacterSkills/SkillInstance.cs)
A serializable class representing a character's progress in a specific skill.
- **XP Progression**: Managed via `AddXP`. The default curve is `NextLevelXP = Level * 100`.
- **Proficiency Formula**:
  `Proficiency = (Level * BaseProficiencyPerLevel) + Sum(SecondaryStatValue * ProficiencyPerPoint)`

### 4. Ranks: [SkillTier](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/Assets/Scripts/Character/CharacterSkills/SkillTier.cs)
Defines skill milestones:
- **Novice** (0-14), **Intermediate** (15-34), **Advanced** (35-54), **Professional** (55-74), **Master** (75-94), **Legendary** (95-100).
- **Mentorship**: Higher tiers grant multipliers (up to 5x for Legendary) when teaching others.

## System Integrations

### Mentorship System
Managed by `CharacterMentorship.cs`. Skills are taught in "Classes":
- A character can teach any skill where they have reached the **Advanced** tier (Level 35+).
- Students receive XP ticks via `ReceiveLessonTick`, scaled by the mentor's tier multiplier.
- Students can only learn from a mentor if the mentor's tier is higher than theirs.

### Job & Crafting System
Used in specialized jobs like `JobBlacksmith.cs`:
- Jobs often require a specific `SkillSO`.
- Completing job tasks (e.g., forging an item) grants XP to the associated skill.
- Proficiency affects crafting speed and potentially item quality (implemented in `CharacterCraftAction`).

## Multiplayer & Persistence

### Networking
- `CharacterSkills` inherits from `NetworkBehaviour`.
- **Current State**: Skills are currently initialized from the inspector or added via server-authoritative logic.
- **Authority**: All XP gains and Level-ups MUST happen on the **Server**.

### Persistence (Rule 20)
- Character skills are intended to be saved in independent local character files (e.g., `.json` or `.dat`).
- **Standard**: Use `ICharacterData` for serialization (Integrate via `SaveManager`). Skills are serialized as a list of `SkillInstance` data.

## Implementation Checklist
- [ ] Is the new skill defined as a `SkillSO` in `Assets/Data/Skills/`?
- [ ] Are stat influences balanced (e.g., Blacksmithing scaled by Strength/Dexterity)?
- [ ] Are level bonuses registered to avoid stat inflation?
- [ ] Does XP gain only occur on the Server?
- [ ] Is `RecalculateAllSkillBonuses()` called after any direct level manipulation?
