using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using TMPro;
public class UI_ItemSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _itemName;
    [SerializeField] private GameObject _newBadge;

    [Header("Hover Effect")]
    [SerializeField] private Color _normalColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
    [SerializeField] private Color _hoverColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);
    private Image _backgroundImage;

    private UI_Inventory _uiInventory;
    private ItemSlot _itemSlot;

    public void Initialize(UI_Inventory ui_inventory, ItemSlot itemSlot)
    {
        _uiInventory = ui_inventory;
        _itemSlot = itemSlot;
        _backgroundImage = GetComponent<Image>();
        if (_backgroundImage != null)
        {
            _backgroundImage.color = _normalColor;
        }

        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        if (_itemName == null) return;

        if (_itemSlot != null && _itemSlot.ItemInstance != null)
        {
            _itemName.text = _itemSlot.ItemInstance.CustomizedName;
        }
        else if (_itemSlot != null)
        {
            _itemName.text = $"<color=#888888>{_itemSlot}</color>";
        }
        else
        {
            _itemName.text = "";
        }

        if (_newBadge != null)
        {
            bool isNew = _itemSlot != null && _itemSlot.ItemInstance != null && _itemSlot.ItemInstance.IsNewlyAdded;
            _newBadge.SetActive(isNew);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Right click to drop the item from inventory
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (_itemSlot != null && !_itemSlot.IsEmpty() && _uiInventory != null && _uiInventory.CharacterOwner != null)
            {
                var dropAction = new CharacterDropItem(_uiInventory.CharacterOwner, _itemSlot.ItemInstance);
                dropAction.OnActionFinished += () => {
                    if (_uiInventory != null) _uiInventory.RefreshDisplay();
                };
                _uiInventory.CharacterOwner.CharacterActions.ExecuteAction(dropAction);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_backgroundImage != null)
        {
            _backgroundImage.color = _hoverColor;
        }

        if (_itemSlot != null && _itemSlot.ItemInstance != null && _itemSlot.ItemInstance.IsNewlyAdded)
        {
            _itemSlot.ItemInstance.IsNewlyAdded = false;
            UpdateVisuals();

            if (_uiInventory != null && _uiInventory.CharacterOwner != null)
            {
                var equipment = _uiInventory.CharacterOwner.CharacterEquipment;
                if (equipment != null && equipment.HaveInventory() && !equipment.GetInventory().HasNewItems())
                {
                    equipment.ClearInventoryNotification();
                }
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_backgroundImage != null)
        {
            _backgroundImage.color = _normalColor;
        }
    }
}
