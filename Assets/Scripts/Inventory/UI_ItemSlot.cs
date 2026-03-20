using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using TMPro;

public class UI_ItemSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    [Header("UI Elements")]
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _itemName;

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
}
