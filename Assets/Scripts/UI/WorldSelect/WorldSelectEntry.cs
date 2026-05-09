using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldSelectEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _worldNameText;
    [SerializeField] private TextMeshProUGUI _lastPlayedText;
    [SerializeField] private Button _selectButton;
    [SerializeField] private Button _deleteButton;

    private string _worldGuid;
    private string _worldName;
    private Action<string> _onSelect;
    private Action<string, string> _onDelete;

    public void Setup(string worldGuid, string worldName, string lastPlayed,
        Action<string> onSelect, Action<string, string> onDelete)
    {
        _worldGuid = worldGuid;
        _worldName = worldName;
        _onSelect = onSelect;
        _onDelete = onDelete;

        if (_worldNameText != null)
            _worldNameText.text = worldName;

        if (_lastPlayedText != null)
            _lastPlayedText.text = string.IsNullOrEmpty(lastPlayed) ? "Never played" : $"Last played: {lastPlayed}";

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
        _onSelect?.Invoke(_worldGuid);
    }

    private void OnDeleteClicked()
    {
        _onDelete?.Invoke(_worldGuid, _worldName);
    }

    private void OnDestroy()
    {
        if (_selectButton != null)
            _selectButton.onClick.RemoveListener(OnSelectClicked);

        if (_deleteButton != null)
            _deleteButton.onClick.RemoveListener(OnDeleteClicked);
    }
}
