using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Built-in Hiring tab — open/closed toggle for accepting job applications.
    /// Body is intentionally minimal per spec narrowing: just one toggle button + label.
    /// Sign-text editing + job-list display from the legacy <c>UI_OwnerHiringPanel</c> are
    /// dropped (sign editing migrates to a future sign-furniture rework).
    ///
    /// Bit-for-bit network behavior preserved: same <c>TryOpenHiring</c>/<c>TryCloseHiring</c>
    /// calls into <c>CommercialBuilding</c>, same <c>OnHiringStateChanged</c> subscription,
    /// no new ServerRpcs introduced.
    ///
    /// Rule #16: <see cref="Dispose"/> unsubscribes both the event + the button click.
    /// </summary>
    public sealed class HiringTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Toggle")]
        [SerializeField] private Button _toggleHiringButton;
        [SerializeField] private TextMeshProUGUI _toggleHiringLabel;

        private CommercialBuilding _building;

        public GameObject Root => gameObject;

        /// <summary>Called by <see cref="HiringTab.CreateView"/> right after Instantiate.</summary>
        public void Bind(CommercialBuilding building)
        {
            _building = building;
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.AddListener(OnToggle);
            if (_building != null) _building.OnHiringStateChanged += HandleHiringChanged;
            Refresh();
        }

        public void OnTabActivated()   { /* no-op — view is live the whole time it's bound */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.RemoveListener(OnToggle);
            if (_building != null) _building.OnHiringStateChanged -= HandleHiringChanged;
            _building = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void HandleHiringChanged(bool _) => Refresh();

        private void Refresh()
        {
            if (_building == null || _toggleHiringLabel == null) return;
            _toggleHiringLabel.text = _building.IsHiring ? "Close Hiring" : "Open Hiring";
        }

        private void OnToggle()
        {
            if (_building == null)
            {
                Debug.LogWarning("[HiringTabView] Toggle rejected — building reference is null.");
                return;
            }
            var localPlayer = ResolveLocalPlayerCharacter();
            if (localPlayer == null)
            {
                Debug.LogWarning("[HiringTabView] Toggle rejected — could not resolve local player Character.");
                return;
            }
            if (_building.IsHiring) _building.TryCloseHiring(localPlayer);
            else                    _building.TryOpenHiring(localPlayer);
            // OnHiringStateChanged fires after replication and triggers Refresh().
        }

        /// <summary>
        /// Same resolver pattern used elsewhere in the UI layer (rule #31 — wraps
        /// network-API access in try/catch). Returns null if NetworkManager isn't up,
        /// no LocalClient, or the player NetworkObject hasn't spawned.
        /// </summary>
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
