using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.WorldSystem;

public class PlayerUI : MonoBehaviour
{
    public static PlayerUI Instance { get; private set; }

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

    // The only link required for the action bar
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
    [SerializeField] private MWI.UI.Orders.UI_OrderImmediatePopup _orderImmediatePopup;
    [SerializeField] private UI_PartyPanel _partyPanel;
    [SerializeField] private UI_PartyWindow _partyWindow;
    [SerializeField] private Button _buttonPartyUI;
    [SerializeField] private MWI.UI.PauseMenuController _pauseMenu;

    [Header("Quest UI")]
    [SerializeField] private UI_QuestTracker _questTrackerUI;
    [SerializeField] private UI_QuestLogWindow _questLogWindow;
    [SerializeField] private QuestWorldMarkerRenderer _questMarkerRenderer;
    [Tooltip("Key that toggles the Quest Log window. Default: L.")]
    [SerializeField] private KeyCode _questLogToggleKey = KeyCode.L;

    private Character characterComponent;
    public Character CharacterComponent => characterComponent;
    public bool IsInitialized => characterComponent != null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning("[PlayerUI] Multiple instances detected! Destroying the new one.");
            Destroy(gameObject);
        }

        if (_pauseMenu == null)
            _pauseMenu = GetComponentInChildren<MWI.UI.PauseMenuController>(true);
    }

    public void Initialize(GameObject newCharacter)
    {
        // Clean up the previous links (Equipment, etc.)
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



        // Hook up the time system
        if (_timeUI != null)
        {
            _timeUI.SetTargetCharacter(characterComponent);
        }

        // Delegate the entire action-bar management to the specialized script
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

        if (_orderImmediatePopup != null)
        {
            _orderImmediatePopup.BindToLocalPlayer(characterComponent.CharacterOrders);
        }

        if (_partyPanel != null)
        {
            _partyPanel.Bind(characterComponent);
        }

        if (_partyWindow != null)
        {
            _partyWindow.Initialize(characterComponent);
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

        // Quest UI — local-player only, gated by Character.IsOwner.
        if (_questTrackerUI != null)
        {
            _questTrackerUI.Initialize(characterComponent.CharacterQuestLog);
        }
        if (_questLogWindow != null)
        {
            _questLogWindow.Initialize(characterComponent.CharacterQuestLog);
            _questLogWindow.CloseWindow();  // start hidden; toggled via _questLogToggleKey in Update.
        }
        if (_questMarkerRenderer != null)
        {
            characterComponent.TryGetComponent(out CharacterMapTracker tracker);
            _questMarkerRenderer.Initialize(characterComponent.CharacterQuestLog, tracker);
        }

        BuildingPlacementManager placementManager = characterComponent.PlacementManager;
        if (placementManager != null && _buildingUI != null)
        {
            placementManager.SetSettings(_buildingUI.WorldSettings);
            placementManager.Initialize(characterComponent);
        }

        // Status effects now handled by UI_PlayerInfo

        // Initialize the global static helper for toasts
        if (_toastChannel != null)
        {
            MWI.UI.Notifications.UI_Toast.Initialize(_toastChannel);
        }

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

        if (_buttonPartyUI != null)
        {
            _buttonPartyUI.onClick.RemoveAllListeners();
            _buttonPartyUI.onClick.AddListener(TogglePartyUI);
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

    public void TogglePartyUI()
    {
        if (_partyWindow == null) return;

        bool isCurrentlyActive = _partyWindow.gameObject.activeSelf;
        _partyWindow.gameObject.SetActive(!isCurrentlyActive);

        if (!isCurrentlyActive && characterComponent != null)
        {
            _partyWindow.Initialize(characterComponent);
        }
    }

    public void TogglePauseMenu()
    {
        if (_pauseMenu == null) return;
        _pauseMenu.Toggle();
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

    /// <summary>
    /// Opens the interaction menu with the given options.
    /// </summary>
    /// <param name="persistAcrossClicks">
    /// When true, the menu stays open after a button click and buttons re-lock — used by
    /// turn-based character dialogue, where the menu must remain visible for the full
    /// interaction and be explicitly closed via <see cref="CloseInteractionMenu"/> when
    /// the CharacterInteraction terminates.
    /// When false (default), the menu closes as soon as any option is clicked — used by
    /// one-shot interactions (hold-E context menu on doors, chests, furniture).
    /// </param>
    public void OpenInteractionMenu(List<InteractionOption> options, bool persistAcrossClicks = false)
    {
        if (_interactionMenu == null)
        {
            Debug.LogWarning("PlayerUI: UI_InteractionMenu component not assigned!");
            return;
        }

        _interactionMenu.gameObject.SetActive(true);
        _interactionMenu.Initialize(options, lockByDefault: persistAcrossClicks);
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

    private void Update()
    {
        // Quest log toggle (L key by default). Uses unscaledDeltaTime context — UI input is real-time.
        if (_questLogWindow != null && Input.GetKeyDown(_questLogToggleKey))
        {
            if (_questLogWindow.gameObject.activeSelf) _questLogWindow.CloseWindow();
            else _questLogWindow.OpenWindow();
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
        if (_orderImmediatePopup != null)
        {
            _orderImmediatePopup.BindToLocalPlayer(null);
        }
        if (_partyPanel != null)
        {
            _partyPanel.Unbind();
        }
        if (_partyWindow != null)
        {
            _partyWindow.Initialize(null);
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

        // No longer need to unsubscribe actions here,
        // No longer need to unsubscribe actions here,
        // UI_Action_ProgressBar takes care of it!
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        CleanupEvents();
    }
}
