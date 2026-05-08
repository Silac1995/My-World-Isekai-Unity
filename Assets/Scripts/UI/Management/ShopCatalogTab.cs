using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Owner's shop-catalog editor. Lists every (ItemSO, MaxStock, PriceOverride)
    /// catalog entry on the parent ShopBuilding. Add / Edit / Remove route through
    /// ShopBuilding.AddCatalogEntryServerRpc / EditCatalogEntryServerRpc /
    /// RemoveCatalogEntryServerRpc. Owner-gating handled server-side; tab does not
    /// duplicate the gate.
    /// </summary>
    public sealed class ShopCatalogTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/ShopCatalogTab";

        private readonly ShopBuilding _building;

        public ShopCatalogTab(ShopBuilding building) { _building = building; }

        public string Name => "Catalog";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<ShopCatalogTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[ShopCatalogTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
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
