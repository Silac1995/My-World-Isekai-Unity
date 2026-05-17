using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.UI.Combat;

/// <summary>
/// Combat action bar — three clusters (weapon · abilities · utility). Shown when
/// CharacterCombat.IsInBattle. Hidden otherwise. The weapon cluster mutates based
/// on the active WeaponInstance shape (Melee / Charging / Magazine).
///
/// Leaf children (UI_CombatAbilitySlot ×6, UI_CombatInitiativeBar, UI_CombatQueuedLabel)
/// are authored as children of _menuContainer in the prefab. See manual prefab
/// authoring checklist for the structural recipe.
///
/// Cluster ordering left → right:
///   [Attack] [Reload?]   |   [1][2][3][4][5][6]   |   [Swap] [Items▾]
/// Reload renders only when active weapon is MagazineWeaponInstance.
/// Swap is greyed when carried weapons &lt; 2.
/// </summary>
public class UI_CombatActionMenu : MonoBehaviour
{
    [Header("Container")]
    [SerializeField] private GameObject _menuContainer;

    [Header("Weapon Cluster")]
    [SerializeField] private Button _attackButton;
    [SerializeField] private TextMeshProUGUI _attackButtonText;
    [SerializeField] private GameObject _ammoBadgeRoot;
    [SerializeField] private TextMeshProUGUI _ammoBadgeText;
    [SerializeField] private GameObject _reloadButtonRoot;
    [SerializeField] private Button _reloadButton;

    [Header("Abilities Cluster")]
    [Tooltip("Six instances of UI_CombatAbilitySlot, indices 0..5 corresponding to active slots 1..6 (hotkeys 1-6).")]
    [SerializeField] private UI_CombatAbilitySlot[] _abilitySlots = new UI_CombatAbilitySlot[6];

    [Header("Utility Cluster")]
    [SerializeField] private Button _swapButton;
    [Tooltip("Glyph showing the currently active weapon. Updated as Active changes.")]
    [SerializeField] private TextMeshProUGUI _swapFromText;
    [Tooltip("Glyph showing the weapon Swap will rotate to next. Updated as Active or carried-list changes.")]
    [SerializeField] private TextMeshProUGUI _swapToText;
    [SerializeField] private CanvasGroup _swapCanvasGroup;
    [SerializeField] private Button _itemsButton;

    [Header("Chrome")]
    [SerializeField] private UI_CombatInitiativeBar _initiativeBar;
    [SerializeField] private UI_CombatQueuedLabel _queuedLabel;

    private Character _character;
    private CharacterCombat _characterCombat;

    public void Initialize(Character character)
    {
        Unsubscribe();
        _character = character;
        _characterCombat = _character != null ? _character.CharacterCombat : null;

        if (_characterCombat != null)
        {
            _characterCombat.OnCombatModeChanged += HandleCombatModeChanged;
        }

        WireButtons();
        InitializeSubElements();
        UpdateMenuVisibility();
    }

    private void WireButtons()
    {
        if (_attackButton != null) { _attackButton.onClick.RemoveAllListeners(); _attackButton.onClick.AddListener(OnAttackClicked); }
        if (_reloadButton != null) { _reloadButton.onClick.RemoveAllListeners(); _reloadButton.onClick.AddListener(OnReloadClicked); }
        if (_swapButton != null) { _swapButton.onClick.RemoveAllListeners(); _swapButton.onClick.AddListener(OnSwapClicked); }
        if (_itemsButton != null) { _itemsButton.onClick.RemoveAllListeners(); _itemsButton.onClick.AddListener(OnItemsClicked); }
    }

    private void InitializeSubElements()
    {
        if (_abilitySlots != null)
        {
            for (int i = 0; i < _abilitySlots.Length; i++)
            {
                if (_abilitySlots[i] != null) _abilitySlots[i].Initialize(i, _character);
            }
        }

        if (_initiativeBar != null) _initiativeBar.Initialize(_character);
        if (_queuedLabel != null) _queuedLabel.Initialize(_character);
    }

    private void Update()
    {
        if (_character == null) return;
        UpdateMenuVisibility();
        if (_menuContainer != null && _menuContainer.activeSelf) UpdateVisuals();
    }

    private void HandleCombatModeChanged(bool isInCombat) { UpdateMenuVisibility(); }

    private void UpdateMenuVisibility()
    {
        if (_menuContainer == null) return;
        bool shouldShow = _characterCombat != null && _characterCombat.IsInBattle;
        if (_menuContainer.activeSelf != shouldShow) _menuContainer.SetActive(shouldShow);
    }

