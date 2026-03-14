# Netcode 2.10+ Implementation Patterns

Advanced patterns for implementing multiplayer features using NGO 2.10 standards.

## 1. Unified RPC System
Using the single `[Rpc]` attribute for all messaging.

```csharp
public class CharacterCombat : NetworkBehaviour {
    public void PerformAttack() {
        if (!IsOwner) return;
        // Request the server to perform the attack
        ExecuteAttackRpc(SendTo.Server);
    }

    [Rpc(SendTo.Server)]
    private void ExecuteAttackRpc(RpcParams rpcParams = default) {
        // 1. Server-side validation
        if (!CanAttack()) return;

        // 2. Logic execution
        ProcessAttack();

        // 3. Broadcast visuals to everyone (including callers)
        PlayAttackVisualsRpc(SendTo.Everyone);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayAttackVisualsRpc() {
        animator.SetTrigger("Attack");
        vfx.Play();
    }
}
```

## 2. Advanced Lifecycle & Initial State
Using `OnNetworkPreSpawn` for latency-free initial state sync.

```csharp
public class CharacterStats : NetworkBehaviour {
    public NetworkVariable<int> Health = new NetworkVariable<int>(100);

    public override void OnNetworkPreSpawn() {
        if (IsServer) {
            // Set initial state BEFORE the spawn fragment is sent to clients
            Health.Value = CalculateStartingHealth();
        }
    }

    public override void OnNetworkSpawn() {
        Health.OnValueChanged += OnHealthChanged;
        RefreshUI(Health.Value);
    }
}
```

## 3. Custom Serialization
Syncing complex data types efficiently.

```csharp
public struct InventoryItem : INetworkSerializable {
    public int ItemID;
    public int Quantity;
    public FixedString32Bytes CustomName;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref ItemID);
        serializer.SerializeValue(ref Quantity);
        serializer.SerializeValue(ref CustomName);
    }
}

public class CharacterInventory : NetworkBehaviour {
    [Rpc(SendTo.Owner)]
    public void UpdateInventoryRpc(InventoryItem item) {
        // Handle inventory update on the owning client
        localInventory.Add(item);
    }
}
```

## 4. Connection Events
Unified handling of connections and peer discovery.

```csharp
public class ConnectionManager : MonoBehaviour {
    private void Start() {
        NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
    }

    private void OnConnectionEvent(NetworkManager networkManager, ConnectionEventData eventData) {
        switch (eventData.EventType) {
            case ConnectionEvent.ClientConnected:
                Debug.Log($"Client {eventData.ClientId} connected.");
                break;
            case ConnectionEvent.PeerConnected:
                // Other clients connected, visible to all
                HandlePeerDiscovery(eventData.ClientId);
                break;
        }
    }
}
```
