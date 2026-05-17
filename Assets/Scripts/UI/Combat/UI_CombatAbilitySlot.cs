using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Single ability slot inside the action bar. One of 6 instances inside
    /// UI_CombatActionMenu's abilities cluster. Renders icon · hotkey label ·
    /// can-use overlay · empty/hatched placeholder when slot is null.
    ///
    /// Not a UI_WindowBase variant — leaf prefab per CLAUDE.md rule #39.
    /// </summary>
    public class UI_CombatAbilitySlot : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image _icon;
        [Tooltip("Semi-transparent overlay shown when the slot's ability cannot be used right now (cooldown / insufficient resources / no valid target).")]
        [SerializeField] private Image _cannotUseOverlay;
        [SerializeField] private TMP_Text _hotkeyText;
        [SerializeField] private GameObject _emptyPlaceholder;
        [SerializeField] private Button _clickButton;

        private Character _character;
        private int _slotIndex;

        public void Initialize(int slotIndex, Character character)
        {
            _slotIndex = slotIndex;
            _character = character;

            if (_hotkeyText != null) _hotkeyText.text = (slotIndex + 1).ToString();
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveAllListeners();
                _clickButton.onClick.AddListener(OnSlotClicked);
            }

            Refresh();
        }

        private void Update()
        {
            // Cheap visual refresh — the can-use overlay needs frequent updates because
            // ability cooldowns + target validity change continuously. An event-driven
            // refactor (subscribe to OnActiveSlotChanged + per-instance cooldown ticks)
            // is a future polish; per-frame Refresh is acceptable for 6 slots at 60 Hz.
            Refresh();
        }

        private void Refresh()
        {
            if (_character == null || _character.CharacterAbilities == null) { ShowEmpty(); return; }

            var ability = _character.CharacterAbilities.GetActiveSlot(_slotIndex);
            if (ability == null) { ShowEmpty(); return; }

            // Slot occupied
            if (_emptyPlaceholder != null) _emptyPlaceholder.SetActive(false);
            if (_icon != null)
            {
                _icon.enabled = true;
                if (ability.Data?.Icon != null) _icon.sprite = ability.Data.Icon;
            }

            if (_cannotUseOverlay != null)
            {
                // CanUse covers cooldown + resources + target validity. For v1 a single
                // "can-use yes/no" overlay is sufficient; finer-grained reasons (cooldown
                // ring vs no-mana glyph) are future polish.
                var target = _character.CharacterCombat != null
                    ? _character.CharacterCombat.PlannedTarget
                    : null;
                _cannotUseOverlay.enabled = !ability.CanUse(target);
            }
        }

        private void ShowEmpty()
        {
            if (_icon != null) _icon.enabled = false;
            if (_cannotUseOverlay != null) _cannotUseOverlay.enabled = false;
            if (_emptyPlaceholder != null) _emptyPlaceholder.SetActive(true);
        }

        private void OnSlotClicked()
        {
            if (_character == null || _character.CharacterAbilities == null) return;
            var target = _character.CharacterCombat != null
                ? _character.CharacterCombat.PlannedTarget
                : null;
            _character.CharacterAbilities.TryUseSlot(_slotIndex, target);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
