using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldSelectPanel : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform _entryContainer;
    [SerializeField] private WorldSelectEntry _entryPrefab;

    [Header("Buttons")]
    [SerializeField] private Button _createNewWorldButton;
    [SerializeField] private Button _backButton;

    [Header("Empty State")]
    [SerializeField] private TextMeshProUGUI _emptyStateText;

    [Header("Popups")]
    [SerializeField] private CreateWorldPopup _createWorldPopup;
    [SerializeField] private DeleteConfirmPopup _deleteConfirmPopup;

    [Header("Navigation")]
    [SerializeField] private CharacterSelectPanel _characterSelectPanel;

    private readonly List<WorldSelectEntry> _spawnedEntries = new List<WorldSelectEntry>();

    public event Action OnBackRequested;

    private void Awake()
    {
        gameObject.SetActive(false);

        if (_createNewWorldButton != null)
            _createNewWorldButton.onClick.AddListener(OnCreateNewWorldClicked);

        if (_backButton != null)
            _backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        RefreshWorldList();
    }

    public void RefreshWorldList()
    {
        ClearEntries();

        List<GameSaveData> worlds = SaveFileHandler.GetAllWorlds();

        bool hasWorlds = worlds.Count > 0;

        if (_emptyStateText != null)
            _emptyStateText.gameObject.SetActive(!hasWorlds);

        if (!hasWorlds)
        {
            if (_emptyStateText != null)
                _emptyStateText.text = "No worlds yet";
            return;
        }

        foreach (GameSaveData world in worlds)
        {
            if (_entryPrefab == null || _entryContainer == null) continue;

            WorldSelectEntry entry = Instantiate(_entryPrefab, _entryContainer);
            entry.gameObject.SetActive(true);
            entry.Setup(
                world.metadata.worldGuid,
                world.metadata.worldName,
                world.metadata.timestamp,
                OnWorldSelected,
                OnWorldDeleteRequested
            );
            _spawnedEntries.Add(entry);
        }
    }

    private void ClearEntries()
    {
        foreach (WorldSelectEntry entry in _spawnedEntries)
        {
            if (entry != null)
                Destroy(entry.gameObject);
        }
        _spawnedEntries.Clear();
    }

    private void OnWorldSelected(string worldGuid)
    {
        if (_characterSelectPanel != null)
        {
            // Find world name for header
            List<GameSaveData> worlds = SaveFileHandler.GetAllWorlds();
            string worldName = worldGuid;
            foreach (GameSaveData w in worlds)
            {
                if (w.metadata.worldGuid == worldGuid)
                {
                    worldName = w.metadata.worldName;
                    break;
                }
            }

            _characterSelectPanel.Open(worldGuid, worldName);
            gameObject.SetActive(false);
        }
    }

    private void OnWorldDeleteRequested(string worldGuid, string worldName)
    {
        if (_deleteConfirmPopup != null)
        {
            _deleteConfirmPopup.Show(worldName, async () =>
            {
                await SaveFileHandler.DeleteWorldAsync(worldGuid);
                RefreshWorldList();
            });
        }
    }

    private void OnCreateNewWorldClicked()
    {
        if (_createWorldPopup != null)
        {
            _createWorldPopup.Show((createdGuid, createdName) =>
            {
                RefreshWorldList();
            });
        }
    }

    private void OnBackClicked()
    {
        OnBackRequested?.Invoke();
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_createNewWorldButton != null)
            _createNewWorldButton.onClick.RemoveListener(OnCreateNewWorldClicked);

        if (_backButton != null)
            _backButton.onClick.RemoveListener(OnBackClicked);
    }
}
