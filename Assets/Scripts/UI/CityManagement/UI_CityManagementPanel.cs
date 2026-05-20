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
    /// Auto-closes when:
    /// - ESC pressed (every-frame branch in Update).
    /// - The bound customer walks out of the CityManagementFurniture's InteractionZone
    ///   (1 Hz unscaled-time poll; mirrors UI_SafePanel / UI_StorageFurniturePanel
    ///   pattern per rule #39 + #36).
    /// - The AB is destroyed (null check at top of Update).
    ///
    /// Plan 4c Task 7. Auto-close + sizing fix 2026-05-18.
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
        private Character _customer;
        private InteractableObject _targetInteractable;
        private float _autoClosePollTimer;

        // Rule #34 — 1 Hz cadence for the out-of-zone poll matches the cheapest UI
        // polling cadence in the project (mirrors UI_SafePanel.AutoClosePollInterval).
        private const float AutoClosePollInterval = 1f;

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
        /// <paramref name="customer"/> is the leader who tapped E on the
        /// CityManagementFurniture — used by the 1 Hz out-of-zone poll. Pass null to
        /// open without auto-close behavior (e.g. dev / debug tool).
        /// </summary>
        public void Initialize(AdministrativeBuilding ab, Character customer = null)
        {
            _ab = ab;
            _customer = customer;
            _autoClosePollTimer = 0f;

            // Resolve the InteractableObject on the CityManagementFurniture child of the
            // AB so the out-of-zone poll has a target. Defensive: skip if the AB has no
            // CityManagementFurniture authored (debug spawn path).
            _targetInteractable = null;
            if (_ab != null)
            {
                var furniture = _ab.GetComponentInChildren<CityManagementFurniture>(true);
                if (furniture != null)
                    _targetInteractable = furniture.GetComponent<InteractableObject>();
            }

            if (_tierUpTab != null) _tierUpTab.Initialize(ab);
            if (_placeBuildingTab != null) _placeBuildingTab.Initialize(ab);
            if (_joinRequestsTab != null) _joinRequestsTab.Initialize(ab);

            // Default to TierUp on open — matches the spec §3 tier-up flow which is the
            // leader's primary "check progress" surface. Subsequent tab clicks remember
            // selection until window close (no state persistence beyond session).
            ShowTierUpTab();
        }

        private void Update()
        {
            // AB despawned mid-session (vandalism, hibernation, dev delete) — close.
            if (_ab == null) { CloseWindow(); return; }

            // ESC closes the panel (rule #33 carve-out: input that targets the UI itself
            // stays in the UI). Mirrors UI_SafePanel / UI_StorageFurniturePanel.
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseWindow(); return; }

            // Rule #26 — UI uses unscaled time so it remains responsive under any
            // GameSpeedController scale (including pause / 0×).
            _autoClosePollTimer += UnityEngine.Time.unscaledDeltaTime;
            if (_autoClosePollTimer >= AutoClosePollInterval)
            {
                _autoClosePollTimer = 0f;
                // Rule #36 — IsCharacterInInteractionZone is the canonical proximity API,
                // never raw Vector3.Distance against the interaction point. Skip when no
                // customer was bound (debug / dev-mode open path).
                if (_customer != null && _targetInteractable != null
                    && !_targetInteractable.IsCharacterInInteractionZone(_customer))
                {
                    CloseWindow();
                    return;
                }
            }
        }

        public override void CloseWindow()
        {
            _customer = null;
            _targetInteractable = null;
            _autoClosePollTimer = 0f;
            base.CloseWindow();
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
