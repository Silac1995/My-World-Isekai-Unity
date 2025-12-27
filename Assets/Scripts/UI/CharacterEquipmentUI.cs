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

    [Header("Unequip Buttons")]
    [SerializeField] private Button unequipHeadButton;
    [SerializeField] private Button unequipArmorButton;
    [SerializeField] private Button unequipGlovesButton;
    [SerializeField] private Button unequipPantsButton;
    [SerializeField] private Button unequipBootsButton;

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

        // 2. Abonnement des boutons de déséquipement
        unequipHeadButton?.onClick.AddListener(() => UnequipFromCurrentLayer(EquipmentType.Helmet));
        unequipArmorButton?.onClick.AddListener(() => UnequipFromCurrentLayer(EquipmentType.Armor));
        unequipGlovesButton?.onClick.AddListener(() => UnequipFromCurrentLayer(EquipmentType.Gloves));
        unequipPantsButton?.onClick.AddListener(() => UnequipFromCurrentLayer(EquipmentType.Pants));
        unequipBootsButton?.onClick.AddListener(() => UnequipFromCurrentLayer(EquipmentType.Boots));
        // AJOUTE CECI : Si tu as déjà glissé un perso dans l'inspecteur, on l'initialise
        if (character != null)
        {
            SetupUI(character);
        }
        // 3. Initialisation si un perso est déjà assigné
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
    /// Déséquipe l'objet uniquement sur la couche sélectionnée dans le dropdown
    /// </summary>
    private void UnequipFromCurrentLayer(EquipmentType type)
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
        if (currentLayer == null) return;

        // Mise à jour des textes
        headgearText.text = GetEquipmentName(EquipmentType.Helmet);
        armorText.text = GetEquipmentName(EquipmentType.Armor);
        glovesText.text = GetEquipmentName(EquipmentType.Gloves);
        pantsText.text = GetEquipmentName(EquipmentType.Pants);
        bootsText.text = GetEquipmentName(EquipmentType.Boots);

        // Optionnel : Désactive les boutons si le slot est vide
        ToggleButtonState(unequipHeadButton, EquipmentType.Helmet);
        ToggleButtonState(unequipArmorButton, EquipmentType.Armor);
        ToggleButtonState(unequipGlovesButton, EquipmentType.Gloves);
        ToggleButtonState(unequipPantsButton, EquipmentType.Pants);
        ToggleButtonState(unequipBootsButton, EquipmentType.Boots);
    }

    private string GetEquipmentName(EquipmentType type)
    {
        EquipmentInstance inst = currentLayer.GetInstance(type);
        if (inst != null && inst.ItemSO != null)
        {
            return inst.ItemSO.ItemName;
        }
        return "<color=#888888>Vide</color>";
    }

    private void ToggleButtonState(Button btn, EquipmentType type)
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