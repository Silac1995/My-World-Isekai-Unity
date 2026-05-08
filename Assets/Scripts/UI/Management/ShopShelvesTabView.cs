using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Shelves tab: lists every StorageFurniture descendant of the shop with a
    /// checkbox toggling whether it's a sell-shelf. Submits via
    /// ShopBuilding.SetSellShelfFlagServerRpc.
    /// </summary>
    public sealed class ShopShelvesTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Wiring")]
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;       // prefab carrying ShopShelvesTabRow

        private ShopBuilding _building;
        private readonly List<ShopShelvesTabRow> _rows = new();

        public GameObject Root => gameObject;

        public void Bind(ShopBuilding building)
        {
            _building = building;
            if (_building != null) _building.OnSellShelvesChanged += Refresh;
            Refresh();
        }

        public void OnTabActivated()   { /* no-op */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_building != null) _building.OnSellShelvesChanged -= Refresh;
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

            var allStorages = _building.GetComponentsInChildren<StorageFurniture>(includeInactive: true);
            for (int i = 0; i < allStorages.Length; i++)
            {
                var storage = allStorages[i];
                if (storage == null) continue;

                bool isSellShelf = false;
                for (int s = 0; s < _building.SellShelves.Count; s++)
                    if (_building.SellShelves[s] == storage) { isSellShelf = true; break; }

                int used = 0;
                for (int sl = 0; sl < storage.Capacity; sl++)
                    if (storage.GetItemSlot(sl) != null && !storage.GetItemSlot(sl).IsEmpty()) used++;

                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopShelvesTabRow>();
                if (row != null) { row.Bind(_building, storage, isSellShelf, used); _rows.Add(row); }
            }
        }
    }

    public sealed class ShopShelvesTabRow : MonoBehaviour
    {
        [SerializeField] private Toggle _toggle;
        [SerializeField] private TMP_Text _label;

        private ShopBuilding _building;
        private StorageFurniture _storage;

        public void Bind(ShopBuilding building, StorageFurniture storage, bool isSellShelf, int used)
        {
            _building = building;
            _storage = storage;
            if (_label != null) _label.text = $"{storage.FurnitureName}    {used}/{storage.Capacity} slots used";
            if (_toggle != null)
            {
                _toggle.SetIsOnWithoutNotify(isSellShelf);
                _toggle.onValueChanged.AddListener(OnToggle);
            }
        }

        private void OnToggle(bool isOn)
        {
            if (_building == null || _storage == null) return;
            var net = _storage.GetComponent<NetworkObject>();
            if (net == null) return;
            _building.SetSellShelfFlagServerRpc(new NetworkObjectReference(net), isOn);
        }

        private void OnDestroy()
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnToggle);
        }
    }
}
