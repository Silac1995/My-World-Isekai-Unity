using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_Inventory : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private GameObject _itemSlotPrefab;
    [SerializeField] private TextMeshProUGUI _inventoryCapacity;

    // Le GameObject qui possède le GridLayoutGroup (ex: "Content")
    [SerializeField] private Transform _slotContainer;

    [Header("Data")]
    [SerializeField] private Inventory _inventory;
    private List<UI_ItemSlot> _instantiatedSlots = new List<UI_ItemSlot>();

    public void Initialize(Inventory inventory)
    {
        // On ne fait plus de return immédiat ici
        _inventory = inventory;

        if (_inventory == null)
        {
            Debug.LogWarning($"<color=orange>[UI_Inventory]</color> L'inventaire est null (Sac retiré).");
            if (_inventoryCapacity != null) _inventoryCapacity.text = "No Bag";

            // IMPORTANT : On doit quand même appeler RefreshDisplay pour vider les slots visuels !
            RefreshDisplay();
            return;
        }

        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        // 1. Nettoyage (Toujours exécuté, que l'inventaire soit null ou non)
        foreach (var slot in _instantiatedSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        _instantiatedSlots.Clear();

        foreach (Transform child in _slotContainer)
        {
            if (child != null) Destroy(child.gameObject);
        }

        // Si on n'a plus d'inventaire, on s'arrête APRÈS avoir nettoyé
        if (_inventory == null) return;

        if (_slotContainer == null)
        {
            Debug.LogError($"<color=red>[UI_Inventory]</color> _slotContainer n'est pas assigné !");
            return;
        }

        // 2. Création (Uniquement si l'inventaire existe)
        if (_itemSlotPrefab == null) return;

        int occupiedSlots = 0;
        foreach (ItemSlot slotData in _inventory.ItemSlots)
        {
            GameObject newSlotObj = Instantiate(_itemSlotPrefab, _slotContainer);
            UI_ItemSlot slotScript = newSlotObj.GetComponent<UI_ItemSlot>();

            if (slotScript != null)
            {
                slotScript.Initialize(this, slotData);
                _instantiatedSlots.Add(slotScript);
                if (!slotData.IsEmpty()) occupiedSlots++;
            }
        }

        // 3. Capacité
        UpdateCapacityText(occupiedSlots, _inventory.Capacity);
    }

    private void UpdateCapacityText(int occupied, int total)
    {
        if (_inventoryCapacity == null) return;
        _inventoryCapacity.text = $"{occupied} / {total}";
        _inventoryCapacity.color = (total > 0 && occupied >= total) ? Color.red : Color.white;
    }
}