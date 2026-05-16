using System.Collections.Generic;
using MWI.Economy;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Furniture
{
    /// <summary>
    /// Player UI panel for <see cref="SafeFurniture"/>. Opens via
    /// <see cref="PlayerUI.OpenSafePanel"/> from <see cref="SafeFurniture.OnInteract"/>
    /// when the local owner-player taps E. Builds one <see cref="UI_SafeCurrencyRow"/>
    /// per <see cref="CurrencyId"/> present in the safe (with a Default fallback row
    /// when the safe is empty so the player can seed it from their wallet).
    ///
    /// <para>
    /// Composition mirrors the sibling <c>UI_StorageFurniturePanel</c>:
    ///   - Header label + close button (close button is the
    ///     <see cref="UI_WindowBase._buttonClose"/> wired by the base class).
    ///   - Scrollable row container, one row per currency.
    ///   - Transient status toast for failure reasons fired by
    ///     <c>SafeFurnitureNetworkSync.OperationResultClientRpc</c>.
    /// </para>
    ///
    /// <para>
    /// Closes on: ESC, target despawn, close button, or the player walking out of
    /// the safe's <see cref="InteractableObject.InteractionZone"/> (1Hz poll —
    /// rule #34 cheap cadence, rule #36 zone-overlap test, rule #26 unscaled time).
    /// </para>
    /// </summary>
    public sealed class UI_SafePanel : UI_WindowBase
    {
        [Header("Wiring (assign in prefab)")]
        [SerializeField] private TMP_Text _titleLabel;

        [Header("Rows")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_SafeCurrencyRow _rowPrefab;

        [Header("Feedback")]
        [Tooltip("Transient label that surfaces deposit/withdraw failure reasons fired by SafeFurnitureNetworkSync.OperationResultClientRpc. Hidden by default.")]
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private float _statusVisibleSeconds = 3f;

        // State
        private SafeFurniture _safe;
        private Character _customer;
        private InteractableObject _targetInteractable;
        private float _autoClosePollTimer;
        private float _statusHideAt;
        private readonly Dictionary<int, UI_SafeCurrencyRow> _rows = new Dictionary<int, UI_SafeCurrencyRow>();

        // Rule #34: cheap 1 Hz cadence for the out-of-zone poll — the only thing
        // we need it for. ESC + status-toast hide are evaluated every frame but
        // both are O(1) branches.
        private const float AutoClosePollInterval = 1f;

        /// <summary>
        /// Awake just chains to the base. We DO NOT add a Canvas/GraphicRaycaster on the
        /// panel root — that would create a second Canvas at runtime because the inherited
        /// <c>Canvas</c> child GameObject (from UI_WindowBase.prefab variant chain) already
        /// supplies them. Having two Canvases (root + child) at different hierarchy levels
        /// produces conflicting render-mode/sorting state and was the root cause of the
        /// "panel visible in Scene view but invisible in Game view" symptom (2026-05-16).
        /// The panel's content (header / row container / status label) lives inside the
        /// inherited Canvas child's hierarchy; that single Canvas is the only render surface.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
        }

        /// <summary>
        /// Called by <see cref="PlayerUI.OpenSafePanel"/>. Activates the panel,
        /// wires up subscriptions, and paints the initial state. Calling this while
        /// already open re-binds to the new target cleanly.
        /// </summary>
        public void Initialize(SafeFurniture safe, Character customer)
        {
            if (safe == null || customer == null)
            {
                Debug.LogWarning("<color=orange>[SafePanel]</color> Initialize called with null safe or customer.");
                return;
            }

            UnbindAll();

            _safe = safe;
            _customer = customer;
            _targetInteractable = safe.GetComponent<InteractableObject>();

            if (_titleLabel != null)
            {
                // Role-based label expansion can come later; "Safe" is the v1 label.
                _titleLabel.text = safe.FurnitureName;
                if (string.IsNullOrEmpty(_titleLabel.text)) _titleLabel.text = "Safe";
            }

            // Authoritative-state subscriptions. Despawn is handled by the Update
            // null-check (CloseWindow on _safe == null) within one frame — no need
            // for a per-NetworkObject despawn event (which doesn't exist on
            // NetworkObject in this NGO version).
            _safe.OnBalanceChanged += HandleSafeBalanceChanged;
            if (_customer.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged += HandleWalletBalanceChanged;

            RebuildRows();

            if (_statusLabel != null) _statusLabel.gameObject.SetActive(false);

            OpenWindow();
        }

        /// <summary>
        /// Closes the panel: unsubscribes events, tears down rows, then defers to
        /// <see cref="UI_WindowBase.CloseWindow"/> for the SetActive(false). Called
        /// by the inherited close button, the ESC handler in <see cref="Update"/>,
        /// the out-of-zone auto-close, target despawn, and external callers like
        /// <see cref="PlayerUI.CloseSafePanel"/>.
        /// </summary>
        public override void CloseWindow()
        {
            UnbindAll();
            ClearRows();
            _safe = null;
            _customer = null;
            _targetInteractable = null;

            base.CloseWindow();
        }

        // Rule #16: unsubscribe in OnDisable + OnDestroy (in addition to CloseWindow
        // — the panel may be disabled by parent-hierarchy SetActive without going
        // through CloseWindow, e.g. a HUD hide-on-pause).
        private void OnDisable() => UnbindAll();

        protected override void OnDestroy()
        {
            UnbindAll();
            base.OnDestroy();
        }

        private void UnbindAll()
        {
            // Unity fake-null safety: destroyed UnityEngine.Objects report `!= null`
            // in plain C# but throw on member access. Unity's overloaded `!= null`
            // returns false correctly for destroyed objects.
            if (_safe != null)
            {
                _safe.OnBalanceChanged -= HandleSafeBalanceChanged;
            }
            if (_customer != null && _customer.CharacterWallet != null)
            {
                _customer.CharacterWallet.OnBalanceChanged -= HandleWalletBalanceChanged;
            }
        }

        private void Update()
        {
            if (_safe == null || _customer == null) { CloseWindow(); return; }

            // ESC closes the panel (rule #33 carve-out: input that targets the UI itself
            // stays in the UI). Same shape as UI_StorageFurniturePanel.Update.
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseWindow(); return; }

            // Rule #26: UI uses unscaled time so it remains responsive under any
            // GameSpeedController scale (including pause / 0x). Fully qualified to
            // disambiguate from the project's MWI.Time namespace (resolved before
            // UnityEngine.Time inside the MWI.UI.Furniture namespace).
            _autoClosePollTimer += UnityEngine.Time.unscaledDeltaTime;
            if (_autoClosePollTimer >= AutoClosePollInterval)
            {
                _autoClosePollTimer = 0f;
                // Rule #36: zone-overlap test — NEVER raw Vector3.Distance against
                // the interaction point. SafeFurniture's FurnitureInteractable
                // exposes IsCharacterInInteractionZone through its InteractableObject
                // base class.
                if (_targetInteractable != null && !_targetInteractable.IsCharacterInInteractionZone(_customer))
                {
                    CloseWindow();
                    return;
                }
            }

            // Hide status toast on timeout (cheap per-frame branch).
            if (_statusLabel != null && _statusLabel.gameObject.activeSelf && UnityEngine.Time.unscaledTime >= _statusHideAt)
            {
                _statusLabel.gameObject.SetActive(false);
            }
        }

        private void RebuildRows()
        {
            ClearRows();
            if (_safe == null || _rowPrefab == null || _rowContainer == null) return;

            // safe.Balances allocates a fresh list per call (documented on the getter)
            // — fine at panel-open / OnBalanceChanged cadence, not a per-frame hot path.
            foreach (var entry in _safe.Balances)
            {
                AddRow(new CurrencyId(entry.CurrencyId));
            }

            // Empty safe (fresh None-role / unseeded Treasury) → show a Default-currency
            // row so the player can seed it by depositing from their wallet.
            if (_rows.Count == 0)
            {
                AddRow(CurrencyId.Default);
            }
        }

        private void AddRow(CurrencyId currency)
        {
            if (_rows.ContainsKey(currency.Id)) return;
            var row = Instantiate(_rowPrefab, _rowContainer);

            // Capture the currency by value into a local so each closure binds to
            // its own copy. Modern C# foreach scoping (per-iteration variable) makes
            // this redundant for the foreach in RebuildRows, but keeping the explicit
            // local-copy is a defense-in-depth habit and matches the spec note.
            var c = currency;
            row.Initialize(
                c,
                DisplayNameFor(c),
                IconColorFor(c),
                getSafeBalance: () => _safe != null ? _safe.GetBalance(c) : 0,
                getWalletBalance: () => _customer != null && _customer.CharacterWallet != null
                    ? _customer.CharacterWallet.GetBalance(c) : 0,
                onDepositSubmit: amount => SubmitDeposit(c, amount),
                onWithdrawSubmit: amount => SubmitWithdraw(c, amount));
            _rows[c.Id] = row;
        }

        /// <summary>
        /// Deterministic icon-color fallback per CurrencyId until a CurrencySO._iconSprite
        /// registry exists. Default (the only currency today) is gold. Future Kingdom
        /// currencies get distinct hues derived from their int id.
        /// </summary>
        private static Color IconColorFor(CurrencyId c)
        {
            if (c.Id == CurrencyId.Default.Id) return new Color(0.97f, 0.78f, 0.31f, 1f); // gold
            // Hash the int id to a hue in [0, 1), saturation 0.55, value 0.85.
            float h = (Mathf.Abs(c.Id) * 137f % 360f) / 360f;
            return Color.HSVToRGB(h, 0.55f, 0.85f);
        }

        private void ClearRows()
        {
            foreach (var row in _rows.Values)
            {
                if (row != null) Destroy(row.gameObject);
            }
            _rows.Clear();
        }

        private void HandleSafeBalanceChanged()
        {
            if (_safe == null) return;

            // A new currency may have appeared (e.g. an NPC just deposited Kingdom
            // coins this safe didn't previously know about). Surface a row for it.
            foreach (var entry in _safe.Balances)
            {
                if (!_rows.ContainsKey(entry.CurrencyId)) AddRow(new CurrencyId(entry.CurrencyId));
            }

            // Repaint every row's labels + submit-button interactability.
            foreach (var row in _rows.Values)
            {
                if (row != null) row.Refresh();
            }
        }

        private void HandleWalletBalanceChanged(CurrencyId currency, int oldVal, int newVal)
        {
            if (_rows.TryGetValue(currency.Id, out var row))
            {
                if (row != null) row.Refresh();
            }
            else if (newVal > 0 && _safe != null)
            {
                // The wallet now carries a currency the safe doesn't have a row for
                // yet — surface a row so the player can deposit it. Edge case for
                // future Kingdom currencies.
                AddRow(currency);
            }
        }

        private void SubmitDeposit(CurrencyId currency, int amount)
        {
            if (_safe == null || _customer == null) return;
            var sync = _safe.NetSync;
            if (sync == null) return;
            var charRef = new NetworkBehaviourReference(_customer);
            sync.RequestDepositServerRpc(charRef, currency.Id, amount);
        }

        private void SubmitWithdraw(CurrencyId currency, int amount)
        {
            if (_safe == null || _customer == null) return;
            var sync = _safe.NetSync;
            if (sync == null) return;
            var charRef = new NetworkBehaviourReference(_customer);
            sync.RequestWithdrawServerRpc(charRef, currency.Id, amount);
        }

        /// <summary>
        /// Called by <see cref="PlayerUI.OnSafeOperationResult"/> when the server's
        /// targeted <c>OperationResultClientRpc</c> lands on this peer. Success path
        /// is silent — the live <see cref="SafeFurniture.OnBalanceChanged"/> repaint
        /// already reflects the result. Failure path lights the transient status
        /// toast for <see cref="_statusVisibleSeconds"/>.
        /// </summary>
        public void OnOperationResult(bool success, string reason)
        {
            if (success) return; // Balance repaint covers the success path.
            if (_statusLabel == null) return;
            _statusLabel.text = TranslateReason(reason);
            _statusLabel.gameObject.SetActive(true);
            _statusHideAt = UnityEngine.Time.unscaledTime + _statusVisibleSeconds;
        }

        private static string DisplayNameFor(CurrencyId c)
        {
            // v1 only ships CurrencyId.Default. Future Kingdom currencies will need
            // a registry / SO-driven lookup.
            if (c.Id == CurrencyId.Default.Id) return "Coins";
            return $"Currency #{c.Id}";
        }

        // Wire-format reason values from SafeFurnitureNetworkSync.NotifyOperationResult
        // (see spec §9). Keep in sync with the server's emit sites:
        //   - SafeFurnitureNetworkSync.RequestDepositServerRpc  → "out-of-zone", "invalid-amount"
        //   - SafeFurnitureNetworkSync.RequestWithdrawServerRpc → "out-of-zone", "invalid-amount"
        //   - CharacterAction_DepositToSafe                     → "insufficient-wallet"
        //   - CharacterAction_WithdrawFromSafe                  → "insufficient-safe"
        private static string TranslateReason(string raw)
        {
            switch (raw)
            {
                case "insufficient-wallet": return "Not enough coins in wallet.";
                case "insufficient-safe":   return "Not enough coins in safe.";
                case "out-of-zone":         return "Too far from the safe.";
                case "invalid-amount":      return "Invalid amount.";
                default:                    return string.IsNullOrEmpty(raw) ? "Failed." : raw;
            }
        }
    }
}
