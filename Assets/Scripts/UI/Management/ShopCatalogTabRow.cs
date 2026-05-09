using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// One row in the catalog tab. Wired to a ShopBuilding entry; offers inline
    /// edit (Max Stock / Price Override) + remove. Submits through ShopBuilding's
    /// existing ServerRpcs.
    ///
    /// Split into its own .cs file 2026-05-08 (was nested inside
    /// <see cref="ShopCatalogTabView"/>). Reason: Unity's prefab serializer maps
    /// a `.meta` GUID to its containing file, not to a class — when two
    /// MonoBehaviours share the same file, only the primary class gets a
    /// resolvable script reference. Secondary classes spawn as missing-script
    /// components in instantiated prefabs (<c>m_Script: {fileID: 0}</c>), so
    /// <see cref="Bind"/> never runs at runtime and row labels stay at the
    /// authored placeholder text. One MonoBehaviour per file is the
    /// convention here.
    /// </summary>
    public sealed class ShopCatalogTabRow : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_InputField _maxStockInput;
        [SerializeField] private TMP_InputField _priceInput;
        [SerializeField] private TMP_Text _priceHelperText;
        [SerializeField] private Button _editButton;
        [SerializeField] private Button _removeButton;

        private ShopBuilding _building;
        private ShopItemEntry _entry;

        public void Bind(ShopBuilding building, ShopItemEntry entry)
        {
            _building = building;
            _entry = entry;

            if (_icon != null) _icon.sprite = entry.Item?.Icon;
            if (_nameText != null) _nameText.text = entry.Item?.ItemName ?? "<missing>";
            if (_maxStockInput != null) _maxStockInput.SetTextWithoutNotify(entry.MaxStock.ToString());
            if (_priceInput != null) _priceInput.SetTextWithoutNotify(entry.PriceOverride.ToString());
            if (_priceHelperText != null && entry.Item != null)
            {
                _priceHelperText.text = entry.Item.BasePrice > 0
                    ? $"0 = use base price ({entry.Item.BasePrice} g)"
                    : "0 = item has no base price";
            }

            if (_editButton != null) _editButton.onClick.AddListener(OnEditClicked);
            if (_removeButton != null) _removeButton.onClick.AddListener(OnRemoveClicked);
        }

        private void OnEditClicked()
        {
            if (_building == null || _entry.Item == null) return;
            int.TryParse(_maxStockInput?.text ?? "0", out int maxStock);
            int.TryParse(_priceInput?.text ?? "0", out int price);
            _building.EditCatalogEntryServerRpc(_entry.Item.ItemId, maxStock, price);
        }

        private void OnRemoveClicked()
        {
            if (_building == null || _entry.Item == null) return;
            _building.RemoveCatalogEntryServerRpc(_entry.Item.ItemId);
        }

        private void OnDestroy()
        {
            if (_editButton != null) _editButton.onClick.RemoveListener(OnEditClicked);
            if (_removeButton != null) _removeButton.onClick.RemoveListener(OnRemoveClicked);
        }
    }
}
