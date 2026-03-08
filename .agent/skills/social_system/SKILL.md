---
description: System of interactions between NPCs/Players (Speaking turns, ICharacterInteractionAction) and relationships (Compatibility modifiers, Friendships/Enmities).
---

# Social System Skill

This skill details how characters dynamically interact with each other (discussions, exchanges) and how these events forge their long-term memories (relationships and personality compatibility).
It encompasses `CharacterInteraction` (the act of expressing oneself) and `CharacterRelation` (the memory).

## When to use this skill
- To add a new type of interaction (e.g., Insult, Give a gift, Propose marriage) via the `ICharacterInteractionAction` interface.
- To understand how the relationship between two characters evolves (opinion modification).
- In case of characters deadlocking each other during a dialogue.

## Architecture

The social system rests on **two interconnected pillars**: The Present (Interaction) and The Past/Future (Relation).

### 1. Present: CharacterInteraction
Interaction is event-driven (e.g., engaging an NPC with 'E', or two NPCs crossing paths and triggering the `Socialize` GOAP action).

#### Flow of an Interaction:
1. **Start (`StartInteractionWith`)**: 
   - _Security_: The system checks that both are free (`IsFree()`).
   - _Connection_: It freezes the target (`Freeze()`), forces them to look at the initiator (`SetLookTarget()`), and instantly adds/updates the `CharacterRelation` to state that they know each other (`SetAsMet()`).
   - _Positioning_: The initiator walks towards the target (`MoveToInteractionBehaviour`).
2. **Dialogue (`DialogueSequence`)**: This is a Coroutine simulating real exchanges.
   - The roles of Speaker and Listener reverse (up to a maximum of 6 exchanges).
   - The algorithm waits for the visual end of the "Speech bubble" (`CharacterSpeech.IsSpeaking`) before starting its delay (`WaitForSeconds(1.0f to 2.5f)`) for a natural response.
3. **End (`EndInteraction`)**: Frees the characters (`Unfreeze()`, clears `LookTarget` and cleans up `MoveToInteractionBehaviour`).

#### How to add an action?
Create a class that implements the `ICharacterInteractionAction` interface which will contain the core of the dialogue line or act (e.g., `InteractionTalk.cs`).

### 2. The Memory: CharacterRelation
`CharacterRelation` stores the list of links (`Relationship`) that a character maintains with the rest of the world.
- **Bilateral Principle**: If A adds B (`AddRelationship`), the code ensures that B adds A instantly.

#### The Compatibility System
Opinion (`UpdateRelation`) never goes up or down in a "raw" manner. It is filtered by the `CharacterProfile` (the Personality).
If A tries to charm B (e.g., +10 relationship):
- If B is **Compatible** with A's personality: The +10 gain is multiplied by 1.5 (Gain = +15). If there was a conflict (-10), the loss is mitigated (-5).
- If B is **Incompatible**: The +10 gain is halved (Gain = +5). If a conflict arises (-10), the disaster is amplified (-15).

## Tips & Troubleshooting
- **My character is stuck indefinitely after speaking**: Verify that the GOAP action or input event properly calls `EndInteraction()` in case of sudden interruption, or check that no `DialogueSequence` Coroutine crashes halfway through.
- **Why did the player get fewer points than expected with this NPC?**: It's personality compatibility (`CharacterProfile.GetCompatibilityWith()`). The agent must always look at this system if a strange point variation is reported.
