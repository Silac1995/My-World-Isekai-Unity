using Unity.Netcode;
using UnityEngine;

namespace MWI.Time
{
    /// <summary>
    /// Manages the global simulation speed (Time.timeScale) across the network.
    /// The Server dictates the game speed, and all clients synchronize to it.
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
        }

        public override void OnNetworkDespawn()
        {
            if (Instance != this) return;

            _serverTimeScale.OnValueChanged -= OnTimeScaleChanged;

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
    }
}
