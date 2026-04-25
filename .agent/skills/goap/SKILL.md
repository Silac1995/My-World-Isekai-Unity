---
name: goap
description: Rules for using GOAP to dictate the daily life and ultimate goals of an NPC (e.g., starting a family).
---

# GOAP System

This skill defines how to use and extend the GOAP (Goal-Oriented Action Planning) system. 
**Important:** The GOAP in this project is not just a job queue manager (e.g., Harvester). It is designed to be the orchestrator of the NPC's **daily life**, based on **ultimate goals**.

## Global Concept: GOAP as a "Life Manager"
GOAP gives the NPC a global and organic direction. Rather than saying "Go chop wood," we give the NPC a life Goal. 

Examples of ultimate goals:
- **Starting a family**: The GOAP Planner will prioritize daily life actions leading to this goal (Talking with NPCs, targeting the opposite sex, flirting, marrying, having children).
- **Being the best martial artist**: The Planner will chain training actions (finding a dojo, fighting opponents, improving on a CombatSO).
- **Financial ambition**: Amassing wealth (which will push them to find a job like Harvester and deposit resources).

The GOAP manages the long/medium-term plan (Needs, Jobs, Socializing), while the Behaviour Tree (BT) handles short-term survival (reacting to aggression, fleeing, executing schedules).

## When to use this skill
- To design a new **Life Goal** or new daily action chains (e.g., a seduction cycle, a martial training cycle).
- When creating a `GoapAction` that advances the NPC's personal story or logic (e.g., `GoapAction_Socialize`, `GoapAction_PlaceOrder`).
- When defining purely functional job logistics loops that require decoupling state from strict FSM execution (e.g., `GoapAction_LoadTransport`, `GoapAction_UnloadTransport` used natively inside `JobTransporter.Execute()`).
- To structure the preconditions and effects linking the NPC's social life, jobs, and foundational Needs.

## How to use it

### 1. Create an Ultimate Goal (`GoapGoal`)
A GOAP Goal defines the absolute state the NPC wants to reach in a part of their life.
- `GoalName`: Name of the goal to accomplish (e.g., "StartAFamily").
- `DesiredState`: Dictionary of boolean states describing the success of the goal.
    - Ex: `new Dictionary<string, bool> { { "hasChildren", true } }`
- `Priority`: Priority level of the goal (allows the NPC to choose between training or developing their social circle).

### 2. Define a Life Action (`GoapAction`)
The action is the basic building block of everyday life.
- `ActionName`: String identifying the action.
- `Preconditions`: The necessary state to launch the action.
    - Ex: to have a child, the precondition might be `{"isMarried", true}`.
- `Effects`: The resulting state of the action.
    - Ex: having a child gives the condition `{"hasChildren", true}`.
- `Cost`: The "tediousness" or difficulty of the process (the Planner chooses the least costly route). This cost can vary according to the NPC's "traits"!
- `IsValid`, `Execute`, `IsComplete`, `Exit`: Functions controlling the action frame by frame.

### 4. Injecting Needs into GOAP (SOLID Architecture)
---
name: goap
description: Rules for using GOAP to dictate the daily life and ultimate goals of an NPC (e.g., starting a family).
---

# GOAP System

This skill defines how to use and extend the GOAP (Goal-Oriented Action Planning) system. 
**Important:** The GOAP in this project is not just a job queue manager (e.g., Harvester). It is designed to be the orchestrator of the NPC's **daily life**, based on **ultimate goals**.

## Global Concept: GOAP as a "Life Manager"
GOAP gives the NPC a global and organic direction. Rather than saying "Go chop wood," we give the NPC a life Goal. 

Examples of ultimate goals:
- **Starting a family**: The GOAP Planner will prioritize daily life actions leading to this goal (Talking with NPCs, targeting the opposite sex, flirting, marrying, having children).
- **Being the best martial artist**: The Planner will chain training actions (finding a dojo, fighting opponents, improving on a CombatSO).
- **Financial ambition**: Amassing wealth (which will push them to find a job like Harvester and deposit resources).

