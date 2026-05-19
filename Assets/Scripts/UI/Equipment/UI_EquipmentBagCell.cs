using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One slot in the bag-inventory grid. Owns a bag slot index. Click opens the
    /// popup with bag-item verbs — verb set depends on the item kind (wearable /
    /// consumable / weapon / misc); decided by the parent window.
    ///
    /// <para>Hover behavior mirrors <c>UI_ItemSlot</c>: if the item is newly added
    /// (<see cref="ItemInstance.IsNewlyAdded"/>), hovering clears the flag, hides
    /// the badge, and tells the parent window to clear the inventory notification
    /// channel if no items remain new (so the HUD's "new item" badge goes away).</para>
    /// </summary>
    public sealed class UI_EquipmentBagCell : MonoBehaviour, IPointerEnterHandler
    {
        [Header("Visual")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _typeTagLabel;      // "W" for WeaponSlot, blank otherwise
        [SerializeField] private TextMeshProUGUI _fallbackLabel;     // shows first 3 chars of item name when no Icon
        [SerializeField] private Button _clickButton;
        [SerializeField] private GameObject _weaponSlotBackground;   // optional visual tint for WeaponSlot
        [SerializeField] private GameObject _newBadge;               // toggled when item is newly added

        private UI_CharacterEquipment _window;
        private int _slotIndex = -1;
        private ItemInstance _currentInstance;

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
            _currentInstance = instance;
            bool filled = instance != null && instance.ItemSO != null;
            bool hasIcon = filled && instance.ItemSO.Icon != null;

            if (_iconImage != null)
            {
                _iconImage.enabled = hasIcon;
                if (hasIcon) _iconImage.sprite = instance.ItemSO.Icon;
            }
            // Text fallback when no icon exists — shows first 3 chars of item name.
            if (_fallbackLabel != null)
            {
                _fallbackLabel.gameObject.SetActive(filled && !hasIcon);
                if (filled && !hasIcon)
                {
                    string n = instance.ItemSO.ItemName ?? "?";
                    _fallbackLabel.text = n.Length > 3 ? n.Substring(0, 3) : n;
                }
            }
            if (_newBadge != null)
            {
                _newBadge.SetActive(filled && instance.IsNewlyAdded);
            }
            // ItemInstance has no Quantity accessor today (1-per-slot model per wiki/systems/inventory.md
            // Open question). When stack sizes land, add a label render here.
            if (_clickButton != null) _clickButton.interactable = filled;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_currentInstance == null || !_currentInstance.IsNewlyAdded) return;

            _currentInstance.IsNewlyAdded = false;
            if (_newBadge != null) _newBadge.SetActive(false);

            // Tell the window to clear the inventory notification channel if no more new items remain.
            _window?.OnBagItemHovered();
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
