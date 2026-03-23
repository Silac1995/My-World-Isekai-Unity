---
name: networkbehaviour-rpc-corruption
description: Diagnostics and solutions for Unity Netcode (NGO) RPCs silently failing or routing incorrectly due to NetworkBehaviour array index corruption.
---

# NetworkBehaviour Array Index Corruption

This diagnostic skill explains the root cause and solution for one of the most insidious bugs in Netcode for GameObjects (NGO): **RPCs silently failing, dropping, or executing completely unrelated logic on clients.**

## The Symptoms
- The Server calls a `[ClientRpc]` or `SendTo.NotServer` method.
- The Host or original caller successfully executes the logic.
- The Client receives **no errors**, but the action (e.g., Speech bubble, Attack animation, Visual effect) completely fails to appear.
- Alternatively, calling an RPC triggers entirely unrelated behavior on the Client (e.g., calling an `AttackRpc` somehow disables a `Collider`).

## The Root Cause: Array Mismatches
When an RPC is sent over the network, NGO **does not** send the string name of the script or the method to save bandwidth. Instead, it relies on an **Index Array** generated from the exact order in which `NetworkBehaviour` components are attached to the `NetworkObject`'s underlying Prefab.

For example, a raw Character Prefab might look like this on the Client:
- `Index 0` = `NetworkTransform`
- `Index 1` = `Character`
- `Index 2` = `NetworkRigidbody`
- `Index 3` = `CharacterSpeech`

If, for any reason, the Server modifies this list dynamically *before* calling `Spawn()`, a severe **Index Mismatch** occurs.

### Example of the Bug (The "DestroyImmediate" Trap)
If you dynamically configure a shared Prefab on the Server by calling `DestroyImmediate(GetComponent<NetworkRigidbody>())` before spawning:

**SERVER'S COMPONENT LIST:**
- `Index 0` = `NetworkTransform`
- `Index 1` = `Character`
- `Index 2` = `CharacterSpeech` *(Shifted up!)*

**CLIENT'S COMPONENT LIST (Instantiated from raw Prefab):**
- `Index 0` = `NetworkTransform`
- `Index 1` = `Character`
- `Index 2` = **`NetworkRigidbody`**
- `Index 3` = `CharacterSpeech`

When the Server attempts to send `SayClientRpc()` from `CharacterSpeech`, it looks up its local index (`Index 2`) and broadcasts: *"Hey Client, execute the RPC on Component 2!"*

The Client receives the command, looks at its list, sees that Component 2 is `NetworkRigidbody`, and fails silently because `NetworkRigidbody` does not possess the corresponding RPC method.

## The Solution: `enabled` is King

**Rule 1: NEVER Add/Destroy NetworkBehaviours Dynamically**
You cannot use `Destroy()`, `DestroyImmediate()`, or `AddComponent<T>()` where `T : NetworkBehaviour` on an instantiated network object. The component structure of an executed instance must remain universally identical across Server and all Clients.

**Rule 2: Gracefully Disable Unused Components**
If a component (like `NetworkRigidbody` or a specific visual effect sender) is not needed for a specific variant (like an NPC vs a Player), you must toggle its activity strictly through the `enabled` property:
```csharp
if (TryGetComponent<Unity.Netcode.Components.NetworkRigidbody>(out var netRb))
{
    // The component remains in the array at its original Index, preserving RPC routes!
    netRb.enabled = false; 
}
```

## Verification Checklist
If you encounter a silent RPC failure on a shared Prefab, ask yourself:
- [ ] Have I used `DestroyImmediate()` or `Destroy()` on any `NetworkBehaviour` prior to or after Spawning?
- [ ] Did I mistakenly add an unexpected `NetworkBehaviour` manually via `AddComponent` right before spawning?
- [ ] Is an empty `NetworkBehaviour` present on the Client Prefab but missing on the loaded Server Prefab (or vice versa)?
- [ ] Are all my `NetworkObject` variations strictly controlled via `enabled = false`?
