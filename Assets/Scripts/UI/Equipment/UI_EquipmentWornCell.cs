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
            if (_iconImage != null)
            {
                _iconImage.enabled = filled;
                if (filled) _iconImage.sprite = instance.ItemSO.Icon;
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
