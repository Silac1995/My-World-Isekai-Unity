using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.WorldSystem;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character;

    [Header("UI Components")]
    [SerializeField] private UI_PlayerInfo _playerInfo;
    [SerializeField] private Button _buttonEquipmentUI;
    [SerializeField] private Button _buttonRelationsUI;
    [SerializeField] private Button _buttonStatsUI;
    [SerializeField] private Button _buttonBuildingUI;
    [SerializeField] private MWI.UI.TimeUI _timeUI;

    [Header("Notification Channels")]
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _inventoryChannel;
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _relationsChannel;
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    // Le seul lien nécessaire pour la barre d'action
    [SerializeField] private UI_Action_ProgressBar _actionProgressBar;
    
    [Header("Combat UI")]
    [SerializeField] private UI_CombatActionMenu _combatActionMenu;
    [SerializeField] private UI_PlayerTargeting _playerTargeting;

    [Header("UI Windows")]
    [SerializeField] private CharacterEquipmentUI _equipmentUI;
    [SerializeField] private UI_CharacterRelations _relationsUI;
    [SerializeField] private UI_CharacterStats _statsUI;
    [SerializeField] private MWI.UI.Building.UI_BuildingPlacementMenu _buildingUI;
    [SerializeField] private UI_ChatBar _chatBar;
    [SerializeField] private UI_InteractionMenu _interactionMenu;
    [SerializeField] private MWI.UI.UI_InvitationPrompt _invitationPrompt;

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

        if (_playerInfo != null)
        {
            _playerInfo.Initialize(characterComponent);
        }

        if (_combatActionMenu != null)
        {
            _combatActionMenu.Initialize(characterComponent);
        }

        if (_playerTargeting != null)
        {
            _playerTargeting.Initialize(characterComponent);
        }

        if (_chatBar != null)
        {
            _chatBar.Initialize(characterComponent);
        }

        if (_invitationPrompt != null && characterComponent.CharacterInvitation != null)
        {
            _invitationPrompt.Initialize(characterComponent.CharacterInvitation);
        }

        // Push notification channels to the equipment system
        if (characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.InitializeNotifications(_inventoryChannel, _toastChannel);
        }

        // Push notification channels to the relation system
        if (characterComponent.CharacterRelation != null)
        {
            characterComponent.CharacterRelation.InitializeNotifications(_relationsChannel, _toastChannel);
        }

        // Initialize the equipment UI if it's already active or for when it's opened
        if (_equipmentUI != null)
        {
            _equipmentUI.Initialize(characterComponent);
        }

        if (_relationsUI != null)
        {
            _relationsUI.Initialize(characterComponent);
        }

        if (_statsUI != null)
        {
            _statsUI.Initialize(characterComponent);
        }

        if (_buildingUI != null)
        {
            _buildingUI.Initialize(characterComponent);
        }

        BuildingPlacementManager placementManager = characterComponent.PlacementManager;
        if (placementManager != null && _buildingUI != null)
        {
            placementManager.SetSettings(_buildingUI.WorldSettings);
            placementManager.Initialize(characterComponent);
        }

        // Status effects now handled by UI_PlayerInfo

        if (_buttonEquipmentUI != null)
        {
            _buttonEquipmentUI.onClick.RemoveAllListeners();
            _buttonEquipmentUI.onClick.AddListener(ToggleEquipmentUI);
        }

        if (_buttonRelationsUI != null)
        {
            _buttonRelationsUI.onClick.RemoveAllListeners();
            _buttonRelationsUI.onClick.AddListener(ToggleRelationsUI);
        }

        if (_buttonStatsUI != null)
        {
            _buttonStatsUI.onClick.RemoveAllListeners();
            _buttonStatsUI.onClick.AddListener(ToggleStatsUI);
        }

        if (_buttonBuildingUI != null)
        {
            _buttonBuildingUI.onClick.RemoveAllListeners();
            _buttonBuildingUI.onClick.AddListener(ToggleBuildingUI);
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

    public void ToggleRelationsUI()
    {
        if (_relationsUI == null) return;

        bool isCurrentlyActive = _relationsUI.gameObject.activeSelf;
        _relationsUI.gameObject.SetActive(!isCurrentlyActive);

        // If we are opening it, re-initialize to be safe with the current character data
        if (!isCurrentlyActive && characterComponent != null)
        {
            _relationsUI.Initialize(characterComponent);
        }
    }

    public void ToggleStatsUI()
    {
        if (_statsUI == null) return;

        bool isCurrentlyActive = _statsUI.gameObject.activeSelf;
        _statsUI.gameObject.SetActive(!isCurrentlyActive);

        if (!isCurrentlyActive && characterComponent != null)
        {
            _statsUI.Initialize(characterComponent);
        }
    }

    public void ToggleBuildingUI()
    {
        if (_buildingUI == null) return;

        if (_buildingUI.gameObject.activeSelf)
        {
            _buildingUI.CloseWindow();
        }
        else
        {
            if (characterComponent != null)
            {
                _buildingUI.Initialize(characterComponent);
            }
            _buildingUI.OpenWindow();
        }
    }

    public void OpenInteractionMenu(List<InteractableObject.InteractionOption> options)
    {
        if (_interactionMenu == null)
        {
            Debug.LogWarning("PlayerUI: UI_InteractionMenu component not assigned!");
            return;
        }

        _interactionMenu.gameObject.SetActive(true);
        _interactionMenu.Initialize(options);
    }

    public void CloseInteractionMenu()
    {
        Debug.LogWarning($"<color=orange>[PlayerUI]</color> CloseInteractionMenu called. StackTrace:\n{System.Environment.StackTrace}");
        if (_interactionMenu != null && _interactionMenu.gameObject.activeSelf)
        {
            _interactionMenu.CloseMenu();
        }
    }

    public bool IsInteractionMenuLocked()
    {
        return _interactionMenu != null && _interactionMenu.gameObject.activeSelf && _interactionMenu.IsLocked;
    }

    public void SetInteractionMenuInteractable(bool interactable)
    {
        if (_interactionMenu != null)
        {
            _interactionMenu.SetOptionsInteractable(interactable);
        }
    }

    public void UpdateInteractionMenuTimer(float normalizedValue)
    {
        if (_interactionMenu != null)
        {
            _interactionMenu.UpdateTimer(normalizedValue);
        }
    }

    

    private void ClearUI()
    {
        if (characterComponent != null && characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.ClearNotifications();
        }

        if (characterComponent != null && characterComponent.CharacterRelation != null)
        {
            characterComponent.CharacterRelation.ClearNotifications();
        }

        this.character = null;
        this.characterComponent = null;
        if (_playerInfo != null)
        {
            _playerInfo.Initialize(null);
        }
        if (_combatActionMenu != null)
        {
            _combatActionMenu.Initialize(null);
        }
        if (_playerTargeting != null)
        {
            _playerTargeting.Initialize(null);
        }
        if (_chatBar != null)
        {
            _chatBar.Initialize(null);
        }
        if (_invitationPrompt != null)
        {
            _invitationPrompt.Initialize(null);
        }
    }

    private void CleanupEvents()
    {
        if (characterComponent != null && characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.ClearNotifications();
        }

        if (characterComponent != null && characterComponent.CharacterRelation != null)
        {
            characterComponent.CharacterRelation.ClearNotifications();
        }

        // Plus besoin de désabonner les actions ici, 
        // Plus besoin de désabonner les actions ici, 
        // c'est UI_Action_ProgressBar qui s'en occupe !
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}
