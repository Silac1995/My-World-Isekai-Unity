# Character Traits Usage Patterns

This document provides examples of how to consume mathematical traits out of `CharacterTraits` into actionable logic that feels balanced in-game.

## Pattern: Gated Curve for Spontaneous Actions
Never use linear scaling (`chance = aggressivity * 0.5f`) for highly dangerous/impactful spontaneous actions. Small linear chances cause characters with low stats to randomly execute extreme actions simply due to the law of large numbers inside polling loops.

```csharp
// BAD: A peaceful character with 0.1 aggressivity will tick 0.1 * 0.3 = 3% chance every cycle
// If checked every 2 seconds in a room full of people, they will inevitably punch someone in under a minute.
float aggroChance = character.CharacterTraits.GetAggressivity() * 0.3f;
if (Random.value < aggroChance) { Attack(); }
```

```csharp
// GOOD: Require a severe threshold for spontaneous extreme actions, and curve the remainder.
float aggressivity = character.CharacterTraits.GetAggressivity();

// Only highly hostile archetypes will ever assault a stranger.
if (aggressivity >= 0.7f)
{
    // Subtract the threshold so a 0.7 trait equals 0.0 chance.
    // 1.0 trait = (0.3)^2 * 0.2 = ~1.8% chance every check cycle.
    float aggroChance = Mathf.Pow(aggressivity - 0.7f, 2f) * 0.2f;

    if (aggroChance > 0f && Random.value < aggroChance)
    {
        SpontaneousAttack();
    }
}
```

## Pattern: Soft Linear Scaling for Ambient Actions
For less dangerous actions (like deciding to chat with an enemy, or slightly shifting a social tone), soft linear modifications to a firm base-chance are perfectly acceptable.

```csharp
// Base 20% response rate for neutral actions
float responseChance = 0.20f;

// Sociable characters get a slight bonus (+0.3 max), solitary ones get a penalty (-0.3 max)
float sociabilityModifier = (character.CharacterTraits.GetSociability() - 0.5f) * 0.6f;
responseChance += sociabilityModifier;

// Always clamp to ensure probability stays valid
responseChance = Mathf.Clamp01(responseChance);

if (Random.value < responseChance)
{
    RespondToInteraction();
}
```

## Pattern: Hard Gate (Abilities)
Booleans operate as pure binary capabilities rather than statistical shifts.

```csharp
if (character.CharacterTraits.CanCreateCommunity())
{
    GenerateFoundingCommunityGoapGoal();
}
```
