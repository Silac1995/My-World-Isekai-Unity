using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character;

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI playerName;
    [SerializeField] private Button _buttonEquipmentUI;
    [SerializeField] private Button _buttonRelationsUI;
    [SerializeField] private MWI.UI.TimeUI _timeUI;

    [Header("Notification Channels")]
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _inventoryChannel;
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    // Le seul lien nécessaire pour la barre d'action
    [SerializeField] private UI_Action_ProgressBar _actionProgressBar;
    [SerializeField] private UI_HealthBar _healthBar;

    [Header("UI Windows")]
    [SerializeField] private CharacterEquipmentUI _equipmentUI;
    [SerializeField] private UI_CharacterRelations _relationsUI;

    [Header("Status Effects")]
    [SerializeField] private Transform _statusEffectsContainer;
    [SerializeField] private UI_StatusEffect _statusEffectPrefab;

    private Dictionary<CharacterStatusEffectInstance, UI_StatusEffect> _activeEffectUIs = new();
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
            characterComponent.CharacterEquipment.InitializeNotifications(_inventoryChannel, _toastChannel);
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

        if (characterComponent.StatusManager != null)
        {
            characterComponent.StatusManager.OnStatusEffectAdded += HandleStatusEffectAdded;
            characterComponent.StatusManager.OnStatusEffectRemoved += HandleStatusEffectRemoved;
            
            // Populate existing
            foreach(var effect in characterComponent.StatusManager.ActiveEffects)
            {
                HandleStatusEffectAdded(effect);
            }
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

        foreach (var kvp in _activeEffectUIs)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }
        _activeEffectUIs.Clear();
    }

    private void CleanupEvents()
    {
        if (characterComponent != null && characterComponent.CharacterEquipment != null)
        {
            characterComponent.CharacterEquipment.ClearNotifications();
        }

        // Plus besoin de désabonner les actions ici, 
        // Plus besoin de désabonner les actions ici, 
        // c'est UI_Action_ProgressBar qui s'en occupe !

        if (characterComponent != null && characterComponent.StatusManager != null)
        {
            characterComponent.StatusManager.OnStatusEffectAdded -= HandleStatusEffectAdded;
            characterComponent.StatusManager.OnStatusEffectRemoved -= HandleStatusEffectRemoved;
        }
    }

    private void HandleStatusEffectAdded(CharacterStatusEffectInstance instance)
    {
        if (_statusEffectPrefab == null || _statusEffectsContainer == null) return;
        
        if (!_activeEffectUIs.ContainsKey(instance))
        {
            UI_StatusEffect newUI = Instantiate(_statusEffectPrefab, _statusEffectsContainer);
            newUI.Setup(instance);
            _activeEffectUIs.Add(instance, newUI);
        }
    }

    private void HandleStatusEffectRemoved(CharacterStatusEffectInstance instance)
    {
        if (_activeEffectUIs.TryGetValue(instance, out UI_StatusEffect uiElement))
        {
            if (uiElement != null)
            {
                Destroy(uiElement.gameObject);
            }
            _activeEffectUIs.Remove(instance);
        }
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}
