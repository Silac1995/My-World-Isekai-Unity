---
name: multiplayer
description: Comprehensive architecture and implementation standards for Unity Netcode for GameObjects (NGO) 2.10+.
---

# Multiplayer (Netcode for GameObjects 2.10)

This skill dictates the mandatory standards for multiplayer development in the project, specifically leveraging **Netcode for GameObjects (NGO) 2.10**. It enforces a server-authoritative, modular, and reactive architecture.

## When to use this skill
- **Systematically** when creating any networked component or system.
- When implementing synchronized state, remote actions, or scene loading.
- When handling player connections and session management.

## Core Architecture & Rules

### 1. The Foundation: NetworkBehaviour & Lifecycle
- **Mandatory:** Inherit from `NetworkBehaviour`.
- **Lifecycle Hooks:**
    - `OnNetworkPreSpawn()`: Initialize `NetworkVariable` values on the server BEFORE spawning.
    - `OnNetworkSpawn()`: Setup logic after the object is ready on the network.
    - `OnNetworkDespawn()`: Cleanup, unsubscribe from events to prevent memory leaks.
    - `OnNetworkSessionSynchronized()`: Client-side only; triggers after full session sync.

### 2. State Sync: NetworkVariable
- **The Rule:** Use `NetworkVariable<T>` for persistent state required by late-joiners.
- **Authority:** Server-authoritative writes by default.
- **Reactivity:** Subscribe to `OnValueChanged` for UI/Visual updates. Use `FixedString32Bytes` for strings in variables.

### 3. Messaging: The Unified [Rpc] System
- **Modern Pattern:** Use the unified `[Rpc]` attribute instead of legacy `[ServerRpc]/[ClientRpc]`.
- **Naming Rule:** Methods MUST end with the `Rpc` suffix.
- **Targeting:** Control execution via `SendTo` (e.g., `SendTo.Server`, `SendTo.Everyone`, `SendTo.Owner`).
```csharp
[Rpc(SendTo.Server)]
public void RequestActionRpc(int actionId) { /* Logic */ }
```

### 4. Serialization
- **Custom Types:** Implement `INetworkSerializable`.
- **BufferSerializer:** Use the unified `NetworkSerialize` method for both reading and writing.
- **Constraints:** Avoid `string` (use `FixedString`), avoid `List` (use `NativeArray` or buffers if possible).

### 5. Object Spawning & Prefabs
- **NetworkObject:** Must be at the root of the prefab. Nested `NetworkObject`s are prohibited.
- **Registration:** Prefabs MUST be registered in the `NetworkManager`'s NetworkPrefabs list.
- **Spawning:** Only the server can call `Spawn()`. Use `networkObject.Despawn(true)` for cleanup.

### 6. Scene Management
- **NetworkSceneManager:** Use `LoadScene` via the `NetworkManager.Singleton.SceneManager`.
- **Synchronization:** Clients automatically sync to the server's active scenes. Track progress via `OnSceneEvent`.

### 7. Connection & Session Management
- **Connection Approval:** Mandatory for security. Set `ConnectionApproval = true` and handle `ConnectionApprovalCallback`.
- **OnConnectionEvent:** The unified source for handling client joins/leaves and peer notifications.

## Network-Ready Checklist
- [ ] Is my script a `NetworkBehaviour`?
- [ ] Are my RPCs using the unified `[Rpc]` attribute and `Rpc` suffix?
- [ ] Am I initializing `NetworkVariable`s in `OnNetworkPreSpawn`?
- [ ] Am I validating all client requests on the server?
- [ ] Does my custom data implement `INetworkSerializable`?
- [ ] Have I registered my NetworkPrefabs?
- [ ] Am I using `NetworkSceneManager` for all scene transitions?

## Examples & Patterns
Refer to [netcode_patterns.md](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/.agent/skills/multiplayer/examples/netcode_patterns.md) for concrete NGO 2.10 implementations.
