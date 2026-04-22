---
name: navmesh-agent
description: Rules and architectural constraints for using UnityEngine.AI.NavMeshAgent in the project, specializing in multiplayer authority, dynamic game speeds, and integration with the pathing system.
---

# NavMesh Agent Architecture

This skill defines the rules for utilizing `UnityEngine.AI.NavMeshAgent` within the project. Because the game features high-speed simulation (up to 8x "Giga Speed") and Unity NGO multiplayer, we cannot treat NavMeshAgents as naive default components.

## When to use this skill
- When implementing a new NPC movement behavior, Behaviour Tree Node, or GOAP action.
- When configuring a new Character prefab that requires navigation.
- When debugging pathfinding, stuck characters, or positional desyncs in multiplayer.

## The Multiplayer-First Paradigm

### 1. Server Authority
`NavMeshAgent` components are strictly **Server-Authoritative**. 
- **Rule:** Clients must NEVER calculate paths, set `destination`, or modify `NavMeshAgent` state directly. 
- **Rule:** On the client side, the `NavMeshAgent` component must ideally be disabled. Positional updates are synchronized via `NetworkTransform` (or a custom positional interpolator) from the server.
- **Rule:** If a player-controlled client wishes to move via point-and-click, it must send an RPC (Command/ServerRpc) to the server with the desired position, and the server's `NavMeshAgent` handles the actual execution.

## Execution and Performance Rules

### 2. Event-Driven Pathing over Continuous Polling
Do not call `NavMeshAgent.SetDestination()` inside an `Update()` loop every frame. `SetDestination` triggers a synchronous pathing request which allocates memory and burns CPU.
- **Rule:** Set the destination once. Recalculate only if the dynamic target has moved beyond a specific distance threshold: `Vector3.SqrMagnitude(targetLastPos - target.position) > threshold`.
- **Rule:** Stagger AI logic using `Time.time`. 
  - *Anti-Pattern:* `if (Time.frameCount % 5 != 0) return;` (Fails at 8x game speed, crippling AI responses).
  - *Correct Pattern:* `if (Time.time >= _nextPathUpdateTime) { _nextPathUpdateTime = Time.time + _pathUpdateInterval; ... }`

### 3. Dynamic Game Speeds & Pathing Timeouts
The project's Game Speed Controller scales simulation up to 8x. The `NavMeshAgent` automatically adjusts its physical delta step, but our logical timeouts do not.
- **Rule:** For any asynchronous pathing timeouts (e.g. `NavMesh.CalculatePath` wrappers waiting to identify failure), NEVER use `Time.time`. ALWAYS use `Time.unscaledTime`. This ensures the pathing algorithm always gets the exact same amount of real-world computing time (e.g., 0.2s) regardless of whether the simulation is paused (`timeScale = 0`) or sped up (`timeScale = 8`).

## 3D Constraints for a 2.5D World

### 4. 2D Sprites in a 3D NavMesh
The project visualizes characters as 2D Sprites (Billboards) navigating a 3D X/Z physics plane.
- **Rule:** Uncheck/disable `updateRotation` on the `NavMeshAgent`. 
- **Rule:** Do not allow the `NavMeshAgent` to rotate the physical `Transform` of the Character. Rotation must be handled manually by the `CharacterVisuals` system, which reads the `NavMeshAgent.velocity.x` to flip the sprite's `SpriteRenderer` left or right smoothly without physically re-orienting the 3D collider.

## NavMeshObstacle sources

- **Buildings**: `Building.cs` triggers a full `NavMeshSurface.BuildNavMesh()` rebake on spawn so building geometry is precisely included.
- **WorldItems**: each carries a `NavMeshObstacle` (carve=true) enabled by the server on first ground contact, propagated to clients via `_obstacleActive` `NetworkVariable<bool>`. Items opt out via `ItemSO.BlocksPathing = false`. No global rebake — runtime carving only. See `.agent/skills/item_system/SKILL.md` for tuning details.

If your agent is failing to path around an item: confirm the item's `ItemSO.BlocksPathing` is true and that `_obstacleActive` is true on the peer that owns the agent.

### 5. Integration with the Pathing System
`NavMeshAgent` natively uses `NavMeshPathStatus.PathPartial` or `PathInvalid` to signal failure, but does not remember targets.
- **Rule:** Never implement raw distance, bounding box checks, or path validation inside individual Movement/GOAP scripts.
- **Rule:** Funnel all validations through `NavMeshUtility.HasPathFailed(...)` and `NavMeshUtility.GetOptimalDestination(...)`.
- **Rule:** Upon detecting a confirmed path failure, you MUST record it to the `Character`'s `PathingMemory` using `worker.PathingMemory.RecordFailure(targetInstanceID)` so the target becomes blacklisted, preventing infinite pathing loops. (See the `pathing-system` skill for more details).
