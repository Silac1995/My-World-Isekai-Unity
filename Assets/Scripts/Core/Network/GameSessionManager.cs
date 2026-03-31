using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

    public static bool AutoStartNetwork = false;
    public static bool IsHost = true;
    public static string TargetIP = "127.0.0.1";
    public static ushort TargetPort = 7777;

    public static string SelectedPlayerRace = "Human";
    [SerializeField] private List<RaceSO> _availableRaces = new List<RaceSO>();
    [SerializeField] private RaceSO _defaultFallbackRace;

    public System.Collections.Generic.IReadOnlyList<RaceSO> AvailableRaces => _availableRaces;
    public RaceSO GetRace(string raceName)
    {
        RaceSO race = _availableRaces.Find(r => r.name == raceName);
        if (race == null)
        {
            Debug.LogWarning($"[GameSession] Race '{raceName}' not found in available races. Using fallback.");
            return _defaultFallbackRace;
        }
        return race;
    }

    private Dictionary<ulong, string> _pendingClientRaces = new Dictionary<ulong, string>();
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private bool _callbacksRegistered;

    private void Start()
    {
        EnsureCallbacksRegistered();
        CheckAutoStart();
    }

    private void OnEnable()
    {
        // Re-check on enable — handles DontDestroyOnLoad surviving scene reloads
        EnsureCallbacksRegistered();
        CheckAutoStart();
    }

    /// <summary>
    /// Resets callback state so they re-register on next call to EnsureCallbacksRegistered.
    /// Call after NetworkManager.Shutdown() since shutdown clears all callbacks.
    /// </summary>
    public void ResetCallbacks()
    {
        _callbacksRegistered = false;
    }

    public void EnsureCallbacksRegistered()
    {
        if (_callbacksRegistered || NetworkManager.Singleton == null) return;
        _callbacksRegistered = true;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
    }

    public void CheckAutoStart()
    {
        if (AutoStartNetwork)
        {
            AutoStartNetwork = false;
            StartCoroutine(WaitAndStartNetwork());
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId && !IsHost)
        {
            Debug.Log("<color=cyan>[GameSession]</color> Connected to Server");
        }

        if (NetworkManager.Singleton.IsServer)
        {
            if (_pendingClientRaces.TryGetValue(clientId, out string requestedRace))
            {
                _pendingClientRaces.Remove(clientId);

                Vector3 spawnPos = SpawnManager.Instance != null ? SpawnManager.Instance.DefaultSpawnPosition : Vector3.zero;
                Quaternion spawnRot = SpawnManager.Instance != null ? SpawnManager.Instance.DefaultSpawnRotation : Quaternion.identity;

                // Custom manual spawn via loaded Race Data
                RaceSO requestedRaceSO = Resources.Load<RaceSO>($"Data/Races/{requestedRace}") ?? Resources.Load<RaceSO>("Data/Races/Human");

                // Needs the visual prefab associated with the race, or a default fallback
                GameObject visualPrefab = requestedRaceSO != null && requestedRaceSO.character_prefabs != null && requestedRaceSO.character_prefabs.Count > 0 
                                          ? requestedRaceSO.character_prefabs[0] 
                                          : NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

                GameObject playerObj = Instantiate(visualPrefab, spawnPos, spawnRot);

                if (playerObj.TryGetComponent(out Character character))
                {
                    character.NetworkRaceId.Value = new Unity.Collections.FixedString64Bytes(requestedRace);

                    // Pre-generate deterministic name so all clients see the same one
                    GenderType gender = character.CharacterBio != null && character.CharacterBio.IsMale ? GenderType.Male : GenderType.Female;
                    if (requestedRaceSO != null && requestedRaceSO.NameGenerator != null)
                        character.NetworkCharacterName.Value = new Unity.Collections.FixedString64Bytes(requestedRaceSO.NameGenerator.GenerateName(gender));

                    character.NetworkVisualSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                }

                if (playerObj.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
                {
                    netObj.SpawnAsPlayerObject(clientId, true);
                    Debug.Log($"<color=cyan>[GameSession]</color> Spawned PlayerObject for Client {clientId} with race {requestedRace}");
                }
            }
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        // clientId is LocalClientId when we fail to connect or disconnect
        if (clientId == NetworkManager.Singleton.LocalClientId && !IsHost)
        {
            ShowToast("Disconnected or failed to reach host.", MWI.UI.Notifications.ToastType.Error);
        }
        
        if (NetworkManager.Singleton.IsServer)
        {
            _pendingClientRaces.Remove(clientId);
        }
    }

    private System.Collections.IEnumerator WaitAndStartNetwork()
    {
        // Wait briefly in real-time to guarantee the scene's NavMesh is fully established and objects are Awoken
        yield return new WaitForSecondsRealtime(0.1f);

        if (IsHost)
        {
            StartSolo();
        }
        else
        {
            JoinMultiplayer();
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Approve all connections
        response.Approved = true;
        
        // Disable automatic player spawning so we can instantiate custom prefabs with custom settings!
        response.CreatePlayerObject = false;

        string requestedRace = "Human";
        if (request.Payload != null && request.Payload.Length > 0)
        {
            requestedRace = System.Text.Encoding.ASCII.GetString(request.Payload);
        }

        // Store it for when the client fully connects
        _pendingClientRaces[request.ClientNetworkId] = requestedRace;
    }

    public void StartSolo()
    {
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
        }

        NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(SelectedPlayerRace);

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("<color=green>[GameSession]</color> Started Solo / Host Mode");
        }
    }

    public void JoinMultiplayer()
    {
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
            ShowToast("Resetting connection... Try joining again.", MWI.UI.Notifications.ToastType.Warning);
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            string cleanIP = TargetIP.Contains(":") ? TargetIP.Split(':')[0] : TargetIP;
            transport.SetConnectionData(cleanIP, TargetPort);
        }

        NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(SelectedPlayerRace);

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("<color=cyan>[GameSession]</color> Started Client Mode");
        }
        else
        {
            ShowToast("Failed to start client.", MWI.UI.Notifications.ToastType.Error);
        }
    }

    [Header("UI Notifications")]
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _generalToastChannel;

    private void ShowToast(string message, MWI.UI.Notifications.ToastType type)
    {
        if (_generalToastChannel != null)
        {
            _generalToastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: message,
                type: type,
                duration: 4f,
                icon: null
            ));
        }
        else
        {
            Debug.LogWarning("[GameSession] Toast channel not assigned in Inspector.");
        }
    }
}
