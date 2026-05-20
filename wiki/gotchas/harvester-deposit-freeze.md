---
type: gotcha
title: "Harvester freezes after pickup when no actionable work remains"
tags: [goap, jobs, ai, harvester, lumberyard, planner, worldstate]
created: 2026-05-19
updated: 2026-05-19
sources:
  - "[Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupLooseItem.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupLooseItem.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_HarvestResources.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_HarvestResources.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_DestroyHarvestable.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_DestroyHarvestable.cs)"
  - "2026-05-19 conversation with [[kevin]] — lumberyard harvester froze after picking up wood instead of depositing"
related:
  - "[[ai-goap]]"
  - "[[jobs-and-logistics]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
  - "[[chain-action-isvalid-pre-filter]]"
status: mitigated
confidence: high
---

# Harvester freezes after pickup when no actionable work remains

## Summary
`JobHarvester.PlanNextActions` uses a "lie to the planner" trick — when the worker has resources but the bag still has free space, it sets `hasResources=false` in the world state so the planner keeps chaining `Harvest → Pickup` instead of detouring to `Deposit`. The lie is only safe when there is **actually** something left to gather. Without an `(canHarvest || looseItemExists)` guard, a worker who just picked up wood from the **only** available destroy/harvest target stays in "keep gathering" mode forever — the planner cannot extend any chain to the `hasDepositedResources=true` goal, returns a null plan, and the worker freezes mid-zone holding the wood.

## Symptom
- A lumberyard / mine / forager harvester walks to a tree, destroys it (or harvests it), picks up the dropped wood (or ore / herb) — then **stops moving**.
- The worker stands in the harvest zone with the item in hand or bag. No path. No animation. No deposit.
- `JobHarvester` debug surface shows `goal=HarvestAndDeposit`, `action=Planning / Idle`.
- `JobHarvester.VerboseJobs` log (if enabled) says `impossible de planifier` once and then nothing.
- Happens specifically when (a) the harvest scan area contains a small number of trees, or (b) other harvesters claimed the remaining ones, or (c) the worker destroyed the last tree before re-planning.

## Root cause
`JobHarvester.PlanNextActions` was building the world state in this order:

1. Compute `hasResourcesForGoap`. Heuristic: if worker has an item AND bag still has space AND the building has a harvest zone AND the building still needs work → `hasResourcesForGoap = false` (lie: keep gathering).
2. **Then** compute `canHarvest` / `looseItemExists` from the `BuildingTaskManager`.
3. Push both into `_scratchWorldState` and call `GoapPlanner.Plan`.

The lie in step 1 was unconditional on `canHarvest` / `looseItemExists`. So in the post-pickup-of-last-tree scenario:

```
_scratchWorldState = {
    hasHarvestZone   = false   // no valid HarvestResourceTask / DestroyHarvestableTask
    looseItemExists  = false   // PickupLooseItemTask already completed
    hasResources     = false   // LIE: actually true, but we lied to keep gathering
    hasDepositedResources = false
    needsToWork      = true
    isIdling         = false
}
goal = hasDepositedResources = true
```

The planner has 6 candidate actions; the relevant chain links:

| Action | Precondition | Effect |
|--------|--------------|--------|
| `GoapAction_DepositResources` | `hasResources=true` | `hasDepositedResources=true` |
| `GoapAction_PickupLooseItem`  | `looseItemExists=true`, `hasResources=false` | `hasResources=true`, `looseItemExists=false` |
| `GoapAction_HarvestResources` | `hasHarvestZone=true`, `looseItemExists=false`, `hasResources=false` | `looseItemExists=true` |
| `GoapAction_DestroyHarvestable` | `hasHarvestZone=true`, `looseItemExists=false`, `hasResources=false` | `looseItemExists=true` |

Every chain to `hasDepositedResources=true` requires `hasResources=true`, which only `Pickup` produces. `Pickup` needs `looseItemExists=true`, which only `Harvest` / `Destroy` produce. Both need `hasHarvestZone=true`, which the world state says is `false`. No chain forms. **Returned plan is null. Worker stands still.**

`stuckWaitingForTrees` (which falls back to the `Idle` goal) explicitly excludes this case: it requires `!hasAtLeastOneResource`, and our worker DOES have a resource. So the worker is not even routed to `Idle` — they sit on `HarvestAndDeposit` with no plan.

This is a close cousin of [[worldstate-predicate-action-isvalid-divergence]] — same class of bug (world state lying about what the planner can extend), different shape (here the lie is intentional but missing a guard rather than a predicate mismatch).

## How to avoid
**Rule:** any "lie to the planner" pattern that keeps a flag `false` to force a longer chain MUST also gate on whether the longer chain is actually formable. Add the same task-availability check the chain's first action would do, on the planning side.

Fix shape applied 2026-05-19 to `JobHarvester.PlanNextActions`:

```csharp
// Move the task scan ABOVE hasResourcesForGoap so we can gate the lie.
bool canHarvest = /* HarvestResourceTask OR DestroyHarvestableTask available for this worker */;
bool looseItemExists = /* PickupLooseItemTask available for this worker */;

bool hasResourcesForGoap = false;
if (hasAtLeastOneResource)
{
    if (!hasFreeSpace)
    {
        hasResourcesForGoap = true; // full → deposit
    }
    else if (building.HasHarvestableZone && needsToWork && (canHarvest || looseItemExists))
    {
        hasResourcesForGoap = false; // lie: keep gathering — but ONLY when the chain can extend
    }
    else
    {
        hasResourcesForGoap = true;  // nothing more actionable → deposit what we have
    }
}
```

The new `(canHarvest || looseItemExists)` clause makes the lie honest: it tells the planner "you have resources" the instant the gather chain dries up, so the planner pivots to `DepositResources`.

## How to fix (if already hit)
1. Confirm the symptom: harvester is idle in the harvest zone, holds an item, no path. Toggle `NPCDebug.VerboseJobs` if uncertain — log says `impossible de planifier`.
2. Inspect the world state at the freeze: `hasResources=false` while the worker visually carries an item is the smoking gun.
3. Apply the fix shape above. The change is local to `PlanNextActions`; no API change to GOAP actions.
4. Re-run. Harvester now walks to `DepositZone` after pickup and drops the wood when no more trees / pickup items remain.

## Affected systems
- [[jobs-and-logistics]] — `JobHarvester` and any future `Job` that uses a similar "lie to keep chaining" pattern.
- [[ai-goap]] — the planner is correct; the gotcha is on the world-state side.

## Links
- [[worldstate-predicate-action-isvalid-divergence]] — same family of bug, different shape (predicate divergence vs missing guard).
- [[chain-action-isvalid-pre-filter]] — sibling pitfall on the action's `IsValid` side.

## Sources
- 2026-05-19 conversation with [[kevin]] — lumberyard harvesters chopped a tree, picked up wood, then stopped moving instead of depositing.
- `JobHarvester.cs:200-240` (post-fix) — fix in place; comment block memorializes the regression.
