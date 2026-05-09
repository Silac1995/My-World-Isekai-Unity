---
type: system
title: "Character Movement"
tags: [character, movement, navmesh, combat, tier-2]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[ai]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents:
  - npc-ai-specialist
  - combat-gameplay-architect
owner_code_path: "Assets/Scripts/Character/CharacterMovement/"
depends_on:
  - "[[character]]"
  - "[[network]]"
depended_on_by:
  - "[[combat]]"
  - "[[ai]]"
---

# Character Movement

## Summary
Unified movement for player (keyboard/mouse) and NPC (NavMeshAgent). Exposes `SetDestination`, `SetDesiredDirection`, `Stop`, `Resume`, and `Warp` / `ForceWarp` for cross-NavMesh teleports. Owns the knockback state machine — disables the agent, switches `Rigidbody` to non-kinematic, applies impulse for a 0.4s window, and re-enables cleanly. Critically, every movement method guards against active knockback, and `PlayerController.Move()` must also respect `IsKnockedBack` or it will zero the impulse.

## Purpose
Keep one movement surface for players and NPCs so combat (knockback, positioning intent, tactical pacer) drives a single contract. Route cross-NavMesh teleports (building interiors at y=5000) through `ForceWarp` to survive NavMesh topology changes.

## Responsibilities
- NavMeshAgent configuration (radius, speed, stoppingDistance) + speed scaling.
- Pathing: `SetDestination`, interruption, `Resume`.
- Direct input: `SetDesiredDirection(v)` for manual WASD.
- Teleport: `Warp` (same NavMesh) vs `ForceWarp` (cross-NavMesh — disable, teleport via `transform.position`, re-enable after 2 frames).
- Knockback: `ApplyKnockback(direction, force, duration)` — disables agent, impulse force, timer-gated restore.
- Stop/Resume guards against knockback (`_knockbackTimer > 0` returns early on SetDestination/Stop/Resume/SetDesiredDirection).
- Multiplayer authority: server authoritative; `ClientNetworkTransform` on owner.

**Non-responsibilities**:
- Does **not** decide where to go — consumers ([[combat]], [[ai]], player input) do.
- Does **not** own `NavMesh` baking — that's world setup.
- Does **not** handle animation — `CharacterAnimator` watches movement state and picks clips.

## Key classes / files

- `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs` — the core component.
- `Assets/Scripts/Core/Network/ClientNetworkTransform.cs` — owner-authoritative sync variant for players.
- `CharacterAnimator.cs` (root) — reads movement state for clip selection.
- `PlayerController.Move()` — must respect `IsKnockedBack` before re-enabling agent.

## Public API

- `character.CharacterMovement.SetDestination(Vector3)` / `Stop()` / `Resume()`.
- `character.CharacterMovement.SetDesiredDirection(Vector3)` — manual WASD.
- `character.CharacterMovement.Warp(Vector3)` / `ForceWarp(Vector3)`.
- `character.CharacterMovement.ApplyKnockback(Vector3 direction, float force, float duration = 0.4f)`.
- `character.CharacterMovement.IsKnockedBack` — must be checked by any code that re-enables the agent.
- `character.CharacterMovement.ConfigureNavMesh(bool enable)` — lower-level agent toggle.

## Data flow

Knockback:
```
combat attack resolves, applies knockback
       │
       ▼
CharacterMovement.ApplyKnockback(dir, force, 0.4)
       │
       ├── NavMeshAgent.enabled = false
       ├── Rigidbody.isKinematic = false
       ├── Rigidbody.AddForce(dir * force, Impulse)
       └── _knockbackTimer = 0.4f
       │
       ▼
FixedUpdate decrements timer
       │
       ├── during window: all movement methods return early
       │
       ▼
timer <= 0
       │
       └── NavMeshAgent.enabled = true (if in combat)
           Rigidbody.isKinematic = true
```

Cross-NavMesh teleport:
```
CharacterMovement.ForceWarp(interiorPos)
       │
       ├── NavMeshAgent.enabled = false
       ├── transform.position = interiorPos
       ├── wait 2 frames
       └── NavMeshAgent.enabled = true
```

## Dependencies

### Upstream
- [[character]] — component on a child GameObject.
- [[network]] — `ClientNetworkTransform` for owner-authoritative movement.

### Downstream
- [[combat]] — `CombatAILogic.Tick` issues SetDestination; knockback comes from attacks.
- [[ai]] — BT/GOAP actions route destinations through here.

## State & persistence

- Current position is saved with the character. Velocity / knockback state is transient.
- `HibernatedNPCData` snapshots the position at hibernation; macro-sim may overwrite it with schedule target.

## Known gotchas

- **PlayerController re-enable bug** — `PlayerController.Move()` has a safety check that re-enables the NavMeshAgent if it detects external disable during combat. This check **must** be gated by `!character.CharacterMovement.IsKnockedBack` or it immediately cancels the knockback.
- **ForceWarp vs Warp** — cross-NavMesh teleports **must** use ForceWarp. Warp silently fails.
- **0.4s window is tuned** — longer windows cause animation desync; shorter breaks the player's sense of impact.
- **Knockback reads from Rigidbody** — character kinematic → non-kinematic state machine. If a consumer sets kinematic elsewhere, it fights the knockback.
- **Agent speed scales with Initiative in combat** (confirm in SKILL) — during the combat window, move speed derives from tertiary `MoveSpeed` + combat-mode modifier.

## Open questions

- [ ] Exact multiplayer authority model — how does knockback sync? Server-broadcast + owner replay? Needs detailed section when expanded.

## Change log
- 2026-04-18 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/player-movement/SKILL.md](../../.agent/skills/player-movement/SKILL.md)
- [.agent/skills/navmesh-agent/SKILL.md](../../.agent/skills/navmesh-agent/SKILL.md)
- [.agent/skills/character-obstacle-avoidance/SKILL.md](../../.agent/skills/character-obstacle-avoidance/SKILL.md)
- [CharacterMovement.cs](../../Assets/Scripts/Character/CharacterMovement/)
- [[combat]] SKILL §11.
