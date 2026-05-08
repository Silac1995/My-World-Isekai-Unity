using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Cashiers tab: per-cashier till + Withdraw button. Withdrawing submits
    /// ShopBuilding.WithdrawCashierTillServerRpc which deposits coins into the
    /// owner's wallet (treasury redirect lands later via separate session).
    /// </summary>
    public sealed class ShopCashiersTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Wiring")]
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;     // prefab carrying ShopCashiersTabRow
        [SerializeField] private TMP_Text _staffingLabel;   // "Cashiers requiring vendor: N    Vendors hired: M / N"

        private ShopBuilding _building;
        private readonly List<ShopCashiersTabRow> _rows = new();

        public GameObject Root => gameObject;

        public void Bind(ShopBuilding building)
        {
            _building = building;
            if (_building != null) _building.OnCashiersChanged += Refresh;
            Refresh();
        }

        public void OnTabActivated()   { /* no-op */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_building != null) _building.OnCashiersChanged -= Refresh;
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

            int requiringVendor = 0;
            int vendorsActive = 0;
            for (int i = 0; i < _building.Cashiers.Count; i++)
            {
                var c = _building.Cashiers[i];
                if (c == null) continue;
                if (c.RequiresVendor) { requiringVendor++; if (c.Occupant != null) vendorsActive++; }
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopCashiersTabRow>();
                if (row != null) { row.Bind(_building, c); _rows.Add(row); }
            }
            if (_staffingLabel != null)
                _staffingLabel.text = $"Cashiers requiring a vendor: {requiringVendor}\nVendors active: {vendorsActive} / {requiringVendor}";
        }
    }

    public sealed class ShopCashiersTabRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _cashierLabel;
        [SerializeField] private TMP_Text _vendorLabel;
        [SerializeField] private TMP_Text _tillLabel;
        [SerializeField] private Button _withdrawButton;

        private ShopBuilding _building;
        private Cashier _cashier;

        public void Bind(ShopBuilding building, Cashier cashier)
        {
            _building = building;
            _cashier = cashier;
            if (_cashierLabel != null) _cashierLabel.text = cashier.FurnitureName;
            UpdateVendorLabel();
            UpdateTillLabel();

            if (_cashier?.NetSync != null)
            {
                _cashier.NetSync.TillBalances.OnListChanged += OnTillChanged;
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged += OnLockChanged;
            }
            if (_withdrawButton != null) _withdrawButton.onClick.AddListener(OnWithdrawClicked);
        }

        private void OnTillChanged(NetworkListEvent<CashierTillEntry> _) => UpdateTillLabel();
        private void OnLockChanged(ulong _, ulong __) => UpdateVendorLabel();

        private void UpdateTillLabel()
        {
            if (_tillLabel == null || _cashier == null) return;
            _tillLabel.text = $"{_cashier.GetTillBalance(MWI.Economy.CurrencyId.Default)} g";
        }

        private void UpdateVendorLabel()
        {
            if (_vendorLabel == null || _cashier == null) return;
            _vendorLabel.text = _cashier.Occupant != null ? _cashier.Occupant.CharacterName : "(vacant)";
        }

        private void OnWithdrawClicked()
        {
            if (_building == null || _cashier == null) return;
            var net = _cashier.GetComponent<NetworkObject>();
            if (net == null) return;
            _building.WithdrawCashierTillServerRpc(new NetworkObjectReference(net));
        }

        private void OnDestroy()
        {
            if (_cashier?.NetSync != null)
            {
                _cashier.NetSync.TillBalances.OnListChanged -= OnTillChanged;
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged -= OnLockChanged;
            }
            if (_withdrawButton != null) _withdrawButton.onClick.RemoveListener(OnWithdrawClicked);
        }
    }
}
