using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateWorldPopup : MonoBehaviour
{
    [SerializeField] private TMP_InputField _nameInput;
    [SerializeField] private TMP_InputField _seedInput;
    [SerializeField] private Button _createButton;
    [SerializeField] private Button _cancelButton;

    private Action<string, string> _onCreated;

    private bool _listenersAdded;

    private void EnsureListeners()
    {
        if (_listenersAdded) return;
        _listenersAdded = true;

        if (_createButton != null)
            _createButton.onClick.AddListener(OnCreateClicked);

        if (_cancelButton != null)
            _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void Awake()
    {
        EnsureListeners();
    }

    public void Show(Action<string, string> onCreated)
    {
        EnsureListeners();
        _onCreated = onCreated;

        if (_nameInput != null)
            _nameInput.text = "";

        if (_seedInput != null)
            _seedInput.text = "";

        gameObject.SetActive(true);
    }

    private async void OnCreateClicked()
    {
        string worldName = _nameInput != null ? _nameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(worldName))
        {
            Debug.LogWarning("[CreateWorldPopup] World name cannot be empty.");
            return;
        }

        string worldGuid = Guid.NewGuid().ToString();
        string seedText = _seedInput != null ? _seedInput.text.Trim() : "";
        string worldSeed = string.IsNullOrEmpty(seedText)
            ? UnityEngine.Random.Range(0, int.MaxValue).ToString()
            : seedText;

        var data = new GameSaveData
        {
            metadata = new SaveSlotMetadata
            {
                worldGuid = worldGuid,
                worldName = worldName,
                worldSeed = worldSeed,
                timestamp = DateTime.UtcNow.ToString("o"),
                isEmpty = false
            }
        };

        await SaveFileHandler.WriteWorldAsync(worldGuid, data);

        gameObject.SetActive(false);
        _onCreated?.Invoke(worldGuid, worldName);
        _onCreated = null;
    }

    private void OnCancelClicked()
    {
        _onCreated = null;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_createButton != null)
            _createButton.onClick.RemoveListener(OnCreateClicked);

        if (_cancelButton != null)
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
    }
}
