using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// JoinRequestsTab — lists the AB's PendingJoinRequests with Accept/Decline buttons.
    /// Subscribes to <see cref="NetworkList{T}.OnListChanged"/> for live refresh.
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_JoinRequestsTab : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_JoinRequestRow _rowPrefab;
        [SerializeField] private TMP_Text _emptyStateLabel;

        private AdministrativeBuilding _ab;
        private readonly List<UI_JoinRequestRow> _rows = new List<UI_JoinRequestRow>();
        private bool _subscribed;

        public void Initialize(AdministrativeBuilding ab)
        {
            Unsubscribe();
            _ab = ab;
            Subscribe();
        }

        public void RefreshFromAB()
        {
            ClearRows();
            if (_ab == null || _ab.PendingJoinRequests == null) return;

            int count = _ab.PendingJoinRequests.Count;
            if (count == 0)
            {
                if (_emptyStateLabel != null)
                {
                    _emptyStateLabel.text = "No pending applicants.";
                    _emptyStateLabel.gameObject.SetActive(true);
                }
                return;
            }

            if (_emptyStateLabel != null) _emptyStateLabel.gameObject.SetActive(false);
            if (_rowPrefab == null || _rowContainer == null) return;

            for (int i = 0; i < count; i++)
            {
                var req = _ab.PendingJoinRequests[i];
                string displayName = ResolveApplicantName(req.ApplicantNetId);
                var row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(req, displayName, OnAcceptClicked, OnDeclineClicked);
                _rows.Add(row);
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _ab == null || _ab.PendingJoinRequests == null) return;
            _ab.PendingJoinRequests.OnListChanged += HandleListChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _ab == null || _ab.PendingJoinRequests == null) return;
            _ab.PendingJoinRequests.OnListChanged -= HandleListChanged;
            _subscribed = false;
        }

        private void HandleListChanged(NetworkListEvent<JoinRequest> _)
        {
            if (isActiveAndEnabled) RefreshFromAB();
        }

        private void OnAcceptClicked(ulong applicantNetId)
        {
            if (_ab == null) return;
            _ab.AcceptJoinRequestServerRpc(applicantNetId);
        }

        private void OnDeclineClicked(ulong applicantNetId)
        {
            if (_ab == null) return;
            _ab.DeclineJoinRequestServerRpc(applicantNetId);
        }

        private static string ResolveApplicantName(ulong netId)
        {
            if (NetworkManager.Singleton == null) return null;
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var no)) return null;
            var ch = no != null ? no.GetComponent<Character>() : null;
            return ch != null ? ch.CharacterName : null;
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i].gameObject);
            }
            _rows.Clear();
        }

        private void OnDisable()
        {
            ClearRows();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
