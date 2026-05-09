# Client-Side Physics Wobble & NetworkTransform Conflicts

When implementing multiplayer movement for shared prefabs (Hybrids that can be either WASD Players or Autonomous NPCs), you will frequently encounter scenarios where NPCs violently wobble, bounce, or sink into the floor on Non-Authoritative Clients.

## The Symptoms
- The Server/Host sees the NPC moving smoothly.
- The Client sees the NPC rapidly bouncing on the Y-axis (vertical wobble).
- The Client sees the NPC accelerating backwards or "stutter-stepping" across the X/Z axes.

## Root Causes & Traces

### 1. Physics Engine Fighting NetworkTransform (The FixedUpdate Trap)
If an NPC is strictly server-authoritative, the `NetworkTransform` continuously pushes discrete position updates to the Client. The Client then uses interpolation to visually smooth the travel between these discrete points.

If the non-authoritative Client allows **any** local physics logic to affect the `Rigidbody` (like Gravity, custom Raycast Step-Up logic, or `AddForce`), it corrupts the interpolation.
- `FixedUpdate` (Client): Applies local gravity/step-up force (Y increases/decreases).
- `Update` (Client): Interpolation script zeroes out the velocity but the transform has already shifted.
- `Update` (Client): `NetworkTransform` receives the real Server position and snaps the object back to the correct track.
*Result:* Three systems fighting over the Y-axis every frame creates extreme visual vibration.

**The Fix:** Aggressively gate all physical movement logic in `FixedUpdate` based on authority:
```csharp
private void FixedUpdate()
{
    // Non-authoritative clients should NEVER drive their own physics!
    if (IsSpawned && !IsOwner && !IsServer) return;

    // ... continued physics logic (AddForce, MovePosition, etc.)
}
```

### 2. Sub-millimeter Transmissions 
If the Server drives the NPC using a `NavMeshAgent` across slightly un-even terrain (or a flat NavMesh raised 0.01 units by the voxel baking), the Server-side Y position fluctuates by fractions of a millimeter on every step.

If `NetworkRigidbody` is disabled (as it should be for NPCs to avoid network syncing conflicts), `NetworkTransform` takes over raw position sync. It will faithfully transmit these micro-bumps over the network, and the Client will meticulously interpolate every microscopic jitter.

**The Fix:** For entities constrained to 2D planes or near-flat NavMeshes, **lock the Y axis sync completely**.
Because a shared Prefab (like a Humanoid) might be controlled by a Player (who needs Y synced to jump/climb stairs), you cannot permanently uncheck `SyncPositionY` in the Inspector. Instead, dynamically disable it when switching to NPC logic:

```csharp
public void SwitchToNPC()
{
    // ...
    if (TryGetComponent<Unity.Netcode.Components.ClientNetworkTransform>(out var netTransform))
    {
        netTransform.SyncPositionY = false; // NPCs don't transmit Y micro-bumps
    }
}
```
