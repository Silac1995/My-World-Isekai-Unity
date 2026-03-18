---
name: character-traits
description: Architecture, math curves, and usage rules for CharacterBehavioralTraitsSO and CharacterTraits determining an NPC's personality (Aggressivity, Sociability, Loyalty).
---

# Character Traits System

The Character Traits system defines the long-term personality matrix of a Character. It dictates their inherent predispositions toward violence, social interaction, and loyalty, independent of their momentary `CharacterNeeds` or temporary `StatusEffects`. 

The system is split into a pure data container (`CharacterBehavioralTraitsSO`) and a MonoBehaviour wrapper (`CharacterTraits`) that ensures safe access and default fallbacks.

## When to use this skill
- When designing new AI behaviors that should differ depending on the NPC's personality (e.g., a shy person vs. an outgoing person responding to an event).
- When adjusting the mathematical probability curves of spontaneous actions like attacking or initiating dialogue.
- When creating a new trait that characters should possess globally.

## The Personality Architecture

The system relies on a clean separation of Data and Execution to allow easy inspector-based tweaking and sharing of personality archetypes (e.g., "Bandit Profile" vs "Townsfolk Profile").

### 1. Data Layer: `CharacterBehavioralTraitsSO`
A ScriptableObject defining raw numerical stats (0.0 to 1.0 ranges).
**Rule:** Never write logic inside the Scriptable Object. It is strictly a container for floats and flags.

### 2. Access Layer: `CharacterTraits.cs`
A MonoBehaviour attached to the Character root. 
**Rule:** Always access traits through `_character.CharacterTraits`, never directly via the Scriptable Object. The wrapper provides essential null-safety (returning `0f` or a neutral `0.5f` if no profile was assigned).

### 3. Execution Layer (Consumer)
AI systems (like `BTCond_DetectedEnemy` or `NPCController`) query `GetAggressivity()` or `GetSociability()` and map those linear 0.0-1.0 values to non-linear probability curves.
**Rule:** When turning traits into actions, always use mathematical gates or curves (like `Mathf.Pow`) rather than flat scaling, to prevent lower-end trait values from triggering extreme behaviors too frequently.

## Known Traits
- **Aggressivity [0.0 - 1.0]**: Defines the threshold for violence. Gated strongly (must be `>= 0.7` to randomly attack strangers). Influences attack chance against known enemies.
- **Sociability [0.0 - 1.0]**: Defines extroversion. Influences base interaction setup chances and affects the dialogue tone (favorable vs hostile). Neutral is `0.5f`.
- **Loyalty [0.0 - 1.0]**: Defines willingness to aid non-friends (acquaintances or same-party members) when they are in danger.
- **CanCreateCommunity (bool)**: Specialized flag dictating leadership initiative in forming new communities.
