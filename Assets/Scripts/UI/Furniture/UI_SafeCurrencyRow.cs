using System;
using MWI.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Furniture
{
    /// <summary>
    /// One row of the SafeFurniture player UI — one <see cref="CurrencyId"/>.
    /// Single amount input + MAX shortcut + Deposit + Withdraw buttons. The clamp-on-submit
    /// behavior means the user can type any value (even higher than they hold) and the
    /// submit handler clamps to the source balance before firing the RPC.
    ///
    /// <para>Stateless about the safe itself: the parent panel injects balance getters and
    /// submit callbacks via <see cref="Initialize"/>, so the row never touches
    /// <c>SafeFurniture</c>, <c>SafeFurnitureNetworkSync</c>, or <c>CharacterWallet</c>
    /// directly. This keeps the row reusable and decoupled from gameplay state.</para>
    /// </summary>
    public sealed class UI_SafeCurrencyRow : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private Image _currencyIcon;        // colored circle fallback until CurrencySO._iconSprite exists
        [SerializeField] private TMP_Text _currencyLabel;
        [SerializeField] private TMP_Text _balanceLabel;     // single line: "Safe 1,250 · You 350"

        [Header("Action row")]
        [SerializeField] private TMP_InputField _amountInput;
        [SerializeField] private Button _maxButton;          // sets _amountInput to max(safe, wallet) — "give me everything in either direction"
        [SerializeField] private Button _depositButton;      // ▼ Deposit — clamps to wallet
        [SerializeField] private Button _withdrawButton;     // ▲ Withdraw — clamps to safe

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
            Color iconColor,
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
            if (_currencyIcon != null) _currencyIcon.color = iconColor;

            // Wire buttons. RemoveAllListeners first so re-Init is safe.
            WireButton(_maxButton, OnMaxClicked);
            WireButton(_depositButton, OnDepositClicked);
            WireButton(_withdrawButton, OnWithdrawClicked);

            if (_amountInput != null)
            {
                _amountInput.onValueChanged.RemoveAllListeners();
                _amountInput.onValueChanged.AddListener(_ => RefreshSubmitAvailability());
                _amountInput.text = "0";
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
            if (_balanceLabel != null)
            {
                int safeBal = _getSafeBalance != null ? _getSafeBalance() : 0;
                int walletBal = _getWalletBalance != null ? _getWalletBalance() : 0;
                // Format matches the final-polish mockup: "Safe 1,250  ·  You 350".
                // Colour accents are baked into TMP rich-text so the same string drives
                // both stocks visually:
                //   Safe value  → gold (#f8d878)
                //   Wallet value → light blue (#b8c8e0)
                _balanceLabel.text =
                    $"Safe <color=#f8d878>{FormatAmount(safeBal)}</color>" +
                    $" <color=#4a5a78>·</color> " +
                    $"You <color=#b8c8e0>{FormatAmount(walletBal)}</color>";
            }
            RefreshSubmitAvailability();
        }

        public CurrencyId Currency => _currency;

        // Submit-button availability rule (Stardew-style clamp-on-submit):
        // Buttons are enabled whenever both (a) the user has typed > 0 AND (b) the source
        // has > 0 to move. We DO NOT disable when typed-amount > source-balance — instead,
        // OnDepositClicked / OnWithdrawClicked clamp the typed amount down to the source
        // balance before invoking the submit callback.
        private void RefreshSubmitAvailability()
        {
            int safeBal = _getSafeBalance != null ? _getSafeBalance() : 0;
            int walletBal = _getWalletBalance != null ? _getWalletBalance() : 0;
            int typed = ParseAmount(_amountInput);

            if (_depositButton != null)
                _depositButton.interactable = typed > 0 && walletBal > 0;
            if (_withdrawButton != null)
                _withdrawButton.interactable = typed > 0 && safeBal > 0;
            if (_maxButton != null)
                _maxButton.interactable = walletBal > 0 || safeBal > 0;
        }

        private void OnMaxClicked()
        {
            // MAX populates the input with the larger of the two balances — "give me
            // everything in either direction". The downstream Deposit/Withdraw click
            // then clamps to its own source balance, so the result is intuitively
            // "deposit all I have" or "withdraw all the safe has".
            int safeBal = _getSafeBalance != null ? _getSafeBalance() : 0;
            int walletBal = _getWalletBalance != null ? _getWalletBalance() : 0;
            int max = Mathf.Max(safeBal, walletBal);
            if (_amountInput != null) _amountInput.text = Mathf.Max(0, max).ToString();
        }

        private void OnDepositClicked()
        {
            int typed = ParseAmount(_amountInput);
            int max = _getWalletBalance != null ? _getWalletBalance() : 0;
            int amount = Mathf.Min(typed, max);
            if (amount <= 0) return;
            _onDepositSubmit?.Invoke(amount);
            if (_amountInput != null) _amountInput.text = "0";
        }

        private void OnWithdrawClicked()
        {
            int typed = ParseAmount(_amountInput);
            int max = _getSafeBalance != null ? _getSafeBalance() : 0;
            int amount = Mathf.Min(typed, max);
            if (amount <= 0) return;
            _onWithdrawSubmit?.Invoke(amount);
            if (_amountInput != null) _amountInput.text = "0";
        }

        private static void WireButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static int ParseAmount(TMP_InputField field)
        {
            if (field == null) return 0;
            if (!int.TryParse(field.text, out int n)) return 0;
            return Mathf.Max(0, n);
        }

        // N0 = thousand-separator grouping; forward-compat for large Kingdom-currency values.
        private static string FormatAmount(int v) => v.ToString("N0");

        // Rule #16: clean up listeners on destroy. Buttons + input may outlive this row
        // in pooled-UI scenarios, so RemoveAll defensively.
        private void OnDestroy()
        {
            if (_maxButton != null) _maxButton.onClick.RemoveAllListeners();
            if (_depositButton != null) _depositButton.onClick.RemoveAllListeners();
            if (_withdrawButton != null) _withdrawButton.onClick.RemoveAllListeners();
            if (_amountInput != null) _amountInput.onValueChanged.RemoveAllListeners();
        }
    }
}
