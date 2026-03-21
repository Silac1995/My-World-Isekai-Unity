---
name: character-invitation
description: The Template Method pattern for managing propositions and delayed responses (Invitations) between characters.
---

# Character Invitation System

This skill details the `CharacterInvitation` system and the abstract `InteractionInvitation` class. This duo handles all interactions where one character makes a "proposition" to another, requiring a reflection delay before the response (e.g., Asking to marry, Joining a party, Asking for a job).

## When to use this skill
- To create a new interaction that requires the target's approval (e.g., `InteractionAskForDate.cs`).
- If a character gets stuck indefinitely waiting for a response.
- To understand how statistics (Friendship, Enemy, Sociability) influence NPC responses.

## Architecture & How to use it

The system uses the **Template Method Pattern**. The overall logic (Talk -> Wait -> Evaluate -> Respond) is locked in the parent classes, while the details of the action ("What do they say?", "What happens if they say yes?") are defined in the child class.

### 1. `InteractionInvitation` (The Action)
This is the abstract class that inherits from `ICharacterInteractionAction`. 
To create a new invitation, you must create a child class (e.g., `InteractionJoinParty`) that implements:

#### Mandatory Methods:
- **`CanExecute()`**: Physically checks if the action is possible before even attempting it.
- **`GetInvitationMessage()`**: The phrase spoken by the initiator (e.g., "Come with me!").
- **`OnAccepted()`**: The code to execute if the target says yes (e.g., `source.CurrentParty.AddMember(target)`).

#### Optional Methods (Overrides):
- **`GetAcceptMessage()`** / **`GetRefuseMessage()`**: Response phrases from the target.
- **`OnRefused()`**: Optional penalty (e.g., lowering friendship).
- **`EvaluateCustomInvitation()`**: **CRITICAL**. By default, the system evaluates the invitation based on sociability and friendship. If you return a value (`true` or `false`) here, it bypasses the social engine entirely. (Example: A boss evaluates an `InteractionAskForJob` based on the applicant's skills, not based on whether they like them).

### 2. `CharacterInvitation` (The Local Receiver)
This is a `MonoBehaviour` attached to the Character. 
- When an `InteractionInvitation` is submitted to it via `.ReceiveInvitation()`, it starts a Coroutine.
- If the target is an NPC, they will stop moving to visually "think" about the invitation. If the target is the player, they retain full movement control.
- While the target is thinking during the `_responseDelay`, the **source character will follow the target**. This is managed via the `StartFollowingTarget` routine which auto-halts once the target responds.
- It waits for a delay (`_responseDelay`, default 3 seconds) to "think".
- Then, it calls the evaluation logic. 
- If `EvaluateCustomInvitation()` returns null, it performs the standard calculation:
  - Friend = 85% chance to accept.
  - Stranger = ~30% - 60% depending on the relation gauge.
  - Enemy = 5%.
  - The **Sociability** trait (`CharacterTraits.GetSociability()`) modifies this score by about 15%.
- Finally, it applies the response (Calls `OnAccepted` or `OnRefused` from the invitation).

## Tips & Troubleshooting
- **An invitation gets stuck in the middle**: Ensure that both the initiator and the target are still alive (`IsAlive()`) after the `_responseDelay`. The script will cancel it if either of them dies or disappears while they are thinking.
- **The target lacks a CharacterInvitation component**: The `Execute()` interface will automatically treat the invitation as an instant Refusal and display it in a `Debug.LogWarning`.
