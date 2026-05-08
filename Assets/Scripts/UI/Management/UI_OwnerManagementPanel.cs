using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Owner-only generic management panel for a <see cref="CommercialBuilding"/>.
    /// Replaces the legacy single-purpose <c>UI_OwnerHiringPanel</c>. Tabs are supplied
    /// by the building via <see cref="CommercialBuilding.GetManagementTabs"/>, so the
    /// panel itself never knows the concrete subtype.
    ///
    /// Lazy singleton-on-demand pattern: first <see cref="Show"/> call instantiates the
    /// prefab from <c>Resources/UI/UI_OwnerManagementPanel</c>; subsequent calls re-bind
    /// to the new building (warm-path = same building, cold-path = different building).
    ///
    /// Owner-gate is at the call sites (<c>ManagementFurniture.Use</c> + <c>CharacterJob.
    /// GetInteractionOptions</c>); the panel adds a defense-in-depth fail-silent re-check
    /// in <see cref="ShowInternal"/> for any future caller that forgets.
    ///
    /// Rule #16: cleanup happens in <see cref="OnDestroy"/> + <see cref="Close"/> —
    /// every spawned tab view is Disposed (which destroys its GameObject + unsubscribes).
    /// </summary>
    public class UI_OwnerManagementPanel : MonoBehaviour
    {
        public const string PrefabResourcePath = "UI/UI_OwnerManagementPanel";
        private static UI_OwnerManagementPanel _instance;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI _titleLabel;

        [Header("Tabs")]
        [Tooltip("Parent transform for the header pill buttons (one per tab).")]
        [SerializeField] private RectTransform _tabHeaderRoot;
        [Tooltip("Pill prefab — must contain a Button at the root and a TextMeshProUGUI in itself or a child for the label.")]
        [SerializeField] private GameObject _tabHeaderPillPrefab;
        [Tooltip("Parent transform for the active tab's view Root.")]
        [SerializeField] private RectTransform _tabBodyRoot;

        [Header("Close")]
        [SerializeField] private Button _closeButton;
        [Tooltip("Full-screen invisible button behind the content panel — outside-click closes the panel.")]
        [SerializeField] private Button _dismissOverlay;

        private CommercialBuilding _building;

        private struct Entry
        {
            public IManagementTabView View;
            public GameObject Pill;
            public Button PillButton;
        }
        private readonly List<Entry> _spawned = new List<Entry>(4);
        private int _activeIndex = -1;

        // ============================================================================
        // PUBLIC ENTRY
        // ============================================================================

        /// <summary>
        /// Open (or re-open) the panel for <paramref name="building"/>. Lazy-instantiates
        /// the singleton instance on first call. Safe to call repeatedly — same building
        /// hits the warm path; different building tears down + rebuilds.
        /// </summary>
        public static void Show(CommercialBuilding building)
        {
            if (building == null)
            {
                Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — building is null.");
                return;
            }
            if (_instance == null)
            {
                try
                {
                    var prefab = Resources.Load<UI_OwnerManagementPanel>(PrefabResourcePath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[UI_OwnerManagementPanel] Prefab missing at Resources/{PrefabResourcePath}.");
                        return;
                    }
                    if (PlayerUI.Instance == null || PlayerUI.Instance.HudCanvas == null)
                    {
                        Debug.LogWarning("[UI_OwnerManagementPanel] PlayerUI HUD canvas unavailable — cannot parent panel.");
                        return;
                    }
                    _instance = Instantiate(prefab, PlayerUI.Instance.HudCanvas.transform, false);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    return;
                }
            }
            _instance.ShowInternal(building);
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        private void Awake()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dismissOverlay != null) _dismissOverlay.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
            if (_dismissOverlay != null) _dismissOverlay.onClick.RemoveListener(Close);
            DisposeAllTabs();
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // Local UI dismissal only — exempt from rule #33 (PlayerController-owned input)
            // because it doesn't drive the player character.
            if (gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ============================================================================
        // SHOW / SWITCH / CLOSE
        // ============================================================================

        private void ShowInternal(CommercialBuilding building)
        {
            // Defense-in-depth owner gate — does not authoritatively reject (call sites do that),
            // just guards against future callers forgetting the gate. Fail-silent.
            var localPlayer = ResolveLocalPlayerCharacter();
            if (localPlayer == null || building.Owner != localPlayer)
            {
                Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — local character is not the owner.");
                return;
            }

            // Warm path: same building → just re-show.
            if (_building == building && _spawned.Count > 0)
            {
                gameObject.SetActive(true);
                if (_activeIndex >= 0) _spawned[_activeIndex].View.OnTabActivated();
                return;
            }

            // Cold path: different building (or first show) → tear down + rebuild.
            DisposeAllTabs();
            _building = building;

            if (_titleLabel != null) _titleLabel.text = building.BuildingName;

            var tabs = building.GetManagementTabs();
            if (tabs == null || tabs.Count == 0)
            {
                Debug.LogWarning($"[UI_OwnerManagementPanel] {building.BuildingName} returned no tabs.");
                return;
            }

            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                if (tab == null) continue;

                var view = tab.CreateView();
                if (view == null || view.Root == null)
                {
                    Debug.LogWarning($"[UI_OwnerManagementPanel] Tab '{tab.Name}' produced null view — skipping.");
                    continue;
                }
                view.Root.transform.SetParent(_tabBodyRoot, false);
                view.Root.SetActive(false);

                GameObject pill = null;
                Button pillButton = null;
                if (_tabHeaderPillPrefab != null && _tabHeaderRoot != null)
                {
                    pill = Instantiate(_tabHeaderPillPrefab, _tabHeaderRoot);
                    pillButton = pill.GetComponent<Button>();
                    var pillLabel = pill.GetComponentInChildren<TextMeshProUGUI>();
                    if (pillLabel != null) pillLabel.text = tab.Name;
                    int capturedIndex = _spawned.Count;
                    if (pillButton != null) pillButton.onClick.AddListener(() => SwitchTo(capturedIndex));
                }

                _spawned.Add(new Entry { View = view, Pill = pill, PillButton = pillButton });
            }

            if (_spawned.Count > 0) SwitchTo(0);
            gameObject.SetActive(true);
        }

        private void SwitchTo(int index)
        {
            if (index < 0 || index >= _spawned.Count) return;
            if (_activeIndex == index) return;

            if (_activeIndex >= 0 && _activeIndex < _spawned.Count)
            {
                var prev = _spawned[_activeIndex];
                prev.View.OnTabDeactivated();
                if (prev.View.Root != null) prev.View.Root.SetActive(false);
                SetPillSelected(prev.Pill, false);
            }

            _activeIndex = index;
            var next = _spawned[index];
            if (next.View.Root != null) next.View.Root.SetActive(true);
            next.View.OnTabActivated();
            SetPillSelected(next.Pill, true);
        }

        private void Close()
        {
            DisposeAllTabs();
            _building = null;
            gameObject.SetActive(false);
        }

        private void DisposeAllTabs()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                var e = _spawned[i];
                try { e.View?.Dispose(); }
                catch (System.Exception ex) { Debug.LogException(ex); }
                if (e.PillButton != null) e.PillButton.onClick.RemoveAllListeners();
                if (e.Pill != null) Destroy(e.Pill);
            }
            _spawned.Clear();
            _activeIndex = -1;
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        /// <summary>
        /// Best-effort visual cue: tints the pill's Image (or leaves it alone if no Image).
        /// Designer is free to override the prefab to do something fancier; this is the
        /// fallback when no custom selection logic is wired.
        /// </summary>
        private static void SetPillSelected(GameObject pill, bool selected)
        {
            if (pill == null) return;
            var image = pill.GetComponent<Image>();
            if (image != null)
            {
                var c = image.color;
                c.a = selected ? 1f : 0.6f;
                image.color = c;
            }
        }

        private static Character ResolveLocalPlayerCharacter()
        {
            try
            {
                if (NetworkManager.Singleton == null) return null;
                var localClient = NetworkManager.Singleton.LocalClient;
                if (localClient == null || localClient.PlayerObject == null) return null;
                return localClient.PlayerObject.GetComponent<Character>();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
