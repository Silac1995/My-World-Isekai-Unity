using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Built-in Hiring tab — open/closed toggle for accepting job applications, plus a
    /// read-only roster of currently-assigned jobs (added 2026-05-08 per owner-workflow
    /// gap surfaced during Phase 2b smoke test). Sign-text editing is still dropped —
    /// migrates to a future sign-furniture rework.
    ///
    /// Bit-for-bit network behavior preserved: same <c>TryOpenHiring</c>/<c>TryCloseHiring</c>
    /// calls into <c>CommercialBuilding</c>, same <c>OnHiringStateChanged</c> subscription,
    /// plus an <c>OnJobsChanged</c> subscription that drives roster repaint. No new
    /// ServerRpcs introduced — roster is read-only for now (firing comes later as a
    /// follow-up enhancement, gated behind a server-authoritative TryFireWorker RPC).
    ///
    /// Rule #16: <see cref="Dispose"/> unsubscribes both events + the button click +
    /// destroys spawned roster rows.
    /// </summary>
    public sealed class HiringTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Toggle")]
        [SerializeField] private Button _toggleHiringButton;
        [SerializeField] private TextMeshProUGUI _toggleHiringLabel;

        [Header("Roster (read-only)")]
        [Tooltip("Parent transform under which roster rows are instantiated. If null, roster section is silently skipped.")]
        [SerializeField] private Transform _rosterParent;
        [Tooltip("Prefab carrying HiringRosterRow. If null, roster section is silently skipped.")]
        [SerializeField] private GameObject _rosterRowPrefab;
        [Tooltip("Optional header label (e.g. 'Roster: 2 / 3'). Null is fine.")]
        [SerializeField] private TextMeshProUGUI _rosterHeaderLabel;

        [Header("Reputation (read-only — 2026-05-17f)")]
        [Tooltip("Optional label showing 'Reputation: N/100' with a colour-coded current value. Subscribes to CommercialBuilding.OnReputationChanged for live updates. Leave null to skip the section entirely (no Inspector wiring required for older HiringTab prefab variants).")]
        [SerializeField] private TextMeshProUGUI _reputationLabel;

        private CommercialBuilding _building;
        private readonly List<HiringRosterRow> _rosterRows = new();

        public GameObject Root => gameObject;

        /// <summary>Called by <see cref="HiringTab.CreateView"/> right after Instantiate.</summary>
        public void Bind(CommercialBuilding building)
        {
            _building = building;
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.AddListener(OnToggle);
            if (_building != null)
            {
                _building.OnHiringStateChanged += HandleHiringChanged;
                _building.OnJobsChanged += RefreshRoster;
                _building.OnReputationChanged += HandleReputationChanged;
            }
            Refresh();
            RefreshRoster();
            RefreshReputation();
        }

        public void OnTabActivated()   { /* no-op — view is live the whole time it's bound */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.RemoveListener(OnToggle);
            if (_building != null)
            {
                _building.OnHiringStateChanged -= HandleHiringChanged;
                _building.OnJobsChanged -= RefreshRoster;
                _building.OnReputationChanged -= HandleReputationChanged;
            }
            _building = null;
            ClearRosterRows();
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void HandleHiringChanged(bool _) => Refresh();
        private void HandleReputationChanged(int _, int __) => RefreshReputation();

        private void Refresh()
        {
            if (_building == null || _toggleHiringLabel == null) return;
            _toggleHiringLabel.text = _building.IsHiring ? "Close Hiring" : "Open Hiring";
        }

        /// <summary>
        /// Update the reputation label (if wired) with the current `Reputation/100`
        /// value, colour-coded against the B2B floor:
        ///   green  ≥ ReputationB2BMinimum (procurement-eligible)
        ///   amber  1..(B2B-1)             (visible to customers, invisible to B2B)
        ///   red    0                      (rock bottom)
        /// Silent no-op when the label SerializeField is unwired — older
        /// HiringTab prefab variants don't carry the label yet (designer adds
        /// it without code changes).
        /// </summary>
        private void RefreshReputation()
        {
            if (_reputationLabel == null || _building == null) return;
            int rep = _building.Reputation;
            string color = rep >= CommercialBuilding.ReputationB2BMinimum
                ? "#64FF64"
                : rep > 0 ? "#FFB060" : "#FF6464";
            string suffix = rep < CommercialBuilding.ReputationB2BMinimum
                ? "  <size=80%><color=#999999>(below B2B floor)</color></size>"
                : "";
            _reputationLabel.text = $"Reputation: <color={color}>{rep}/{CommercialBuilding.ReputationMax}</color>{suffix}";
        }

        private void ClearRosterRows()
        {
            for (int i = 0; i < _rosterRows.Count; i++)
                if (_rosterRows[i] != null && _rosterRows[i].gameObject != null) Destroy(_rosterRows[i].gameObject);
            _rosterRows.Clear();
        }

        /// <summary>
        /// Rebuilds the roster section from <see cref="CommercialBuilding.Jobs"/>. Lists
        /// every <see cref="Job"/> whose <see cref="Job.IsAssigned"/> is true. Header label
        /// (if wired) shows assigned/total counts. Read-only — no fire button yet (future
        /// enhancement). Idempotent + cheap (rosters are tiny — typically &lt;10 jobs).
        /// </summary>
        private void RefreshRoster()
        {
            ClearRosterRows();
            if (_building == null) return;

            int assigned = 0;
            int total = _building.Jobs?.Count ?? 0;
            if (_building.Jobs != null)
            {
                for (int i = 0; i < _building.Jobs.Count; i++)
                {
                    var job = _building.Jobs[i];
                    if (job == null || !job.IsAssigned) continue;
                    assigned++;
                    if (_rosterParent == null || _rosterRowPrefab == null) continue;
                    var rowGo = Instantiate(_rosterRowPrefab, _rosterParent);
                    var row = rowGo.GetComponent<HiringRosterRow>();
                    if (row != null)
                    {
                        row.Bind(job.JobTitle, job.Worker?.CharacterName ?? "<unknown>");
                        _rosterRows.Add(row);
                    }
                }
            }

            if (_rosterHeaderLabel != null)
                _rosterHeaderLabel.text = $"Roster: {assigned} / {total}";
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
