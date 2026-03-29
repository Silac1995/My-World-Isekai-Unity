using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugScript : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown raceDropdown;
    [SerializeField] private TMP_Dropdown characterDefaultPrefab_dropdown;
    [SerializeField] private TMP_InputField spawnNumberInput;
    [SerializeField] private Toggle isPlayerToggle;
    [SerializeField] private Button spawnButton;
    [SerializeField] private Button spawnItem;
    [SerializeField] private Button testInstallFurnitureBtn; // NOUVEAU
    [SerializeField] private TMP_Dropdown itemsSOList;
    [SerializeField] private Button switchButton;
    [SerializeField] private GameObject debugPanel;

    [Header("Prefabs & Managers")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("FurnitureItemSO to test placement via ghost HUD")]
    [SerializeField] private FurnitureItemSO _testFurnitureItemSO;

    private List<RaceSO> availableRaces = new List<RaceSO>();
    private List<ItemSO> availableItems = new List<ItemSO>();
    private RaceSO selectedRace;
    private GameObject selectedCharacterDefaultPrefab;

    private void Start()
    {
        LoadRaces();
        LoadItems();

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

        spawnItem.onClick.AddListener(OnSpawnItemClicked);
        spawnButton.onClick.AddListener(SpawnCharacters);
        
        if (testInstallFurnitureBtn != null)
        {
            testInstallFurnitureBtn.onClick.AddListener(TestInstallFurniture);
        }
        
        if (switchButton != null)
        {
            switchButton.onClick.AddListener(TogglePanel);
        }
    }

    public void TogglePanel()
    {
        if (debugPanel != null)
        {
            debugPanel.SetActive(!debugPanel.activeSelf);
        }
    }

    private void LoadItems()
    {
        ItemSO[] items = Resources.LoadAll<ItemSO>("Data/Item");
        availableItems.Clear();
        availableItems.AddRange(items);

        itemsSOList.ClearOptions();
        List<string> options = new List<string>();
        foreach (ItemSO item in availableItems)
        {
            options.Add(item.ItemName);
        }
        itemsSOList.AddOptions(options);
        itemsSOList.RefreshShownValue();
    }

    private void OnSpawnItemClicked()
    {
        if (availableItems.Count == 0 || itemsSOList.value >= availableItems.Count)
        {
            Debug.LogWarning("Aucun item sélectionné ou liste vide.");
            return;
        }

        ItemSO itemToSpawn = availableItems[itemsSOList.value];
        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        SpawnManager.Instance.SpawnItem(itemToSpawn, pos);
    }

    private void LoadRaces()
    {
        RaceSO[] races = Resources.LoadAll<RaceSO>("Data/Races");
        availableRaces.Clear();
        if (GameSessionManager.Instance != null && GameSessionManager.Instance.AvailableRaces != null)
        {
            availableRaces.AddRange(GameSessionManager.Instance.AvailableRaces);
        }

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
            SpawnManager.Instance.SpawnCharacter(
                pos: pos,
                race: selectedRace,
                visualPrefab: selectedCharacterDefaultPrefab,
                isPlayer: isPlayerToggle.isOn && i == 0
            );
        }
    }

    private void TestInstallFurniture()
    {
        Character player = FindObjectOfType<PlayerController>()?.GetComponent<Character>();
        if (player == null)
        {
            Debug.LogWarning("[Debug] No player found for furniture test.");
            return;
        }

        if (_testFurnitureItemSO == null)
        {
            Debug.LogError("[Debug] No _testFurnitureItemSO assigned in DebugScript inspector.");
            return;
        }

        if (player.FurniturePlacementManager == null)
        {
            Debug.LogError("[Debug] Player has no FurniturePlacementManager. Add it as a child CharacterSystem.");
            return;
        }

        player.FurniturePlacementManager.StartPlacementDebug(_testFurnitureItemSO);
        Debug.Log($"<color=green>[Debug]</color> Started furniture placement mode for {_testFurnitureItemSO.name}.");
    }
}
