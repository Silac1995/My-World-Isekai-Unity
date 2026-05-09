---
type: gotcha
title: "worldState predicate must mirror action IsValid filter"
tags: [goap, jobs, ai, isvalid, planner, worldstate]
created: 2026-05-02
updated: 2026-05-02
sources:
  - "[Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobFarmer.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_HarvestResources.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_HarvestResources.cs)"
  - "Commit 85e7af59 — fix(farmer): worldState/IsValid predicate alignment + arrived-but-stuck harvest guard"
related:
  - "[[ai-goap]]"
  - "[[job-farmer]]"
  - "[[chain-action-isvalid-pre-filter]]"
  - "[[ai-pathing]]"
status: mitigated
confidence: high
---

# worldState predicate must mirror action IsValid filter

## Summary
When a `Job` builds a `_scratchWorldState[key]` that the goal cascade reads to pick a goal (e.g. `JobFarmer` does `if (hasUnfilledHarvestTask) → HarvestGoal`), the predicate computing `key` MUST mirror the consuming GOAP action's `IsValid` filter exactly — including blacklist filters, yield-match filters, and any other gate the action applies. If the worldState predicate is more permissive than the action's `IsValid`, the cascade picks a goal whose plan can't form (the action is filtered out of `_scratchValidActions` by its own `IsValid`), and the worker freezes on that goal forever.

## Symptom
- Worker is on shift, building has tasks, but worker is doing nothing.
- Job debug shows `Job Goal: HarvestMatureCells, Action: Planning / Idle`.
- `JobFarmer`'s NO-PLAN diagnostic dump prints `worldState: hasUnfilledHarvestTask=True, ...` but the dump for `GoapAction_HarvestResources.IsValid` REJECTED prints `blacklisted=N` or `noYieldMatch=N` — every harvest task in the manager has been rejected for *this* worker.
- Idle dump shows `avail.Harvest=N (canHarvestNow=0)` — tasks exist but none are valid for this worker.

## Root cause
`GoapPlanner.Plan` does not call `IsValid` itself (intentional — see [[chain-action-isvalid-pre-filter]] for the rationale). `Job.PlanNextActions` pre-filters `_availableActions` through `IsValid` BEFORE calling `GoapPlanner.Plan`. So:

1. `worldState` predicate says `hasUnfilledHarvestTask = true` because *some* `HarvestResourceTask` exists.
2. Cascade picks `HarvestGoal` (goal: `hasDepositedResources=true`).
3. Pre-filter walks `_availableActions`. `GoapAction_HarvestResources.IsValid` calls `ClaimBestTask<HarvestResourceTask>(worker, predicate)` with the same blacklist + yield-match filter that the worldState predicate SHOULD have used. Every task is rejected. `IsValid` returns false. Action is dropped from `_scratchValidActions`.
4. `GoapPlanner.Plan` runs without `HarvestResources` in the candidate set. No action's `Effects` produce `hasDepositedResources=true` (the goal). No plan forms.
5. Worker stays on `goal=HarvestGoal, action=Planning/Idle`.

The bug is the **predicate divergence** between the worldState computation and the action's `IsValid`. They MUST agree on the same definition of "is there at least one actionable task for this worker".

## How to avoid
When you write a `worldState` predicate that mirrors task availability for a consuming GOAP action, copy the action's `IsValid` filter line-by-line:

```csharp
// In Job.PlanNextActions:
var farmWanted = farm.GetWantedItems();
bool hasUnfilledHarvestTask = farm.TaskManager.HasAvailableOrClaimedTask<HarvestResourceTask>(_worker, task =>
{
    var h = task.Target as Harvestable;
    if (h == null) return false;
    if (_worker.PathingMemory != null && _worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) return false;
    return farmWanted != null && farmWanted.Count > 0 && h.HasAnyYieldOutput(farmWanted);
});

// In GoapAction_HarvestResources.IsValid (consuming action):
var task = building.TaskManager.ClaimBestTask<HarvestResourceTask>(worker, t =>
{
    var h = t.Target as Harvestable;
    if (h == null) return false;
    if (worker.PathingMemory != null && worker.PathingMemory.IsBlacklisted(h.gameObject.GetInstanceID())) return false;
    return wanted != null && wanted.Count > 0 && h.HasAnyYieldOutput(wanted);
});
```

The two predicate lambdas should be byte-identical. If they have to evolve, evolve them together. Co-locate them in code review — change one without changing the other and the bug returns.

**`HasAvailableOrClaimedTask<T>(worker, predicate)`** is the idiom on `BuildingTaskManager` that walks BOTH `_availableTasks` AND `_inProgressTasks[claimed-by-this-worker]` AND applies the predicate. Use it; the plain `HasAvailableTask<T>` only walks `_availableTasks` and misses tasks moved to InProgress by auto-claim.

## How to fix (if already hit)
1. Run the Job and capture the NO-PLAN diagnostic dump.
2. Compare `worldState[<key>]` against the consuming action's `IsValid` REJECTED dump.
3. The divergence is the bug. Copy the action's `IsValid` filter into the worldState predicate (as a `predicate` arg to `HasAvailableOrClaimedTask<T>`). Make them mirror exactly.
4. Re-run. The cascade should now pick the goal only when a plan can actually form, OR fall through to the next-priority goal.

## Affected systems
- [[ai-goap]]
- [[job-farmer]]
- [[ai-pathing]] — `PathingMemory` blacklist is the canonical filter that diverged in the original incident.

## Links
- [[chain-action-isvalid-pre-filter]] — sibling pitfall on the action's `IsValid` side.
- [[ai-goap]] §planner discipline.

## Sources
- 2026-05-02 conversation with [[kevin]] — symptom on JobFarmer where blacklisted harvest tasks left the worker frozen on `HarvestMatureCells`.
- Commit `85e7af59` — fix(farmer): worldState/IsValid predicate alignment + arrived-but-stuck harvest guard.
- Commit `2b9df2d1` — diag: HarvestResources.IsValid prints task-rejection breakdown when predicate filters all (the diagnostic that caught this).