The GOAP manages the long/medium-term plan (Needs, Jobs, Socializing), while the Behaviour Tree (BT) handles short-term survival (reacting to aggression, fleeing, executing schedules).

## When to use this skill
- To design a new **Life Goal** or new daily action chains (e.g., a seduction cycle, a martial training cycle).
- When creating a `GoapAction` that advances the NPC's personal story or logic (e.g., `GoapAction_Socialize`, `GoapAction_PlaceOrder`).
- When defining purely functional job logistics loops that require decoupling state from strict FSM execution (e.g., `GoapAction_LoadTransport`, `GoapAction_UnloadTransport` used natively inside `JobTransporter.Execute()`).
- To structure the preconditions and effects linking the NPC's social life, jobs, and foundational Needs.

## How to use it

### 1. Create an Ultimate Goal (`GoapGoal`)
A GOAP Goal defines the absolute state the NPC wants to reach in a part of their life.
- `GoalName`: Name of the goal to accomplish (e.g., "StartAFamily").
- `DesiredState`: Dictionary of boolean states describing the success of the goal.
    - Ex: `new Dictionary<string, bool> { { "hasChildren", true } }`
- `Priority`: Priority level of the goal (allows the NPC to choose between training or developing their social circle).

### 2. Define a Life Action (`GoapAction`)
The action is the basic building block of everyday life.
- `ActionName`: String identifying the action.
- `Preconditions`: The necessary state to launch the action.
    - Ex: to have a child, the precondition might be `{"isMarried", true}`.
- `Effects`: The resulting state of the action.
    - Ex: having a child gives the condition `{"hasChildren", true}`.
- `Cost`: The "tediousness" or difficulty of the process (the Planner chooses the least costly route). This cost can vary according to the NPC's "traits"!
- `IsValid`, `Execute`, `IsComplete`, `Exit`: Functions controlling the action frame by frame.

### 4. Injecting Needs into GOAP (SOLID Architecture)
The `CharacterGoapController` does not directly manage or hardcode specific needs (e.g., jobs, socializing). Instead, the system uses **Dependency Inversion**:
- The `CharacterNeeds` system acts as a **Data Provider**.
- Each `CharacterNeed` subclass (like `NeedJob` or `NeedSocial`) implements `GetGoapGoal()` and `GetGoapActions()`.
- During `Replan()`, the `CharacterGoapController` iterates through all active needs and dynamically loads their goals and actions directly into the planner.
- **Rule:** Never execute logic inside `CharacterNeed`. Needs are exclusively read-only state sensors that provide GOAP goals via the `GetGoapGoal()` method.

### 5. GOAP & Interaction Synchronization (Critical Rule)
When a `GoapAction` triggers a `CharacterInteraction` (e.g., asking for a job, passing an order):
1. **Wait for Interaction**: The GOAP Action **must not complete** (`_isComplete = true`) immediately after starting the interaction. It must explicitly remain running (`return;`) as long as `worker.CharacterInteraction.IsInteracting` is true. Failing to do so causes the Behaviour Tree to override the interaction's movement, causing the NPC to walk away mid-conversation.
2. **Handle Failure Gracefully**: If the interaction **fails to start** (e.g., the target is busy), the GOAP Action should cleanly abort (`_isComplete = true` or `Exit()`). **It must not assume the underlying task succeeded.** State tracking (such as an `IsPlaced` boolean for commercial orders) must be managed independently by the parent system (like `JobLogisticsManager`) so failed actions can be safely retried later without losing the data.

