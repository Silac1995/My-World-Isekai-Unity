using UnityEngine;
using System.Collections.Generic;
using MWI.Dialogue;
using MWI.UI.Core;
using System.Collections;

public class DialogueManager : MonoBehaviour
{
    [Header("Testing")]
    [SerializeField] private DialogueSO _currentDialogue;
    [SerializeField] private List<Character> _testParticipants;
    
    [Header("References")]
    [SerializeField] private UI_DialogueChoicesWindow _choicesWindow;
    
    private Dictionary<int, Character> _participantsIndices = new Dictionary<int, Character>();
    private int _currentLineIndex = -1;
    private bool _isTyping = false;
    private bool _isWaitingForInput = false;

    [ContextMenu("Trigger Serialized Dialogue")]
    public void TriggerTestDialogue()
    {
        if (_currentDialogue == null)
        {
            Debug.LogError("<color=red>[Dialogue]</color> No dialogue assigned for testing!");
            return;
        }

        List<Character> participants = new List<Character>();

        // Priority: Use inspector participants if assigned
        if (_testParticipants != null && _testParticipants.Count > 0)
        {
            participants.AddRange(_testParticipants);
        }
        else
        {
            // Fallback: Automatic discovery
            Character[] allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
            if (TryGetComponent<Character>(out var selfChar)) participants.Add(selfChar);
            foreach (var c in allCharacters)
            {
                if (!participants.Contains(c)) participants.Add(c);
                if (participants.Count >= 5) break;
            }
        }

        StartDialogue(_currentDialogue, participants);
    }

    public bool IsInDialogue => _currentDialogue != null;

    public void StartDialogue(DialogueSO dialogue, List<Character> participants)
    {
        if (dialogue == null || participants == null || participants.Count == 0) return;

        _currentDialogue = dialogue;
        _participantsIndices.Clear();
        
        // Map participants (1-indexed)
        for (int i = 0; i < participants.Count; i++)
        {
            _participantsIndices[i + 1] = participants[i];
        }

        // Initialize lines (transient reference)
        foreach (var line in _currentDialogue.Lines)
        {
            if (_participantsIndices.TryGetValue(line.CharacterIndex, out Character c))
            {
                line.Initialize(c);
            }
        }

        _currentLineIndex = 0;
        
        Debug.Log($"<color=green>[Dialogue]</color> Starting dialogue with {participants.Count} participants.");
        
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
        if (_isTyping) return;

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
        
        if (!_participantsIndices.TryGetValue(line.CharacterIndex, out Character speaker))
        {
            Debug.LogError($"<color=red>[Dialogue]</color> No character found for index {line.CharacterIndex}!");
            AdvanceDialogue();
            return;
        }

        // Close ALL potential speech bubbles from participants to avoid overlaps
        foreach (var participant in _participantsIndices.Values)
        {
            participant.CharacterSpeech?.CloseSpeech();
        }

        _isTyping = true;
        _isWaitingForInput = false;

        string processedText = ProcessDialogueTags(line.LineText);

        speaker.CharacterSpeech?.SayScripted(processedText, line.TypingSpeedOverride, () => {
            _isTyping = false;
            _isWaitingForInput = true;
        });
    }

    private string ProcessDialogueTags(string originalText)
    {
        string result = originalText;

        // Replace [indexX].getName
        foreach (var pair in _participantsIndices)
        {
            string tag = $"[index{pair.Key}].getName";
            if (result.Contains(tag))
            {
                result = result.Replace(tag, pair.Value.CharacterName);
            }
        }

        return result;
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
                StartDialogue(nextDialogue, new List<Character>(_participantsIndices.Values));
            }
            else
            {
                EndDialogue();
            }
        }
    }

    public void EndDialogue()
    {
        foreach (var participant in _participantsIndices.Values)
        {
            participant.CharacterSpeech?.CloseSpeech();
        }
        
        _currentDialogue = null;
        _participantsIndices.Clear();
        _currentLineIndex = -1;
        
        Debug.Log("<color=green>[Dialogue]</color> Dialogue ended.");
    }
}
