using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Built-in Hiring tab — every <see cref="CommercialBuilding"/> gets one.
    /// Stateless POCO; instantiates a <see cref="HiringTabView"/> on demand from
    /// <c>Resources/UI/Management/HiringTab</c>.
    /// </summary>
    public sealed class HiringTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/HiringTab";

        private readonly CommercialBuilding _building;

        public HiringTab(CommercialBuilding building) { _building = building; }

        public string Name => "Hiring";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<HiringTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[HiringTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
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
