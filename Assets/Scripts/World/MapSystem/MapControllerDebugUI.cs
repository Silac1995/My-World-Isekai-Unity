using UnityEngine;
using TMPro;
using System.Text;
using MWI.WorldSystem;
using Unity.Netcode;
using System.Linq;

namespace MWI.World.UI
{
    /// <summary>
    /// Debug UI per-map to visualize its macro-simulation and player tracking state.
    /// Attach this script to a Canvas in the UI_DebugMapController prefab.
    /// </summary>
    public class MapControllerDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapController _mapController;

        [Header("UI Text Outputs")]
        [SerializeField] private TMP_Text txtMapName;
        [SerializeField] private TMP_Text txtGameState;
        [SerializeField] private TMP_Text txtOwnerId;
        [SerializeField] private TMP_Text txtIsServer;
        
        [Header("Player Tracking")]
        [SerializeField] private TMP_Text txtTotalPlayers;
        [SerializeField] private TMP_Text txtActivePlayerIds;

        [Header("Hibernation Data")]
        [SerializeField] private TMP_Text txtHasHibernationData;
        [SerializeField] private TMP_Text txtLastSavedTime;
        [SerializeField] private TMP_Text txtSubscribers;
        [SerializeField] private TMP_Text txtNpcsCount;
        [SerializeField] private TMP_Text txtItemsCount;

        [Header("Settings")]
        [Tooltip("How often the UI updates in seconds to save performance.")]
        [SerializeField] private float _refreshRate = 0.5f;

        private float _lastRefreshTime;
        private StringBuilder _sb = new StringBuilder();

        private void Start()
        {
            if (_mapController == null)
            {
                _mapController = GetComponentInParent<MapController>();
            }

            if (_mapController == null)
            {
                Debug.LogWarning("[MapControllerDebugUI] MapController reference is missing and not found in parents!");
                // we don't disable because we might assign it later in editor dynamically
            }
        }

        private void Update()
        {
            // Fully qualify UnityEngine.Time to avoid collision with MWI.Time namespace
            if (UnityEngine.Time.unscaledTime - _lastRefreshTime >= _refreshRate)
            {
                _lastRefreshTime = UnityEngine.Time.unscaledTime;
                RefreshUI();
            }
        }

        private void RefreshUI()
        {
            if (_mapController == null) return;

            // Header Info
            if (txtMapName != null) txtMapName.text = $"<b>Map:</b> {_mapController.MapId}";
            if (txtGameState != null) txtGameState.text = $"<b>State:</b> {(!_mapController.IsHibernating ? "<color=green>Active</color>" : "<color=yellow>Hibernating</color>")}";
            if (txtOwnerId != null) txtOwnerId.text = $"<b>OwnerId:</b> {(_mapController.NetworkObject != null ? _mapController.NetworkObject.OwnerClientId.ToString() : "N/A")}";
            if (txtIsServer != null) txtIsServer.text = $"<b>IsServer:</b> {(_mapController.IsServer ? "<color=green>True</color>" : "<color=red>False</color>")}";

            // Player Tracking
            int playerCount = _mapController.ActivePlayers != null ? _mapController.ActivePlayers.Count() : 0;
            if (txtTotalPlayers != null) txtTotalPlayers.text = $"<b>Players in Map:</b> {playerCount}";

            if (txtActivePlayerIds != null)
            {
                if (playerCount > 0)
                {
                    _sb.Clear();
                    foreach (var playerId in _mapController.ActivePlayers)
                    {
                        _sb.Append($"[{playerId}] ");
                    }
                    txtActivePlayerIds.text = $"<b>IDs:</b> <color=#00FFFF>{_sb}</color>";
                }
                else
                {
                    txtActivePlayerIds.text = "<b>IDs:</b> <color=grey>None</color>";
                }
            }

            // Hibernation Data
            var data = _mapController.HibernationData;
            bool hasData = data != null;
            if (txtHasHibernationData != null) txtHasHibernationData.text = $"<b>Has Hibernation Data:</b> {(hasData ? "<color=green>Yes</color>" : "<color=red>No</color>")}";

            if (hasData)
            {
                if (txtLastSavedTime != null) txtLastSavedTime.text = $"<b>Last Saved:</b> {data.LastHibernationTime:F2}";
                if (txtSubscribers != null) txtSubscribers.text = $"<b>Subscribers:</b> N/A";
                if (txtNpcsCount != null) txtNpcsCount.text = $"<b>Hibernated NPCs:</b> {(data.HibernatedNPCs != null ? data.HibernatedNPCs.Count.ToString() : "0")}";
                if (txtItemsCount != null) txtItemsCount.text = $"<b>Hibernated Items:</b> N/A";
            }
            else
            {
                if (txtLastSavedTime != null) txtLastSavedTime.text = "<b>Last Saved:</b> ---";
                if (txtSubscribers != null) txtSubscribers.text = "<b>Subscribers:</b> ---";
                if (txtNpcsCount != null) txtNpcsCount.text = "<b>Hibernated NPCs:</b> 0";
                if (txtItemsCount != null) txtItemsCount.text = "<b>Hibernated Items:</b> 0";
            }
        }
    }
}
