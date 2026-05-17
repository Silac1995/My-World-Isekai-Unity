using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Storages tab view: lists every <see cref="StorageFurniture"/> descendant of the
    /// owning <see cref="CommercialBuilding"/>, one row per storage. Each row carries
    /// a TMP_Dropdown bound to <see cref="CommercialBuilding.SupportedStorageRoles"/>.
    /// On dropdown change the row fires <see cref="CommercialBuilding.TrySetStorageRoleServerRpc"/>
    /// — the server is the single source of truth, the view re-renders on the resulting
    /// <see cref="CommercialBuilding.OnStorageRolesChanged"/> event (or per-storage
    /// <see cref="StorageFurniture.OnRoleChanged"/> for changes that bypass the building's
    /// ServerRpc, e.g. save-restore / migration writes).
    ///
    /// Empty state: when the building has zero storage children, the rows section is
    /// hidden and <see cref="_emptyStateLabel"/> is shown with the placeholder copy.
    ///
    /// Rule #16: <see cref="Dispose"/> unsubscribes the building event, every row's
    /// per-storage subscription (handled inside the row's own OnDestroy), and destroys
    /// the spawned row GameObjects.
    /// </summary>
    public sealed class StorageRolesTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Storages — wiring")]
        [Tooltip("Parent transform under which storage rows are instantiated.")]
        [SerializeField] private Transform _rowsParent;

        [Tooltip("Prefab carrying StorageRolesTabRow at its root.")]
        [SerializeField] private GameObject _rowPrefab;

        [Header("Storages — empty state")]
        [Tooltip("Shown when the building has zero StorageFurniture children. Optional — if null, empty state is silently skipped.")]
        [SerializeField] private TextMeshProUGUI _emptyStateLabel;

        [Tooltip("Text displayed in the empty state. Designer-overridable.")]
        [TextArea(2, 4)]
        [SerializeField] private string _emptyStateText = "Place a storage furniture inside the building to assign roles.";

        [Header("Safes — wiring (Phase 1.7)")]
        [Tooltip("Parent transform under which safe rows are instantiated. Optional — leave null on buildings that should not surface a Safes section (e.g. a pure-residential tab variant).")]
        [SerializeField] private Transform _safesRowsParent;

        [Tooltip("Prefab carrying StorageRolesTabSafeRow at its root.")]
        [SerializeField] private GameObject _safeRowPrefab;

        [Header("Safes — empty state (Phase 1.7)")]
        [Tooltip("Shown when the building has zero SafeFurniture children. Optional.")]
        [SerializeField] private TextMeshProUGUI _safesEmptyStateLabel;

        [Tooltip("Text displayed in the safes empty state. Designer-overridable.")]
        [TextArea(2, 4)]
        [SerializeField] private string _safesEmptyStateText = "Place a Safe Base furniture inside to track treasury.";

        private CommercialBuilding _building;
        private readonly List<StorageRolesTabRow> _rows = new();
        private readonly List<StorageRolesTabSafeRow> _safeRows = new();

        public GameObject Root => gameObject;

        /// <summary>Called by <see cref="StorageRolesTab.CreateView"/> right after Instantiate.</summary>
        public void Bind(CommercialBuilding building)
        {
            _building = building;
            if (_building != null)
            {
                _building.OnStorageRolesChanged += Refresh;
                // Safes section refreshes on every treasury change so role flips,
                // deposits, withdraws, B2B debits, and refunds redraw the rows. The
                // building-level event fans out from per-safe OnRoleChanged +
                // OnBalanceChanged subscriptions inside CommercialBuilding —
                // late-joiner safe (per-safe replication catches up on connect).
                _building.OnTreasuryChanged += RefreshSafes;
            }
            Refresh();
        }

        public void OnTabActivated()   { /* no-op — view stays bound while tab is alive */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_building != null)
            {
                _building.OnStorageRolesChanged -= Refresh;
                _building.OnTreasuryChanged     -= RefreshSafes;
            }
            _building = null;
            ClearRows();
            ClearSafeRows();
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i] != null && _rows[i].gameObject != null) Destroy(_rows[i].gameObject);
            _rows.Clear();
        }

        private void ClearSafeRows()
        {
            for (int i = 0; i < _safeRows.Count; i++)
                if (_safeRows[i] != null && _safeRows[i].gameObject != null) Destroy(_safeRows[i].gameObject);
            _safeRows.Clear();
        }

        private void Refresh()
        {
            ClearRows();
            RefreshSafes(); // keep both sections in lockstep on full refresh.

            if (_building == null) { ShowEmptyState(true); return; }

            var allStorages = _building.GetComponentsInChildren<StorageFurniture>(includeInactive: true);
            int count = allStorages != null ? allStorages.Length : 0;

            // Toggle empty state visibility.
            ShowEmptyState(count == 0);
            if (_rowsParent != null) _rowsParent.gameObject.SetActive(count > 0);

            if (count == 0 || _rowsParent == null || _rowPrefab == null) return;

            var supported = _building.SupportedStorageRoles;
            for (int i = 0; i < allStorages.Length; i++)
            {
                var storage = allStorages[i];
                if (storage == null) continue;

                int used = 0;
                int capacity = storage.Capacity;
                for (int sl = 0; sl < capacity; sl++)
                {
                    var slot = storage.GetItemSlot(sl);
                    if (slot != null && !slot.IsEmpty()) used++;
                }

                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<StorageRolesTabRow>();
                if (row != null)
                {
                    row.Bind(_building, storage, supported, used, capacity);
                    _rows.Add(row);
                }
            }
        }

        /// <summary>
        /// Rebuilds the safes section only — invoked on full Refresh and on the
        /// building's <see cref="CommercialBuilding.OnTreasuryChanged"/> event (role
        /// flips + every balance mutation). Storage rows are untouched.
        /// </summary>
        private void RefreshSafes()
        {
            ClearSafeRows();
            if (_building == null)
            {
                ShowSafesEmptyState(true);
                return;
            }

            var safes = _building.Safes;
            int count = safes != null ? safes.Count : 0;

            ShowSafesEmptyState(count == 0);
            if (_safesRowsParent != null) _safesRowsParent.gameObject.SetActive(count > 0);

            if (count == 0 || _safesRowsParent == null || _safeRowPrefab == null) return;

            var supported = _building.SupportedSafeRoles;
            for (int i = 0; i < safes.Count; i++)
            {
                var safe = safes[i];
                if (safe == null) continue;

                var rowGo = Instantiate(_safeRowPrefab, _safesRowsParent);
                var row = rowGo.GetComponent<StorageRolesTabSafeRow>();
                if (row != null)
                {
                    row.Bind(_building, safe, supported);
                    _safeRows.Add(row);
                }
            }
        }

        private void ShowEmptyState(bool show)
        {
            if (_emptyStateLabel == null) return;
            _emptyStateLabel.gameObject.SetActive(show);
            if (show && !string.IsNullOrEmpty(_emptyStateText)) _emptyStateLabel.text = _emptyStateText;
        }

        private void ShowSafesEmptyState(bool show)
        {
            if (_safesEmptyStateLabel == null) return;
            _safesEmptyStateLabel.gameObject.SetActive(show);
            if (show && !string.IsNullOrEmpty(_safesEmptyStateText)) _safesEmptyStateLabel.text = _safesEmptyStateText;
        }
    }
}
