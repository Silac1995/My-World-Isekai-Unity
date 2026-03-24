---
name: network-troubleshooting
description: Diagnostics, root causes, and solutions for common Unity Netcode (NGO) issues, including RPC corruption, silent failures, and physics wobble.
---

# Network Troubleshooting

This skill contains root causes, architectural rules, and verified solutions for insidious multiplayer bugs in Netcode for GameObjects (NGO). It serves as the primary diagnostic reference when clients experience desyncs, silent failures, RPC routing issues, or physics glitches.

## When to use this skill
- When **RPCs silently fail**, drop, or execute completely unrelated logic on clients on Hybrid Prefabs.
- When **Non-Authoritative Clients observe entities wobbling**, jittering, or bouncing (especially on the Y-axis).
- When physics (gravity, manual transform manipulation) seem to fight network synchronization loops.

## Core Diagnostic Pillars

### 1. NetworkBehaviour Array Index Corruption (The "DestroyImmediate" Trap)
When an RPC is sent over the network, NGO **does not** send the string name of the script or the method. It relies on an **Index Array** generated from the exact order in which `NetworkBehaviour` components are attached to the `NetworkObject`'s underlying Prefab.

**Rule: NEVER Add or Destroy `NetworkBehaviour` components dynamically.**
If the Server modifies the component list (e.g., `DestroyImmediate(GetComponent<NetworkRigidbody>())`) before spawning, but the Client instantiates the raw Prefab, their Component Arrays will mismatch. 
*Result:* The Server tells the Client to execute the RPC on "Component 2", but the Client's Component 2 is now the wrong script. The RPC will drop silently or trigger unrelated logic.

**Solution:** Always disable unused components gracefully. 
`if (TryGetComponent<NetworkRigidbody>(out var netRb)) netRb.enabled = false;`

*(See `examples/rpc_corruption.md` for detailed traces).*

### 2. Client-Side Physics Wobble & Jitter
When using Authoritative servers (or Owner-Authoritative structures where the Server owns NPCs), the Client's job is purely to interpolate visual states.

**Rule: Non-Authoritative Clients must NEVER drive their own physics or modify transforms.**
If a client runs local physics (e.g., custom `HandleStepUp` logic, movement applying `AddForce`, or gravity) while simultaneously receiving `NetworkTransform` updates from the Server, the two systems fight every frame. The Client pushes the entity up/down, and the NetworkTransform snaps it back, creating severe vertical wobble.

**Solution:** Aggressively guard physics updates. 
`if (IsSpawned && !IsOwner && !IsServer) return;` at the absolute top of `FixedUpdate()`.

*(See `examples/client_physics_wobble.md` for architecture details regarding NetworkTransform vs Rigidbody).*

### 3. Sub-millimeter NavMesh Jitter (NetworkTransform Y-Sync)
If a Server-side `NavMeshAgent` moves an NPC across a flat or near-flat surface, the agent produces tiny, sub-millimeter Y fluctuations as it adheres to the NavMesh polygons. If `NetworkRigidbody` is disabled (meaning `NetworkTransform` handles raw position sync), it will faithfully transmit these micro-bumps, adding a second layer of visual wobble to Clients.

**Solution:** Dynamically disable `SyncPositionY` for NPCs.
Because shared prefabs (like humanoid characters) need Y-sync for actual Players jumping or climbing stairs, you cannot disable it globally in the Inspector. Instead, toggle it at runtime during initialization:
`if (TryGetComponent<ClientNetworkTransform>(out var netTransform)) netTransform.SyncPositionY = false;`
