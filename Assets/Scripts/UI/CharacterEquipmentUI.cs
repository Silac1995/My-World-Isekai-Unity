using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

public class CharacterEquipmentUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character _character;
    private EquipmentLayer _currentLayer;

    [Header("Layer Selection")]
    [SerializeField] private Button _buttonArmorLayer;
    [SerializeField] private Button _buttonClothingLayer;
    [SerializeField] private Button _buttonUnderwearLayer;
    [SerializeField] private TextMeshProUGUI _selectedEquipmentLayer;

    [Header("Unequip Buttons")]
    [SerializeField] private Button _unequipHeadButton;
    [SerializeField] private Button _unequipArmorButton;
    [SerializeField] private Button _unequipGlovesButton;
    [SerializeField] private Button _unequipPantsButton;
    [SerializeField] private Button _unequipBootsButton;
    [SerializeField] private Button _unequipBagButton;

    private void Start()
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

        if (_character != null) SetupUI(_character);
    }
    /// <summary>
    /// Initialise la fenêtre avec le personnage cible.
    /// </summary>
    public void Initialize(Character target)
    {
        if (target == null)
        {
            Debug.LogError("[UI] Tentative d'initialisation avec un Character nul.");
            return;
        }

        _character = target;

        // On nettoie les anciens listeners pour éviter les doublons (important si le prefab est réutilisé)
        RemoveAllButtonListeners();

        // On ré-abonne les boutons
        SetupButtonEvents();

        // On affiche la couche par défaut
        SwitchLayer(WearableLayerEnum.Armor);

        Debug.Log($"<color=green>[UI]</color> Fenêtre d'équipement initialisée pour : {_character.CharacterName}");
    }
    private void SetupButtonEvents()
    {
        // Couches
        _buttonArmorLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Armor));
        _buttonClothingLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Clothing));
        _buttonUnderwearLayer?.onClick.AddListener(() => SwitchLayer(WearableLayerEnum.Underwear));

        // Déséquipement
        _unequipHeadButton?.onClick.AddListener(() => RequestUnequip(WearableType.Helmet));
        _unequipArmorButton?.onClick.AddListener(() => RequestUnequip(WearableType.Armor));
        _unequipGlovesButton?.onClick.AddListener(() => RequestUnequip(WearableType.Gloves));
        _unequipPantsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Pants));
        _unequipBootsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Boots));
        _unequipBagButton?.onClick.AddListener(() => RequestUnequip(WearableType.Bag));
    }

    private void RemoveAllButtonListeners()
    {
        _buttonArmorLayer?.onClick.RemoveAllListeners();
        _buttonClothingLayer?.onClick.RemoveAllListeners();
        _buttonUnderwearLayer?.onClick.RemoveAllListeners();
        _unequipHeadButton?.onClick.RemoveAllListeners();
        _unequipArmorButton?.onClick.RemoveAllListeners();
        _unequipGlovesButton?.onClick.RemoveAllListeners();
        _unequipPantsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Pants));
        _unequipBootsButton?.onClick.RemoveAllListeners();
        _unequipBagButton?.onClick.RemoveAllListeners();
    }

    public void SetupUI(Character newCharacter)
    {
        if (newCharacter == null) return;
        _character = newCharacter;
        SwitchLayer(WearableLayerEnum.Armor);
    }

    /// <summary>
    /// Change la couche d'équipement affichée (Armor, Clothing, Underwear)
    /// </summary>
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

    /// <summary>
    /// Change la couleur du titre selon la catégorie sélectionnée
    /// </summary>
    private void UpdateLayerTextColor(WearableLayerEnum layerType)
    {
        if (_selectedEquipmentLayer == null) return;

        switch (layerType)
        {
            case WearableLayerEnum.Armor:
                _selectedEquipmentLayer.color = new Color(0.8f, 0.2f, 0.2f); // Rouge/Acier
                break;
            case WearableLayerEnum.Clothing:
                _selectedEquipmentLayer.color = new Color(0.2f, 0.8f, 0.2f); // Vert/Tissu
                break;
            case WearableLayerEnum.Underwear:
                _selectedEquipmentLayer.color = new Color(0.2f, 0.6f, 1f);   // Bleu/Sous-vêtements
                break;
        }
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

        // Mise à jour visuelle des slots de la couche actuelle
        UpdateSlotButtonState(_unequipHeadButton, WearableType.Helmet);
        UpdateSlotButtonState(_unequipArmorButton, WearableType.Armor);
        UpdateSlotButtonState(_unequipGlovesButton, WearableType.Gloves);
        UpdateSlotButtonState(_unequipPantsButton, WearableType.Pants);
        UpdateSlotButtonState(_unequipBootsButton, WearableType.Boots);

        // Mise à jour spécifique du sac (Global)
        if (_unequipBagButton != null)
        {
            var bag = _character.CharacterEquipment.GetBagInstance();
            UpdateSlotText(_unequipBagButton, bag);
            _unequipBagButton.interactable = (bag != null);
        }
    }

    /// <summary>
    /// Gère à la fois l'état interactif et le texte du bouton
    /// </summary>
    private void UpdateSlotButtonState(Button btn, WearableType type)
    {
        if (btn == null) return;

        EquipmentInstance item = _currentLayer.GetInstance(type);

        // 1. On change le texte ("None" ou nom de l'item)
        UpdateSlotText(btn, item);

        // 2. On active/désactive le bouton
        btn.interactable = (item != null);
    }

    private void UpdateSlotText(Button btn, ItemInstance item)
    {
        TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            // Si l'item existe, on affiche son nom, sinon "None"
            btnText.text = (item != null) ? item.ItemSO.ItemName : "None";

            // Optionnel : On change la couleur si c'est vide pour que ce soit plus lisible
            btnText.color = (item != null) ? Color.white : new Color(1, 1, 1, 0.5f);
        }
    }

    private WearableLayerEnum GetEnumFromCurrentLayer()
    {
        if (_currentLayer is ArmorLayer) return WearableLayerEnum.Armor;
        if (_currentLayer is UnderwearLayer) return WearableLayerEnum.Underwear;
        return WearableLayerEnum.Clothing;
    }
}