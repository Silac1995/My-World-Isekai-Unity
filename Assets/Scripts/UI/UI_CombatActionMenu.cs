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

    public void Initialize(Character character)
    {
        Unsubscribe();
        
        _character = character;
        if (_character != null)
        {
            _characterCombat = _character.CharacterCombat;

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
        UpdateMenuVisibility();
        UpdateVisuals();
    }

    private void HandleCombatModeChanged(bool isInCombat)
    {
        UpdateMenuVisibility();
    }

    private void UpdateMenuVisibility()
    {
        if (_character == null || _characterCombat == null || _menuContainer == null)
        {
            if (_menuContainer != null) _menuContainer.SetActive(false);
            return;
        }

        // Only show if in battle
        bool shouldShow = _characterCombat.IsInBattle;
        
        if (_menuContainer.activeSelf != shouldShow)
        {
            _menuContainer.SetActive(shouldShow);
        }
    }

    private void UpdateVisuals()
    {
        if (!_menuContainer.activeSelf || _attackButtonText == null || _characterCombat == null) return;

        bool hasIntent = _characterCombat.HasPlannedAction;
        
        string baseText = "Melee Attack";
        if (_characterCombat.CurrentCombatStyleExpertise != null && 
            _characterCombat.CurrentCombatStyleExpertise.Style is RangedCombatStyleSO)
        {
            baseText = "Ranged Attack";
        }

        if (hasIntent)
        {
            _attackButtonText.text = $"<color=blue>{baseText} [Queued]</color>";
        }
        else
        {
            _attackButtonText.text = baseText;
        }
    }

    private void OnAttackClicked()
    {
        if (_character == null || _characterCombat == null) return;

        // Toggle logic: if an action is already queued, cancel it.
        if (_characterCombat.HasPlannedAction)
        {
            _characterCombat.ClearActionIntent();
            return;
        }

        Character target = _characterCombat.CurrentBattleManager?.GetBestTargetFor(_character);
        
        // Instead of executing immediately, we lock the intent.
        _characterCombat.SetActionIntent(() => _characterCombat.Attack(target), target);
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
