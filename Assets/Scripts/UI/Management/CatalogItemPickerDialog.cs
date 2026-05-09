using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Modal dialog for adding a new catalog entry. Spawned by ShopCatalogTabView's
    /// "+ Add" button. Lists every ItemSO in Resources/Data/Item (alphabetically),
    /// excluding items already in the shop's catalog.
    ///
    /// Phase 2b: shows ALL items. Future "known-entities registry" session swaps the
    /// candidate source to caller.CharacterKnownEntities.GetKnownItems() — one-line change.
    ///
    /// Singleton instance pattern (matches HiringTabView idiom for transient panels).
    /// </summary>
    public sealed class CatalogItemPickerDialog : MonoBehaviour
    {
        public const string PrefabResourcePath = "UI/Management/CatalogItemPickerDialog";

        [Header("Wiring")]
        [SerializeField] private TMP_Dropdown _itemDropdown;
        [SerializeField] private TMP_InputField _maxStockInput;
        [SerializeField] private TMP_InputField _priceOverrideInput;
        [SerializeField] private TMP_Text _priceHelper;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private static CatalogItemPickerDialog _instance;

        private ShopBuilding _shop;
        private List<ItemSO> _availableItems;
        private Action<ItemSO, int, int> _onPicked;

        public static void Show(ShopBuilding shop, Action<ItemSO, int, int> onPicked)
        {
            try
            {
                if (shop == null)
                {
                    Debug.LogWarning("[CatalogItemPickerDialog] Show called with null shop — ignored.");
                    return;
                }
                if (_instance == null)
                {
                    var prefab = Resources.Load<GameObject>(PrefabResourcePath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[CatalogItemPickerDialog] Prefab missing at Resources/{PrefabResourcePath} — dialog will not open.");
                        return;
                    }
                    var go = Instantiate(prefab);
                    _instance = go.GetComponent<CatalogItemPickerDialog>();
                    if (_instance == null)
                    {
                        Debug.LogError("[CatalogItemPickerDialog] Prefab does not carry the CatalogItemPickerDialog component.");
                        Destroy(go);
                        return;
                    }
                }
                _instance.Bind(shop, onPicked);
                _instance.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void Bind(ShopBuilding shop, Action<ItemSO, int, int> onPicked)
        {
            _shop = shop;
            _onPicked = onPicked;
            PopulateDropdown();

            if (_itemDropdown != null) _itemDropdown.onValueChanged.AddListener(OnDropdownChanged);
            if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirm);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancel);
        }

        private void PopulateDropdown()
        {
            // Phase 2b: list every ItemSO. Future swap to known-entities registry is one line.
            _availableItems = new List<ItemSO>(Resources.LoadAll<ItemSO>("Data/Item"));
            // Filter out items already in the catalog (no duplicate entries).
            _availableItems.RemoveAll(item => _shop.GetCatalogEntry(item).HasValue);
            _availableItems.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));

            if (_itemDropdown != null)
            {
                _itemDropdown.ClearOptions();
                var options = new List<TMP_Dropdown.OptionData>(_availableItems.Count);
                for (int i = 0; i < _availableItems.Count; i++)
                {
                    var item = _availableItems[i];
                    options.Add(new TMP_Dropdown.OptionData(item.ItemName, item.Icon, Color.white));
                }
                _itemDropdown.AddOptions(options);
                _itemDropdown.value = 0;
            }

            if (_maxStockInput != null) _maxStockInput.SetTextWithoutNotify("10");
            if (_priceOverrideInput != null) _priceOverrideInput.SetTextWithoutNotify("0");
            UpdatePriceHelper();
        }

        private void OnDropdownChanged(int idx) => UpdatePriceHelper();

        private void UpdatePriceHelper()
        {
            if (_priceHelper == null) return;
            int idx = _itemDropdown != null ? _itemDropdown.value : -1;
            if (idx < 0 || idx >= _availableItems.Count) { _priceHelper.text = ""; return; }
            var item = _availableItems[idx];
            _priceHelper.text = item.BasePrice > 0
                ? $"0 = use base price ({item.BasePrice} g)"
                : "0 = item has no base price";
        }

        private void OnConfirm()
        {
            int idx = _itemDropdown != null ? _itemDropdown.value : -1;
            if (idx < 0 || idx >= _availableItems.Count) { Hide(); return; }
            int.TryParse(_maxStockInput?.text ?? "0", out int maxStock);
            int.TryParse(_priceOverrideInput?.text ?? "0", out int price);
            _onPicked?.Invoke(_availableItems[idx], maxStock, price);
            Hide();
        }

        private void OnCancel() => Hide();

        private void Hide()
        {
            if (_itemDropdown != null) _itemDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirm);
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancel);
            gameObject.SetActive(false);
            _shop = null;
            _onPicked = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
