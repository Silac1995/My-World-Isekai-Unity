using System.Collections.Generic;
using MWI.Economy;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Shop
{
    /// <summary>
    /// Player-facing shop buy panel. Lives as a scene child of UI_PlayerHUD per the
    /// canonical HUD pattern (mirrors <see cref="UI_StorageFurniturePanel"/>). Opened
    /// by <see cref="PlayerUI.OpenShopBuyPanel"/> from <c>CashierNetSync.OpenBuyPanelClientRpc</c>;
    /// closed by <see cref="UI_WindowBase.CloseWindow"/> (auto-wired close button) or
    /// <see cref="PlayerUI.CloseShopBuyPanel"/>.
    ///
    /// Reads authoritative state from ShopBuilding (catalog) + per-shelf StorageFurniture
    /// (stock) + CharacterWallet. Reactive — refreshes on any of those events. All
    /// mutations go through Cashier.NetSync ServerRpcs.
    /// </summary>
    public class UI_ShopBuyPanel : UI_WindowBase
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _walletText;
        [SerializeField] private TMP_Text _totalText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;   // prefab carrying a UI_ShopBuyRow component

        private Cashier _cashier;
        private Character _customer;
        private ShopBuilding _shop;
        private readonly Dictionary<ItemSO, int> _quantities = new();
        private readonly List<UI_ShopBuyRow> _rows = new();

        /// <summary>
        /// Programmatically ensure the panel root has its own Canvas + GraphicRaycaster
        /// so it renders/raycasts independently of the parent HUD canvas — defends
        /// against prefab override propagation quirks where the nested UI_PlayerHUD
        /// instance might not pick up Canvas/sortingOrder values authored on this prefab.
        /// Mirrors <see cref="UI_StorageFurniturePanel"/>'s guard.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            var canvas = GetComponent<UnityEngine.Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<UnityEngine.Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        /// <summary>
        /// Called by <see cref="PlayerUI.OpenShopBuyPanel"/>. Activates the panel, wires
        /// up subscriptions, and paints the initial state. Calling this while already
        /// open re-binds to the new target cleanly.
        /// </summary>
        public void Initialize(Cashier cashier, Character customer)
        {
            Debug.Log($"<color=magenta>[UI_ShopBuyPanel]</color> Initialize ENTRY. cashier={(cashier != null ? cashier.FurnitureName : "null")}, customer={(customer != null ? customer.CharacterName : "null")}, cashier.LinkedShop={(cashier != null && cashier.LinkedShop != null ? cashier.LinkedShop.BuildingName : "null")}, cashier.LinkedBuilding={(cashier != null && cashier.LinkedBuilding != null ? cashier.LinkedBuilding.name : "null")}.", this);

            if (cashier == null || customer == null)
            {
                Debug.LogWarning("<color=orange>[UI_ShopBuyPanel]</color> Initialize called with null cashier or customer.");
                return;
            }

            UnsubscribeAll();
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            _quantities.Clear();

            _cashier = cashier;
            _customer = customer;
            _shop = cashier.LinkedShop;
            if (_shop == null)
            {
                // Late-bind fallback: on a joining client the Cashier may have spawned before
                // its parent ShopBuilding finished parenting, so the Awake-time
                // GetComponentInParent<CommercialBuilding>() returned null. Trigger the same
                // idempotent re-resolution path that ShopBuilding.OnNetworkSpawn uses.
                Debug.LogWarning($"<color=orange>[UI_ShopBuyPanel]</color> cashier.LinkedShop was null at Initialize — attempting TryRegisterWithShop fallback.");
                cashier.TryRegisterWithShop();
                _shop = cashier.LinkedShop;
            }
            if (_shop == null)
            {
                Debug.LogError("[UI_ShopBuyPanel] cashier has no LinkedShop after fallback — aborting Initialize.");
                CloseWindow();
                return;
            }

            if (_titleText != null) _titleText.text = $"Shop: {_shop.BuildingName}";

            _confirmButton.onClick.RemoveAllListeners();
            _cancelButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);

            SubscribeAll();
            RebuildRows();
            Refresh();

            OpenWindow();
            Debug.Log($"<color=magenta>[UI_ShopBuyPanel]</color> Initialize completed: OpenWindow called. gameObject.activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}.", this);
        }

        /// <summary>
        /// Closes the panel: unsubscribes events, clears rows, then defers to
        /// <see cref="UI_WindowBase.CloseWindow"/> for the SetActive(false). Called by
        /// the inherited close button (auto-wired in <see cref="UI_WindowBase.Awake"/>),
        /// the cancel button, the lock-released subscriber, and
        /// <see cref="PlayerUI.CloseShopBuyPanel"/>.
        /// </summary>
        public override void CloseWindow()
        {
            UnsubscribeAll();
            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
            for (int i = 0; i < _rows.Count; i++) if (_rows[i] != null) Destroy(_rows[i].gameObject);
            _rows.Clear();
            _quantities.Clear();
            _cashier = null;
            _customer = null;
            _shop = null;

            base.CloseWindow();
        }

        private void SubscribeAll()
        {
            if (_shop != null) _shop.OnCatalogChanged += Refresh;
            if (_shop != null)
            {
                for (int i = 0; i < _shop.SellShelves.Count; i++)
                {
                    var shelf = _shop.SellShelves[i];
                    if (shelf != null) shelf.OnInventoryChanged += Refresh;
                }
            }
            if (_cashier?.NetSync != null)
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged += OnLockChanged;
            if (_customer?.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged += OnWalletChanged;
        }

        private void UnsubscribeAll()
        {
            if (_shop != null)
            {
                _shop.OnCatalogChanged -= Refresh;
                for (int i = 0; i < _shop.SellShelves.Count; i++)
                {
                    var shelf = _shop.SellShelves[i];
                    if (shelf != null) shelf.OnInventoryChanged -= Refresh;
                }
            }
            if (_cashier?.NetSync != null)
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged -= OnLockChanged;
            if (_customer?.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged -= OnWalletChanged;
        }

        private void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            for (int i = 0; i < _shop.Catalog.Count; i++)
            {
                var entry = _shop.Catalog[i];
                if (entry.Item == null) continue;
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<UI_ShopBuyRow>();
                row.Bind(entry, OnRowQuantityChanged);
                _rows.Add(row);
            }
        }

        private void OnRowQuantityChanged(ItemSO item, int qty)
        {
            if (qty <= 0) _quantities.Remove(item);
            else _quantities[item] = qty;
            Refresh();
        }

        private void OnWalletChanged(CurrencyId currency, int oldValue, int newValue) => Refresh();

        private void OnLockChanged(ulong previous, ulong current)
        {
            if (current == 0 && _customer != null)
            {
                MWI.UI.Notifications.UI_Toast.Show("Vendor left — purchase cancelled.", MWI.UI.Notifications.ToastType.Warning);
                CloseWindow();
            }
        }

        private void Refresh()
        {
            if (_shop == null || _customer == null) return;

            int total = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int stock = AggregateStockAcrossShelves(row.Item);
                row.SetStock(stock);
                row.ClampQuantity(0, stock);
                int qty = row.CurrentQuantity;
                if (qty > 0) _quantities[row.Item] = qty; else _quantities.Remove(row.Item);

                var entry = _shop.GetCatalogEntry(row.Item);
                if (entry.HasValue) total += ShopBuilding.ResolvePrice(entry.Value) * qty;
            }

            int wallet = _customer.CharacterWallet.GetBalance(CurrencyId.Default);
            if (_walletText != null) _walletText.text = $"Wallet: {wallet} g";
            if (_totalText != null) _totalText.text = $"Total: {total} g";
            if (_confirmButton != null) _confirmButton.interactable = total <= wallet && _quantities.Count > 0;
        }

        private int AggregateStockAcrossShelves(ItemSO item)
        {
            int count = 0;
            for (int s = 0; s < _shop.SellShelves.Count; s++)
            {
                var shelf = _shop.SellShelves[s];
                if (shelf == null) continue;
                for (int sl = 0; sl < shelf.Capacity; sl++)
                {
                    var slot = shelf.GetItemSlot(sl);
                    if (slot != null && !slot.IsEmpty() && slot.ItemInstance.ItemSO == item) count++;
                }
            }
            return count;
        }

        private void OnConfirmClicked()
        {
            if (_cashier?.NetSync == null) return;
            var ids = new FixedString64Bytes[_quantities.Count];
            var qtys = new int[_quantities.Count];
            int i = 0;
            foreach (var kv in _quantities)
            {
                ids[i] = new FixedString64Bytes(kv.Key.ItemId);
                qtys[i] = kv.Value;
                i++;
            }
            var payload = new BuySelectionPayload { ItemIds = ids, Quantities = qtys };
            _cashier.NetSync.SubmitPlayerSelectionServerRpc(payload);
        }

        private void OnCancelClicked()
        {
            if (_cashier?.NetSync == null) { CloseWindow(); return; }
            _cashier.NetSync.CancelPlayerTransactionServerRpc();
        }

        private void OnDisable() => UnsubscribeAll();

        protected override void OnDestroy()
        {
            UnsubscribeAll();
            base.OnDestroy();
        }
    }
}
