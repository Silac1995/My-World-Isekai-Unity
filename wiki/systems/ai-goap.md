---
type: system
title: "AI GOAP"
tags: [ai, goap, planning, npc, tier-2]
created: 2026-04-19
updated: 2026-04-27
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
- **Host-only performance trap — `Replan()` throttle is mandatory** — `CharacterGoapController.Replan()` is guarded by `_planReevaluationInterval` (default 2s). Without the guard, a jobless NPC bounces Replan→fail→Wander→re-enter GOAP every BT tick (0.1s), scanning all buildings twice per attempt. With `N` NPCs and `B` buildings that is `O(N·B·log B·20)` per second on the host (server-authoritative). Clients are unaffected because `NPCBehaviourTree.Update` early-returns on non-server. If you add a new entry point that forces a replan, always route it through `CancelPlan()` (which resets the throttle) instead of bypassing it.
- **`GoapPlanner._usedActions` and `_scratchState` are static scratch buffers** — used for path-tracking and world-state mutate+restore during backward search. Safe only because `Plan()` is called on the server main thread and is non-reentrant. Do not call `Plan()` recursively from inside a `GoapAction.Execute`, `ArePreconditionsMet`, or `ApplyEffects` override. If you ever parallelise planning across threads, each worker needs its own copies of both buffers plus the `_restorePool` stack.
- **`GoapPlanner.VerboseLogging` is off by default** — on Windows, the Unity console stalls progressively as entries pile up. Flip the static on only while debugging.

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
- 2026-04-27 — **Performance pass: per-action TTL cache + `OverlapBoxNonAlloc` (Tier 3 Bₐ)**. `GoapAction_GatherStorageItems.FindLooseWorldItem` was running 3 `Physics.OverlapBox` calls per `IsValid()` tick (BuildingZone + DepositZone + DeliveryZone) and allocating fresh `List<Collider>` + `Collider[]` every call. Replaced with `Physics.OverlapBoxNonAlloc` against a static shared `Collider[128]` buffer + reused scratch `List<Collider>` member. Added per-action 0.5 s TTL cache on the result — cleared in `Exit()` so a new action invocation always starts cold; cache invalidated if cached item was picked up by another worker. Saturation warning if buffer fills (rule #31). Pattern now canonical for any GOAP action whose `IsValid` does heavy `Physics.Overlap*` work — see [[performance-conventions]] Pattern 6 + Pattern 4. **`GoapAction` instance pooling (Tier 4 Dₐ) explicitly NOT shipped** — the `JobLogisticsManager.cs:173-178` comment block memorializes a prior regression where this exact change broke shop ordering (`_isComplete=true` leaked across plans, only first BuyOrder placed); revisit only with profiler evidence. — claude
- 2026-04-19 — Initial pass. — Claude / [[kevin]]
- 2026-04-24 — Host-only perf fix: `Replan()` now honours `_planReevaluationInterval` (was dead code); `GoapPlanner` recursion uses a shared `HashSet<GoapAction>` with backtracking instead of per-level `.Where().ToList()`; Debug logs gated behind `GoapPlanner.VerboseLogging`; `BuildingManager.FindAvailableJob` replaces `OrderBy(Random.value)` shuffle with random-start iteration; `UpdateWorldState()` caches `FindAvailableJob` result across the two sensor calls in a single Replan. — claude
- 2026-04-24 — Regression fix: `CancelPlan()` no longer resets `_lastReplanAttemptTime` (was defeating the throttle because `BTAction_ExecuteGoapPlan.OnExit` calls Cancel every failed tick); added explicit `ForceReplanNextTick()` for intent-driven transitions. Restored `AddComponent` fallback in `BTAction_ExecuteGoapPlan.OnEnter` (removing it caused `Debug.LogError` spam every 0.1s per NPC on prefabs without a `GOAPController` child — `Character_Default_Humanoid/Quadruped`, `Character_Animal` — which refilled the Windows console and re-created the original progressive-freeze symptom). — claude
- 2026-04-24 — Log-spam purge + planner allocation elimination: (a) `CharacterGoapController` `_debugLog` now only logs when a real (non-throttled) Replan runs, not on throttled returns (was 20 lines/sec/NPC when enabled). (b) Introduced `Assets/Scripts/AI/NPCDebug.cs` with four domain flags (`VerbosePlanning`, `VerboseJobs`, `VerboseActions`, `VerboseMovement`, all default off) and gated every per-tick `Debug.Log` in `JobLogisticsManager`, `JobTransporter`, `JobHarvester`, `GoapAction_HarvestResources`, `GoapAction_LocateItem` behind them. (c) `BTAction_Work` and `BTAction_PunchOut` added `_warnedNoInteractable` one-shot flags mirroring the existing `_warnedNoTimeClock` pattern. (d) `GoapPlanner.BuildGraph` now mutates a single shared `_scratchState` dictionary with a pooled journal-based undo — eliminates the per-node `new Dictionary<string, bool>(parent.State)` allocation (previously thousands per Plan call, the dominant GC source). Removed `PlanNode.State` since reconstruction only needs `Parent` + `Action`. — claude
- 2026-04-25 — Residual progressive-lag hunt (second pass). Symptom report: lag only kicked in when a boss + workers were assigned to commercial buildings — confirming the accumulator was on the worker Job-tick path. Fixes: (a) `BuildingTaskManager` rewritten to eliminate LINQ on hot paths — `.OfType<T>()`, `.Any(...)`, `.RemoveAll(predicate)` all replaced by manual indexed loops in `ClaimBestTask`, `HasAvailableOrClaimedTask`, `HasAnyTaskOfType`, `RegisterTask`, `UnregisterTaskByTarget`, `ClearAvailableTasksOfType` (each call previously allocated 1–3 closures + enumerator wrappers; `ClaimBestTask` runs per worker per GOAP plan cycle). Also gated all five per-event `Debug.Log` calls (task registered / claimed / unclaimed / invalid-unclaim / completed) behind `NPCDebug.VerboseJobs`. (b) `JobLogisticsManager.PlanNextActions`, `JobHarvester.PlanNextActions`, `JobTransporter.PlanNextActions` now reuse `_scratchWorldState` dict + cached `GoapGoal` instances (DesiredState dicts are constant) + a persistent `_availableActions` list that's `Clear()`ed + repopulated each call. Eliminates per-tick allocation of a world-state dict, 2 goals with their own dicts, a list wrapper, plus `.Where().ToList()` in the LogisticsManager case. Action instances themselves still get recreated each plan (they're stateful — `_currentTarget`, `_isComplete`, phase fields). (c) Gated the remaining state-transition `Debug.Log` calls in `GoapAction_DepositResources` and `GoapAction_HarvestResources` behind `NPCDebug.VerboseActions`. (d) `CommercialBuilding.HandleQuestStateChanged` now auto-unsubscribes from `quest.OnStateChanged` once the quest reaches a terminal state (Completed / Abandoned / Expired). Without this, each published task/order left a delegate reference pointing back at the building, anchoring the quest object against GC; over a long worker shift with hundreds of quests published, this was a slow-but-steady memory+iteration accumulator that compounded with the hot-path allocations. — claude

## Sources
- [[performance-conventions]] — Pattern 4 (NonAlloc) + Pattern 6 (per-action TTL cache) extracted from this system.
- [[optimisation-backlog]] — Tier 3 Bₐ measurements + Tier 4 Dₐ rationale (GoapAction pooling NOT shipped).
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md)
- [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs).
- [[ai]] parent.
