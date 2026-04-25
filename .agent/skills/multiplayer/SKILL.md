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

### 5. Ownership and Authority
- **The Rule:** Validate everything on the server. Clients "request" actions; the server "executes" them if valid.
- **Player Objects:** The client has ownership of their local player character. Use `IsOwner` to gate input in `PlayerController`.
- **NPC Authority:** NPCs are **Server-Authoritative**. Their AI controllers (`NPCController`, Behaviour Trees) MUST only run on the server (`IsServer`).
- **Possession Switch:** When switching between NPC and Player control:
    - Enable `PlayerController` ONLY if `IsOwner` AND the character is currently player-controlled.
    - Enable `NPCController` (and AI) ONLY if `IsServer` AND the character is NOT player-controlled.

### 6. Object Spawning & Prefabs
- **NetworkObject:** Must be at the root of the prefab. Nested `NetworkObject`s are prohibited.
- **Registration:** Prefabs MUST be registered in the `NetworkManager`'s NetworkPrefabs list.
- **Spawning:** Only the server can call `Spawn()`. Use `networkObject.Despawn(true)` for cleanup.
- **NetworkBehaviour Integrity (CRITICAL):** NEVER use `DestroyImmediate()` or `Destroy()` on a `NetworkBehaviour` component on a network instance. Doing so before or after `Spawn()` corrupts the array indices matching Server to Client prefabs, causing all subsequent RPCs and NetworkVariables to silently fail or route to wrong components. To turn off a networking component, strictly use `component.enabled = false`.

### 6. Scene Management
- **NetworkSceneManager:** Use `LoadScene` via the `NetworkManager.Singleton.SceneManager`.
- **Synchronization:** Clients automatically sync to the server's active scenes. Track progress via `OnSceneEvent`.

### 7. Connection & Session Management
- **Connection Approval:** Mandatory for security. Set `ConnectionApproval = true` and handle `ConnectionApprovalCallback`.
- **OnConnectionEvent:** The unified source for handling client joins/leaves and peer notifications.

### 8. NetworkList event-type fan-out
- **The pitfall:** `NetworkList<T>.OnListChanged` fires distinct `NetworkListEvent<T>.EventType` values depending on which mutator was used: `Add`, `Insert`, `Value`, `Remove` (by-value), `RemoveAt` (by-index), `Clear`, `Full`. Handlers that only branch on one removal type silently drop the others — the bug is invisible during single-player testing because the server's local state is already correct, but every client sees stale data.
- **The rule:** any client-side handler that reacts to removal must check **both** `EventType.Remove` and `EventType.RemoveAt` (and ideally `EventType.Clear` as a defensive bulk wipe). Symptom when violated: client UI/snapshot dictionaries that should clear after a server-side `RemoveAt(i)` stay populated forever, even though the server's own list is correct.
- **Reference implementations:** `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` (lines 67-77) and `Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs` (`HandleClaimedListChanged`) both branch on the full removal set. Use them as the canonical pattern.