### 6. Critical Job Transitions & Action Lifecycle
When writing `GoapAction`s or Behaviour Tree wrappers (like `BTAction_PunchOut`), you must **strictly guarantee** that any physical `CharacterAction` triggered on the character is properly cleaned up if the logic branch abruptly changes.
- **Dangling Actions:** If a BT node triggers a visual animation (e.g., `CharacterActions.ExecuteAction(new Action_PunchOut(...))`) and then the node is aborted (because the 5:00 PM shift ended), the physical `CharacterAction` will keep running internally, permanently blocking all future actions (like `CharacterPickUpItem`). You must explicitly call `self.CharacterActions.ClearCurrentAction()` in the `OnExit()` of the BT node to shatter the lock.
- **Physical Possession:** For jobs involving item transport (`GoapAction_UnloadTransport`), never blindly trigger drop actions or award delivery progress. Always forcefully assert `hands.AreHandsFree() == false` or `inventory.HasItem(...)` immediately prior. Otherwise, rapid GOAP loops can generate "Ghost Drops" that fulfill targets with thin air.
- **Interaction Bounds Validation:** When navigating to a `WorldItem` for interaction, do **not** use `bounds.ClosestPoint(...)` for the destination. The `NavMeshAgent` deceleration will stop the agent just short of the bounds, causing `bounds.Intersects(...)` to randomly flap and the character to stutter endlessly. Always sequence the destination directly to the center (`targetPos`) of the item so the agent naturally deeply pierces the interaction boundary as it slows to a halt.

### 7. Strict Architectural Rules
- **Interaction Distance**: To interact with an object or get in range, **always** use the `InteractionZone` (its colliders or explicit properties).
- **Physical Destruction**: When picking up an item from the scene/world, you must **always destroy it IN THE `Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs`**. NOWHERE ELSE. Never call Destroy manually from GOAP actions.
- **Spawning Rules**: To SPAWN an item in the world through `Assets/Scripts/Item/WorldItem.cs`:
    - If it's an existing item, use the methods in `Assets/Scripts/Item/ItemInstance.cs` to keep the ItemInstance parameters intact.
### 8. Handling Unreachable Targets (Pathing Memory)
To prevent infinite planner loops where an NPC tries endlessly to interact with a physically unreachable target, you must adhere strictly to the **Target Blacklisting** mechanics in the `pathing-system` skill.
- **Fail Fast:** In custom movement loops (e.g. `GoapAction_GatherStorageItems`), use `NavMeshUtility.HasPathFailed()`. If it fails, call `worker.PathingMemory.RecordFailure(targetId)`. If blacklisted, aggressively abort the action (`_isComplete = true`) so the Planner re-evaluates.
- **Filter Targets:** When locating `WorldItem`s or `Characters` using Physics overlaps, ignore any target where `worker.PathingMemory.IsBlacklisted(targetId)` is true.

### 8.5 Performance: Replan Throttle & Non-Allocating Planner (Host-Critical)

GOAP runs **server-authoritative** from `NPCBehaviourTree.Update()` (gated by `if (!IsServer) return;`) every `_tickIntervalSeconds` (default 0.1s). That is the single biggest source of host-only CPU cost. Two rules keep it bounded:

1. **`CharacterGoapController.Replan()` is throttled** by `_planReevaluationInterval` (default `2f` seconds). If the previous attempt ran within that window, the call returns early (`_currentAction != null`) without touching `BuildingManager`, `CharacterNeeds`, or `GoapPlanner`. Without this guard, a jobless NPC calls Replan ~20×/sec because the BT re-enters the GOAP branch each tick and fires `OnEnter` → `Replan` → fail → Wander → re-enter. That compounds with `B` buildings and `N` NPCs into `O(N · B · log B · 20)` per second.
   - **CRITICAL: `CancelPlan()` does NOT reset `_lastReplanAttemptTime`.** `BTAction_ExecuteGoapPlan.OnExit` invokes `CancelPlan()` every failed tick; if Cancel reset the throttle, the whole guard would be defeated (tested — progressive host freeze returns). Keep the throttle state orthogonal to plan state.
   - For rare intent-driven transitions that genuinely need an immediate replan (combat-end heuristics, NPC revival, dialogue-triggered goal switch), call `ForceReplanNextTick()` explicitly — never from internal BT OnExit paths.
   - `UpdateWorldState()` caches the result of `BuildingManager.FindAvailableJob<Job>(true)` via `GetCachedVacantJobBuilding()` so `CheckForJobKnowledge()` and `CheckAtBossLocation()` share a single O(B) scan per Replan instead of doing it twice.

