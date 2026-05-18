using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// Row leaf inside <see cref="UI_JoinRequestsTab"/>'s scroll list. Displays applicant
    /// name + requested-day + Accept/Decline buttons. Initialize-callback decoupling per
    /// rule #39.
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_JoinRequestRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _applicantNameLabel;
        [SerializeField] private TMP_Text _requestedAtLabel;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _declineButton;

        private ulong _applicantNetId;
        private Action<ulong> _onAccept;
        private Action<ulong> _onDecline;

        public void Initialize(JoinRequest req, string applicantDisplayName,
                                Action<ulong> onAccept, Action<ulong> onDecline)
        {
            _applicantNetId = req.ApplicantNetId;
            _onAccept = onAccept;
            _onDecline = onDecline;

            if (_applicantNameLabel != null)
                _applicantNameLabel.text = string.IsNullOrEmpty(applicantDisplayName)
                    ? $"Applicant #{req.ApplicantNetId}"
                    : applicantDisplayName;

            if (_requestedAtLabel != null)
                _requestedAtLabel.text = $"Day {req.RequestedAtDay}";

            if (_acceptButton != null)
            {
                _acceptButton.onClick.RemoveAllListeners();
                _acceptButton.onClick.AddListener(OnAcceptClicked);
            }
            if (_declineButton != null)
            {
                _declineButton.onClick.RemoveAllListeners();
                _declineButton.onClick.AddListener(OnDeclineClicked);
            }
        }

        private void OnAcceptClicked()  => _onAccept?.Invoke(_applicantNetId);
        private void OnDeclineClicked() => _onDecline?.Invoke(_applicantNetId);

        private void OnDestroy()
        {
            if (_acceptButton != null) _acceptButton.onClick.RemoveAllListeners();
            if (_declineButton != null) _declineButton.onClick.RemoveAllListeners();
        }
    }
}
