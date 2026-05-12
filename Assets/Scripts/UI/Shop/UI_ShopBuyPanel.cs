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
    /// Player-facing shop buy panel. Opens via OpenBuyPanelClientRpc; reads
    /// authoritative state from ShopBuilding (catalog) + per-shelf StorageFurniture
    /// (stock) + CharacterWallet. Reactive — refreshes on any of those events.
    ///
    /// All mutations server-authoritative through Cashier.NetSync ServerRpcs.
    /// </summary>
    public class UI_ShopBuyPanel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _walletText;
        [SerializeField] private TMP_Text _totalText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;   // prefab carrying a UI_ShopBuyRow component

        /// <summary>
        /// Programmatically ensure the panel root has its own Canvas + GraphicRaycaster
        /// so it renders and raycasts independently of whatever scene canvas it ends up
        /// under — Resources.Load → Instantiate places the prefab at the scene root by
        /// default. Mirrors the defensive guard in UI_StorageFurniturePanel.cs:58-71.
        /// </summary>
        private void Awake()
        {
            var canvas = GetComponent<UnityEngine.Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<UnityEngine.Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private static UI_ShopBuyPanel _instance;
        private Cashier _cashier;
        private Character _customer;
        private ShopBuilding _shop;
        private readonly Dictionary<ItemSO, int> _quantities = new();
        private readonly List<UI_ShopBuyRow> _rows = new();

        public static void Open(Cashier cashier, Character customer)
        {
            if (_instance == null)
            {
                var prefab = Resources.Load<GameObject>("UI/UI_ShopBuyPanel");
                if (prefab == null) { Debug.LogError("[UI_ShopBuyPanel] prefab not found at Resources/UI/UI_ShopBuyPanel"); return; }

                // Parent under PlayerUI.HudCanvas so the child Canvas inherits the HUD's
                // RenderMode (ScreenSpaceOverlay) — without this, the standalone Canvas
                // defaults to WorldSpace and the panel renders in the game world.
                // Matches UI_OwnerManagementPanel.cs:84-89 pattern.
                if (PlayerUI.Instance == null || PlayerUI.Instance.HudCanvas == null)
                {
                    Debug.LogWarning("[UI_ShopBuyPanel] PlayerUI HUD canvas unavailable — cannot parent panel.");
                    return;
                }
                var go = Instantiate(prefab, PlayerUI.Instance.HudCanvas.transform, false);
                _instance = go.GetComponent<UI_ShopBuyPanel>();
            }
            _instance.Bind(cashier, customer);
            _instance.gameObject.SetActive(true);
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance.Unbind();
            _instance.gameObject.SetActive(false);
        }

        private void Bind(Cashier cashier, Character customer)
        {
            _cashier = cashier;
            _customer = customer;
            _shop = cashier.LinkedShop;
            if (_shop == null) { Debug.LogError("[UI_ShopBuyPanel] cashier has no LinkedShop"); Close(); return; }

            _titleText.text = $"Shop: {_shop.BuildingName}";
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);

            SubscribeAll();
            RebuildRows();
            Refresh();
        }

        private void Unbind()
        {
            _confirmButton.onClick.RemoveListener(OnConfirmClicked);
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
            UnsubscribeAll();

            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            _quantities.Clear();
            _cashier = null; _customer = null; _shop = null;
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
                Close();
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
            _walletText.text = $"Wallet: {wallet} g";
            _totalText.text = $"Total: {total} g";
            _confirmButton.interactable = total <= wallet && _quantities.Count > 0;
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
            if (_cashier?.NetSync == null) { Close(); return; }
            _cashier.NetSync.CancelPlayerTransactionServerRpc();
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
            if (_instance == this) _instance = null;
        }
    }
}
