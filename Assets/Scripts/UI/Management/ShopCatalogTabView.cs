using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Catalog editor: rows for each ShopItemEntry with inline MaxStock + Price edit,
    /// remove button, and an "+ Add" button that opens the CatalogItemPickerDialog.
    ///
    /// Reactive: subscribes to ShopBuilding.OnCatalogChanged for live updates after
    /// any peer's catalog mutation. Cleanup in Dispose (rule #16).
    /// </summary>
    public sealed class ShopCatalogTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Wiring")]
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;       // prefab carrying ShopCatalogTabRow
        [SerializeField] private Button _addButton;

        private ShopBuilding _building;
        private readonly List<ShopCatalogTabRow> _rows = new();

        public GameObject Root => gameObject;

        public void Bind(ShopBuilding building)
        {
            _building = building;
            if (_addButton != null) _addButton.onClick.AddListener(OnAddClicked);
            if (_building != null) _building.OnCatalogChanged += Refresh;
            Refresh();
        }

        public void OnTabActivated()   { /* no-op — view is live the whole time it's bound */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_addButton != null) _addButton.onClick.RemoveListener(OnAddClicked);
            if (_building != null) _building.OnCatalogChanged -= Refresh;
            _building = null;
            ClearRows();
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null && _rows[i].gameObject != null) Destroy(_rows[i].gameObject);
            _rows.Clear();
        }

        private void Refresh()
        {
            ClearRows();
            if (_building == null || _rowsParent == null || _rowPrefab == null) return;
            for (int i = 0; i < _building.Catalog.Count; i++)
            {
                var entry = _building.Catalog[i];
                if (entry.Item == null) continue;
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopCatalogTabRow>();
                if (row != null) { row.Bind(_building, entry); _rows.Add(row); }
            }
        }

        private void OnAddClicked()
        {
            if (_building == null) return;
            CatalogItemPickerDialog.Show(_building, OnItemPicked);
        }

        private void OnItemPicked(ItemSO item, int maxStock, int priceOverride)
        {
            if (_building == null || item == null) return;
            _building.AddCatalogEntryServerRpc(item.ItemId, maxStock, priceOverride);
        }
    }

    /// <summary>
    /// One row in the catalog tab. Wired to a ShopBuilding entry; offers inline
    /// edit (Max Stock / Price Override) + remove. Submits through ShopBuilding's
    /// existing ServerRpcs.
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
