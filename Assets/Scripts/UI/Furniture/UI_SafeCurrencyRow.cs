using System;
using MWI.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Furniture
{
    /// <summary>
    /// One row of the SafeFurniture player UI — one <see cref="CurrencyId"/>. Owns the
    /// deposit/withdraw inputs + submit buttons for a single currency. Bound by
    /// <c>UI_SafePanel</c>.
    ///
    /// Stateless about the safe itself: the parent panel injects balance getters and
    /// submit callbacks via <see cref="Initialize"/>, so the row never touches
    /// <c>SafeFurniture</c>, <c>SafeFurnitureNetworkSync</c>, or <c>CharacterWallet</c>
    /// directly. This keeps the row reusable and decoupled from gameplay state.
    /// </summary>
    public sealed class UI_SafeCurrencyRow : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private TMP_Text _currencyLabel;
        [SerializeField] private TMP_Text _safeBalanceLabel;
        [SerializeField] private TMP_Text _walletBalanceLabel;

        [Header("Deposit")]
        [SerializeField] private TMP_InputField _depositInput;
        [SerializeField] private Button _depositPlusButton;
        [SerializeField] private Button _depositMinusButton;
        [SerializeField] private Button _depositMaxButton;
        [SerializeField] private Button _depositSubmitButton;

        [Header("Withdraw")]
        [SerializeField] private TMP_InputField _withdrawInput;
        [SerializeField] private Button _withdrawPlusButton;
        [SerializeField] private Button _withdrawMinusButton;
        [SerializeField] private Button _withdrawMaxButton;
        [SerializeField] private Button _withdrawSubmitButton;

        private CurrencyId _currency;
        private Func<int> _getSafeBalance;
        private Func<int> _getWalletBalance;
        private Action<int> _onDepositSubmit;
        private Action<int> _onWithdrawSubmit;

        /// <summary>
        /// Bind the row to a currency. Re-Init safe (RemoveAllListeners before AddListener),
        /// so the parent panel may rebuild rows on currency-set changes without leaking
        /// stale callbacks.
        /// </summary>
        public void Initialize(
            CurrencyId currency,
            string displayName,
            Func<int> getSafeBalance,
            Func<int> getWalletBalance,
            Action<int> onDepositSubmit,
            Action<int> onWithdrawSubmit)
        {
            _currency = currency;
            _getSafeBalance = getSafeBalance;
            _getWalletBalance = getWalletBalance;
            _onDepositSubmit = onDepositSubmit;
            _onWithdrawSubmit = onWithdrawSubmit;

            if (_currencyLabel != null) _currencyLabel.text = displayName;

            // Wire buttons. RemoveAllListeners first so re-Init is safe.
            WireButton(_depositPlusButton, () => Step(_depositInput, +1));
            WireButton(_depositMinusButton, () => Step(_depositInput, -1));
            WireButton(_depositMaxButton, () => SetInput(_depositInput, _getWalletBalance != null ? _getWalletBalance() : 0));
            WireButton(_depositSubmitButton, OnDepositClicked);

            WireButton(_withdrawPlusButton, () => Step(_withdrawInput, +1));
            WireButton(_withdrawMinusButton, () => Step(_withdrawInput, -1));
            WireButton(_withdrawMaxButton, () => SetInput(_withdrawInput, _getSafeBalance != null ? _getSafeBalance() : 0));
            WireButton(_withdrawSubmitButton, OnWithdrawClicked);

            if (_depositInput != null)
            {
                _depositInput.onValueChanged.RemoveAllListeners();
                _depositInput.onValueChanged.AddListener(_ => RefreshSubmitAvailability());
            }
            if (_withdrawInput != null)
            {
                _withdrawInput.onValueChanged.RemoveAllListeners();
                _withdrawInput.onValueChanged.AddListener(_ => RefreshSubmitAvailability());
            }

            Refresh();
        }

        /// <summary>
        /// Repaint balance labels and re-evaluate submit-button interactability.
        /// Driven by <c>OnBalanceChanged</c> events from the parent panel — never
        /// from Update — so it is not a per-frame hot path.
        /// </summary>
        public void Refresh()
        {
            if (_safeBalanceLabel != null)
                _safeBalanceLabel.text = $"Safe: {FormatAmount(_getSafeBalance != null ? _getSafeBalance() : 0)}";
            if (_walletBalanceLabel != null)
                _walletBalanceLabel.text = $"Wallet: {FormatAmount(_getWalletBalance != null ? _getWalletBalance() : 0)}";
            RefreshSubmitAvailability();
        }

        public CurrencyId Currency => _currency;

        private void RefreshSubmitAvailability()
        {
            int safeBal = _getSafeBalance != null ? _getSafeBalance() : 0;
            int walletBal = _getWalletBalance != null ? _getWalletBalance() : 0;
            int dep = ParseAmount(_depositInput);
            int wd = ParseAmount(_withdrawInput);

            if (_depositSubmitButton != null)
                _depositSubmitButton.interactable = dep > 0 && dep <= walletBal;
            if (_withdrawSubmitButton != null)
                _withdrawSubmitButton.interactable = wd > 0 && wd <= safeBal;
        }

        private void OnDepositClicked()
        {
            int amount = ParseAmount(_depositInput);
            if (amount <= 0) return;
            _onDepositSubmit?.Invoke(amount);
            if (_depositInput != null) _depositInput.text = "0";
        }

        private void OnWithdrawClicked()
        {
            int amount = ParseAmount(_withdrawInput);
            if (amount <= 0) return;
            _onWithdrawSubmit?.Invoke(amount);
            if (_withdrawInput != null) _withdrawInput.text = "0";
        }

        private static void WireButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static void Step(TMP_InputField field, int delta)
        {
            if (field == null) return;
            int cur = int.TryParse(field.text, out int n) ? n : 0;
            int next = Mathf.Max(0, cur + delta);
            field.text = next.ToString();
        }

        private static void SetInput(TMP_InputField field, int value)
        {
            if (field == null) return;
            field.text = Mathf.Max(0, value).ToString();
        }

        private static int ParseAmount(TMP_InputField field)
        {
            if (field == null) return 0;
            if (!int.TryParse(field.text, out int n)) return 0;
            return Mathf.Max(0, n);
        }

        // N0 = thousand-separator grouping; forward-compat for large Kingdom-currency values.
        private static string FormatAmount(int v) => v.ToString("N0");

        // Rule #16: clean up listeners on destroy. Buttons + input fields may outlive
        // this row in pooled-UI scenarios, so RemoveAll defensively.
        private void OnDestroy()
        {
            if (_depositPlusButton != null) _depositPlusButton.onClick.RemoveAllListeners();
            if (_depositMinusButton != null) _depositMinusButton.onClick.RemoveAllListeners();
            if (_depositMaxButton != null) _depositMaxButton.onClick.RemoveAllListeners();
            if (_depositSubmitButton != null) _depositSubmitButton.onClick.RemoveAllListeners();

            if (_withdrawPlusButton != null) _withdrawPlusButton.onClick.RemoveAllListeners();
            if (_withdrawMinusButton != null) _withdrawMinusButton.onClick.RemoveAllListeners();
            if (_withdrawMaxButton != null) _withdrawMaxButton.onClick.RemoveAllListeners();
            if (_withdrawSubmitButton != null) _withdrawSubmitButton.onClick.RemoveAllListeners();

            if (_depositInput != null) _depositInput.onValueChanged.RemoveAllListeners();
            if (_withdrawInput != null) _withdrawInput.onValueChanged.RemoveAllListeners();
        }
    }
}
