using TMPro;
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// One read-only row in the Hiring tab's roster section. Pure display — shows the
    /// job title (e.g. "Vendor", "Blacksmith") and the worker's character name. No fire
    /// button yet; firing is a follow-up enhancement that requires a server-authoritative
    /// <c>TryFireWorkerServerRpc</c> on <see cref="CommercialBuilding"/>.
    ///
    /// Owned by <see cref="HiringTabView"/>; instantiated via the view's
    /// <c>_rosterRowPrefab</c> + parented under <c>_rosterParent</c>. Destroyed by the
    /// view in <see cref="HiringTabView.Dispose"/> + <c>RefreshRoster</c>.
    /// </summary>
    public sealed class HiringRosterRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _jobTitleLabel;
        [SerializeField] private TMP_Text _workerNameLabel;

        public void Bind(string jobTitle, string workerName)
        {
            if (_jobTitleLabel != null) _jobTitleLabel.text = jobTitle ?? "<unknown>";
            if (_workerNameLabel != null) _workerNameLabel.text = workerName ?? "<unknown>";
        }
    }
}
