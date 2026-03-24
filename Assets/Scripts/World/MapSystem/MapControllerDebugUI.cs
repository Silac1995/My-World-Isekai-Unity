using UnityEngine;
using Unity.Netcode;
using TMPro;

namespace MWI.WorldSystem
{
    public class MapControllerDebugUI : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Link the MapController you want to debug here.")]
        [SerializeField] private MapController _controller;

        [Header("UI Fields to Link")]
        [SerializeField] private TMP_Text _txtMapId;
        [SerializeField] private TMP_Text _txtNetworkState;
        [SerializeField] private TMP_Text _txtIsActive;
        [SerializeField] private TMP_Text _txtIsInteriorOffset;
        [SerializeField] private TMP_Text _txtIsPredefinedMap;
        [SerializeField] private TMP_Text _txtBiome;
        [SerializeField] private TMP_Text _txtJobYields;
        
        [Header("Simulation State")]
        [SerializeField] private TMP_Text _txtIsHibernating;
        [SerializeField] private TMP_Text _txtConnectedPlayersCount;
        [SerializeField] private TMP_Text _txtActivePlayerCount;
        [SerializeField] private TMP_Text _txtPlayerIds;

        [Header("Offline Data")]
        [SerializeField] private TMP_Text _txtLastHibernationTime;
        [SerializeField] private TMP_Text _txtSavedNPCsCount;

        [Header("Analytics")]
        [SerializeField] private TMP_Text _txtVirtualResources;
        [SerializeField] private TMP_Text _txtCharactersParented;
        [SerializeField] private TMP_Text _txtNetworkObjectsParented;

        [Header("Settings")]
        [Tooltip("How often the UI updates in real-time seconds (to save performance on heavy maps).")]
        [SerializeField] private float _refreshRate = 0.5f;
        private float _nextRefreshTime;

        private void Update()
        {
            if (_controller == null) return;
            
            // Throttle UI updates so checking hierarchy analytics doesn't frame-drop the editor
            if (UnityEngine.Time.unscaledTime < _nextRefreshTime) return;
            _nextRefreshTime = UnityEngine.Time.unscaledTime + _refreshRate;
            
            RefreshUI();
        }

        private void RefreshUI()
        {
            // --- Core Info ---
            if (_txtMapId != null) _txtMapId.text = $"<b>Map ID:</b> {_controller.MapId}";

            if (_txtNetworkState != null)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    _txtNetworkState.text = $"<b>Netcode:</b> {(NetworkManager.Singleton.IsServer ? "<color=#00FF00>Server</color>" : "<color=#00BFFF>Client</color>")}";
                else
                    _txtNetworkState.text = "<b>Netcode:</b> <color=gray>Offline</color>";
            }

            if (_txtIsActive != null) _txtIsActive.text = $"<b>Is Active:</b> {_controller.IsActive.Value}";

            // --- Map Configuration ---
            if (_txtIsInteriorOffset != null) _txtIsInteriorOffset.text = $"<b>Interior Offset:</b> {_controller.IsInteriorOffset}";
            if (_txtIsPredefinedMap != null) _txtIsPredefinedMap.text = $"<b>Predefined Map:</b> {_controller.IsPredefinedMap}";
            if (_txtBiome != null) _txtBiome.text = $"<b>Biome:</b> {(_controller.Biome != null ? _controller.Biome.name : "<color=gray>None</color>")}";
            if (_txtJobYields != null) _txtJobYields.text = $"<b>Job Yields:</b> {(_controller.JobYields != null ? _controller.JobYields.name : "<color=gray>None</color>")}";

            // --- Simulation & Player State ---
            if (_txtIsHibernating != null) _txtIsHibernating.text = $"<b>Hibernating:</b> {(_controller.IsHibernating ? "<color=orange>True</color>" : "<color=green>False</color>")}";
            if (_txtConnectedPlayersCount != null) _txtConnectedPlayersCount.text = $"<b>Total Connected:</b> {_controller.ConnectedPlayersCount}";
            
            if (_txtActivePlayerCount != null) 
            {
                _txtActivePlayerCount.text = $"<b>Active Here:</b> {_controller.ActivePlayers.Count}";
            }

            if (_txtPlayerIds != null)
            {
                _txtPlayerIds.text = $"<b>Player IDs:</b> {(_controller.ActivePlayers.Count > 0 ? string.Join(", ", _controller.ActivePlayers) : "<color=gray>None</color>")}";
            }

            // --- Hibernation Data (Offline Math) ---
            var hData = _controller.HibernationData;
            if (_txtLastHibernationTime != null)
            {
                _txtLastHibernationTime.text = $"<b>Last Hibernation:</b> {(hData != null ? hData.LastHibernationTime.ToString("F2") : "<color=gray>N/A</color>")}";
            }
            if (_txtSavedNPCsCount != null)
            {
                _txtSavedNPCsCount.text = $"<b>Saved NPCs:</b> {(hData != null ? (hData.HibernatedNPCs?.Count ?? 0).ToString() : "0")}";
            }

            // --- Children/Hierarchy Analytics ---
            int virtualResourceCount = 0;
            int npcsCount = 0;
            int networkObjectsCount = 0;

            foreach (Transform child in _controller.transform)
            {
                if (child.GetComponent<VirtualResourceSupplier>() != null) virtualResourceCount++;
                if (child.GetComponentInChildren<Character>() != null) npcsCount++;
                if (child.GetComponentInChildren<NetworkObject>() != null) networkObjectsCount++;
            }

            if (_txtVirtualResources != null) _txtVirtualResources.text = $"<b>Virtual Resources:</b> {virtualResourceCount}";
            if (_txtCharactersParented != null) _txtCharactersParented.text = $"<b>Characters:</b> {npcsCount}";
            if (_txtNetworkObjectsParented != null) _txtNetworkObjectsParented.text = $"<b>NetworkObjects:</b> {networkObjectsCount}";
        }
    }
}
