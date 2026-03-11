using UnityEngine;
using System.Collections.Generic;

namespace MWI.Dialogue
{
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("If true, the line is spoken by the player. If false, it's the NPC.")]
        [SerializeField] private bool _isPlayerLine;
        [TextArea(3, 10)]
        [SerializeField] private string _text;
        [SerializeField] private float _typingSpeedOverride = 0f;

        public bool IsPlayerLine => _isPlayerLine;
        public string Text => _text;
        public float TypingSpeedOverride => _typingSpeedOverride;
    }

    [System.Serializable]
    public class DialogueChoice
    {
        [SerializeField] private string _choiceText;
        [SerializeField] private DialogueSO _targetDialogue;

        public string ChoiceText => _choiceText;
        public DialogueSO TargetDialogue => _targetDialogue;
    }

    [CreateAssetMenu(fileName = "NewDialogue", menuName = "MWI/Dialogue/Dialogue")]
    public class DialogueSO : ScriptableObject
    {
        [SerializeField] private List<DialogueLine> _lines = new List<DialogueLine>();
        [SerializeField] private List<DialogueChoice> _choices = new List<DialogueChoice>();

        public IReadOnlyList<DialogueLine> Lines => _lines;
        public IReadOnlyList<DialogueChoice> Choices => _choices;
        public bool HasChoices => _choices != null && _choices.Count > 0;
    }
}