### 9. Transport tuning for content-heavy scenes
- **`UnityTransport.MaxPacketQueueSize`** is the transport's **per-connection receive-queue size** in packets. The default `128` is too low for any scene with more than a handful of scene-placed / early-spawned `NetworkObject`s.
- **Symptom when too low:** at client connect time the transport logs `Receive queue is full, some packets could be dropped, consider increase its size (N).` Some spawn packets get dropped → the server keeps ticking `NetworkTransform` / `NetworkVariable` deltas for NOs the client never received → after 10 s the client logs `[Deferred OnSpawn] Messages were received for a trigger of type NetworkTransformMessage associated with id (X), but the NetworkObject was not received within the timeout period 10 second(s).`
- **Fix:** set `MaxPacketQueueSize` high (**4096**) AND raise `NetworkConfig.SpawnTimeout` to **30 s** so late-arriving spawn messages still resolve their pending update deltas. This project applies both at runtime in `GameSessionManager.ApplyTransportTuning()` (called from `StartSolo()` and `JoinMultiplayer()`), so the values are consistent across host and client regardless of what the scene serialized.
- Why both knobs together: raising only the queue prevents the drop; raising only the timeout only buys more time for already-dropped packets that will never arrive. With both, the receive queue can hold the full initial snapshot, and any delta that transiently outruns its spawn message has a longer window to reconcile.
- **Related knobs:** `MaxPayloadSize` (default 6144 B — keep unless your individual messages exceed that), `MaxSendQueueSize` (0 = unbounded, leave alone unless you see the reverse symptom on the server).
- **Architectural levers** to reduce pressure on the queue: fewer scene-placed NOs (spawn them on demand), lower `NetworkTransform` tick rate on non-critical objects, disable per-tick replication for idle objects, or shard the scene via `NetworkSceneManager` so the initial sync is smaller.

