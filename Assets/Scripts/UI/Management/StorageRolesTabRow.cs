using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// One row in the unified Storages tab. Renders the parent
    /// <see cref="StorageFurniture"/>'s name, slot usage, and a TMP_Dropdown listing the
    /// owner-allowed roles for the building (<see cref="CommercialBuilding.SupportedStorageRoles"/>).
    /// Selecting a role fires <see cref="CommercialBuilding.TrySetStorageRoleServerRpc"/>;
    /// the server-side <see cref="StorageFurniture.OnRoleChanged"/> event refreshes the
    /// dropdown's selection on round-trip so optimistic local state stays in sync with
    /// authoritative state (and so rejected calls revert).
    ///
    /// Split into its own .cs file (one MonoBehaviour per file) so Unity's prefab
    /// serializer reliably resolves the script GUID — same fix history as
    /// <see cref="ShopCatalogTabRow"/> / <see cref="ShopShelvesTabRow"/>.
    /// </summary>
    public sealed class StorageRolesTabRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;       // "Crate (north)    8/16 slots used"
        [SerializeField] private TMP_Dropdown _dropdown; // populated from SupportedStorageRoles

        private CommercialBuilding _building;
        private StorageFurniture _storage;

        // Index → enum mapping. The TMP_Dropdown only exposes int indices, so we cache
        // the type-by-index list for the OnValueChanged → ServerRpc translation. Cleared
        // on Bind to support row recycling (currently rows are destroyed/recreated, but
        // the cheap reset keeps recycling viable).
        private readonly List<StorageRoleType> _indexToType = new();

        public void Bind(
            CommercialBuilding building,
            StorageFurniture storage,
            IReadOnlyList<StorageRoleDescriptor> supportedRoles,
            int slotsUsed,
            int slotsCapacity)
        {
            _building = building;
            _storage = storage;

            if (_label != null && storage != null)
            {
                _label.text = $"{ResolveStorageDisplayName(storage)}    {slotsUsed}/{slotsCapacity} slots used";
            }

            BuildDropdown(supportedRoles);
            SyncDropdownToCurrentRole();

            // Subscribe to the per-storage role event so the dropdown reflects role
            // writes that bypass the building's ServerRpc (save-restore, ShopBuilding
            // sell-shelf migration, future programmatic writes).
            if (_storage != null) _storage.OnRoleChanged += HandleRoleChanged;

            if (_dropdown != null) _dropdown.onValueChanged.AddListener(OnDropdownChanged);
        }

        private void BuildDropdown(IReadOnlyList<StorageRoleDescriptor> supportedRoles)
        {
            _indexToType.Clear();
            if (_dropdown == null) return;

            _dropdown.ClearOptions();
            if (supportedRoles == null || supportedRoles.Count == 0)
            {
                // Defensive — every CommercialBuilding's catalog should at least include
                // None. If it's empty, we still show a single "—" entry mapped to None
                // so the dropdown isn't visually broken.
                _dropdown.AddOptions(new List<string> { StorageRoleCatalog.None.DisplayName });
                _indexToType.Add(StorageRoleType.None);
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
            if (_dropdown == null || _storage == null) return;

            var current = _storage.Role;
            int idx = _indexToType.IndexOf(current);
            if (idx < 0)
            {
                // Role isn't in the supported catalog (e.g. a SellShelf carry-over on a
                // Forge after refactor). Fall back to None visually; server-side filter
                // in TrySetStorageRoleServerRpc rejects out-of-catalog writes anyway.
                idx = _indexToType.IndexOf(StorageRoleType.None);
                if (idx < 0) idx = 0;
            }
            _dropdown.SetValueWithoutNotify(idx);
            _dropdown.RefreshShownValue();
        }

        /// <summary>
        /// Heuristic: prefer the GameObject's name (designer-friendly, e.g. "Crate (north)",
        /// "Shelf_A") when <see cref="Furniture.FurnitureName"/> is empty or contains a
        /// placeholder of the form &lt;name&gt; (the SerializeField default convention used on
        /// some Inspector-authored furniture). Falls back to the FurnitureName for storages
        /// that authored a real value.
        /// </summary>
        private static string ResolveStorageDisplayName(StorageFurniture storage)
        {
            if (storage == null) return "<unknown>";
            var fn = storage.FurnitureName;
            bool placeholder = string.IsNullOrEmpty(fn)
                || (fn.Length >= 2 && fn[0] == '<' && fn[fn.Length - 1] == '>');
            return placeholder ? storage.gameObject.name : fn;
        }

        private void OnDropdownChanged(int index)
        {
            if (_building == null || _storage == null) return;
            if (index < 0 || index >= _indexToType.Count) return;

            var newRole = _indexToType[index];
            var net = _storage.GetComponent<NetworkObject>();
            if (net == null)
            {
                Debug.LogWarning($"[StorageRolesTabRow] Storage '{_storage.FurnitureName}' has no NetworkObject — role write skipped.");
                return;
            }
            _building.TrySetStorageRoleServerRpc(new NetworkObjectReference(net), newRole);
        }

        private void HandleRoleChanged(StorageRoleType _) => SyncDropdownToCurrentRole();

        private void OnDestroy()
        {
            if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            if (_storage != null) _storage.OnRoleChanged -= HandleRoleChanged;
        }
    }
}
