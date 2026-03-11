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
- **DialogueLine**: Contains a boolean `IsPlayerLine` and the `Text` to display.
- **DialogueChoice**: Contains the button text and a reference to the next `DialogueSO` to branch to.

### 2. Dialogue Management
Each player character should have a `DialogueManager` component.
- Call `StartDialogue(DialogueSO dialogue, Character npc)` to initiate a conversation.
- The manager handles `Input.GetKeyDown(KeyCode.Space)` and `Input.GetMouseButtonDown(0)` to advance lines.

### 3. Speech Bubbles
The system uses the `ScriptedSpeech` component (which inherits from `Speech`).
- It must be attached to the speech bubble prefab and linked in the `CharacterSpeech` component.
- `CharacterSpeech.SayScripted()` is the entry point for non-auto-hiding bubbles.

## Technical Details
- **Location**: `Assets/Scripts/Dialogue/`
- **UI**: `Assets/Scripts/UI/Dialogue/UI_DialogueChoicesWindow.cs` handles branching UI.
- **Input**: Hardcoded to Space/Left Click in `DialogueManager.Update()`.

## Examples

### Triggering a dialogue from code
```csharp
DialogueManager manager = player.GetComponent<DialogueManager>();
manager.StartDialogue(introDialogueSO, npcInstance);
```

### Checking if a character is in dialogue
```csharp
if (player.GetComponent<DialogueManager>().IsInDialogue) {
    // Prevent other actions
}
```
