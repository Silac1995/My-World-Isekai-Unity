using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_ItemSlot : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Image _iconImage; // Garde-le au cas où, on pourra l'utiliser plus tard
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

        // 1. On vérifie si le slot contient un item
        if (_itemSlot != null && _itemSlot.ItemInstance != null)
        {
            _itemName.text = _itemSlot.ItemInstance.CustomizedName;
        }
        // 2. Si le slot est vide, on affiche son type/catégorie
        else if (_itemSlot != null)
        {
            // On affiche la catégorie (ex: "Emplacement Tête") en gris
            _itemName.text = $"<color=#888888>{_itemSlot}</color>";
        }
        else
        {
            _itemName.text = "";
        }
    }
}