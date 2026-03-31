using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectPanel : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI _worldNameHeader;

    [Header("List")]
    [SerializeField] private Transform _entryContainer;
    [SerializeField] private CharacterSelectEntry _entryPrefab;

    [Header("Buttons")]
    [SerializeField] private Button _createCharacterButton;
    [SerializeField] private Button _backButton;

    [Header("Empty State")]
    [SerializeField] private TextMeshProUGUI _emptyStateText;

    [Header("Popups")]
    [SerializeField] private CreateCharacterPopup _createCharacterPopup;
    [SerializeField] private DeleteConfirmPopup _deleteConfirmPopup;

    [Header("Navigation")]
    [SerializeField] private WorldSelectPanel _worldSelectPanel;

    private string _selectedWorldGuid;
    private string _selectedWorldName;
    private readonly List<CharacterSelectEntry> _spawnedEntries = new List<CharacterSelectEntry>();
    private bool _listenersAdded;

    private void EnsureListeners()
    {
        if (_listenersAdded) return;
        _listenersAdded = true;

        if (_createCharacterButton != null)
            _createCharacterButton.onClick.AddListener(OnCreateCharacterClicked);

        if (_backButton != null)
            _backButton.onClick.AddListener(OnBackClicked);
    }

    private void Awake()
    {
        EnsureListeners();
    }

    public void Open(string worldGuid, string worldName)
    {
        EnsureListeners();
        _selectedWorldGuid = worldGuid;
        _selectedWorldName = worldName;

        if (_worldNameHeader != null)
            _worldNameHeader.text = worldName;

        gameObject.SetActive(true);
        RefreshCharacterList();
    }

    private void RefreshCharacterList()
    {
        ClearEntries();

        List<CharacterProfileSaveData> profiles = SaveFileHandler.GetAllProfiles();

        bool hasProfiles = profiles.Count > 0;

        if (_emptyStateText != null)
            _emptyStateText.gameObject.SetActive(!hasProfiles);

        if (!hasProfiles)
        {
            if (_emptyStateText != null)
                _emptyStateText.text = "No characters yet";
            return;
        }

        foreach (CharacterProfileSaveData profile in profiles)
        {
            if (_entryPrefab == null || _entryContainer == null) continue;

            bool hasWorldSave = profile.worldAssociations != null &&
                profile.worldAssociations.Any(wa => wa.worldGuid == _selectedWorldGuid);

            CharacterSelectEntry entry = Instantiate(_entryPrefab, _entryContainer);
            entry.gameObject.SetActive(true);
            entry.Setup(
                profile.characterGuid,
                profile.characterName,
                hasWorldSave,
                OnCharacterSelected,
                OnCharacterDeleteRequested
            );
            _spawnedEntries.Add(entry);
        }
    }

    private void ClearEntries()
    {
        foreach (CharacterSelectEntry entry in _spawnedEntries)
        {
            if (entry != null)
                Destroy(entry.gameObject);
        }
        _spawnedEntries.Clear();
    }

    private void OnCharacterSelected(string characterGuid)
    {
        if (GameLauncher.Instance != null)
        {
            GameLauncher.Instance.SelectedWorldGuid = _selectedWorldGuid;
            GameLauncher.Instance.SelectedCharacterGuid = characterGuid;
            GameLauncher.Instance.LaunchSolo();
        }
        else
        {
            Debug.LogError("[CharacterSelectPanel] GameLauncher.Instance is null. Cannot launch game.");
        }
    }

    private void OnCharacterDeleteRequested(string characterGuid, string characterName)
    {
        if (_deleteConfirmPopup != null)
        {
            _deleteConfirmPopup.Show(characterName, async () =>
            {
                await SaveFileHandler.DeleteProfileAsync(characterGuid);
                RefreshCharacterList();
            });
        }
    }

    private void OnCreateCharacterClicked()
    {
        if (_createCharacterPopup != null)
        {
            _createCharacterPopup.Show((createdGuid) =>
            {
                RefreshCharacterList();
            });
        }
    }

    private void OnBackClicked()
    {
        gameObject.SetActive(false);

        if (_worldSelectPanel != null)
            _worldSelectPanel.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        if (_createCharacterButton != null)
            _createCharacterButton.onClick.RemoveListener(OnCreateCharacterClicked);

        if (_backButton != null)
            _backButton.onClick.RemoveListener(OnBackClicked);
    }
}
