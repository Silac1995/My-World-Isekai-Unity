---
name: dialogue-system
description: Manages scripted conversations with player input advancement, using the speech bubble system and ScriptableObjects.
---

# Dialogue System

The Dialogue System allows for scripted, non-ambient conversations between the player and NPCs. Unlike standard speech, these lines stay on screen until the player provides input (Space or Click).

## When to use this skill
- When you need to implement or modify scripted story sequences or interactions.
- When creating new `DialogueSO` assets to define conversation flow.
- When debugging issues with player input advancement or branching choices.
- When extending the `DialogueManager` or `ScriptedSpeech` logic.

## How to use it

### 1. Data Structure (`DialogueSO`)
Dialogue is stored in `DialogueSO` ScriptableObjects.
- **DialogueLine**: Contains a `characterIndex` (integer) and the `lineText`.
    - **Initialize(Character)**: Lines support runtime initialization with a transient character reference.
- **Placeholder Tags**: `lineText` supports tags like `[indexX].getName`, which are replaced by the actual character's name at runtime (e.g., `[index1].getName`).

### 2. Dialogue Management
Each player character has a `DialogueManager` component.
- **Multiplayer Synchronization**: 
    - If no players are participating in the dialogue, the script will **automatically advance** lines with a **1.5-second delay** after the speech text has finished typing.
    - If at least one player character is participating, the dialogue waits for player input (`Space` or `Left Click`) to advance.

### 3. Speech Bubbles
The system uses the `ScriptedSpeech` component.
- Attached to the speech bubble prefab and linked in `CharacterSpeech`.
- `CharacterSpeech.SayScripted()` displays bubbles that persist until player input.

### 4. Testing Tools
The `DialogueManager` includes inspector-driven testing:
- **_currentDialogue**: Assign a `DialogueSO` to test.
- **_testParticipants**: List of characters to map to indices (Element 0 = Index 1).
- **Trigger Serialized Dialogue**: Context menu option to start the dialogue immediately.

## Examples

### Triggering a dialogue from code
```csharp
DialogueManager manager = player.GetComponent<DialogueManager>();
manager.StartDialogue(introDialogueSO, new List<Character> { player, npc1, npc2 });
```

### Checking if a character is in dialogue
```csharp
if (player.GetComponent<DialogueManager>().IsInDialogue) {
    // Prevent other actions
}
```
