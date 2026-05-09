using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Generic per-building storage-role editor (replaces ShopShelvesTab as of 2026-05-08).
    /// Lists every <see cref="StorageFurniture"/> child of the parent <see cref="CommercialBuilding"/>
    /// and lets the owner pick one role per storage from the building's
    /// <see cref="CommercialBuilding.SupportedStorageRoles"/>. Owner-gating is server-side
    /// inside <see cref="CommercialBuilding.TrySetStorageRoleServerRpc"/> — this tab does
    /// not duplicate the gate.
    ///
    /// Subclasses extend the role catalog by overriding <c>SupportedStorageRoles</c>
    /// (e.g. <see cref="ShopBuilding"/> appends <c>SellShelf</c>); the dropdown reads
    /// the building's list and renders accordingly. No subclass-specific tab needed.
    ///
    /// See sketch <c>.planning/sketches/001-storages-tab/</c> Variant B (winner) for the
    /// dropdown-per-row design and <c>wiki/projects/management-panel-followups.md §1</c>
    /// for the per-storage-exclusivity decision.
    /// </summary>
    public sealed class StorageRolesTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/StorageRolesTab";

        private readonly CommercialBuilding _building;

        public StorageRolesTab(CommercialBuilding building) { _building = building; }

        public string Name => "Storages";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<StorageRolesTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[StorageRolesTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
                    return null;
                }
                var view = Object.Instantiate(prefab);
                view.Bind(_building);
                return view;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
