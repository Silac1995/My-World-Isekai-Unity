using UnityEngine;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// Multi-tab management window opened by <see cref="CityManagementFurniture"/>.
    /// UI_WindowBase variant — per rule #39 the prefab lives at
    /// <c>Assets/UI/Player HUD/UI_CityManagementPanel.prefab</c> (variant of
    /// <c>UI_WindowBase.prefab</c>).
    ///
    /// Three tabs (each a <see cref="MonoBehaviour"/> child wired via SerializeField):
    /// TierUp, PlaceBuilding, JoinRequests. Each tab Initializes against the bound
    /// <see cref="AdministrativeBuilding"/>.
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_CityManagementPanel : UI_WindowBase
    {
        [Header("Tabs")]
        [SerializeField] private UI_TierUpTab _tierUpTab;
        [SerializeField] private UI_PlaceBuildingTab _placeBuildingTab;
        [SerializeField] private UI_JoinRequestsTab _joinRequestsTab;

        [Header("Tab buttons")]
        [SerializeField] private UnityEngine.UI.Button _tabButtonTierUp;
        [SerializeField] private UnityEngine.UI.Button _tabButtonPlaceBuilding;
        [SerializeField] private UnityEngine.UI.Button _tabButtonJoinRequests;

        private AdministrativeBuilding _ab;

        public bool IsOpen => gameObject != null && gameObject.activeSelf;

        protected override void Awake()
        {
            base.Awake();

            if (_tabButtonTierUp != null)
            {
                _tabButtonTierUp.onClick.RemoveAllListeners();
                _tabButtonTierUp.onClick.AddListener(ShowTierUpTab);
            }
            if (_tabButtonPlaceBuilding != null)
            {
                _tabButtonPlaceBuilding.onClick.RemoveAllListeners();
                _tabButtonPlaceBuilding.onClick.AddListener(ShowPlaceBuildingTab);
            }
            if (_tabButtonJoinRequests != null)
            {
                _tabButtonJoinRequests.onClick.RemoveAllListeners();
                _tabButtonJoinRequests.onClick.AddListener(ShowJoinRequestsTab);
            }
        }

        /// <summary>
        /// Open the window against an AB. Idempotent — re-binding refreshes the tabs.
        /// </summary>
        public void Initialize(AdministrativeBuilding ab)
        {
            _ab = ab;
            if (_tierUpTab != null) _tierUpTab.Initialize(ab);
            if (_placeBuildingTab != null) _placeBuildingTab.Initialize(ab);
            if (_joinRequestsTab != null) _joinRequestsTab.Initialize(ab);

            // Default to TierUp on open — matches the spec §3 tier-up flow which is the
            // leader's primary "check progress" surface. Subsequent tab clicks remember
            // selection until window close (no state persistence beyond session).
            ShowTierUpTab();
        }

        public void ShowTierUpTab()
        {
            SetTabActive(_tierUpTab, true);
            SetTabActive(_placeBuildingTab, false);
            SetTabActive(_joinRequestsTab, false);
            _tierUpTab?.RefreshFromAB();
        }

        public void ShowPlaceBuildingTab()
        {
            SetTabActive(_tierUpTab, false);
            SetTabActive(_placeBuildingTab, true);
            SetTabActive(_joinRequestsTab, false);
            _placeBuildingTab?.RefreshFromAB();
        }

        public void ShowJoinRequestsTab()
        {
            SetTabActive(_tierUpTab, false);
            SetTabActive(_placeBuildingTab, false);
            SetTabActive(_joinRequestsTab, true);
            _joinRequestsTab?.RefreshFromAB();
        }

        private static void SetTabActive(MonoBehaviour tab, bool active)
        {
            if (tab == null) return;
            if (tab.gameObject.activeSelf != active) tab.gameObject.SetActive(active);
        }
    }
}
