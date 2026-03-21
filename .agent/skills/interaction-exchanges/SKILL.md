---
name: interaction-exchanges
description: Architecture of the turn-based dialogue, the pre-interaction Invitation system, timeouts, and rules for Player vs NPC vs Player exchanges.
---

# Interaction Exchanges (Player, NPC, Multiplayer)

This system establishes the rules of engagement between any two characters (NPC-to-NPC, Player-to-NPC, Player-to-Player). Unlike the passive `social_system` (which handles how relationships evolve), this specific architecture manages the **operational flow** of engaging in a conversation—ensuring no entity is indefinitely locked out of gameplay by another.

## Core Pillars of the Exchange

There are two major stages to any character-to-character interaction exchange: The Invitation Phase and The Turn-Based Sequence.

---

### 1. The Invitation Phase (`CharacterInvitation`)

Before two characters lock themselves into a formal dialogue interaction, the initiator must send an invitation. This serves as the asynchronous handshake, vital for both AI realism and Multiplayer validation.

1. **Initiation**: The requesting character sends an `InteractionInvitation` containing an `ICharacterInteractionAction` (usually `InteractionStartDialogue`).
2. **Evaluation**: The receiving character (`target`) evaluates the invitation through `CharacterInvitation.AddInvitation(invitation)`.
    - If the target is busy (`!IsFree()`), the invitation is instantly **Rejected**.
3. **Response Delay**: The target evaluates its response.
    - **For NPCs**: The response is scheduled automatically after a short delay (e.g., `_responseDelay = 1.0f`).
    - **For Players**: The invitation triggers a UI notification (e.g., "Press 'Y' to Accept or 'N' to Decline"). A strict timeout must apply so the initiator isn't waiting indefinitely.
4. **Acceptance (The First Turn)**: Once the target accepts, the `OnAccepted` callback fires. The **initiator** formally calls `StartInteractionWith(target, action)`, which guarantees that the one who sent the invitation gets the **First Turn** in the dialogue.

---

### 2. The Turn-Based Sequence (`CharacterInteraction.DialogueSequence`)

Once an interaction starts, both characters are Frozen, and they enter the `DialogueSequence` coroutine. The sequence strictly enforces a back-and-forth exchange up to a set limit (e.g., 6 exchanges).

- **Role Reversal**: The system tracks `Speaker` and `Listener`. After every speech action, these roles are swapped.
- **Turn Execution**:
    - **When the Speaker is an NPC**: The sequence triggers standard AI text, waits for the visual "Speech bubble" (`IsSpeaking`) to finish, and injects an artificial delay (1.0s to 2.5s) to simulate breathing/reading time before passing the turn.
    - **When the Speaker is a Player**: The coroutine pauses indefinitely (`WaitUntil`). The `OnPlayerTurnStarted` event is broadcast globally.

---

### 3. The Player's Turn & UI Interaction

When it is the Player's turn to speak (`OnPlayerTurnStarted`), the game hands control to the player's UI wrapper.

1. **Menu Activation**: `PlayerInteractionDetector` detects the player's turn and instantly loads context-specific actions (`GetDialogueInteractionOptions()`—e.g., *Talk*, *Insult*, *Gift*).
2. **Action Selection**: The player clicks a UI button.
3. **Execution Routing**: The UI directly calls `PerformInteraction(action)` on the **Player's** `CharacterInteraction` component.
4. **Advancing the Sequence**: The `CharacterInteraction` processes the choice, instantiates the text bubble, evaluates the social consequence, and formally Ends the Player's turn, unpausing the `DialogueSequence` coroutine and swapping roles back to the target.

---

## 4. Interaction Topologies

Because the game supports Multiplayer, the exchange system is designed to handle three distinct scenarios:

#### A. NPC vs NPC
- **Flow**: Immediate, fully simulated.
- **Invitations**: NPCs must use `InteractionStartDialogue` to invite each other. One NPC may reject another if they are busy sleeping or working.
- **Turns**: Handled via coroutine delays. No UI is required.

#### B. Player vs NPC
- **Flow**: Asynchronous start, synchronous sequence.
- **Invitations**: The Player "Taps E", sending an invitation. The NPC accepts 1 second later. The Player gets the first turn.
- **Turns**: When the Player speaks, the coroutine pauses for UI input. When the NPC speaks, the coroutine uses AI generation and artificial delays.

#### C. Player vs Player (Multiplayer Core)
- **Flow**: Fully asynchronous, strict timeouts.
- **Invitations**: Player A invites Player B. Player B receives a UI prompt. If Player B takes longer than 10 seconds to respond, the invitation Self-Destructs, freeing Player A.
- **Turns**: The sequence is paused on *both* sides depending on whose turn it is. If Player A takes too long to select a dialogue option (e.g., more than 15-30 seconds), the interaction must forcefully Time Out and `EndInteraction()` to prevent greifing/locking Player B in place.

## 5. Overriding & Interruptions

If an interaction is forcefully interrupted by a new one (e.g. Player forces an interaction on an NPC who was walking to start a different one):
- **Coroutine Cancellation**: `SetInteractionTargetInternal(...)` must strictly cancel the existing `_activeDialogueCoroutine` (or movement routine) before starting the new one. Failure to do so will cause the old routine to finish in the background, which will inadvertently call `EndInteraction()` and prematurely destroy the new Interaction state (and any associated player UI).

## Tips & Troubleshooting
- **"The UI Action triggered the NPC instead of the Player"**: Ensure that `InteractionOption` delegates always call `interactor.CharacterInteraction.PerformInteraction(action)` instead of the target's interaction component.
- **"NPCs keep interrupting each other"**: Ensure AI nodes formally spawn an `InteractionInvitation` rather than forcing a direct `StartInteractionWith()`, respecting the target's `IsFree()` status.
- **"Interaction Menu closes randomly"**: Check if the target NPC had a lingering interaction coroutine that finished and called `EndInteraction()` in the background. Ensure their previous coroutine was properly stopped when the player interrupted them.
