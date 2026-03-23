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

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (AutoStartNetwork)
        {
            AutoStartNetwork = false;
            StartCoroutine(DelayedStartNetwork());
        }
    }

    private System.Collections.IEnumerator DelayedStartNetwork()
    {
        yield return null; // Wait 1 frame to ensure all Start() methods and NGO initializations are finished

        if (IsHost)
        {
            StartSolo();
        }
        else
        {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(TargetIP, TargetPort);
            }
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
        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("<color=green>[GameSession]</color> Started Solo / Host Mode");
        }
    }

    public void JoinMultiplayer()
    {
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("<color=cyan>[GameSession]</color> Started Client Mode");
        }
    }
}
