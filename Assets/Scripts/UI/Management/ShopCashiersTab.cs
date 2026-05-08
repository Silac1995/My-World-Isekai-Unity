using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Owner's cashiers tab. Lists every <see cref="Cashier"/> registered with
    /// the parent ShopBuilding with a per-cashier till + Withdraw button.
    /// Withdraw routes through <see cref="ShopBuilding.WithdrawCashierTillServerRpc"/>.
    /// Owner-gating handled server-side; tab does not duplicate the gate.
    /// </summary>
    public sealed class ShopCashiersTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/ShopCashiersTab";

        private readonly ShopBuilding _building;

        public ShopCashiersTab(ShopBuilding building) { _building = building; }

        public string Name => "Cashiers";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<ShopCashiersTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[ShopCashiersTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
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
