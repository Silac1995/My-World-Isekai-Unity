using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Encapsulates the Player Info UI, including Name, Target HP, EXP, and Status Effects.
/// Used on the UI_PlayerInfo prefab.
/// </summary>
public class UI_PlayerInfo : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI _playerNameText;
    [SerializeField] private UI_HealthBar _healthBar;
    [SerializeField] private UI_CombatExpBar _expBar;
    
    [Header("Status Effects")]
    [SerializeField] private Transform _statusEffectsContainer;
    [SerializeField] private UI_StatusEffect _statusEffectPrefab;

    private Dictionary<CharacterStatusEffectInstance, UI_StatusEffect> _activeEffectUIs = new();
    private Character _lastCharacter;

    public void Initialize(Character characterComponent)
    {
        CleanupEvents();
        _lastCharacter = characterComponent;

        ClearUI();

        if (characterComponent == null)
        {
            return;
        }

        if (_playerNameText != null)
        {
            _playerNameText.text = characterComponent.CharacterName;
        }

        if (_healthBar != null && characterComponent.Stats != null)
        {
            _healthBar.Initialize(characterComponent.Stats.Health);
        }

        if (_expBar != null)
        {
            // Assuming the player character always has a CharacterCombatLevel attached.
            var combatLvl = characterComponent.CharacterCombatLevel;
            if (combatLvl != null)
            {
                _expBar.Initialize(combatLvl);
            }
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
    }

    private void ClearUI()
    {
        if (_playerNameText != null) _playerNameText.text = "";

        if (_statusEffectsContainer != null)
        {
            foreach (Transform child in _statusEffectsContainer)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
        _activeEffectUIs.Clear();
    }

    private void CleanupEvents()
    {
        if (_lastCharacter != null && _lastCharacter.StatusManager != null)
        {
            _lastCharacter.StatusManager.OnStatusEffectAdded -= HandleStatusEffectAdded;
            _lastCharacter.StatusManager.OnStatusEffectRemoved -= HandleStatusEffectRemoved;
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
                uiElement.gameObject.SetActive(false);
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