    private void UpdateVisuals()
    {
        var equipment = _character.CharacterEquipment;
        if (equipment == null) return;

        var inventory = equipment.GetInventory();
        var weapons = inventory?.GetWeaponInstances();
        int carriedCount = weapons?.Count ?? 0;

        int activeIdx = equipment.ActiveWeaponIndex;
        WeaponInstance active = (weapons != null && activeIdx >= 0 && activeIdx < weapons.Count) ? weapons[activeIdx] : null;

        bool isMag = active is MagazineWeaponInstance;
        bool isRanged = active is RangedWeaponInstance;

        // Attack label + queued visual
        if (_attackButtonText != null)
        {
            string label = isRanged ? "Ranged Attack" : "Melee Attack";
            bool queued = _characterCombat != null && _characterCombat.HasPlannedAction;
            _attackButtonText.text = queued ? $"<color=#9bf>{label} [Queued]</color>" : label;
        }

        // Ammo badge — magazine weapons only
        if (_ammoBadgeRoot != null) _ammoBadgeRoot.SetActive(isMag);
        if (_ammoBadgeText != null && isMag)
        {
            var mag = (MagazineWeaponInstance)active;
            _ammoBadgeText.text = $"{equipment.ActiveAmmo}/{mag.MagazineSize}";
        }

        // Attack interactable — disabled when magazine empty or reloading
        if (_attackButton != null)
        {
            bool canFire = !isMag || (equipment.ActiveAmmo > 0 && !equipment.IsActiveReloading);
            _attackButton.interactable = canFire;
        }

        // Reload slot — magazine only; greyed when full or reloading-in-progress
        if (_reloadButtonRoot != null) _reloadButtonRoot.SetActive(isMag);
        if (_reloadButton != null && isMag)
        {
            var mag = (MagazineWeaponInstance)active;
            bool needsReload = equipment.ActiveAmmo < mag.MagazineSize && !equipment.IsActiveReloading;
            _reloadButton.interactable = needsReload;
        }

        // Swap button + preview glyphs
        if (_swapButton != null)
        {
            bool canSwap = carriedCount >= 2;
            _swapButton.interactable = canSwap;
            if (_swapCanvasGroup != null) _swapCanvasGroup.alpha = canSwap ? 1f : 0.4f;

            if (_swapFromText != null) _swapFromText.text = WeaponIconGlyph(active);
            if (_swapToText != null)
            {
                WeaponInstance next = canSwap ? weapons[(activeIdx + 1) % carriedCount] : null;
                _swapToText.text = WeaponIconGlyph(next);
            }
        }
    }

    private static string WeaponIconGlyph(WeaponInstance w)
    {
        if (w == null) return "—";
        // Single-char glyphs for the swap preview. Replace with proper sprite icons once
        // a per-weapon UI icon registry exists (future polish).
        if (w is RangedWeaponInstance) return "Rng";
        return "Mle";
    }

    private void OnAttackClicked()
    {
        if (_character == null || _characterCombat == null) return;

        var equipment = _character.CharacterEquipment;
        var weapons = equipment?.GetInventory()?.GetWeaponInstances();
        WeaponInstance active = (weapons != null && equipment.ActiveWeaponIndex >= 0 && equipment.ActiveWeaponIndex < weapons.Count)
            ? weapons[equipment.ActiveWeaponIndex]
            : null;

        // Empty-magazine auto-queue Reload (spec §3 decision #4).
        if (active is MagazineWeaponInstance mag && mag.CurrentAmmo == 0 && !mag.IsReloading)
        {
            _characterCombat.TryQueueReload();
            return;
        }

        // Toggle behaviour preserved from the prior implementation: cancel queued action.
        if (_characterCombat.HasPlannedAction)
        {
            _characterCombat.ClearActionIntent();
            return;
        }

        // Resolve initial target (same fallback logic as the original UI).
        Character initialTarget = _characterCombat.PlannedTarget;
        var bm = _characterCombat.CurrentBattleManager;
        if (initialTarget != null && bm != null && (bm.GetTeamOf(initialTarget) == null || !initialTarget.IsAlive()))
        {
            initialTarget = null;
        }
        if (initialTarget == null && bm != null) initialTarget = bm.GetBestTargetFor(_character);
        if (initialTarget == null) return;

        // active was resolved at the top of this method — re-use it to label the queued action.
        string actionName = active is RangedWeaponInstance ? "Ranged Attack" : "Melee Attack";
        _characterCombat.SetActionIntent(() => _characterCombat.Attack(_characterCombat.PlannedTarget), initialTarget, actionName);
    }

    private void OnReloadClicked() { _characterCombat?.TryQueueReload(); }
    private void OnSwapClicked() { _characterCombat?.TryQueueSwapWeapon(); }

    private void OnItemsClicked()
    {
        if (_character == null || PlayerUI.Instance == null) return;
        PlayerUI.Instance.ToggleCombatItemsWindow(_character);
    }

    private void Unsubscribe()
    {
        if (_characterCombat != null)
        {
            _characterCombat.OnCombatModeChanged -= HandleCombatModeChanged;
        }
    }

    private void OnDestroy() { Unsubscribe(); }
}
