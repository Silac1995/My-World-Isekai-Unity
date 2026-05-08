using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Owner's sell-shelf editor. Lists every <see cref="StorageFurniture"/>
    /// descendant of the parent ShopBuilding with a checkbox toggling sell-shelf
    /// status. Toggling routes through <see cref="ShopBuilding.SetSellShelfFlagServerRpc"/>.
    /// Owner-gating handled server-side; tab does not duplicate the gate.
    /// </summary>
    public sealed class ShopShelvesTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/ShopShelvesTab";

        private readonly ShopBuilding _building;

        public ShopShelvesTab(ShopBuilding building) { _building = building; }

        public string Name => "Shelves";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<ShopShelvesTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[ShopShelvesTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
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
