using UnityEngine;
using MWI.Dialogue;
using MWI.UI.Core;
using System.Collections;
using System.Collections.Generic;

public class DialogueManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character _playerCharacter;
    [SerializeField] private UI_DialogueChoicesWindow _choicesWindow;
    
    private DialogueSO _currentDialogue;
    private Character _npcCharacter;
    private int _currentLineIndex = -1;
    private bool _isTyping = false;
    private bool _isWaitingForInput = false;

    public bool IsInDialogue => _currentDialogue != null;

    public void StartDialogue(DialogueSO dialogue, Character npc)
    {
        if (dialogue == null || npc == null) return;

        _currentDialogue = dialogue;
        _npcCharacter = npc;
        _currentLineIndex = 0;
        
        Debug.Log($"<color=green>[Dialogue]</color> Starting dialogue with {npc.CharacterName}");
        
        ShowCurrentLine();
    }

    private void Update()
    {
        if (!IsInDialogue) return;

        // Basic input check (Space or Left Click)
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
        {
            HandleInput();
        }
    }

    private void HandleInput()
    {
        if (_isTyping)
        {
            // Optional: Skip typing effect
            // For now we just wait
            return;
        }

        if (_isWaitingForInput)
        {
            AdvanceDialogue();
        }
    }

    private void ShowCurrentLine()
    {
        if (_currentLineIndex >= _currentDialogue.Lines.Count)
        {
            EndDialogue();
            return;
        }

        DialogueLine line = _currentDialogue.Lines[_currentLineIndex];
        Character speaker = line.IsPlayerLine ? _playerCharacter : _npcCharacter;
        Character listener = line.IsPlayerLine ? _npcCharacter : _playerCharacter;

        // Ensure listener's speech is closed
        listener.CharacterSpeech?.CloseSpeech();

        _isTyping = true;
        _isWaitingForInput = false;

        speaker.CharacterSpeech?.SayScripted(line.Text, line.TypingSpeedOverride, () => {
            _isTyping = false;
            _isWaitingForInput = true;
        });
    }

    public void AdvanceDialogue()
    {
        _currentLineIndex++;
        
        if (_currentLineIndex < _currentDialogue.Lines.Count)
        {
            ShowCurrentLine();
        }
        else if (_currentDialogue.HasChoices)
        {
            ShowChoices();
        }
        else
        {
            EndDialogue();
        }
    }

    private void ShowChoices()
    {
        if (_choicesWindow != null)
        {
            _choicesWindow.ShowChoices(_currentDialogue.Choices, this);
        }
        else
        {
            Debug.LogWarning("<color=yellow>[Dialogue]</color> No Choices Window assigned to DialogueManager!");
            EndDialogue();
        }
    }

    public void SelectChoice(int index)
    {
        _choicesWindow?.Close();
        
        if (index >= 0 && index < _currentDialogue.Choices.Count)
        {
            DialogueSO nextDialogue = _currentDialogue.Choices[index].TargetDialogue;
            if (nextDialogue != null)
            {
                StartDialogue(nextDialogue, _npcCharacter);
            }
            else
            {
                EndDialogue();
            }
        }
    }

    public void EndDialogue()
    {
        _playerCharacter.CharacterSpeech?.CloseSpeech();
        _npcCharacter?.CharacterSpeech?.CloseSpeech();
        
        _currentDialogue = null;
        _npcCharacter = null;
        _currentLineIndex = -1;
        
        Debug.Log("<color=green>[Dialogue]</color> Dialogue ended.");
    }
}
