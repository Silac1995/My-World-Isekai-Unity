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

    // ShopCatalogTabRow split into its own file — see ShopCatalogTabRow.cs.
}
