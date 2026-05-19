using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One mini-cell on the paper-doll. Owns a single (layer, slot) coordinate.
    /// Click opens the popup with worn-item verbs (Unequip · CarryInHand · DropToGround).
    /// Empty cells are non-interactive visual placeholders.
    /// </summary>
    public sealed class UI_EquipmentWornCell : MonoBehaviour
    {
        [Header("Coordinate")]
        [SerializeField] private WearableLayerEnum _layer;
        [SerializeField] private WearableType _slot;

        [Header("Visual")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _layerTag;
        [SerializeField] private TextMeshProUGUI _fallbackLabel;
        [SerializeField] private Button _clickButton;

        private UI_CharacterEquipment _window;

        public WearableLayerEnum Layer => _layer;
        public WearableType Slot => _slot;

        public void Initialize(UI_CharacterEquipment window)
        {
            _window = window;
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveAllListeners();
                _clickButton.onClick.AddListener(OnCellClicked);
            }
        }

        /// <summary>
        /// Repaints from the current equipment state. Called by the parent window
        /// after every OnEquipmentChanged.
        /// </summary>
        public void Refresh(EquipmentInstance instance)
        {
            bool filled = instance != null && instance.ItemSO != null;
            bool hasIcon = filled && instance.ItemSO.Icon != null;

            if (_iconImage != null)
            {
                _iconImage.enabled = hasIcon;
                if (hasIcon) _iconImage.sprite = instance.ItemSO.Icon;
            }
            // Text fallback when no icon — shows first 3 chars of item name so the player
            // can still see SOMETHING is equipped even on items without authored icons.
            if (_fallbackLabel != null)
            {
                _fallbackLabel.gameObject.SetActive(filled && !hasIcon);
                if (filled && !hasIcon)
                {
                    string n = instance.ItemSO.ItemName ?? "?";
                    _fallbackLabel.text = n.Length > 3 ? n.Substring(0, 3) : n;
                }
            }
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        private void OnCellClicked()
        {
            if (_window == null) return;
            _window.OpenPopupForWornCell(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
