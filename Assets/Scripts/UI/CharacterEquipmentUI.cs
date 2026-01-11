using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class CharacterEquipmentUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character character;
    private EquipmentLayer currentLayer; // Le layer actuellement sélectionné (Armor, Clothing ou Underwear)

    [Header("General UI")]
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private TMP_Dropdown layerDropdown;

    [Header("Equipment Slot Texts")]
    [SerializeField] private TextMeshProUGUI headgearText;
    [SerializeField] private TextMeshProUGUI armorText;
    [SerializeField] private TextMeshProUGUI glovesText;
    [SerializeField] private TextMeshProUGUI pantsText;
    [SerializeField] private TextMeshProUGUI bootsText;
    [SerializeField] private TextMeshProUGUI bagText;

    [Header("Unequip Buttons")]
    [SerializeField] private Button unequipHeadButton;
    [SerializeField] private Button unequipArmorButton;
    [SerializeField] private Button unequipGlovesButton;
    [SerializeField] private Button unequipPantsButton;
    [SerializeField] private Button unequipBootsButton;
    [SerializeField] private Button unequipBagButton;

    private List<EquipmentLayer> availableLayers = new List<EquipmentLayer>();

    private void Update()
    {
        // Si tu appuies sur F1, on force la recherche
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("Force SetupUI...");
            // On cherche le premier Character dans la scène pour tester
            Character testChar = Object.FindFirstObjectByType<Character>();
            if (testChar != null) SetupUI(testChar);
        }
    }
    private void Start()
    {
        // 1. Abonnement au changement du Dropdown
        if (layerDropdown != null)
        {
            layerDropdown.onValueChanged.AddListener(OnDropdownLayerChanged);
        }

        // 2. Abonnement des boutons de déséquipement mis à jour
        // On passe maintenant par la méthode générique qui utilise CharacterEquipment
        unequipHeadButton?.onClick.AddListener(() => RequestUnequip(WearableType.Helmet));
        unequipArmorButton?.onClick.AddListener(() => RequestUnequip(WearableType.Armor));
        unequipGlovesButton?.onClick.AddListener(() => RequestUnequip(WearableType.Gloves));
        unequipPantsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Pants));
        unequipBootsButton?.onClick.AddListener(() => RequestUnequip(WearableType.Boots));
        unequipBagButton?.onClick.AddListener(() => RequestUnequip(WearableType.Bag));

        if (character != null) SetupUI(character);
    }

    /// <summary>
    /// Appelé par la caméra ou le manager pour lier l'UI à un personnage précis
    /// </summary>
    public void SetupUI(Character newCharacter)
    {
        if (newCharacter == null)
        {
            Debug.LogWarning("[UI] SetupUI appelé avec un personnage NUL");
            return;
        }

        character = newCharacter;

        // On force la recherche sur TOUTE la hiérarchie du perso
        availableLayers = character.GetComponentsInChildren<EquipmentLayer>(true).ToList();

        Debug.Log($"[UI] Tentative de Setup pour {character.name}. Couches trouvées : {availableLayers.Count}");

        if (availableLayers.Count > 0)
        {
            PopulateLayerDropdown();
            currentLayer = availableLayers[0];
            UpdateUI();
        }
        else
        {
            Debug.LogError($"[UI] {character.name} n'a aucun composant EquipmentLayer sur lui ou ses enfants !");
        }
    }

    private void PopulateLayerDropdown()
    {
        if (layerDropdown == null) return;

        layerDropdown.ClearOptions();
        List<string> options = new List<string>();

        if (availableLayers.Count == 0)
        {
            Debug.LogError("Attention : Aucun EquipmentLayer trouvé sur " + character.name);
            options.Add("Aucun Layer trouvé");
        }
        else
        {
            foreach (var layer in availableLayers)
            {
                options.Add(layer.GetType().Name);
                Debug.Log("Layer ajouté au dropdown : " + layer.GetType().Name);
            }
        }

        layerDropdown.AddOptions(options);
        layerDropdown.RefreshShownValue();
    }

    private void OnDropdownLayerChanged(int index)
    {
        if (index >= 0 && index < availableLayers.Count)
        {
            currentLayer = availableLayers[index];
            UpdateUI();
            Debug.Log($"<color=orange>[UI]</color> Couche active : {currentLayer.GetType().Name}");
        }
    }

    /// <summary>
    /// Envoie une requête de déséquipement au CharacterEquipment global.
    /// </summary>
    private void RequestUnequip(WearableType slotType)
    {
        if (character == null || character.CharacterEquipment == null) return;

        // 1. On détermine le LayerEnum basé sur le nom du layer actuel (ou son type)
        // On pourrait aussi stocker l'enum directement dans le composant EquipmentLayer
        WearableLayerEnum layerEnum = GetEnumFromCurrentLayer();

        // 2. On appelle la méthode centralisée que tu as implémentée
        character.CharacterEquipment.Unequip(layerEnum, slotType);

        // 3. On rafraîchit l'UI
        UpdateUI();
    }
    /// <summary>
    /// Helper pour convertir le layer sélectionné dans le dropdown en EquipmentLayerEnum
    /// </summary>
    private WearableLayerEnum GetEnumFromCurrentLayer()
    {
        if (currentLayer is ArmorLayer) return WearableLayerEnum.Armor;
        if (currentLayer is ClothingLayer) return WearableLayerEnum.Clothing;
        if (currentLayer is UnderwearLayer) return WearableLayerEnum.Underwear;

        return WearableLayerEnum.Clothing; // Valeur par défaut
    }
    /// <summary>
    /// Déséquipe l'objet uniquement sur la couche sélectionnée dans le dropdown
    /// </summary>
    private void UnequipFromCurrentLayer(WearableType type)
    {
        if (currentLayer == null || character == null) return;

        // 1. On récupère d'abord l'instance de l'objet porté
        EquipmentInstance itemToDrop = currentLayer.GetInstance(type);

        if (itemToDrop != null)
        {
            // 2. On le retire du personnage (visuels et données)
            currentLayer.Unequip(type);

            // 3. On demande au personnage de créer l'objet dans le monde
            character.DropItem(itemToDrop);

            // 4. On rafraîchit l'UI
            UpdateUI();

            Debug.Log($"[UI] {itemToDrop.ItemSO.ItemName} a été retiré et jeté au sol.");
        }
    }

    public void UpdateUI()
    {
        if (character == null || character.CharacterEquipment == null || currentLayer == null) return;

        // Mise à jour des textes
        headgearText.text = GetEquipmentName(WearableType.Helmet);
        armorText.text = GetEquipmentName(WearableType.Armor);
        glovesText.text = GetEquipmentName(WearableType.Gloves);
        pantsText.text = GetEquipmentName(WearableType.Pants);
        bootsText.text = GetEquipmentName(WearableType.Boots);

        // --- LOGIQUE SPÉCIFIQUE POUR LE SAC ---
        // Le sac est maintenant global dans CharacterEquipment, on ne le cherche plus dans le currentLayer
        if (bagText != null)
        {
            // On accède à l'underscore _bag via une propriété publique Bag si tu l'as créée, 
            // ou on adapte GetEquipmentName pour regarder dans CharacterEquipment
            bagText.text = GetGlobalBagName();
        }

        // Mise à jour de l'état des boutons
        ToggleButtonState(unequipHeadButton, WearableType.Helmet);
        ToggleButtonState(unequipArmorButton, WearableType.Armor);
        ToggleButtonState(unequipGlovesButton, WearableType.Gloves);
        ToggleButtonState(unequipPantsButton, WearableType.Pants);
        ToggleButtonState(unequipBootsButton, WearableType.Boots);

        // État du bouton sac
        if (unequipBagButton != null)
        {
            // On vérifie si CharacterEquipment a un sac
            unequipBagButton.interactable = character.CharacterEquipment.HasBagEquipped();
        }
    }

    private string GetGlobalBagName()
    {
        // On demande directement au manager global
        var bag = character.CharacterEquipment.GetBagInstance();
        return bag != null ? bag.ItemSO.ItemName : "<color=#888888>Vide</color>";
    }

    private string GetEquipmentName(WearableType type)
    {
        EquipmentInstance inst = currentLayer.GetInstance(type);
        if (inst != null && inst.ItemSO != null)
        {
            return inst.ItemSO.ItemName;
        }
        return "<color=#888888>Vide</color>";
    }

    private void ToggleButtonState(Button btn, WearableType type)
    {
        if (btn != null)
        {
            // On désactive l'interaction du bouton s'il n'y a rien à déséquiper
            btn.interactable = currentLayer.GetInstance(type) != null;
        }
    }

    // Utilisé pour forcer un rafraîchissement externe (après un ramassage par exemple)
    public void Refresh() => UpdateUI();
}