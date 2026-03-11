using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using MWI.UI.Core;

namespace MWI.Dialogue
{
    public class UI_DialogueChoice : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TextMeshProUGUI _choiceText;
        
        private int _index;
        private DialogueManager _manager;

        public void Setup(int index, string text, DialogueManager manager)
        {
            _index = index;
            _choiceText.text = text;
            _manager = manager;
            
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(OnClicked);
        }

        private void OnClicked()
        {
            _manager.SelectChoice(_index);
        }
    }

    public class UI_DialogueChoicesWindow : ClosableWindow
    {
        [Header("Choice Settings")]
        [SerializeField] private RectTransform _choiceContainer;
        [SerializeField] private UI_DialogueChoice _choicePrefab;

        public void ShowChoices(IReadOnlyList<DialogueChoice> choices, DialogueManager manager)
        {
            Open();
            
            // Clear existing
            foreach (Transform child in _choiceContainer)
            {
                Destroy(child.gameObject);
            }

            // Create new ones
            for (int i = 0; i < choices.Count; i++)
            {
                UI_DialogueChoice choiceUI = Instantiate(_choicePrefab, _choiceContainer);
                choiceUI.Setup(i, choices[i].ChoiceText, manager);
            }
        }
    }
}
