using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

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
