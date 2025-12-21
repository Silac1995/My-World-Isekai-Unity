using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RaceSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown raceDropdown;
    [SerializeField] private TMP_Dropdown characterDefaultPrefab_dropdown;
    [SerializeField] private TMP_InputField spawnNumberInput;
    [SerializeField] private Toggle isPlayerToggle;
    [SerializeField] private Button spawnButton;

    [Header("Prefabs & Managers")]
    [SerializeField] private Transform spawnPoint;

    [SerializeField] private List<RaceSO> availableRaces = new List<RaceSO>();
    [SerializeField] private RaceSO selectedRace;
    [SerializeField] private GameObject selectedCharacterDefaultPrefab;

    private void Start()
    {
        LoadRaces();

        raceDropdown.onValueChanged.AddListener(OnRaceSelected);
        characterDefaultPrefab_dropdown.onValueChanged.AddListener(OnPrefabSelected);

        // Sélection initiale
        if (availableRaces.Count > 0)
        {
            raceDropdown.value = 0;
            OnRaceSelected(0);

            if (selectedRace.character_prefabs.Count > 0)
            {
                characterDefaultPrefab_dropdown.value = 0;
                OnPrefabSelected(0);
            }
        }

        spawnButton.onClick.AddListener(SpawnCharacters);
    }

    private void LoadRaces()
    {
        RaceSO[] races = Resources.LoadAll<RaceSO>("Data/Races");
        availableRaces.Clear();
        availableRaces.AddRange(races);

        raceDropdown.ClearOptions();
        List<string> options = new List<string>();
        foreach (RaceSO race in availableRaces)
        {
            options.Add(race.raceName);
        }
        raceDropdown.AddOptions(options);
        raceDropdown.RefreshShownValue();
    }

    private void OnRaceSelected(int index)
    {
        if (index < 0 || index >= availableRaces.Count) return;

        selectedRace = availableRaces[index];

        characterDefaultPrefab_dropdown.ClearOptions();
        List<string> options = new List<string>();

        foreach (GameObject prefab in selectedRace.character_prefabs)
        {
            options.Add(prefab.name);
        }

        characterDefaultPrefab_dropdown.AddOptions(options);
        characterDefaultPrefab_dropdown.value = 0;
        characterDefaultPrefab_dropdown.RefreshShownValue();

        // Met à jour le prefab sélectionné
        if (selectedRace.character_prefabs.Count > 0)
            selectedCharacterDefaultPrefab = selectedRace.character_prefabs[0];
    }

    private void OnPrefabSelected(int index)
    {
        if (selectedRace == null || index < 0 || index >= selectedRace.character_prefabs.Count) return;

        selectedCharacterDefaultPrefab = selectedRace.character_prefabs[index];
    }

    private void SpawnCharacters()
    {
        if (selectedCharacterDefaultPrefab == null || selectedRace == null) return;

        int number = 1;
        if (!string.IsNullOrEmpty(spawnNumberInput.text) && int.TryParse(spawnNumberInput.text, out int parsed))
            number = Mathf.Max(1, parsed);

        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;

        for (int i = 0; i < number; i++)
        {
            Character character = SpawnManager.Instance.SpawnCharacter(
                pos: pos,
                health: 50f,           // ajuste ou passe via UI
                mana: 50f,              // ajuste
                str: 10f, // exemple
                agi: 10f,   // exemple
                race: selectedRace,
                visualPrefab: selectedCharacterDefaultPrefab,
                isPlayer: isPlayerToggle.isOn && i == 0
            );

            if (character == null)
                Debug.LogError("Échec spawn personnage");
        }
    }
}
