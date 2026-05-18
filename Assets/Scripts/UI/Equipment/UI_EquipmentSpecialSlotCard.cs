using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One of the three top-row cards in the equipment window — Active Weapon,
    /// Hands Carry, or Equipped Bag. Owns a SlotKind discriminator. Click opens
    /// the popup with kind-appropriate verbs.
    /// </summary>
    public sealed class UI_EquipmentSpecialSlotCard : MonoBehaviour
    {
        public enum SlotKind { ActiveWeapon, HandsCarry, EquippedBag }

        [Header("Identity")]
        [SerializeField] private SlotKind _kind;

        [Header("Visual")]
        [SerializeField] private TextMeshProUGUI _labelText;
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private TextMeshProUGUI _metaText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private Button _clickButton;

        private UI_CharacterEquipment _window;

        public SlotKind Kind => _kind;

        public void Initialize(UI_CharacterEquipment window)
        {
            _window = window;
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveAllListeners();
                _clickButton.onClick.AddListener(OnCardClicked);
            }
        }

        public void RefreshActiveWeapon(WeaponInstance weapon)
        {
            bool filled = weapon != null && weapon.ItemSO != null;
            if (_valueText != null) _valueText.text = filled ? weapon.ItemSO.ItemName : "(empty)";
            if (_metaText != null) _metaText.text = filled ? "swap via combat HUD (Y)" : string.Empty;
            if (_iconImage != null)
            {
                _iconImage.enabled = filled;
                if (filled) _iconImage.sprite = weapon.ItemSO.Icon;
            }
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        public void RefreshHandsCarry(ItemInstance carry)
        {
            bool filled = carry != null && carry.ItemSO != null;
            if (_valueText != null) _valueText.text = filled ? carry.ItemSO.ItemName : "(empty)";
            if (_metaText != null) _metaText.text = filled ? "click for actions" : string.Empty;
            if (_iconImage != null)
            {
                _iconImage.enabled = filled;
                if (filled) _iconImage.sprite = carry.ItemSO.Icon;
            }
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        public void RefreshEquippedBag(BagInstance bag, int used, int capacity)
        {
            bool filled = bag != null && bag.ItemSO != null;
            if (_valueText != null) _valueText.text = filled ? bag.ItemSO.ItemName : "(none)";
            if (_metaText != null) _metaText.text = filled ? $"{used} / {capacity} slots" : string.Empty;
            if (_iconImage != null)
            {
                _iconImage.enabled = filled;
                if (filled) _iconImage.sprite = bag.ItemSO.Icon;
            }
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        private void OnCardClicked()
        {
            if (_window == null) return;
            _window.OpenPopupForSpecialCard(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
