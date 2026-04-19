---
type: system
title: "AI"
tags: [ai, npc, gameplay, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[jobs-and-logistics]]"
  - "[[social]]"
  - "[[world]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: npc-ai-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/AI/"
depends_on:
  - "[[character]]"
  - "[[character-needs]]"
  - "[[character-schedule]]"
  - "[[world]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
  - "[[combat]]"
  - "[[social]]"
---

# AI

## Summary
The NPC decision stack has two complementary planners. A **Behaviour Tree** (BT) ticks on every NPC at a staggered interval; it evaluates 9 priority branches (orders > shift-end > combat > assistance > aggression > GOAP > schedule > social > wander) and runs the winner. Slot 6 hands control to **GOAP** (Goal-Oriented Action Planning), a long-term "life manager" that chains actions toward ultimate goals (start a family, become the best martial artist, amass wealth). Short-term survival is BT; long-term direction is GOAP.

## Purpose
Let NPCs behave autonomously without hand-written FSMs per character, while keeping high-frequency reactions (combat, orders) cheap and predictable. Let the same GOAP graph drive job work, socializing, needs fulfillment, and life goals without baking them into behaviour tree nodes.

## Responsibilities
- Evaluating priority branches in order every tick (BT root selector).
- Staggering ticks across NPCs so the CPU cost is spread (default 5-frame interval with unique offset).
- Pausing the BT cleanly when the character is in interaction, frozen, dead, or running a legacy behaviour.
- Running the GOAP planner on demand to pick the next action toward an ultimate goal.
- Executing GOAP actions frame-by-frame via `CharacterGoapController`.
- Taking external orders (`GiveOrder(NPCOrder)`) — player build mode, mind-control, cutscenes.
- Driving combat via the shared `CombatAILogic` brain (used by players too — see [[combat]]).
- Managing pathing failure memory (`CharacterPathingMemory`) and obstacle avoidance.
- Switching a character between manual and AI control without breaking state (`player-ai-nav-switch`).

**Non-responsibilities**:
- Does not own any gameplay state — only reads `CharacterNeeds`, `CharacterSchedule`, `Stats`, etc.
- Does not own combat execution — delegates to [[combat]].
- Does not own job assignment — consumes [[jobs-and-logistics]] data.

## Key classes / files

| File | Role |
|------|------|
| [NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs) | Root BT host; staggered tick, public `GiveOrder` / `CancelOrder` / `ForceNextTick`. |
| [Assets/Scripts/AI/Behaviours/](../../Assets/Scripts/AI/Behaviours/) | Native `BTNode` implementations (Work, Wander, Combat, PunchOut, etc.). |
| [Assets/Scripts/AI/Conditions/](../../Assets/Scripts/AI/Conditions/) | `BTCond_*` — HasOrder, NeedsToPunchOut, IsInCombat, FriendInDanger, DetectedEnemy, HasScheduledActivity, WantsToSocialize. |
| [Assets/Scripts/AI/Core/](../../Assets/Scripts/AI/Core/) | Base `BTNode`, `BTSelector`, `BTSequence`, blackboard. |
| [Assets/Scripts/AI/GOAP/](../../Assets/Scripts/AI/GOAP/) | `GoapPlanner`, `GoapAction`, `GoapGoal`, action library. |
| [Assets/Scripts/AI/Actions/](../../Assets/Scripts/AI/Actions/) | `GoapAction_*` — Socialize, PlaceOrder, LoadTransport, UnloadTransport, MoveTo, etc. |
| [Assets/Scripts/AI/Orders/](../../Assets/Scripts/AI/Orders/) | `NPCOrder` types (build, move, follow, etc.). |
| [CombatAILogic.cs](../../Assets/Scripts/AI/CombatAILogic.cs) | Shared player/NPC combat brain — see [[combat]]. |
| [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs) | Per-character GOAP orchestrator — tracks current plan, switches goals, pushes actions into the legacy behaviour stack where needed. |
| [CharacterPathingMemory.cs](../../Assets/Scripts/Character/CharacterPathingMemory.cs) | Blacklist of unreachable targets; resets on `TimeManager` day change via `OnDestroy`. |

## Public API / entry points

BT control (external):
- `NPCBehaviourTree.GiveOrder(NPCOrder order)` — queues an order on the blackboard (max priority).
- `NPCBehaviourTree.CancelOrder()`.
- `NPCBehaviourTree.ForceNextTick()` — call after un-freezing an NPC.

BT pause conditions (the BT does **not** tick when):
- The character is a player (controller is `PlayerController`).
- The character is dead.
- `controller.IsFrozen == true`.
- `CharacterInteraction.IsInteracting == true`.
- A legacy `IAIBehaviour` is on the stack (`Controller.CurrentBehaviour != null`).

GOAP:
- Authoring: subclass `GoapGoal` (desired state + priority), `GoapAction` (preconditions, effects, cost, Execute).
- `CharacterGoapController.SelectGoal()` — picks next ultimate goal per NPC profile/traits.
- `CharacterGoapController.ExecutePlan()` — runs the current action's frame loop.

## Data flow

