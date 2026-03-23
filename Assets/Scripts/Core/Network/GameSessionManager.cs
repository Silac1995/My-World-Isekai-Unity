using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

    public static bool AutoStartNetwork = false;
    public static bool IsHost = true;
    public static string TargetIP = "127.0.0.1";
    public static ushort TargetPort = 7777;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;

            // Connection state callbacks
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

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
            ShowToast("Connected to Server!", MWI.UI.Notifications.ToastType.Success);
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        // clientId is LocalClientId when we fail to connect or disconnect
        if (clientId == NetworkManager.Singleton.LocalClientId && !IsHost)
        {
            ShowToast("Disconnected or failed to reach host.", MWI.UI.Notifications.ToastType.Error);
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
        // For testing, approve all connections and spawn the player object.
        response.Approved = true;
        response.CreatePlayerObject = true;

        if (SpawnManager.Instance != null)
        {
            response.Position = SpawnManager.Instance.DefaultSpawnPosition;
            response.Rotation = SpawnManager.Instance.DefaultSpawnRotation;
        }
    }

    public void StartSolo()
    {
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
        }

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("<color=green>[GameSession]</color> Started Solo / Host Mode");
            ShowToast("Server Started Successfully", MWI.UI.Notifications.ToastType.Success);
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

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("<color=cyan>[GameSession]</color> Started Client Mode");
            ShowToast($"Connecting to {TargetIP}:{TargetPort}...", MWI.UI.Notifications.ToastType.Info);
        }
        else
        {
            ShowToast("Failed to start client.", MWI.UI.Notifications.ToastType.Error);
        }
    }

    private void ShowToast(string message, MWI.UI.Notifications.ToastType type)
    {
        var channel = Resources.Load<MWI.UI.Notifications.ToastNotificationChannel>("Data/UI/ToastGeneralChannel");
        if (channel != null)
        {
            channel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: message,
                type: type,
                duration: 4f,
                icon: null
            ));
        }
        else
        {
            Debug.LogWarning("[GameSession] Toast channel not found in Resources.");
        }
    }
}
