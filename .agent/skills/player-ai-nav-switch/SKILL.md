---
name: player-ai-nav-switch
description: Architecture, rules and exact order of operations for securely switching a Character between Manual Player physics (WASD) and Autonomous AI (NavMeshAgent).
---

# Player / AI NavMesh Switch System

This system dictates how a Character transitions seamlessly between physical manual movement (controlled by a player via WASD or Joypad) and autonomous AI movement (click-to-move, combat pacing, interact-to-move).

## The Conflict (Stuttering Root Cause)
When the `NavMeshAgent` is active on a `Rigidbody` that is NOT set to `isKinematic = true`, the Unity Physics Engine struggles against the NavMesh Engine. The NavAgent overwrites the `Transform.position` directly, while the Rigidbody applies its own inertia, friction, and gravity, resulting in extreme **stuttering** and slowing down.

## Unity's Gold Standard switching order

To fix this securely, the precise order of property assignments MUST be respected. Unity requires the physics simulation to be locked **before** the NavMeshAgent starts writing to the transform, and similarly, the NavMeshAgent must be completely shut down **before** physics unlocks.

All operations must be centralized in `Character.ConfigureNavMesh(bool enabled)`.

### 1. Switching to NavAgent (AI Mode)
When autonomous movement is needed (Combat, Click-to-Move, Cutscenes):

```csharp
// 1. Lock physics BEFORE enabling the agent
_rb.linearVelocity = Vector3.zero;
_rb.angularVelocity = Vector3.zero;
_rb.isKinematic = true;

// 2. Enable and configure agent
_cachedNavMeshAgent.enabled = true;
if (_cachedNavMeshAgent.isOnNavMesh) _cachedNavMeshAgent.isStopped = false;
_cachedNavMeshAgent.updatePosition = true;
_cachedNavMeshAgent.updateRotation = false;
```

### 2. Switching to Player Controls (Manual Mode)
When the player cancels an action with WASD or finishes an autonomous move:

```csharp
// 1. Stop and disable agent BEFORE unlocking physics
if (_cachedNavMeshAgent.isOnNavMesh)
{
    _cachedNavMeshAgent.isStopped = true;
    _cachedNavMeshAgent.ResetPath(); // Crucial to prevent resuming leftover paths if re-enabled later
}
_cachedNavMeshAgent.enabled = false;

// 2. Unlock physics (ONLY for Players)
if (_controller is PlayerController) 
{
    _rb.isKinematic = false;
}
```

## The Command Pattern Integration (`IPlayerCommand`)

To respect **SRP** (Single Responsibility Principle), `PlayerController` must not handle interaction distances or combat tactics itself. It strictly uses the **Command Pattern**:

1. `PlayerController` holds a single `IPlayerCommand _currentOrder`.
2. When the player engages in autonomous action, `SetOrder(new PlayerMoveCommand(...))` is called.
3. Every frame that `_currentOrder != null`, it uses the AI NavMesh logic.
4. When `_currentOrder == null` (or canceled by WASD), `PlayerController` falls back to `Rigidbody` manual forces.
5. In **Combat State** (`IsInBattle == true`), the PlayerController enforces a `PlayerCombatCommand` and strictly ignores WASD movement inputs to prevent the player from breaking AI tactical pacing, fully locking them into AI mode until combat ends.
