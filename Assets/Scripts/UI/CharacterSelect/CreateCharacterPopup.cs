using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateCharacterPopup : MonoBehaviour
{
    [SerializeField] private TMP_InputField _nameInput;
    [SerializeField] private Button _createButton;
    [SerializeField] private Button _cancelButton;

    private Action<string> _onCreated;

    private void Awake()
    {
        gameObject.SetActive(false);

        if (_createButton != null)
            _createButton.onClick.AddListener(OnCreateClicked);

        if (_cancelButton != null)
            _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    public void Show(Action<string> onCreated)
    {
        _onCreated = onCreated;

        if (_nameInput != null)
            _nameInput.text = "";

        gameObject.SetActive(true);
    }

    private async void OnCreateClicked()
    {
        string characterName = _nameInput != null ? _nameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(characterName))
        {
            Debug.LogWarning("[CreateCharacterPopup] Character name cannot be empty.");
            return;
        }

        string characterGuid = Guid.NewGuid().ToString();

        var races = Resources.LoadAll<RaceSO>("Data/Race");
        var race = races.Length > 0 ? races[UnityEngine.Random.Range(0, races.Length)] : null;

        var profileComponentData = new ProfileSaveData
        {
            raceId = race != null ? race.name : "",
            gender = UnityEngine.Random.value > 0.5f ? 0 : 1,
            visualSeed = UnityEngine.Random.Range(0, int.MaxValue),
            archetypeId = "Human"
        };

        var profile = new CharacterProfileSaveData
        {
            characterGuid = characterGuid,
            characterName = characterName,
            archetypeId = "Human",
            timestamp = DateTime.UtcNow.ToString("o"),
            componentStates = new Dictionary<string, string>
            {
                ["CharacterProfile"] = JsonConvert.SerializeObject(profileComponentData)
            },
            worldAssociations = new System.Collections.Generic.List<WorldAssociation>()
        };

        await SaveFileHandler.WriteProfileAsync(characterGuid, profile);

        gameObject.SetActive(false);
        _onCreated?.Invoke(characterGuid);
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
