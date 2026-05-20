---
type: gotcha
title: "Harvester picks up a non-wanted loose item and freezes"
tags: [goap, jobs, ai, harvester, lumberyard, pickup, looseitem, planner]
created: 2026-05-20
updated: 2026-05-20
sources:
  - "[Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupLooseItem.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupLooseItem.cs)"
  - "[Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs)"
  - "2026-05-20 conversation with [[kevin]] — lumberyard harvester chopped apple tree, picked up sapling instead of wood, froze"
related:
  - "[[ai-goap]]"
  - "[[jobs-and-logistics]]"
  - "[[harvester-deposit-freeze]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
status: mitigated
confidence: high
---

# Harvester picks up a non-wanted loose item and freezes

## Summary
`CharacterActions.ApplyHarvestOnServer` and `ApplyDestroyOnServer` register a `PickupLooseItemTask` for **every** spawned `WorldItem`, regardless of whether the workplace's `_wantedResources` list contains the item. When a harvestable's outputs include items the workplace does NOT want (e.g. apple tree destruction drops wood + apple sapling; lumberyard only wants wood), the sapling task is registered alongside the wood task. `ClaimBestTask` picks the closest unclaimed task, and the sapling can win. Worker walks to sapling, picks it up, and on the next plan tick `hasAtLeastOneResource = false` (sapling not in `acceptedItems`), so no deposit goal forms and the worker freezes mid-zone holding an item the building cannot accept.

## Symptom
- Harvester at a lumberyard / mine / forager destroys a multi-output harvestable (apple tree, ore vein with byproducts, perennial fruit tree).
- Walks to the spawn pile, picks up **the wrong item** (the byproduct, not the wanted resource).
- Then stands still holding that item. Indefinitely.
- `JobHarvester.VerboseJobs` log: planning loop runs but produces null plans.
- The wanted item (e.g. wood) is still on the ground — visible to the player but never collected by the worker.

## Root cause
`CharacterActions.ApplyHarvestOnServer` / `ApplyDestroyOnServer` register pickup tasks without filtering:

```csharp
WorldItem spawned = WorldItem.SpawnWorldItem(entry.Item, jitter);
if (spawned != null)
{
    if (harvesterWorkplace != null)
        harvesterWorkplace.TaskManager?.RegisterTask(new PickupLooseItemTask(spawned)); // ← unfiltered
}
```

`GoapAction_PickupLooseItem.IsValid` only checked bag/hand space — not whether any task pointed to a wanted item. `Execute.ClaimBestTask` also only checked blacklist. `JobHarvester.PlanNextActions` computed `looseItemExists` with the same blacklist-only filter.

