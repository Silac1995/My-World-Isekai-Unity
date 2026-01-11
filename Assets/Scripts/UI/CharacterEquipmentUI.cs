using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

public class CharacterEquipmentUI : MonoBehaviour
{
    [SerializeField] private Button _buttonClose;
    [SerializeField] private UI_Inventory _ui_inventory;

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
        // Ajout du listener pour le bouton fermer
        _buttonClose?.onClick.AddListener(CloseUI);

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
        if (target == null) return;

        // ... (ton code de désabonnement existant) ...

        _character = target;

        // --- MISE À JOUR ICI ---
        // On initialise l'inventaire en même temps que le reste
        if (_ui_inventory != null)
        {
            _ui_inventory.Initialize(_character.CharacterEquipment.GetInventory());
        }

        // On s'abonne à l'événement
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
        _buttonClose?.onClick.AddListener(CloseUI);
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
        _buttonClose?.onClick.RemoveAllListeners(); // On nettoie aussi le bouton close
        _buttonArmorLayer?.onClick.RemoveAllListeners();
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

        // 1. Mise à jour des boutons de déséquipement (Head, Armor, etc.)
        UpdateSlotButtonState(_unequipHeadButton, WearableType.Helmet);
        UpdateSlotButtonState(_unequipArmorButton, WearableType.Armor);
        UpdateSlotButtonState(_unequipGlovesButton, WearableType.Gloves);
        UpdateSlotButtonState(_unequipPantsButton, WearableType.Pants);
        UpdateSlotButtonState(_unequipBootsButton, WearableType.Boots);

        // 2. Mise à jour du bouton Sac
        if (_unequipBagButton != null)
        {
            var bag = _character.CharacterEquipment.GetBagInstance();
            UpdateSlotText(_unequipBagButton, bag);
            _unequipBagButton.interactable = (bag != null);
        }

        // 3. REFRESH DE L'INVENTAIRE
        // Dès que l'équipement change, on rafraîchit la grille d'objets
        if (_ui_inventory != null)
        {
            // On récupère l'inventaire actuel (car si on a changé de sac, l'instance a changé)
            _ui_inventory.Initialize(_character.CharacterEquipment.GetInventory());
            Debug.Log("<color=yellow>[UI_Sync]</color> Équipement modifié -> Refresh de la grille d'inventaire.");
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
    /// <summary>
    /// Ferme la fenêtre d'interface
    /// </summary>
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