```
Every 5 frames (staggered)
        │
        ▼
 NPCBehaviourTree.Tick()
        │
        ├── is paused? ──► skip
        │
        ▼
 Root BTSelector — evaluates top-to-bottom
        │
  1. BTCond_HasOrder                  ──► player-issued / game-issued order
  2. BTCond_NeedsToPunchOut           ──► safe shift-end exit
  3. BTCond_IsInCombat                ──► delegates to CombatAILogic (shared)
  4. BTCond_FriendInDanger            ──► assistance
  5. BTCond_DetectedEnemy             ──► aggression
  6. BTAction_ExecuteGoapPlan         ──► CharacterGoapController.ExecutePlan
  7. BTCond_HasScheduledActivity      ──► native BTAction_Work etc.
  8. BTCond_WantsToSocialize          ──► spontaneous talk
  9. BTAction_Wander                  ──► fallback
```

GOAP planning loop:
```
CharacterGoapController.SelectGoal()
        │
        ▼
 GoapPlanner.Plan(currentState, desiredState, availableActions)
        │                  │
        │                  └── A*-style backward search
        │                        over preconditions/effects
        │
        ▼
 Plan = [Action_a, Action_b, Action_c]
        │
        ▼
 Execute current action frame-by-frame
        │
        ├── action.IsValid() fails        ──► replan
        ├── action.IsComplete() true      ──► next action
        └── action.Exit() on plan change
```

## Dependencies

### Upstream
- [[character]] — owns `NPCBehaviourTree`, `CharacterGoapController`, `CharacterPathingMemory` as subsystems.
- [[character-needs]] — Needs provide GOAP state variables (hunger, social, sleep).
- [[character-schedule]] — daily time slots drive `BTAction_Work` and shift-end detection.
- [[world]] — `BuildingTaskManager.ClaimBestTask<T>()`, community state, map hibernation pauses ticks.

### Downstream
- [[jobs-and-logistics]] — `JobLogisticsManager` GOAP actions (`PlaceOrder`, `LoadTransport`, `UnloadTransport`), `JobCrafter`, `JobVendor`, `JobTransporter` all live inside GOAP.
- [[combat]] — `CombatAILogic.Tick` is called by the BT's combat branch (and by player controllers).
- [[social]] — `GoapAction_Socialize` feeds the relationship system.

## State & persistence

- Runtime: BT blackboard (`CurrentOrder`, `LastTickFrame`), GOAP (`CurrentGoal`, `CurrentPlan`, action state), `CharacterPathingMemory` unreachable-target set.
- Persisted: an NPC's ultimate goal(s) and GOAP state are snapshotted for [[save-load]] and [[world]] hibernation.
- Macro-simulation: during hibernation, `MacroSimulator` does **not** re-run BT/GOAP frame-by-frame. It skips to the end of the current scheduled task and snaps position. Needs decay is pure math. See [[world]] section 3.

## Known gotchas / edge cases

- **Legacy `IAIBehaviour` stack freezes the BT** — `BTCond_HasLegacyBehaviour` + `BTAction_ExecuteLegacyStack` bridges the old stack. Do **not** add new legacy behaviours.
- **Coroutines forbidden inside native BT nodes** — use `UnityEngine.Time.time` to stamp time; coroutines break across stagger ticks.
- **PathingMemory reset on day change** — relies on `OnDestroy` triggered by `TimeManager`. Don't persist the memory to disk.
- **GOAP action `Exit()` must stop coroutines and reset paths** — otherwise state leaks into the next action.
- **Duplicate order placement** — `BuildingLogisticsManager` duplicate check lives on the building, not the GOAP action. See [[jobs-and-logistics]].

## Open questions / TODO

- [ ] No separate SKILL.md for `ai-actions` or `ai-conditions` subfolders — current `behaviour_tree` + `goap` SKILLs cover the shape but not the individual actions. Tracked in [[TODO-skills]].
- [ ] Clarify whether legacy `IAIBehaviour` stack will ever be fully removed or stay as a bridge indefinitely.

## Child sub-pages (to be written in Batch 2)

- [[ai-behaviour-tree]] — root selector, priority branches, tick staggering.
- [[ai-goap]] — planner, actions, goals, `CharacterGoapController`.
- [[ai-actions]] — the GOAP action library.
- [[ai-conditions]] — the BT condition library.
- [[ai-pathing]] — `CharacterPathingMemory`, path diversification.
- [[ai-navmesh]] — NavMeshAgent authority + multiplayer concerns.
- [[ai-obstacle-avoidance]] — tight-space navigation.
- [[ai-player-nav-switch]] — switching manual/AI without state loss.

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/behaviour_tree/SKILL.md](../../.agent/skills/behaviour_tree/SKILL.md)
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md)
- [.agent/skills/pathing-system/SKILL.md](../../.agent/skills/pathing-system/SKILL.md)
- [.agent/skills/navmesh-agent/SKILL.md](../../.agent/skills/navmesh-agent/SKILL.md)
- [.agent/skills/character-obstacle-avoidance/SKILL.md](../../.agent/skills/character-obstacle-avoidance/SKILL.md)
- [.agent/skills/player-ai-nav-switch/SKILL.md](../../.agent/skills/player-ai-nav-switch/SKILL.md)
- [.claude/agents/npc-ai-specialist.md](../../.claude/agents/npc-ai-specialist.md)
- [NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs)
- [CombatAILogic.cs](../../Assets/Scripts/AI/CombatAILogic.cs)
- [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs)
- 2026-04-18 conversation with [[kevin]].
