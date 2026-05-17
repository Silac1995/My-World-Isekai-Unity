using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Leaf row inside UI_CombatItemsWindow. One ConsumableInstance per row.
    /// Disabled when the consumable is not usable in combat (e.g., food) — shown
    /// with a "Not usable in combat" reason rather than hidden, so the player
    /// understands why the item isn't pickable.
    ///
    /// Not a UI_WindowBase variant — leaf prefab per CLAUDE.md rule #39 (no close
    /// affordance; lives + dies with its parent window).
    /// </summary>
    public class UI_CombatItemRow : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _effectText;
        [SerializeField] private TMP_Text _hotkeyText;
        [SerializeField] private Button _rowButton;
        [SerializeField] private CanvasGroup _canvasGroup;

        private ConsumableInstance _instance;
        private Action<ConsumableInstance> _onUseClicked;

        /// <summary>
        /// Bind this row to a consumable. The parent window injects the use callback
        /// so the row never touches CharacterCombat directly (mirrors UI_SafeCurrencyRow
        /// decoupling pattern).
        ///
        /// Note: ItemInstance has no stack-count concept in this codebase — each slot
        /// holds exactly one ItemInstance. If a player has 4 Health Potions, the window
        /// builds 4 separate rows. Quantity aggregation (single row per ItemSO) is a
        /// future UX polish.
        /// </summary>
        public void Initialize(
            ConsumableInstance instance,
            int hotkeyNumber,
            Action<ConsumableInstance> onUseClicked)
        {
            _instance = instance;
            _onUseClicked = onUseClicked;

            if (_icon != null && instance?.ItemSO?.Icon != null) _icon.sprite = instance.ItemSO.Icon;
            if (_nameText != null) _nameText.text = instance?.ItemSO?.ItemName ?? "(null)";

            bool usable = instance?.ItemSO is ConsumableSO so && so.IsUsableInCombat;
            if (_effectText != null)
            {
                _effectText.text = usable
                    ? FormatEffectLine(instance.ItemSO as ConsumableSO)
                    : "<color=#a55>Not usable in combat.</color>";
            }
            if (_hotkeyText != null) _hotkeyText.text = usable && hotkeyNumber > 0 ? hotkeyNumber.ToString() : "—";
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = usable ? 1f : 0.4f;
                _canvasGroup.interactable = usable;
            }

            if (_rowButton != null)
            {
                _rowButton.onClick.RemoveAllListeners();
                if (usable) _rowButton.onClick.AddListener(OnRowClicked);
            }
        }

        private static string FormatEffectLine(ConsumableSO so)
        {
            if (so == null || so.Effects == null || so.Effects.Count == 0) return "";
            // ConsumableSO.Effects is List<string> (stringly-typed effect names).
            // Render the first line; richer formatting can wait on the effect-system rework.
            return so.Effects[0];
        }

        private void OnRowClicked()
        {
            _onUseClicked?.Invoke(_instance);
        }

        private void OnDestroy()
        {
            if (_rowButton != null) _rowButton.onClick.RemoveAllListeners();
        }
    }
}
