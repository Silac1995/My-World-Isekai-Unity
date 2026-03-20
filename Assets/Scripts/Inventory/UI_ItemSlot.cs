using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using TMPro;
public class UI_ItemSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _itemName;
    [SerializeField] private GameObject _newBadge;

    private UI_Inventory _uiInventory;
    private ItemSlot _itemSlot;

    public void Initialize(UI_Inventory ui_inventory, ItemSlot itemSlot)
    {
        _uiInventory = ui_inventory;
        _itemSlot = itemSlot;

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
                var dropAction = new CharacterDropItem(_uiInventory.CharacterOwner, _itemSlot.ItemInstance, false);
                dropAction.OnActionFinished += () => {
                    if (_uiInventory != null) _uiInventory.RefreshDisplay();
                };
                _uiInventory.CharacterOwner.CharacterActions.ExecuteAction(dropAction);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
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
}
