using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterEquipmentUI : MonoBehaviour
{
    [SerializeField] private Character _character;
    
    [Header("Window Components")]
    [SerializeField] private TextMeshProUGUI _selectedEquipmentLayer;
    [SerializeField] private UI_Inventory _ui_inventory;

    [Header("Layer Selection")]
    [SerializeField] private Button _buttonArmorLayer;
    [SerializeField] private Button _buttonClothingLayer;
    [SerializeField] private Button _buttonUnderwearLayer;

    [Header("Slot Actions")]
    [SerializeField] private Button _unequipHeadButton;
    [SerializeField] private Button _unequipArmorButton;
    [SerializeField] private Button _unequipGlovesButton;
    [SerializeField] private Button _unequipPantsButton;
    [SerializeField] private Button _unequipBootsButton;
    [SerializeField] private Button _unequipBagButton;

    [Header("Hands (Carry)")]
    [Tooltip("Drops the item the character is currently carrying in their hands (HandsController.CarriedItem). Hidden / disabled when nothing is being carried. Mirrors the G hotkey in PlayerController.")]
    [SerializeField] private Button _dropHandsItemButton;

    private EquipmentLayer _currentLayer;

    private void OnEnable()
    {
        RemoveAllButtonListeners();
        SetupButtonEvents();
        
        if (_character != null) UpdateUI();
    }

    /// <summary>
    /// Initializes the window with the target character.
    /// </summary>
    public void Initialize(Character target)
    {
        if (target == null) return;

        // Cleanup previous character subscription if any
        if (_character != null && _character.CharacterEquipment != null)
        {
            _character.CharacterEquipment.OnEquipmentChanged -= UpdateUI;
        }

        _character = target;

        if (_ui_inventory != null)
        {
            _ui_inventory.Initialize(_character.CharacterEquipment.GetInventory(), _character);
        }

        if (_character.CharacterEquipment != null)
        {
            _character.CharacterEquipment.OnEquipmentChanged += UpdateUI;
        }

        RemoveAllButtonListeners();
        SetupButtonEvents();
        SwitchLayer(WearableLayerEnum.Armor);
    }

    private void SetupButtonEvents()
    {
        _buttonArmorLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Armor));
        _buttonClothingLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Clothing));
        _buttonUnderwearLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Underwear));

        _unequipHeadButton?.onClick.AddListener(() => RequestUnequip(WearableType.Helmet));
        _unequipArmorButton?.onClick.AddListener(() => RequestUnequip(WearableType.Armor));
        _unequipGlovesButton?.onClick.AddListener(() => RequestUnequip(WearableType.Gloves));
        _unequipPantsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Pants));
        _unequipBootsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Boots));
        _unequipBagButton?.onClick.AddListener(() => RequestUnequip(WearableType.Bag));

        _dropHandsItemButton?.onClick.AddListener(RequestDropHandsItem);
    }

    private void RemoveAllButtonListeners()
    {
        _buttonArmorLayer?.onClick.RemoveAllListeners();
        _buttonClothingLayer?.onClick.RemoveAllListeners();
        _buttonUnderwearLayer?.onClick.RemoveAllListeners();
        _unequipHeadButton?.onClick.RemoveAllListeners();
        _unequipArmorButton?.onClick.RemoveAllListeners();
        _unequipGlovesButton?.onClick.RemoveAllListeners();
        _unequipPantsButton?.onClick.RemoveAllListeners();
        _unequipBootsButton?.onClick.RemoveAllListeners();
        _unequipBagButton?.onClick.RemoveAllListeners();

        _dropHandsItemButton?.onClick.RemoveAllListeners();
    }

    public void SetupUI(Character newCharacter)
    {
        if (newCharacter == null) return;
        _character = newCharacter;
        SwitchLayer(WearableLayerEnum.Armor);
    }

    private void SwitchLayer(WearableLayerEnum layerType)
    {
        if (_character == null || _character.CharacterEquipment == null) return;

        switch (layerType)
        {
            case WearableLayerEnum.Armor: _currentLayer = _character.CharacterEquipment.ArmorLayer; break;
            case WearableLayerEnum.Clothing: _currentLayer = _character.CharacterEquipment.ClothingLayer; break;
            case WearableLayerEnum.Underwear: _currentLayer = _character.CharacterEquipment.UnderwearLayer; break;
        }

        if (_selectedEquipmentLayer != null)
            _selectedEquipmentLayer.text = layerType.ToString() + " Layer";

        UpdateUI();
    }

    private void RequestUnequip(WearableType slotType)
    {
        if (_character == null || _character.CharacterEquipment == null) return;

        if (slotType == WearableType.Bag)
            _character.CharacterEquipment.Unequip(WearableLayerEnum.Bag, slotType);
        else
            _character.CharacterEquipment.Unequip(GetEnumFromCurrentLayer(), slotType);

        UpdateUI();
    }

    public void UpdateUI()
    {
        if (_character == null || _character.CharacterEquipment == null || _currentLayer == null) return;

        UpdateSlotButtonState(_unequipHeadButton, WearableType.Helmet);
        UpdateSlotButtonState(_unequipArmorButton, WearableType.Armor);
        UpdateSlotButtonState(_unequipGlovesButton, WearableType.Gloves);
        UpdateSlotButtonState(_unequipPantsButton, WearableType.Pants);
        UpdateSlotButtonState(_unequipBootsButton, WearableType.Boots);

        if (_unequipBagButton != null)
        {
            var bag = _character.CharacterEquipment.GetBagInstance();
            UpdateSlotText(_unequipBagButton, bag);
            _unequipBagButton.interactable = (bag != null);
        }

        if (_ui_inventory != null)
        {
            _ui_inventory.Initialize(_character.CharacterEquipment.GetInventory(), _character);
        }

        RefreshHandsButton();
    }

    /// <summary>
    /// Refreshes the "drop carried item" button. Called from UpdateUI (whenever equipment changes)
    /// AND from Update() each frame, because HandsController carry state changes do not fire
    /// OnEquipmentChanged — the carried-in-hands slot is separate from the inventory.
    /// </summary>
    private void RefreshHandsButton()
    {
        if (_dropHandsItemButton == null) return;

        var hands = _character != null && _character.CharacterVisual != null
            ? _character.CharacterVisual.BodyPartsController?.HandsController
            : null;

        ItemInstance carried = hands != null ? hands.CarriedItem : null;
        UpdateSlotText(_dropHandsItemButton, carried);

        bool busy = _character != null
            && _character.CharacterActions != null
            && _character.CharacterActions.CurrentAction != null;

        _dropHandsItemButton.interactable = (carried != null) && !busy;
    }

    private void Update()
    {
        // Hands carry state has no event — poll while the panel is visible.
        // Only runs while this GameObject is active (panel is open).
        if (_character != null) RefreshHandsButton();
    }

    private void RequestDropHandsItem()
    {
        if (_character == null) return;

        var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return;

        if (_character.CharacterActions == null) return;
        if (_character.CharacterActions.CurrentAction != null) return;

        _character.CharacterActions.ExecuteAction(new CharacterDropItem(_character, hands.CarriedItem));
    }

    private void UpdateSlotButtonState(Button btn, WearableType type)
    {
        if (btn == null) return;
        EquipmentInstance item = _currentLayer.GetInstance(type);
        UpdateSlotText(btn, item);
        btn.interactable = (item != null);
    }

    private void UpdateSlotText(Button btn, ItemInstance item)
    {
        TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.text = (item != null) ? item.ItemSO.ItemName : "None";
            btnText.color = (item != null) ? Color.white : new Color(1, 1, 1, 0.5f);
        }
    }

    private WearableLayerEnum GetEnumFromCurrentLayer()
    {
        if (_currentLayer is ArmorLayer) return WearableLayerEnum.Armor;
        if (_currentLayer is UnderwearLayer) return WearableLayerEnum.Underwear;
        return WearableLayerEnum.Clothing;
    }

    public void CloseUI()
    {
        this.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_character != null && _character.CharacterEquipment != null)
        {
            _character.CharacterEquipment.OnEquipmentChanged -= UpdateUI;
        }
    }
}
