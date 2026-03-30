using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _characterNameText;
    [SerializeField] private TextMeshProUGUI _worldSaveText;
    [SerializeField] private Button _selectButton;
    [SerializeField] private Button _deleteButton;

    private string _characterGuid;
    private string _characterName;
    private Action<string> _onSelect;
    private Action<string, string> _onDelete;

    public void Setup(string charGuid, string charName, bool hasWorldSave,
        Action<string> onSelect, Action<string, string> onDelete)
    {
        _characterGuid = charGuid;
        _characterName = charName;
        _onSelect = onSelect;
        _onDelete = onDelete;

        if (_characterNameText != null)
            _characterNameText.text = charName;

        if (_worldSaveText != null)
        {
            _worldSaveText.text = "Has a save from this world";
            _worldSaveText.gameObject.SetActive(hasWorldSave);
        }

        if (_selectButton != null)
        {
            _selectButton.onClick.RemoveAllListeners();
            _selectButton.onClick.AddListener(OnSelectClicked);
        }

        if (_deleteButton != null)
        {
            _deleteButton.onClick.RemoveAllListeners();
            _deleteButton.onClick.AddListener(OnDeleteClicked);
        }
    }

    private void OnSelectClicked()
    {
        _onSelect?.Invoke(_characterGuid);
    }

    private void OnDeleteClicked()
    {
        _onDelete?.Invoke(_characterGuid, _characterName);
    }

    private void OnDestroy()
    {
        if (_selectButton != null)
            _selectButton.onClick.RemoveListener(OnSelectClicked);

        if (_deleteButton != null)
            _deleteButton.onClick.RemoveListener(OnDeleteClicked);
    }
}
