using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_Inventory : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private GameObject _itemSlotPrefab;
    [SerializeField] private TextMeshProUGUI _inventoryCapacity;

    // Le GameObject qui possede le GridLayoutGroup (ex: "Content")
    [SerializeField] private Transform _slotContainer;

    [Header("Data")]
    [SerializeField] private Inventory _inventory;
    public Character CharacterOwner { get; private set; }
    private List<UI_ItemSlot> _instantiatedSlots = new List<UI_ItemSlot>();

    public void Initialize(Inventory inventory, Character character = null)
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= RefreshDisplay;
        }

        _inventory = inventory;
        if (character != null) CharacterOwner = character;

        if (_inventory == null)
        {
            Debug.Log($"<color=orange>[UI_Inventory]</color> No inventory active (Bag unequipped).");
            if (_inventoryCapacity != null) _inventoryCapacity.text = "No Bag";

            RefreshDisplay();
            return;
        }

        _inventory.OnInventoryChanged += RefreshDisplay;
        RefreshDisplay();
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= RefreshDisplay;
        }
    }

    public void RefreshDisplay()
    {
        foreach (var slot in _instantiatedSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        _instantiatedSlots.Clear();

        foreach (Transform child in _slotContainer)
        {
            if (child != null) Destroy(child.gameObject);
        }

        if (_inventory == null) return;

        if (_slotContainer == null)
        {
            Debug.LogError($"<color=red>[UI_Inventory]</color> _slotContainer n'est pas assigne !");
            return;
        }

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

        UpdateCapacityText(occupiedSlots, _inventory.Capacity);
    }

    private void UpdateCapacityText(int occupied, int total)
    {
        if (_inventoryCapacity == null) return;
        _inventoryCapacity.text = $"{occupied} / {total}";
        _inventoryCapacity.color = (total > 0 && occupied >= total) ? Color.red : Color.white;
    }
}