2. **`GoapPlanner.Plan()` is allocation-minimised**:
   - Recursion uses a shared `_usedActions` `HashSet<GoapAction>` with **backtracking** (Add before recursing, Remove after). This replaces the previous `availableActions.Where(a => a != action).ToList()` which allocated a new filtered list at every recursive node (worst case `O(actions^depth)` lists per Plan).
   - Recursion also uses a shared `_scratchState` `Dictionary<string, bool>` mutated in place with a journal-based undo (one pooled `List<StateRestoreEntry>` per stack frame). This replaces the previous `new Dictionary<string, bool>(parent.State)` copy inside `GoapAction.ApplyEffects` which allocated a fresh dictionary at every node — the dominant per-Plan GC pressure. `PlanNode.State` was removed because reconstruction only walks `Parent` + `Action`.
   - Cheapest-leaf selection is a linear scan instead of `leaves.OrderBy(n => n.RunningCost).First()` (avoids a LINQ iterator).
   - **Debug logs are gated behind `GoapPlanner.VerboseLogging = false`** (static, default off). On Windows, the Unity console slows progressively as entries accumulate — 50 jobless NPCs × 20 failed plans/sec fills the buffer within minutes. Only flip the static on when actively diagnosing a planner issue.
   - `GoapAction.ApplyEffects` still exists (unchanged signature) for any external callers, but the planner no longer calls it — the planner inlines the mutate+restore journal directly against `_scratchState`.

3. **`BuildingManager.FindAvailableJob<T>()`** iterates from a random start index (`UnityEngine.Random.Range(0, count)`) instead of `allBuildings.OrderBy(b => Random.value)`. Same "don't flock to the same boss first" property, zero LINQ allocation, O(B) worst case instead of O(B log B).

4. **`BTAction_ExecuteGoapPlan.OnEnter` resolver is tolerant** — it tries `GetComponentInChildren<CharacterGoapController>()` first (honors the Character Facade convention if a child `GOAPController` GameObject exists, as in `Character_Default.prefab`), then silently falls back to `AddComponent<CharacterGoapController>()` on the root if no prefab instance exists. **The root prefabs in use (`Character_Default_Humanoid`, `Character_Default_Quadruped`, `Character_Animal`) do not have the child** — NPCs spawned from them get a root-added component via `CharacterSystem.OnEnable` auto-registration. Never add a `Debug.LogError` in the not-found branch: it would fire every 0.1s per NPC and refill the Windows console buffer, re-creating the progressive host freeze.

5. **Per-tick Debug.Log discipline (BT/Job/GoapAction hot paths)** — anything reachable from `NPCBehaviourTree.Update`, `Job.Execute`, or `GoapAction.Execute` that logs must be gated behind a compile-time or runtime flag, **or** rate-limited with a last-log-time field. Flags live in `Assets/Scripts/AI/NPCDebug.cs`: `NPCDebug.VerbosePlanning` (GOAP replans), `NPCDebug.VerboseJobs` (Job.Execute transitions **and** BuildingTaskManager claim/unclaim/complete events), `NPCDebug.VerboseActions` (GoapAction.Execute progress, incl. deposit/harvest state transitions), `NPCDebug.VerboseMovement` (pathfinding probes). All default `false`. State-transition one-shots (e.g. "assigned as X", "shift ended") stay unconditional — they only fire on state change. The failure mode to avoid is unconditional `Debug.Log` inside `if (target == null)` / `if (!IsValid)` / "waiting for …" branches, which become per-tick spam the moment the NPC is stuck in that state.

