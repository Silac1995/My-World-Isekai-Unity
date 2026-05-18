using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One slot in the bag-inventory grid. Owns a bag slot index. Click opens the
    /// popup with bag-item verbs — verb set depends on the item kind (wearable /
    /// consumable / weapon / misc); decided by the parent window.
    /// </summary>
    public sealed class UI_EquipmentBagCell : MonoBehaviour
    {
        [Header("Visual")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _typeTagLabel;      // "W" for WeaponSlot, blank otherwise
        [SerializeField] private Button _clickButton;
        [SerializeField] private GameObject _weaponSlotBackground;   // optional visual tint for WeaponSlot

        private UI_CharacterEquipment _window;
        private int _slotIndex = -1;

        public int SlotIndex => _slotIndex;

        public void Initialize(UI_CharacterEquipment window, int slotIndex, bool isWeaponSlot)
        {
            _window = window;
            _slotIndex = slotIndex;
            if (_typeTagLabel != null) _typeTagLabel.text = isWeaponSlot ? "W" : string.Empty;
            if (_weaponSlotBackground != null) _weaponSlotBackground.SetActive(isWeaponSlot);
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveAllListeners();
                _clickButton.onClick.AddListener(OnCellClicked);
            }
        }

        public void Refresh(ItemInstance instance)
        {
            bool filled = instance != null && instance.ItemSO != null;
            if (_iconImage != null)
            {
                _iconImage.enabled = filled;
                if (filled) _iconImage.sprite = instance.ItemSO.Icon;
            }
            // ItemInstance has no Quantity accessor today (1-per-slot model per wiki/systems/inventory.md
            // Open question). When stack sizes land, add a label render here.
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        private void OnCellClicked()
        {
            if (_window == null || _slotIndex < 0) return;
            _window.OpenPopupForBagCell(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
