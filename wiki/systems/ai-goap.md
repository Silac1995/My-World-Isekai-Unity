---
type: system
title: "AI GOAP"
tags: [ai, goap, planning, npc, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[ai]]"
  - "[[ai-behaviour-tree]]"
  - "[[character-needs]]"
  - "[[jobs-and-logistics]]"
  - "[[social]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/GOAP/"
depends_on:
  - "[[ai]]"
  - "[[character]]"
  - "[[character-needs]]"
depended_on_by:
  - "[[ai]]"
  - "[[jobs-and-logistics]]"
  - "[[social]]"
---

# AI GOAP

## Summary
Goal-Oriented Action Planning as a **life manager**. NPCs are given ultimate `GoapGoal`s ("start a family", "be the best martial artist", "amass wealth"); the planner chains `GoapAction`s by backward-searching over preconditions/effects to build a plan that achieves the goal. Actions run frame-by-frame via `CharacterGoapController`. Replans trigger when an action's `IsValid` returns false or the character's current state diverges from the plan's assumptions.

## Purpose
Remove per-NPC FSM authoring. Instead of scripting "Bartender Bob does X at 9am", give Bob a goal ("be prosperous") plus the actions available to anyone, and let the planner figure out that this means "go to work, serve customers, deposit gold". Needs, jobs, and social actions are all first-class GOAP actions.

## Responsibilities
- Defining goals as desired world-state dictionaries.
- Defining actions with preconditions, effects, cost, `IsValid`, `Execute`, `IsComplete`, `Exit`.
- Running the planner to produce an ordered action list.
- Executing the current action frame-by-frame.
- Replanning on action failure or stale state.
- Injecting needs into GOAP as state variables (SOLID need-provider pattern).
- Bridging to BT via `BTAction_ExecuteGoapPlan` (slot 6).

**Non-responsibilities**:
- Does **not** own short-term reactive survival — see [[ai-behaviour-tree]] (combat, assistance, aggression live higher in priority).
- Does **not** own job schedules — GOAP reads them via [[character-schedule]] and [[jobs-and-logistics]].
- Does **not** own action execution animations/physics — actions delegate to `CharacterAction` / movement systems.

## Key classes / files

- `Assets/Scripts/AI/GOAP/GoapPlanner.cs` — backward A*-style search over precondition graph.
- `Assets/Scripts/AI/GOAP/GoapAction.cs` — action base: preconditions, effects, cost, per-frame loop.
- `Assets/Scripts/AI/GOAP/GoapGoal.cs` — goal base: desired state + priority.
- `Assets/Scripts/AI/Actions/GoapAction_*` — concrete action library.
- [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs) — per-character orchestrator.

## GoapAction lifecycle

| Method | When called |
|---|---|
| `IsValid()` | Before and during execution — replan if false. |
| `Execute(delta)` | Every tick while active. |
| `IsComplete()` | Checked each tick — advance to next action if true. |
| `Exit()` | Called on plan change or completion; **must stop coroutines + reset paths**. |

## Ultimate goal examples

- **StartAFamily** — DesiredState `{ hasChildren: true }`. Plan: find compatible NPC → charm → marry → have child.
- **BestMartialArtist** — plan: find dojo → fight opponents → level up expertise.
- **FinancialAmbition** — plan: take a Harvester job → chop wood → deposit resources → accumulate gold.

## Needs injection (SOLID)

Needs feed into GOAP state via `NeedProvider`s — decoupled from the planner itself. Low Hunger sets a state variable that lets a Eat-style action be selectable; satisfying the need removes the variable.

## Data flow

```
CharacterGoapController.Tick()
       │
       ├── CurrentGoal? If no: SelectGoal()
       │
       ├── CurrentPlan? If no: GoapPlanner.Plan(state, goal, actions)
       │
       ▼
currentAction = plan[index]
       │
       ├── !action.IsValid() ──► replan (or pop goal if no plan)
       │
       ├── action.Execute(delta)
       │
       ├── action.IsComplete() ──► index++, Exit; next action
       │
       └── (repeat)
```

## Known gotchas

- **`Exit` must clean up** — stop coroutines, reset NavMeshAgent paths. State leaks cause drift into the next action.
- **Coroutines are allowed inside GOAP actions** (unlike native BT nodes) — but still release on `Exit`.
- **Cost tuning drives behaviour** — a cheap "Steal" action will be picked over "EarnMoney" unless traits/personality raise its cost.
- **NativeGoapAction in JobTransporter** — `GoapAction_LoadTransport` / `GoapAction_UnloadTransport` run inside `JobTransporter.Execute()` to decouple state from FSM. See [[jobs-and-logistics]].
- **Needs threshold oscillation** — thresholds near the boundary churn replans. Hysteresis is your friend.

## Dependencies

### Upstream
- [[ai]] — parent.
- [[character-needs]] — state variables.
- [[character-schedule]] — time-of-day context for action validity.

### Downstream
- [[jobs-and-logistics]] — `GoapAction_PlaceOrder`, `GoapAction_LoadTransport`, `GoapAction_UnloadTransport`.
- [[social]] — `GoapAction_Socialize`.
- [[combat]] — not directly; BT handles combat (slot 3) before GOAP (slot 6).

## State & persistence

- Current goal, plan, action index: saved with NPC for warm reloads / hibernation.
- Macro-sim: does **not** replay GOAP frame-by-frame. Snaps NPC to the end of their current scheduled task (see [[world]] macro-sim).

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md)
- [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs).
- [[ai]] parent.
