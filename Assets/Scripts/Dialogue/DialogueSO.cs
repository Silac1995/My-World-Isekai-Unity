using UnityEngine;
using System.Collections.Generic;

namespace MWI.Dialogue
{
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("The index of the participant (1-indexed based on the list passed to StartDialogue).")]
        [SerializeField] private int _characterIndex = 1;
        [System.NonSerialized] private Character _character;
        [TextArea(3, 10)]
        [SerializeField] private string _lineText;
        [SerializeField] private float _typingSpeedOverride = 0f;

        public int CharacterIndex => _characterIndex;
        public Character Character => _character;
        public string LineText => _lineText;
        public float TypingSpeedOverride => _typingSpeedOverride;

        public void Initialize(Character character)
        {
            _character = character;
        }
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
