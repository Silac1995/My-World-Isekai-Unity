using Unity.Netcode;
using UnityEngine;

namespace MWI.Time
{
    /// <summary>
    /// Manages the global simulation speed (Time.timeScale) across the network.
    /// The Server dictates the game speed, and all clients synchronize to it.
    /// Also synchronizes the in-game time (day + time01) so late-joining clients
    /// receive the host's current time instead of stale save data.
    /// </summary>
    public class GameSpeedController : NetworkBehaviour
    {
        public static GameSpeedController Instance { get; private set; }

        /// <summary>Invoked locally when the simulation speed is changed.</summary>
        public event System.Action<float> OnSpeedChanged;

        [Tooltip("The synchronized time scale across all clients.")]
        private NetworkVariable<float> _serverTimeScale = new NetworkVariable<float>(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>Server-authoritative in-game day. Syncs to late-joiners automatically.</summary>
        private NetworkVariable<int> _serverDay = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>Server-authoritative normalized time (0-1). Syncs to late-joiners automatically.</summary>
        private NetworkVariable<float> _serverTime01 = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>Threshold in normalized time units before a client corrects its local clock.</summary>
        private const float TimeDriftThreshold = 0.002f; // ~2.88 in-game minutes

        /// <summary>Real-time interval (seconds) between periodic time pushes from server.</summary>
        private const float PeriodicPushInterval = 10f;

        private float _nextPushTime;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (Instance != this) return;

            // Subscribe to changes so we can apply them locally when the server updates the variable
            _serverTimeScale.OnValueChanged += OnTimeScaleChanged;

            // Apply the current server time scale immediately upon spawning
            ApplyTimeScale(_serverTimeScale.Value);

            if (IsServer)
            {
                // Server pushes its current time into the NetworkVariables
                PushTimeToNetwork();
                _nextPushTime = UnityEngine.Time.unscaledTime + PeriodicPushInterval;

                // Subscribe to TimeManager hour changes to keep NetworkVariables updated
                if (TimeManager.Instance != null)
                {
                    TimeManager.Instance.OnHourChanged += OnServerHourChanged;
                    TimeManager.Instance.OnNewDay += OnServerNewDay;
                }
                else
                {
                    Debug.LogError("[GameSpeedController] TimeManager.Instance is null on server spawn! " +
                                   "Time will not sync to clients until TimeManager initializes. " +
                                   "Check script execution order.");
                }
            }
            else
            {
                // Client: apply the server's authoritative time immediately (late-joiner sync)
                ApplyServerTimeToLocal();

                // Subscribe for ongoing drift correction
                _serverDay.OnValueChanged += OnServerDayChanged;
                _serverTime01.OnValueChanged += OnServerTime01Changed;
            }
        }

        private void Update()
        {
            // Server: periodic time push to keep clients in sync between hour changes
            if (IsServer && IsSpawned && UnityEngine.Time.unscaledTime >= _nextPushTime)
            {
                _nextPushTime = UnityEngine.Time.unscaledTime + PeriodicPushInterval;
                PushTimeToNetwork();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance != this) return;

            _serverTimeScale.OnValueChanged -= OnTimeScaleChanged;

            if (IsServer)
            {
                if (TimeManager.Instance != null)
                {
                    TimeManager.Instance.OnHourChanged -= OnServerHourChanged;
                    TimeManager.Instance.OnNewDay -= OnServerNewDay;
                }
            }
            else
            {
                _serverDay.OnValueChanged -= OnServerDayChanged;
                _serverTime01.OnValueChanged -= OnServerTime01Changed;
            }

            // Safety fallback: reset time scale to normal when disconnecting
            UnityEngine.Time.timeScale = 1f;
        }

        private void OnTimeScaleChanged(float previousValue, float newValue)
        {
            ApplyTimeScale(newValue);
        }

        private void ApplyTimeScale(float newScale)
        {
            // Unity's global timeScale wrapper. 
            // Setting this to 0 pauses the game. Setting it to 2 runs it at double speed.
            // Note: NGO uses RealTime/UnscaledTime for ticking, so this won't drop connections.
            UnityEngine.Time.timeScale = newScale;
            
            OnSpeedChanged?.Invoke(newScale);

            Debug.Log($"[GameSpeedController] Simulation speed changed to: {newScale}x");
        }

        /// <summary>
        /// Proposes a new game speed. Can be called by the Server or any Client.
        /// </summary>
        /// <param name="newSpeed">The desired speed multiplier (e.g., 0 for pause, 1 for normal, 2 for fast).</param>
        public void RequestSpeedChange(float newSpeed)
        {
            // Ensure no negative time scale
            newSpeed = Mathf.Max(0f, newSpeed);

            // If we are playing offline or the network isn't started yet, just apply it locally
            if (!IsSpawned)
            {
                ApplyTimeScale(newSpeed);
                return;
            }

            if (IsServer)
            {
                _serverTimeScale.Value = newSpeed;
            }
            else
            {
                RequestSpeedChangeRpc(newSpeed);
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestSpeedChangeRpc(float newSpeed)
        {
            // Only the server can evaluate and change the networked variable
            _serverTimeScale.Value = Mathf.Max(0f, newSpeed);
        }

        #region Time Synchronization

        /// <summary>
        /// Pushes the current TimeManager state into the NetworkVariables.
        /// Called by the server on spawn and periodically (every in-game hour + day rollover).
        /// </summary>
        private void PushTimeToNetwork()
        {
            if (!IsServer || TimeManager.Instance == null) return;

            _serverDay.Value = TimeManager.Instance.CurrentDay;
            _serverTime01.Value = TimeManager.Instance.CurrentTime01;

            Debug.Log($"[GameSpeedController] Pushed time to network: Day {_serverDay.Value}, " +
                      $"{TimeManager.Instance.CurrentHour:00}:{TimeManager.Instance.CurrentMinute:00}");
        }

        private void OnServerHourChanged(int newHour)
        {
            PushTimeToNetwork();
        }

        private void OnServerNewDay()
        {
            PushTimeToNetwork();
        }

        private void OnServerDayChanged(int previousValue, int newValue)
        {
            ApplyServerTimeToLocal();
        }

        private void OnServerTime01Changed(float previousValue, float newValue)
        {
            ApplyServerTimeToLocal();
        }

        /// <summary>
        /// Applies the server's authoritative time to the local TimeManager.
        /// Used on client spawn (late-joiner) and for periodic drift correction.
        /// </summary>
        private void ApplyServerTimeToLocal()
        {
            if (TimeManager.Instance == null) return;

            int localDay = TimeManager.Instance.CurrentDay;
            float localTime = TimeManager.Instance.CurrentTime01;
            int serverDay = _serverDay.Value;
            float serverTime = _serverTime01.Value;

            // Only correct if there's meaningful drift
            bool dayMismatch = localDay != serverDay;
            bool timeDrift = Mathf.Abs(localTime - serverTime) > TimeDriftThreshold;

            if (dayMismatch || timeDrift)
            {
                TimeManager.Instance.SyncFromNetwork(serverDay, serverTime);
                Debug.Log($"[GameSpeedController] Client time corrected: " +
                          $"Day {localDay}->{serverDay}, " +
                          $"Time {localTime:F4}->{serverTime:F4}");
            }
        }

        #endregion
    }
}
