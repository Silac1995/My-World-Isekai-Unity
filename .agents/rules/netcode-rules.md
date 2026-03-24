---
trigger: always_on
---

# NETWORK ARCHITECTURE
> Reference document for all network-related implementation decisions in this project.
> All rules about networking in the main rules file point here.

---

## 1. Core Philosophy

This game follows the **"Solo is a special case of Multiplayer"** model.
Even in solo play, a local server (Host) is always started. The client connects to it exactly as it would to a remote server. There are no shortcuts, no solo-only code paths, no bypasses.

This means:
- One codebase handles both solo and multiplayer.
- Bugs fixed in solo are fixed in multiplayer.
- The game can always be opened to other players without architectural changes.

---

## 2. Roles & Responsibilities

### Server
- The **sole authority** over the game world state.
- Validates every action before applying it.
- Runs all simulation logic: NPC AI, combat resolution, world mutations (block destruction, loot spawning, triggers, etc.).
- Never trusts Client input blindly — always validate before applying.
- Notifies all relevant Clients of state changes via `ClientRpc` or `NetworkVariable`.

### Client
- Sends **intent only** to the Server via `ServerRpc` (e.g., "I want to attack", "I want to pick up this item").
- Never directly mutates shared world state, NPC state, or another player's state.
- Applies **Client-Side Prediction** locally for immediate feedback, then reconciles with the Server's authoritative response.
- Receives state updates and interpolates visuals smoothly.

### Host
- Runs **both** the Server and a local Client in the same process.
- The Host's local Client must go through the **same ServerRpc validation path** as any remote Client.
- No shortcuts. No direct state mutation from the Host Client. Ever.

```
[ Remote Client A ] ──┐
                       ├──► [ SERVER (authoritative simulation) ] ──► [ World State ]
[ Host Client     ] ──┘              │
                                     └──► Notifies all Clients (ClientRpc / NetworkVariable)
```

---

## 3. Client-to-Client Interactions

**Clients never communicate directly with each other.**

All interactions between two players (PvP combat, trade, shared object manipulation, inspecting another player's inventory, etc.) must follow this exact flow:

```
Client A  ──ServerRpc──►  Server (validates + applies)  ──ClientRpc──►  Client A + Client B
```

This guarantees:
- A single source of truth for all outcomes.
- Clean handling of disconnections mid-interaction.
- No desync between clients.

---

## 4. Latency & Performance

### 4.1 Client-Side Prediction
To eliminate perceived latency, Clients apply actions **immediately** on their local simulation without waiting for Server confirmation.

Flow:
1. Client sends `ServerRpc` with intent.
2. Client **immediately** applies the action locally (predicted state).
3. Server validates and applies the authoritative result.
4. Server sends confirmation back to Client.
5. If Server result differs from prediction → **Client rolls back and reconciles**.

Apply prediction to: player movement, attacks, item pickup, building placement.
Do NOT apply prediction to: economy transactions, loot drops, NPC state — these must be fully server-authoritative.

### 4.2 Interest Management
Clients only receive state updates for entities **within their proximity range**.
- Define a visibility/interest radius per entity type (players, NPCs, world objects).
- Entities outside the radius are not synced to that Client.
- This drastically reduces network traffic in large worlds.

### 4.3 Delta Compression
Never sync the full state of an object every tick.
- Use `NetworkVariable` with change callbacks — they only fire when the value actually changes.
- For complex objects, manually track dirty flags and only send changed fields.
- Never send position updates for stationary entities.

### 4.4 Physics on Clients
- Server-owned entities (NPCs, world objects): **set Rigidbody to kinematic on Clients**. The Server simulates physics, Clients receive position updates and interpolate.
- Client-owned entities (local player): physics runs on Client for prediction, Server corrects if needed.
- Never let a Client's physics engine fight against a NetworkTransform update.

---

## 5. Component Rules

### NetworkTransform vs ClientNetworkTransform
| Use case | Component |
|---|---|
| NPC, world object, server-owned entity | `NetworkTransform` (server authoritative) |
| Local player character | `ClientNetworkTransform` (client authoritative) |

**Never** put `ClientNetworkTransform` on an NPC. The Server runs NPC logic — it must own the transform.

### NetworkBehaviour Checks
Every networked class must explicitly gate its logic with the correct checks:

```csharp
// Server-only logic (NPC AI, combat validation, world mutation)
if (!IsServer) return;

// Client-only logic (input, UI feedback, visual interpolation)
if (!IsClient) return;

// Owner-only logic (local player input)
if (!IsOwner) return;
```

Never mix server and client logic in the same method without explicit gating.

### NetworkVariable
- Use for persistent, observable state (health, position, inventory count, etc.).
- Always define `ReadPerm` and `WritePerm` explicitly.
- Typically: `ServerWrite / OwnerRead` or `ServerWrite / Everyone Read`.
- Subscribe to `OnValueChanged` on the Client for reactive UI updates.

```csharp
private NetworkVariable<int> _health = new NetworkVariable<int>(
    100,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
```

---

## 6. NPC Networking

- NPC logic (AI, pathfinding, decisions) runs **Server only**.
- NPC Rigidbody is **kinematic on Clients** — no physics simulation client-side.
- NPC position is synced via `NetworkTransform` (server authoritative).
- NPC state (health, target, status) is synced via `NetworkVariable`.
- Clients receive NPC updates and interpolate visuals only — they never drive NPC behavior.

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    if (!IsServer)
    {
        _rb.isKinematic = true; // Clients never simulate NPC physics
    }
}

private void FixedUpdate()
{
    if (!IsServer) return; // All NPC logic is server-only
    RunAI();
}
```

---

## 7. The Golden Question

Before writing **any** interaction logic, always ask and document the answer in code comments:

> **"Who owns this action — Server, Client, or both?"**

- If Server → use `IsServer` guard, apply directly, notify Clients.
- If Client → use `IsOwner` guard, send `ServerRpc`, apply prediction locally.
- If both → Client predicts, Server validates and reconciles.

---

## 8. Quick Reference Checklist

Before submitting any networked system, verify:

- [ ] Is the transform authority correct? (`NetworkTransform` vs `ClientNetworkTransform`)
- [ ] Is the Rigidbody kinematic on Clients for server-owned entities?
- [ ] Does every state change go through the Server?
- [ ] Are `IsServer` / `IsClient` / `IsOwner` guards explicit and correct?
- [ ] Does the Host Client go through the same ServerRpc path as remote Clients?
- [ ] Is Client-Side Prediction applied where needed?
- [ ] Are NetworkVariables using delta (change callbacks) and not polled every tick?
- [ ] Are Clients only receiving updates for nearby entities (Interest Management)?
- [ ] Is there zero direct Client-to-Client communication?
