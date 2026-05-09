using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Shop
{
    /// <summary>
    /// Single row inside <see cref="UI_ShopBuyPanel"/>. Displays one catalog entry
    /// (icon, name, price, stock, subtotal) and a quantity stepper (+/- + input).
    /// Forwards quantity changes to the parent panel via the <c>onQuantityChanged</c>
    /// callback supplied at <see cref="Bind"/> time.
    /// </summary>
    public class UI_ShopBuyRow : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _priceText;
        [SerializeField] private TMP_Text _stockText;
        [SerializeField] private TMP_Text _subtotalText;
        [SerializeField] private TMP_InputField _quantityInput;
        [SerializeField] private Button _plusButton;
        [SerializeField] private Button _minusButton;

        public ItemSO Item { get; private set; }
        public int CurrentQuantity { get; private set; }
        private int _stock;
        private int _price;
        private Action<ItemSO, int> _onQuantityChanged;

        public void Bind(ShopItemEntry entry, Action<ItemSO, int> onQuantityChanged)
        {
            Item = entry.Item;
            _price = ShopBuilding.ResolvePrice(entry);
            _onQuantityChanged = onQuantityChanged;

            _icon.sprite = Item.Icon;
            _nameText.text = Item.ItemName;
            _priceText.text = $"{_price} g";

            _quantityInput.text = "0";
            _quantityInput.onEndEdit.AddListener(OnQuantityInputChanged);
            _plusButton.onClick.AddListener(OnPlus);
            _minusButton.onClick.AddListener(OnMinus);
            UpdateSubtotal();
        }

        public void SetStock(int stock)
        {
            _stock = stock;
            _stockText.text = $"{stock} in stock";
        }

        public void ClampQuantity(int min, int max)
        {
            int q = CurrentQuantity;
            if (q < min) q = min;
            if (q > max) q = max;
            if (q != CurrentQuantity) SetQuantity(q, fireCallback: false);
        }

        private void SetQuantity(int q, bool fireCallback)
        {
            CurrentQuantity = q;
            _quantityInput.SetTextWithoutNotify(q.ToString());
            UpdateSubtotal();
            if (fireCallback) _onQuantityChanged?.Invoke(Item, q);
        }

        private void UpdateSubtotal() => _subtotalText.text = $"= {_price * CurrentQuantity} g";

        private void OnQuantityInputChanged(string raw)
        {
            if (!int.TryParse(raw, out int q)) q = 0;
            if (q < 0) q = 0;
            if (q > _stock) q = _stock;
            SetQuantity(q, fireCallback: true);
        }

        private void OnPlus()
        {
            if (CurrentQuantity < _stock) SetQuantity(CurrentQuantity + 1, fireCallback: true);
        }

        private void OnMinus()
        {
            if (CurrentQuantity > 0) SetQuantity(CurrentQuantity - 1, fireCallback: true);
        }

        private void OnDestroy()
        {
            _quantityInput.onEndEdit.RemoveListener(OnQuantityInputChanged);
            _plusButton.onClick.RemoveListener(OnPlus);
            _minusButton.onClick.RemoveListener(OnMinus);
        }
    }
}
