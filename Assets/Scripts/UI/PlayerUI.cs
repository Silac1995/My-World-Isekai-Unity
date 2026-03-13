using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character;

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI playerName;
    [SerializeField] private Button _buttonEquipmentUI;
    [SerializeField] private MWI.UI.TimeUI _timeUI;

    [Header("Notification Channels")]
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _inventoryChannel;

    // Le seul lien nécessaire pour la barre d'action
    [SerializeField] private UI_Action_ProgressBar _actionProgressBar;
    [SerializeField] private UI_HealthBar _healthBar;

    [Header("UI Windows")]
    [SerializeField] private CharacterEquipmentUI _equipmentUI;

    private Character characterComponent;

    public void Initialize(GameObject newCharacter)
    {
        // On nettoie les anciens liens (Equipement, etc.)
        CleanupEvents();

        if (newCharacter == null)
        {
            ClearUI();
            return;
        }

        this.character = newCharacter;
        this.characterComponent = newCharacter.GetComponent<Character>();

        if (characterComponent == null)
        {
            Debug.LogError("GameObject doesn't have a Character component!");
            ClearUI();
            return;
        }

        UpdatePlayerName();

        // Lien avec le système de temps
        if (_timeUI != null)
        {
            _timeUI.SetTargetCharacter(characterComponent);
        }

        // On délègue toute la gestion de la barre au script spécialisé
        if (_actionProgressBar != null)
        {
            _actionProgressBar.InitializeCharacterActions(characterComponent.CharacterActions);
        }

        if (_healthBar != null && characterComponent.Stats != null)
        {
            _healthBar.Initialize(characterComponent.Stats.Health);
        }

        // Push notification channels to the equipment system
        if (characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.InitializeNotifications(_inventoryChannel);
        }

        // Initialize the equipment UI if it's already active or for when it's opened
        if (_equipmentUI != null)
        {
            _equipmentUI.Initialize(characterComponent);
        }

        if (_buttonEquipmentUI != null)
        {
            _buttonEquipmentUI.onClick.RemoveAllListeners();
            _buttonEquipmentUI.onClick.AddListener(ToggleEquipmentUI);
        }
    }

    public void ToggleEquipmentUI()
    {
        if (_equipmentUI == null) return;

        bool isCurrentlyActive = _equipmentUI.gameObject.activeSelf;
        _equipmentUI.gameObject.SetActive(!isCurrentlyActive);

        // If we are opening it, re-initialize to be safe with the current character data
        if (!isCurrentlyActive && characterComponent != null)
        {
            _equipmentUI.Initialize(characterComponent);
        }
    }

    private void UpdatePlayerName()
    {
        if (playerName != null && characterComponent != null)
            playerName.text = characterComponent.CharacterName;
    }

    private void ClearUI()
    {
        if (characterComponent != null && characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.ClearNotifications();
        }

        this.character = null;
        this.characterComponent = null;
        if (playerName != null) playerName.text = "";
    }

    private void CleanupEvents()
    {
        if (characterComponent != null && characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.ClearNotifications();
        }

        // Plus besoin de désabonner les actions ici, 
        // c'est UI_Action_ProgressBar qui s'en occupe !
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}
