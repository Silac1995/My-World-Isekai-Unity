---
type: gotcha
title: "Chain-action IsValid must NOT pre-filter by carry state"
tags: [goap, jobs, ai, isvalid, planner]
created: 2026-05-02
updated: 2026-05-02
sources:
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PlantCrop.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_WaterCrop.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_ReturnToolToStorage.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_ReturnToolToStorage.cs)"
  - "[Assets/Scripts/AI/GOAP/GoapPlanner.cs](../../Assets/Scripts/AI/GOAP/GoapPlanner.cs)"
  - "Commit f35e3e2c — fix(goap): chain-action IsValid no longer pre-filters by hand contents"
related:
  - "[[ai-goap]]"
  - "[[job-farmer]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
status: mitigated
confidence: high
---

# Chain-action IsValid must NOT pre-filter by carry state

## Summary
GOAP actions whose preconditions express a chain (e.g. `PlantCrop` requires `hasSeedInHand=true`, supplied by `FetchSeed`'s effect) MUST keep `IsValid` minimal — only invariants the planner cannot deduce from preconditions. If `IsValid` re-checks the precondition itself (e.g. asserts seed-in-hand), Job-side pre-filtering filters the action out before the planner ever sees it as a candidate, and the chain `Fetch → Consume` becomes unbuildable. Result: no plan forms, the worker falls through to Idle even when goal + tasks both exist.

## Symptom
- A `Job` reports a goal (`PlantEmptyCells`, `WaterDryCells`, etc.) but `_currentAction` ping-pongs to `Planning / Idle`.
- `JobFarmer`'s NO-PLAN diagnostic dump prints `validActions=[FetchSeed,IdleInBuilding]` — `PlantCrop` is missing.
- The worker holds nothing, repeatedly walks to the storage chest, fetches a seed, then drops it back / gets stuck — the planner can't bridge `FetchSeed` to anything because `PlantCrop` was never in the candidate set.

## Root cause
`Job.PlanNextActions` (canonical: `JobHarvester`, `JobTransporter`, `JobLogisticsManager`, `JobFarmer`) builds a `_scratchValidActions` list by walking `_availableActions` and keeping only those whose `IsValid(worker) == true`. `GoapPlanner.Plan` does NOT call `IsValid` itself (this is intentional — without the pre-filter, identical-prec/effect actions with different `Cost` cause infinite-replan loops). So if a chain consumer like `PlantCrop` returns `false` from `IsValid` because the worker isn't yet carrying a seed (which is true at the START of every plan tick before `FetchSeed` has run), the planner never sees `PlantCrop` as a candidate and the `FetchSeed → PlantCrop` chain cannot be built.

The planner's job is to build the chain by walking `Effects → Preconditions`. The Job-side pre-filter exists to drop actions that are structurally inapplicable (no workplace, no task, no hands controller). Carry state is precondition-domain — the planner uses it to pick which action to chain in.

## How to avoid
**Chain-action `IsValid` checks only invariants the planner cannot deduce from preconditions:**

- Workplace exists / TaskManager is non-null.
- At least one task of the relevant type exists in `Available` + `InProgress[claimed-by-me]`.
- Hands controller exists (only if the action touches hands).
- The cell / target hasn't been concurrently consumed (cross-actor race detection — see [[job-farmer]] §"Cross-actor race detection").

**NEVER:**

- Re-check `hands.CarriedItem.ItemSO is SeedSO` in `PlantCrop.IsValid`.
- Re-check `hands.CarriedItem.ItemSO == _wateringCanItem` in `WaterCrop.IsValid` or `ReturnToolToStorage.IsValid`.
- Re-check anything that's already declared in `Preconditions`.

**Pickup/Fetch actions DO correctly require hands free in `IsValid`** — there's no precondition-driven path that could free the hand mid-plan, so it's a legitimate invariant.

## How to fix (if already hit)

Diff a working chain consumer (e.g. `GoapAction_PlantCrop` post-fix) against your action's `IsValid`. Strip every check that's already encoded in `Preconditions`. Move the carry-state check to:

1. **`Preconditions`** — the planner uses it to chain (e.g. `Preconditions["hasSeedInHand"] = true`).
2. **`Execute` body** — Re-check at execution time as a runtime safety net (the worker's hand could change between plan-time and execute-time). On mismatch, set `_isComplete = true` and let the next tick replan.

Run `JobFarmer.PlanNextActions` and check the NO-PLAN dump's `validActions` list — the chain consumer should now appear in the candidate set even when the worker has empty hands.

## Affected systems
- [[ai-goap]]
- [[job-farmer]]
- [[ai-actions]]

## Links
- [[worldstate-predicate-action-isvalid-divergence]] — sibling pitfall on the worldState side.
- [[ai-goap]] §planner discipline.

## Sources
- 2026-05-02 conversation with [[kevin]] surfacing the symptom on JobFarmer.
- Commit `f35e3e2c` — fix(goap): chain-action IsValid no longer pre-filters by hand contents.
- Commits `52949ecf` (FetchSeed.IsValid widened), `da260bcc` (PlantCrop / WaterCrop work radius + softlock), `1cb1b13d` (storage softlock), `d95dd4db` (ReturnTool softlock).
