using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// One row in the Cashiers tab. Shows the cashier name + current vendor (or
    /// "(vacant)") + till balance + a Withdraw button. Reactively updates as the
    /// underlying <see cref="CashierNetSync"/>'s till and customer-lock change.
    ///
    /// Split into its own .cs file 2026-05-08 — see <see cref="ShopCatalogTabRow"/>
    /// for the rationale (one MonoBehaviour per file so Unity's prefab serializer
    /// can resolve the script GUID).
    /// </summary>
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
