---
name: character-obstacle-avoidance
description: Architecture of the raycast-based, proactive Character obstacle steering and Physical step climbing for manual Rigidbody WASD movement.
---

# Character Obstacle Avoidance & Step Climbing

This skill defines the rules for how characters (Player or NPCs) navigate manual physics-based obstacles without relying on `NavMeshAgent`. Because physical WASD movement directly uses `Rigidbody.AddForce`, it lacks the built-in collision steering and step jumping inherent to Unity's NavMesh. 

## 1. The Core Philosophy
When the character is controlled via physical movement (e.g. `_currentOrder == null` or WASD input):
- **Rigidbody Physics** purely pushes the character flatly on the X and Z axes. 
- It has zero vertical assist out-of-the-box.
- It will slam horizontally into walls and corners, bringing the character to a hard stop.

To fix this, we use a custom dual-system approach inside `CharacterMovement.cs`: **Raycast Steering** (Obstacle Avoidance) and **Raycast Step-Jumping** (Step Handle).

## 2. Proactive Obstacle Steering (`CharacterObstacleAvoidance.cs`)
Standard Unity collisions slide against perfectly flat walls, but snag on sharp edges, corners, and overlapping box colliders. 

**The Rule:** We don't wait for physics to snag. We proactively bend the `_desiredDirection` slightly *before* impact.

### How it works:
1. `CharacterObstacleAvoidance` shoots three 'feeler' Raycasts forward at chest level. 
   - A straight **Forward** ray.
   - A **45-degree Right** ray.
   - A **45-degree Left** ray.
2. If the Forward ray hits an obstacle, we forcefully project the intended movement vector along the `RaycastHit.normal` to scrape along the wall.
3. If the Left or Right rays hit, it softly blends an opposing normal force into the `_desiredDirection` (controlled by `_avoidanceStrength`).
4. This results in the Player naturally sliding smoothly past pillars, jagged walls, and doorways.

> [!WARNING]
> Do NOT use `CharacterObstacleAvoidance` for autonomous NavMesh paths! This system is EXCLUSIVELY to assist blind manual WASD/Gamepad inputs.

## 3. Physical Step Handling (`HandleStepUp`)
Characters will naturally get completely blocked by a 1-pixel height ledge because a purely horizontal force pressing against a purely vertical face resolves to zero movement.

### How it works natively in `CharacterMovement.cs`:
We execute `HandleStepUp()` after `ApplyPhysicalMovement()` inside `FixedUpdate`.

1. **Grounded Check:** We only apply step handling if the character is currently standing on the floor.
2. **Lower Feeler:** Shoot a ray horizontally forward from `transform.position + 0.05f` higher than the ground. This detects if a vertical face is directly blocking the foot.
3. **Upper Feeler Clearance:** If the lower feeler detects a wall (step), shoot a second, higher ray at `transform.position + _stepHeight`.
4. If the lower ray hits the step, but the upper ray returns *empty* (clearance), it means it's a climbable ledge.
5. **The Lift:** Instead of applying tricky upward physics force, we cleanly shift `_rb.position += new Vector3(0, _stepSmooth, 0)`. The `CapsuleCollider` handles the rest, allowing the character to "pop" smoothly onto stairs.

## Implementation Guidelines
- Adjust `_stepHeight` based on the maximum stair riser scale in the scene. `0.3f` is the typical standard for humanoid steps in Unity.
- Leave `_stepSmooth` relatively low (like `0.08f`), or the camera/model will violently bounce while walking up stairs. The physics loop handles it cleanly.
- Keep the `_groundLayer` mask properly configured on the scene logic so steps are recognized accurately.