So when an apple tree destruction spawned `[Wood, AppleSapling]`:
1. Both became `PickupLooseItemTask` entries on the lumberyard's `BuildingTaskManager`.
2. `looseItemExists = true` (worldState sees the entries).
3. Planner picked `Pickup` (precondition `looseItemExists=true` met).
4. `Execute.ClaimBestTask` returned the geometrically-closest task — sometimes the sapling.
5. Worker walked to sapling, `CharacterPickUpItem` succeeded, sapling went into the bag.
6. Next plan tick: `acceptedItems = [Wood]`. Inventory `HasAnyItemSO([Wood])` = false; hands holding sapling not in `[Wood]`. → `hasAtLeastOneResource = false`.
7. `hasResourcesForGoap = false` (lie-or-honest, doesn't matter — `hasAtLeastOneResource` is the gate above).
8. Goal `HarvestAndDeposit` requires `hasDepositedResources=true`. `Deposit` requires `hasResources=true`. Worker's lie-to-planner can never flip true because they don't carry an accepted item. The wood is still on the ground but the worker holds the sapling — no chain to deposit. **Frozen.**

## How to avoid
Filter the `PickupLooseItemTask` registration AND the claim/worldState predicates by `HarvestingBuilding.GetAcceptedItems()`. The fix is three-layer (defense in depth + canonical worldState/IsValid symmetry):

### Layer 1 — Registration (primary fix)
`CharacterActions.ApplyHarvestOnServer` + `ApplyDestroyOnServer`: cache the workplace's accepted-items list once; gate `RegisterTask` on `accepted.Contains(spawned.ItemSO)`.

```csharp
List<ItemSO> workplaceAccepted = null;
if (harvesterWorkplace is HarvestingBuilding hb) workplaceAccepted = hb.GetAcceptedItems();

// per spawned WorldItem:
if (workplaceAccepted != null && !workplaceAccepted.Contains(spawned.ItemInstance.ItemSO)) continue;
harvesterWorkplace.TaskManager.RegisterTask(new PickupLooseItemTask(spawned));
```

Non-wanted drops stay on the ground for players to pick up. The harvester ignores them.

### Layer 2 — Claim filter (defense in depth, Execute-time only)
`GoapAction_PickupLooseItem.Execute.ClaimBestTask` predicate must check `accepted.Contains(wi.ItemInstance.ItemSO)`. If Layer 1 missed a task (e.g. a future caller bypasses it), the action still refuses to claim it. **Do NOT add this check to `IsValid`** — Pickup is a CHAIN CONSUMER (the planner inserts it AFTER Harvest/Destroy, whose Effect produces `looseItemExists=true` in the SIMULATED state). Pre-filtering Pickup's `IsValid` on "does a PickupLooseItemTask exist right now?" rejects it at punch-in time (no items dropped yet) — planner can't form `Harvest→Pickup→Deposit`, returns null, worker stuck on `Planning / Idle`. See [[chain-action-isvalid-pre-filter]].

### Layer 3 — worldState reflects registered-task reality
`JobHarvester.PlanNextActions` `looseItemExists` predicate mirrors Layer 1's registration filter: only counts `PickupLooseItemTask`s pointing to accepted items. Because Layer 1 already prevents non-accepted drops from getting a task, this is automatic — but the explicit predicate guards against any future code path that registers tasks bypassing Layer 1. Unlike Layer 2's IsValid, this is safe to include because worldState reads CURRENT state (the planner doesn't simulate registrations).

## How to fix (if already hit)
1. Confirm the symptom: harvester is standing in the harvest zone holding an item that is NOT in the workplace's `_wantedResources` list. Inspector check.
2. Apply the three-layer fix above. All three layers are required — Layer 1 alone leaves Layer 2/3 vulnerable to future callers; Layer 2/3 alone leaves dead tasks in the manager that bloat `HasAvailableOrClaimedTask` walks.
3. Re-run. Worker now ignores byproducts (saplings, etc.) and goes straight to the wood / ore / whatever the workplace wants.

## Affected systems
- [[jobs-and-logistics]] — `JobHarvester`, `JobFarmer` (both consume `PickupLooseItemTask` registered by `CharacterActions`).
- [[ai-goap]] — `GoapAction_PickupLooseItem` consumer side.

## Links
- [[harvester-deposit-freeze]] — sibling gotcha (lie-to-planner with no actionable work). Both ship in the same fix family; this one is the **upstream** trigger (wrong pickup), the deposit-freeze is the **downstream** symptom shape (no chain to deposit).
- [[worldstate-predicate-action-isvalid-divergence]] — canonical pattern for the Layer 3 fix.

## Sources
- 2026-05-20 conversation with [[kevin]] — lumberyard harvester chopped an apple tree, picked up the sapling instead of the wood, then froze.
- `CharacterActions.cs:423-504` (post-fix) — `ApplyHarvestOnServer` + `ApplyDestroyOnServer` gate task registration on workplace's accepted-items list.
- `GoapAction_PickupLooseItem.cs:39-87` (post-fix) — `Execute.ClaimBestTask` filter (Execute-time only — IsValid intentionally does NOT pre-filter, because Pickup is a chain consumer).
- `JobHarvester.cs:213-228` (post-fix) — `looseItemExists` predicate filters by accepted items, mirroring Layer 1's registration filter.
