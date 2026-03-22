using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_CombatActionMenu : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _menuContainer; // The visual part to hide/show
    [SerializeField] private Button _attackButton;
    [SerializeField] private TextMeshProUGUI _attackButtonText;

    private Character _character;
    private CharacterCombat _characterCombat;
    private CharacterInitiative _initiativeStat;

    public void Initialize(Character character)
    {
        Unsubscribe();
        
        _character = character;
        if (_character != null)
        {
            _characterCombat = _character.CharacterCombat;
            if (_character.Stats != null)
            {
                _initiativeStat = _character.Stats.Initiative;
            }

            if (_characterCombat != null)
            {
                _characterCombat.OnCombatModeChanged += HandleCombatModeChanged;
            }
        }

        if (_attackButton != null)
        {
            _attackButton.onClick.RemoveAllListeners();
            _attackButton.onClick.AddListener(OnAttackClicked);
        }

        UpdateMenuVisibility();
    }

    private void Update()
    {
        // Continuously check initiative and combat status to toggle the menu
        UpdateMenuVisibility();
    }

    private void HandleCombatModeChanged(bool isInCombat)
    {
        UpdateMenuVisibility();
    }

    private void UpdateMenuVisibility()
    {
        if (_character == null || _characterCombat == null || _initiativeStat == null || _menuContainer == null)
        {
            if (_menuContainer != null) _menuContainer.SetActive(false);
            return;
        }

        // Only show if in battle and initiative is ready
        bool shouldShow = _characterCombat.IsInBattle && _initiativeStat.IsReady();
        
        if (_menuContainer.activeSelf != shouldShow)
        {
            _menuContainer.SetActive(shouldShow);
        }

        if (shouldShow && _attackButtonText != null)
        {
            // Dynamic attack text based on weapon
            if (_characterCombat.CurrentCombatStyleExpertise != null && 
                _characterCombat.CurrentCombatStyleExpertise.Style is RangedCombatStyleSO)
            {
                _attackButtonText.text = "Ranged Attack";
            }
            else
            {
                _attackButtonText.text = "Melee Attack";
            }
        }
    }

    private void OnAttackClicked()
    {
        if (_character == null || _characterCombat == null) return;

        Character target = _characterCombat.CurrentBattleManager?.GetBestTargetFor(_character);
        
        _characterCombat.ExecuteAction(() => _characterCombat.Attack(target));
        
        // Hide immediately to prevent double clicks
        if (_menuContainer != null) _menuContainer.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (_characterCombat != null)
        {
            _characterCombat.OnCombatModeChanged -= HandleCombatModeChanged;
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
