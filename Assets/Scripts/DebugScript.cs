using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LEGACY debug harness. Superseded by <see cref="DevModeManager"/> + module-based
/// dev panel (DevSelectionModule, DevSpawnModule, future DevCinematicModule).
/// Do not add new functionality here — extend DevMode instead.
/// </summary>
[System.Obsolete("Use DevModeManager + module-based dev panel instead. F3 toggles the panel; "
                 + "/devmode on unlocks in release builds. New diagnostic features should be a "
                 + "Dev*Module under Assets/Scripts/Debug/DevMode/Modules/, not added here.", error: false)]
public class DebugScript : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button spawnItem;
    [SerializeField] private Button testInstallFurnitureBtn;
    [SerializeField] private TMP_Dropdown itemsSOList;
    [SerializeField] private Button switchButton;
    [SerializeField] private GameObject debugPanel;

    [Header("Prefabs & Managers")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("FurnitureItemSO to test placement via ghost HUD")]
    [SerializeField] private FurnitureItemSO _testFurnitureItemSO;

    private List<ItemSO> availableItems = new List<ItemSO>();

    private void Start()
    {
        LoadItems();

        spawnItem.onClick.AddListener(OnSpawnItemClicked);

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
