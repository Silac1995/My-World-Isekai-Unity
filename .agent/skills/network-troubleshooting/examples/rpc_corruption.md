# NetworkBehaviour Array Index Mismatches (The "DestroyImmediate" Trap)

This example details how RPC index corruption occurs dynamically and how to diagnose it quickly.

## The Symptoms
- The Server calls a `[ClientRpc]` or `SendTo.NotServer` method.
- The Host or original caller successfully executes the logic.
- The Client receives **no errors**, but the action (e.g., Speech bubble, Attack animation, Visual effect) completely fails to appear.
- Alternatively, calling an RPC triggers entirely unrelated behavior on the Client (e.g., calling an `AttackRpc` somehow disables a `Collider`).

## The Trace
For example, a raw Character Prefab might look like this on the Client:
- `Index 0` = `NetworkTransform`
- `Index 1` = `Character`
- `Index 2` = `NetworkRigidbody`
- `Index 3` = `CharacterSpeech`

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

## Verification Checklist
If you encounter a silent RPC failure on a shared Prefab, verify:
- [ ] Have I used `DestroyImmediate()` or `Destroy()` on any `NetworkBehaviour` prior to or after Spawning?
- [ ] Did I mistakenly add an unexpected `NetworkBehaviour` manually via `AddComponent` right before spawning?
- [ ] Is an empty `NetworkBehaviour` script attached to the Client Prefab but missing on the loaded Server Prefab?
- [ ] Are all my `NetworkObject` hybrid variations logically decoupled strictly via `enabled = false`?