### 10. Half-spawned NetworkObjects and client-join NRE
- **The pitfall:** a `NetworkObject` that ends up in `NetworkManager.SpawnManager.SpawnedObjects` with a null internal `NetworkManagerOwner` field will make `NetworkObject.Serialize` NRE (line 3182: `NetworkManagerOwner.DistributedAuthorityMode`). That throw happens inside NGO's client-approval sync loop (`NetworkSceneManager.SynchronizeNetworkObjects`), which aborts the whole handshake — the client **silently fails to join** without the host seeing anything other than the NRE in its own log.
- **How it happens:** a networked GO gets `Destroy()`'d without first calling `NetworkObject.Despawn(true)`, or save-restore reparents/respawns a networked entity in an order NGO doesn't expect. Fresh worlds are unaffected — the bug only shows up with loaded saves, because only the restore pipeline produces the half-spawned state.
- **Why the obvious check misses it:** `NetworkObject.NetworkManager` is a public property that silently falls back to `NetworkManager.Singleton` when the internal `NetworkManagerOwner` field is null. Checking `no.NetworkManager == null` therefore **always returns false** even for broken NOs. The field itself is internal — you must reflect into it, or better: actually invoke `Serialize` inside a try/catch.
- **Defensive purge:** `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` (invoked from `ApprovalCheck`) reflects into `NetworkManagerOwner`, invokes `Serialize` as a probe, and removes any entry that NRE's before NGO's sync sees it. Use it as the reference pattern if you hit the same symptom elsewhere. The purge is a band-aid — the real fix lives at the source (trace the offending despawn/reparenting path from the warning's `name=` and `id=`).
- **Dict-vs-HashSet trap (CRITICAL):** `NetworkSpawnManager` keeps the spawned set in **two** parallel containers — `SpawnedObjects` (the `Dictionary<ulong, NetworkObject>` you usually see) and `SpawnedObjectsList` (a `HashSet<NetworkObject>`). They are **not auto-synced**. NGO's client-sync iteration source is `SceneEventData.AddSpawnedNetworkObjects()` which walks `SpawnedObjectsList`, NOT the dict. Removing a broken NO only from the dict (the obvious thing) leaves it in the HashSet and the NRE still fires during sync. Always purge **both**: `spawned.Remove(id); spawnedList.Remove(no);`. The current `PurgeBrokenSpawnedNetworkObjects` does this and additionally sweeps the HashSet for Unity-destroyed (fake-null) orphans the dict scan missed.
- **Canonical spawn-with-parent order:** for a network-spawned object that must be parented, `SetParent` **before** `Spawn()` is the safe order. `NetworkObject.TrySetParent` exists for the post-spawn case but has gotchas.
- **Furniture-with-NetworkObject must NOT be nested in runtime-spawned building prefabs.** When the building prefab spawns, NGO walks the hierarchy and tries to register every nested NetworkObject — a baked child NO ends up in `SpawnedObjectsList` with a half-initialised state and NRE's during the next client-sync. Authoring rule: only NetworkObject-FREE furniture (e.g. TimeClock, which strips its NO via `m_RemovedComponents`) may be baked into the building prefab. Anything that needs network state — `CraftingStation`, `Bed` — must be runtime-spawned by the building via `CommercialBuilding._defaultFurnitureLayout` (a `List<DefaultFurnitureSlot>` of `(FurnitureItemSO, LocalPosition, LocalEulerAngles, TargetRoom)`). On the server-side branch of `OnNetworkSpawn` the building calls `TrySpawnDefaultFurniture()`, which for each slot: (1) `Instantiate`s the prefab at the world position derived from the building's transform, (2) calls `NetworkObject.Spawn()` while the instance is still at scene-root, (3) `SetParent`s under the **building root** (worldPositionStays:true), then (4) calls `FurnitureManager.RegisterSpawnedFurnitureUnchecked` on the target room to record grid + furniture-list membership. The spawn loop is gated by `_defaultFurnitureSpawned` (idempotent) and a per-slot match by `FurnitureItemSO` against existing children (skips slots whose furniture is already baked or restored). The Forge prefab is the canonical example.
- **NGO `InvalidParentException` — never SetParent a NetworkObject under a non-NetworkObject parent.** NGO's `OnTransformParentChanged` throws `InvalidParentException` when a spawned NetworkObject is reparented under a GameObject that doesn't carry its own `NetworkObject` component. This is why default furniture is parented under the **building root** (a NO) rather than under `Room_Main` (a `NetworkBehaviour` on a non-NO GameObject). Promoting `Room_Main` to a NetworkObject would re-introduce the half-spawn class above (nested NO inside a runtime-spawned building prefab). Logical "furniture-belongs-to-room" membership lives in `FurnitureManager._furnitures`, NOT in transform parenting; `Room.GetFurniture*` queries hit that list. The same restriction bites the existing player-place flow at `CharacterActions.RequestFurniturePlaceServerRpc → FurnitureManager.RegisterSpawnedFurniture` — that path will throw the same exception the first time a player drops a NO-bearing furniture inside a non-NO room. Use `RegisterSpawnedFurnitureUnchecked` (which deliberately does **not** reparent) when the caller has already parented under a valid NO ancestor.
- **Surface NGO connection progress to UI via the driver pattern, never by polling from a UI MonoBehaviour.** UI components should never subscribe to `NetworkManager` events directly — it couples UI to networking and leaks event handlers across scene loads. Instead: a short-lived **driver** observes the relevant NGO events and pushes already-translated stage data into a generic UI controller (e.g. `MWI.UI.Loading.LoadingOverlay`). `NetworkConnectionLoadingDriver` (in `Assets/Scripts/UI/Loading/`) is the canonical example for client-join progress — it subscribes to `OnClientStarted` / `OnSceneEvent` (Load / Synchronize / SynchronizeComplete) / `OnClientConnectedCallback` / `OnClientDisconnectCallback` in `OnEnable`, polls `SpawnManager.SpawnedObjectsList.Count` at 10 Hz during the synchronize stage to report a running entity-loaded counter, and self-destructs on connect/disconnect/cancel. Future loading scenarios (save-load, scene transitions, solo session boot) follow the same shape: implement a new driver, reuse the overlay. Spec at `docs/superpowers/specs/2026-04-25-loading-overlay-design.md`.
- **`Application.isPlaying` guard around `DontDestroyOnLoad`.** `DontDestroyOnLoad` throws when invoked from EditMode (editor scripts, asset utilities, smoke tests). For singletons that may be touched outside playmode, gate the call: `if (Application.isPlaying) DontDestroyOnLoad(go);`. Runtime behavior is unchanged; the singleton becomes usable from EditMode tooling. `LoadingOverlay.Instance` follows this pattern after the Task 3 EditMode smoke test surfaced the throw.

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
