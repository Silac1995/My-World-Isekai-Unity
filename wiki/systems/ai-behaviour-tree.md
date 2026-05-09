---
type: system
title: "AI Behaviour Tree"
tags: [ai, bt, npc, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[ai]]"
  - "[[character]]"
  - "[[combat]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/"
depends_on:
  - "[[ai]]"
  - "[[character]]"
depended_on_by:
  - "[[ai]]"
---

# AI Behaviour Tree

## Summary
Root `BTSelector` with 9 top-to-bottom priority branches. Each branch is a condition (or action) that either claims the tick or falls through. Ticks are **staggered** — each NPC has a unique frame offset and ticks only every `_tickInterval` frames (default 5). The BT is **paused** when the character is a player, is dead, frozen, interacting, or running a legacy `IAIBehaviour`.

## Purpose
Make NPC decision-making cheap (staggered ticks = CPU spread across frames), predictable (strict priority order), and extensible (add a branch = add a native `BTNode`, don't touch existing logic).

## Responsibilities
- Evaluating priority branches every tick.
- Staggering ticks across NPCs.
- Honoring pause conditions.
- Exposing external controls (`GiveOrder`, `CancelOrder`, `ForceNextTick`).
- Bridging to GOAP (slot 6) and legacy `IAIBehaviour` stack (where still present).

**Non-responsibilities**:
- Does **not** own GOAP planning — see [[ai-goap]].
- Does **not** own combat logic — delegates to [[combat]] `CombatAILogic`.
- Does **not** own day schedules — reads from [[character-schedule]].

## Priority branches (in evaluation order)

| # | Condition/Action | Purpose |
|---|---|---|
| 1 | `BTCond_HasOrder` | Max priority — player/game-issued order. |
| 2 | `BTCond_NeedsToPunchOut` | Shift ended while at work — safe exit action. |
| 3 | `BTCond_IsInCombat` | Already fighting — delegates to `CombatAILogic`. |
| 4 | `BTCond_FriendInDanger` | Assistance — join a friend's fight. |
| 5 | `BTCond_DetectedEnemy` | Aggression — attack a detected threat. |
| 6 | `BTAction_ExecuteGoapPlan` | GOAP — proactive life management. |
| 7 | `BTCond_HasScheduledActivity` | Native daily-routine actions (`BTAction_Work`, sleep). |
| 8 | `BTCond_WantsToSocialize` | Spontaneous discussions. |
| 9 | `BTAction_Wander` | Fallback. |

## Pause conditions

The BT does **not** tick when any of the following is true:
- Character is a player (has `PlayerController` active).
- Character is dead (`Character._isDead`).
- `controller.IsFrozen` — cutscenes, strong dialogues.
- `CharacterInteraction.IsInteracting` — prevents mid-dialogue jumps.
- Legacy behaviour active — `Controller.CurrentBehaviour != null`.
- A GOAP action has pushed to the legacy stack (`MoveToTargetBehaviour` etc.).

## Key classes / files

- [NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs) — host; `_tickInterval`, `_frameOffset`, blackboard.
- `Assets/Scripts/AI/Core/` — `BTNode`, `BTSelector`, `BTSequence`, blackboard types.
- `Assets/Scripts/AI/Conditions/` — `BTCond_*` library.
- `Assets/Scripts/AI/Behaviours/` — native action nodes (`BTAction_Work`, `BTAction_Wander`, `BTAction_Combat`, `BTAction_PerformCraft`, `BTAction_ExecuteGoapPlan`).
- Legacy bridges: `BTCond_HasLegacyBehaviour`, `BTAction_ExecuteLegacyStack`.

## Public API

External overrides:
- `NPCBehaviourTree.GiveOrder(NPCOrder)` — queues on blackboard, top priority next tick.
- `NPCBehaviourTree.CancelOrder()`.
- `NPCBehaviourTree.ForceNextTick()` — call after un-freezing an NPC.

## Native nodes vs legacy wrappers

- **Native `BTNode`**: implement logic in `OnExecute` as a state machine. Use `UnityEngine.Time.time` for timing. No coroutines. This is the **standard**.
- **Legacy wrappers** (`BTCond_HasLegacyBehaviour`, `BTAction_ExecuteLegacyStack`): bridges that pause the BT while an `IAIBehaviour` is on the legacy stack. Do **not** create new legacy behaviours.

## Known gotchas

- **No coroutines inside native BT nodes** — coroutines break across stagger ticks. Use timestamp-based state machines.
- **`IsFrozen` + `ForceNextTick`** — if you unfreeze an NPC, call `ForceNextTick()`, otherwise they wait up to 5 frames to react.
- **GOAP bridge pauses BT** — when `BTAction_ExecuteGoapPlan` pushes `MoveToTargetBehaviour`, the BT yields to the legacy stack until the action completes.
- **Player BT is inactive** — the ticker checks for `PlayerController`; bugs in switching logic can leave a "zombie" BT.

## Dependencies

### Upstream
- [[character]] — hosts `NPCBehaviourTree` as a `CharacterSystem`.
- [[character-schedule]] — drives `BTCond_HasScheduledActivity`.
- [[ai-goap]] — bridges via `BTAction_ExecuteGoapPlan`.
- [[combat]] — `BTCond_IsInCombat` delegates to `CombatAILogic`.

### Downstream
- [[ai]] — parent.

## State & persistence

- Blackboard is transient; reset on load.
- NPC orders are transient (confirm — some orders might persist: follow-player).

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/behaviour_tree/SKILL.md](../../.agent/skills/behaviour_tree/SKILL.md)
- [NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs).
- [[ai]] parent.
