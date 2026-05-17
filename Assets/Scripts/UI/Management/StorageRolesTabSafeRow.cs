using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// One row in the Safes section of the unified Storages tab. Mirrors
    /// <see cref="StorageRolesTabRow"/> for the SafeFurniture primitive. Renders the
    /// parent <see cref="SafeFurniture"/>'s name + per-currency balance + a
    /// TMP_Dropdown listing the owner-allowed roles for the building
    /// (<see cref="CommercialBuilding.SupportedSafeRoles"/>). Selecting a role fires
    /// <see cref="CommercialBuilding.TrySetSafeRoleServerRpc"/>; the server-side
    /// <see cref="SafeFurniture.OnRoleChanged"/> event refreshes the dropdown's
    /// selection on round-trip so optimistic local state stays in sync with the
    /// authoritative value (and so rejected calls revert visually).
    ///
    /// Subscribes to <see cref="SafeFurniture.OnBalanceChanged"/> so deposits /
    /// withdraws / B2B debits / refunds repaint the per-currency balance line
    /// without needing the whole tab to refresh.
    ///
    /// Split into its own .cs file (one MonoBehaviour per file) so Unity's prefab
    /// serializer reliably resolves the script GUID — mirrors the
    /// <see cref="StorageRolesTabRow"/> shape.
    /// </summary>
    public sealed class StorageRolesTabSafeRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;       // "Safe (north)    Treasury: 120 Default"
        [SerializeField] private TMP_Dropdown _dropdown; // populated from SupportedSafeRoles

        private CommercialBuilding _building;
        private SafeFurniture _safe;

        // Index → enum mapping. The TMP_Dropdown only exposes int indices, so we cache
        // the type-by-index list for the OnValueChanged → ServerRpc translation. Cleared
        // on Bind to support row recycling (currently rows are destroyed/recreated, but
        // the cheap reset keeps recycling viable).
        private readonly List<SafeRoleType> _indexToType = new();

        public void Bind(
            CommercialBuilding building,
            SafeFurniture safe,
            IReadOnlyList<SafeRoleDescriptor> supportedRoles)
        {
            _building = building;
            _safe = safe;

            RefreshLabel();
            BuildDropdown(supportedRoles);
            SyncDropdownToCurrentRole();

            // Subscribe to the per-safe events so the row refreshes on writes that
            // bypass the building's ServerRpc (save-restore, NPC auto-assign on
            // shift-punch, future programmatic writes) AND on every balance mutation
            // (deposit / withdraw / B2B / refund).
            if (_safe != null)
            {
                _safe.OnRoleChanged    += HandleRoleChanged;
                _safe.OnBalanceChanged += HandleBalanceChanged;
            }

            if (_dropdown != null)
            {
                // Re-Init-safe: clear any prior listener before adding (mirror
                // StorageRolesTabRow pattern + rule #16 belt-and-braces).
                _dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
                _dropdown.onValueChanged.AddListener(OnDropdownChanged);
            }
        }

        private void BuildDropdown(IReadOnlyList<SafeRoleDescriptor> supportedRoles)
        {
            _indexToType.Clear();
            if (_dropdown == null) return;

            _dropdown.ClearOptions();
            if (supportedRoles == null || supportedRoles.Count == 0)
            {
                // Defensive — every CommercialBuilding's catalog should at least include
                // None. If it's empty, we still show a single "—" entry mapped to None
                // so the dropdown isn't visually broken.
                _dropdown.AddOptions(new List<string> { SafeRoleCatalog.None.DisplayName });
                _indexToType.Add(SafeRoleType.None);
                return;
            }

            var labels = new List<string>(supportedRoles.Count);
            for (int i = 0; i < supportedRoles.Count; i++)
            {
                var d = supportedRoles[i];
                labels.Add(d.DisplayName);
                _indexToType.Add(d.Type);
            }
            _dropdown.AddOptions(labels);
        }

        private void SyncDropdownToCurrentRole()
        {
            if (_dropdown == null || _safe == null) return;

            var current = _safe.Role;
            int idx = _indexToType.IndexOf(current);
            if (idx < 0)
            {
                // Role isn't in the supported catalog. Fall back to None visually; the
                // server-side filter in TrySetSafeRoleServerRpc rejects out-of-catalog
                // writes anyway.
                idx = _indexToType.IndexOf(SafeRoleType.None);
                if (idx < 0) idx = 0;
            }
            _dropdown.SetValueWithoutNotify(idx);
            _dropdown.RefreshShownValue();
        }

        private void RefreshLabel()
        {
            if (_label == null || _safe == null) return;
            _label.text = $"{ResolveSafeDisplayName(_safe)}    {FormatBalances(_safe)}";
        }

        /// <summary>
        /// Heuristic: prefer the GameObject's name when <see cref="Furniture.FurnitureName"/>
        /// is empty or carries a placeholder of the form &lt;name&gt;. Mirror of
        /// <see cref="StorageRolesTabRow"/>'s ResolveStorageDisplayName.
        /// </summary>
        private static string ResolveSafeDisplayName(SafeFurniture safe)
        {
            if (safe == null) return "<unknown>";
            var fn = safe.FurnitureName;
            bool placeholder = string.IsNullOrEmpty(fn)
                || (fn.Length >= 2 && fn[0] == '<' && fn[fn.Length - 1] == '>');
            return placeholder ? safe.gameObject.name : fn;
        }

        /// <summary>
        /// Render "{amount} {CurrencyName}, …" for every currency this safe holds.
        /// Empty balance shows the role's display name only ("Treasury: empty") so
        /// the owner can see whether the safe is contributing zero or carries funds.
        /// </summary>
        private static string FormatBalances(SafeFurniture safe)
        {
            var balances = safe.Balances;
            string rolePrefix = safe.Role == SafeRoleType.None ? "Unassigned" : safe.Role.ToString();
            if (balances == null || balances.Count == 0) return $"{rolePrefix}: empty";

            var sb = new StringBuilder(64);
            sb.Append(rolePrefix).Append(": ");
            for (int i = 0; i < balances.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var entry = balances[i];
                sb.Append(entry.Amount).Append(' ').Append(CurrencyDisplayName(entry.CurrencyId));
            }
            return sb.ToString();
        }

        private static string CurrencyDisplayName(int rawId)
        {
            // Default currency surfaces as "Coin" for owner-facing copy; other currencies
            // (future Kingdom-currency, etc.) render as "Currency#<id>" until a registry
            // surfaces a display name. Keeps the row readable today without blocking on
            // a CurrencyRegistry pass.
            if (rawId == 0) return "Coin";
            return $"Currency#{rawId}";
        }

        private void OnDropdownChanged(int index)
        {
            if (_building == null || _safe == null) return;
            if (index < 0 || index >= _indexToType.Count) return;

            var newRole = _indexToType[index];
            var net = _safe.GetComponent<NetworkObject>();
            if (net == null)
            {
                Debug.LogWarning($"[StorageRolesTabSafeRow] Safe '{_safe.FurnitureName}' has no NetworkObject — role write skipped.");
                return;
            }
            _building.TrySetSafeRoleServerRpc(new NetworkObjectReference(net), newRole);
        }

        private void HandleRoleChanged(SafeRoleType _)
        {
            SyncDropdownToCurrentRole();
            RefreshLabel(); // role prefix flips with the dropdown.
        }

        private void HandleBalanceChanged() => RefreshLabel();

        private void OnDestroy()
        {
            if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            if (_safe != null)
            {
                _safe.OnRoleChanged    -= HandleRoleChanged;
                _safe.OnBalanceChanged -= HandleBalanceChanged;
            }
        }
    }
}