6. **Per-tick allocation discipline on worker Job ticks** — `Job.Execute` runs at 10Hz per worker on the server. Every `new Dictionary<string, bool>`, `new List<GoapAction>`, `new GoapGoal`, `.Where(...).ToList()`, `.OfType<T>()`, `.RemoveAll(predicate)`, or string interpolation inside the tick is a per-tick GC allocation that compounds with N workers. The canonical pattern for `JobX.PlanNextActions`:
   - Declare `private readonly Dictionary<string, bool> _scratchWorldState = new(...);` — `Clear()` and repopulate each call.
   - Cache each `GoapGoal` in a `private GoapGoal _cachedXxxGoal;` field (their `DesiredState` dicts are constant — build once, reuse forever).
   - Cache the `List<GoapAction>` wrapper in a field; `Clear()` + `Add` each tick if instances must be recreated (they're stateful per-plan), skip rebuild entirely if stateless.
   - Filter-in-place with an indexed `for` loop into a pre-allocated scratch list — don't use `.Where(...).ToList()`.

   Same discipline applies to `BuildingTaskManager`'s LINQ-heavy methods (`ClaimBestTask`, `HasAvailableOrClaimedTask`, `HasAnyTaskOfType`, `UnregisterTaskByTarget`, `RegisterTask`) — they run per worker per plan cycle and were rewritten to manual loops for the same reason. See the 2026-04-25 change-log entry in [[ai-goap]] for the concrete pattern.

7. **Publisher→subscriber cleanup on terminal states** — when something subscribes to a long-lived event on a quest/order/task (`IQuest.OnStateChanged`, `OnProgressRecorded`), the subscription must be removed when the quest reaches `Completed`/`Abandoned`/`Expired`. The canonical pattern (`CommercialBuilding.HandleQuestStateChanged`): self-unsubscribe inside the handler once the state is terminal. Without this, the delegate reference anchors the quest against GC for the rest of the session — slow accumulation that compounds with N quests × long sessions.

**Non-reentrancy assumption:** `GoapPlanner._usedActions` is a single static scratch buffer. It's safe because `Plan()` runs on the server main thread and is not reentrant (each NPC's Replan finishes before the next NPC's Replan starts, and no GoapAction inside Plan calls Plan recursively). If you ever add parallel/async GOAP planning, give each worker its own buffer.

### 9. Anti-Patterns & Safety Guidelines
- **The "False Success" Anti-Pattern:** When an execution error or race condition occurs inside a `GoapAction` (e.g. an item is destroyed by another NPC first), **never** fallback to setting `_isComplete = true` to manually bypass the loop! Doing so falsely tricks the parent GOAP plan into believing the step succeeded, and it will forcefully pop the action and pass corrupted context (like empty hands) to the next sequential action (like `MoveToDestination`). Instead, nullify the target and let `IsValid()` organically fail on the next tick to trigger a clean `replanification`. 
- **The Self-Sabotage Race Condition:** When a `GoapAction` executes a physical `CharacterAction` (e.g. `CharacterPickUpItem`), be hyper-aware that the physical action might immediately assert locks on the object (e.g. `IsBeingCarried = true`) while the animation plays over several frames. The `GoapAction` must strictly wrap its external race-condition assertions inside `if (!_isActionStarted)` to guarantee it does not mistakenly read its *own* physical lock on the next frame and violently cancel itself!
- **The Single-Frame Action Rejection:** Never iterate through an array (like an `Inventory.ItemSlots`) and trigger multiple physical `CharacterAction`s (like `CharacterDropItem`) synchronously in the same frame/tick. The core `CharacterActions` system strictly prevents instantaneous overlap; only the first loop iteration will succeed, while all subsequent `.ExecuteAction(...)` calls are immediately rejected because the animator is now locked for the duration of the first 0.5s animation. Always engineer a sequential state-machine pattern (e.g. `ProcessSequentialDeposit(...)`) that yields execution to the active `CurrentAction` between elements.
