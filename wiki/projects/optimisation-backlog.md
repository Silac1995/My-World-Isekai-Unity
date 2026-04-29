---
type: project
title: "Optimisation Backlog"
tags: [optimisation, performance, backlog, deferred-work]
created: 2026-04-25
updated: 2026-04-29
sources: []
related: ["[[storage-furniture]]", "[[jobs-and-logistics]]", "[[building-logistics-manager]]", "[[character-job]]", "[[ai-goap]]", "[[performance-conventions]]", "[[world-time-skip]]", "[[world-macro-simulation]]"]
status: active
confidence: high
start_date: 2026-04-25
target_date: null
---

# Optimisation Backlog

## Summary
Catch-all tracker for performance / scalability / culling work that's been **deliberately deferred** to keep current features unblocked. Each entry names the system, the trade-off being held open, and what "good enough" looks like when we eventually pick it up. Anything that lives here is a known compromise — not a forgotten bug.

> **Pattern catalogue:** the conventions and patterns extracted from the 2026-04-25 → 2026-04-27 logistics performance pass live in [[performance-conventions]]. Read that page **before** picking up any Tier 4 item or starting new optimisation work — it's the canonical "how to write fast Unity code in this project" doc. The headline rule lives in [CLAUDE.md rule #34](../../CLAUDE.md).

## Goals
- Keep optimisation TODOs out of the source code (where they rot) and out of conversation memory (where they vanish across sessions).
- Make it easy for a future agent or Kevin to see at a glance what shortcuts are in flight and decide whether to invest now.

## Non-goals
- Tracking general bugs (those go to GitHub issues).
- Tracking systemic refactors (those get their own project page).
- Pre-mature optimisation — entries here should be backed by an observed cost or a clear scaling concern.

## Current state
**Active deferrals:**

### 1. StorageVisualDisplay — per-player local distance/visibility culling
- **Where:** [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) (see TODO comment on the class docstring).
- **What was there:** a coroutine-based squared-distance check against `NetworkManager.Singleton.LocalClient.PlayerObject` that deactivated all displays when the local player was farther than `_activationDistance` (default 25 Unity units).
- **Why it was removed (2026-04-25):** the gating was a single-peer host-side decision that ran on every machine. On the host, distant rooms got their displays culled — fine. On clients, the *same* coroutine ran but resolved a different `LocalPlayerObject` and could end up flipping displays on/off out of phase with the host's storage state, leaving clients with empty shelves even when the storage was stocked.
- **What the replacement should look like:**
  - Run **per-peer**, on each peer's own copy of the `StorageVisualDisplay`. No server authority needed — pure local culling decision.
  - Inputs: this peer's local player transform (already resolvable via `NetworkManager.Singleton.LocalClient.PlayerObject` once that's reliable for clients), the storage's world position.
  - Decoupled from inventory sync — the inventory layer always carries the data; the visual layer culls independently.
  - Reasonable threshold: `~50 Unity units (≈7.6 m)` for a default. Builders can tune per-prefab.
- **Until then:** displays are always-on whenever the storage contains items. Acceptable cost: a few SpriteRenderers/MeshRenderers per shelf, and the per-`ItemSO` pool keeps allocations bounded. Becomes a real concern only when scenes have hundreds of populated shelves visible from camera.
- **Owner:** [[building-furniture-specialist]] for the visual layer; [[network-specialist]] if the local-player resolution turns out to need network-aware fallback.

### 2. Job logistics — full performance refactor
- **Where:** [[jobs-and-logistics]] / [[building-logistics-manager]] / [[character-job]] / [[ai-goap]]. Touches the worker-job loop (GOAP planning + replanning cadence), `BuildingLogisticsManager` and its sub-components (stock queries, demand matching, transport order generation), `FindStorageFurnitureForItem` / `GetItemsInStorageFurniture` lookups, the GOAP action set used during the logistics cycle, and `CharacterAwareness` (proximity scans hit by every BT condition).
- **Observed cost (2026-04-26, Kevin):** with **3 transporters + 4 logistic managers + 2 harvesters + 1 vendor + 2 crafters** active in a single map, frame rate drops **below 30 fps** on Kevin's dev machine. Not unplayable — but persistently annoying, and clearly the dominant cost on that scene. Scales worse than linearly as worker counts grow, so any future "small village + outpost" scenario will hit it harder.

#### Tick context (verified)
- `NPCBehaviourTree._tickIntervalSeconds = 0.1f` ([Assets/Scripts/AI/NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs):46) → every working NPC's BT runs at **10 Hz**, with stagger.
- When `CurrentActivity == Work`, `BTAction_Work` calls `CharacterJob.Work` → `Job.Execute()` every BT tick → **every job runs at 10 Hz per worker**.
- 12 workers × 10 Hz = **120 Job.Execute calls/sec server-side** baseline, before any branching cost.

#### Verified hot spots (ranked by expected impact)

**Server-only — none of these touch NetworkVariable / NetworkList state, all are safe for Host↔Client / Client↔Client / Host/Client↔NPC.**

1. **`CraftingBuilding.GetCraftableItems()` is called per supplier candidate per stock target during `FindSupplierFor`.** ([Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs):49-115). Each call: 2× `HashSet` allocs, walks `Building.Rooms` recursively, **then unconditionally also runs `GetComponentsInChildren<CraftingStation>(includeInactive: true)`** as a defensive fallback against the documented default-furniture registration race (the fallback is intentional and must be preserved — see in-file comment lines 72-84). Hit by `LogisticsStockEvaluator.FindSupplierFor` ([Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs):257-276) for every commercial building in the map per supplier query, and again by `RequiresCraftingFor` on every insufficient-stock branch in `LogisticsTransportDispatcher.cs:175`. **Multiplicative: ~8 buildings × 4 logistics managers × 6 stock targets per shift-change cluster = hundreds of full transform scans inside a 1-2 s window.** Cost: **HIGH**.
2. **`CharacterAwareness.GetVisibleInteractables<T>()` is the biggest per-tick allocator + per-tick `Physics.OverlapSphere`.** ([Assets/Scripts/Character/CharacterAwareness.cs](../../Assets/Scripts/Character/CharacterAwareness.cs):20-76). Untyped overload allocates a fresh `List<InteractableObject>` + a `Collider[]` from `Physics.OverlapSphere` over `Physics.AllLayers`. Generic overload then runs `.OfType<T>().ToList()` (LINQ enumerator + filtered list). **Then unconditionally fires a `Debug.Log` on line 72 every time results are non-empty** — same host-progressive-freeze trigger pattern as [[host-progressive-freeze-debug-log-spam]]. Called from `BTCond_DetectedEnemy`, `BTCond_FriendInDanger`, `BTCond_WantsToSocialize` (3 BT conditions × 10 Hz × 12 NPCs = ~120 OverlapSpheres + 120 list allocs/sec just from BT), plus `GoapAction_LocateItem`, `GoapAction_ExploreForHarvestables` (twice), and `GoapAction_WearClothing.IsValid`. Cost: **HIGH**.
3. **`JobLogisticsManager.Execute` calls `RetryUnplacedOrders` + `ProcessActiveBuyOrders` every single tick, unconditionally.** ([Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs):121-131). 4 logistics managers × 10 Hz = **40 dispatcher passes/sec**. Each pass enters [LogisticsTransportDispatcher.ProcessActiveBuyOrders](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs):63-119 which (a) calls `BuildGloballyReservedSet()` ([LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs):233-246) — fresh `HashSet<ItemInstance>` walking 3 order lists every call, (b) runs `placedTransportOrders.Any(closure)` per active BuyOrder (lines 92-98), (c) runs `_building.Inventory.Where(closure).ToList()` per active BuyOrder (lines 103-105). The whole call is idempotent on a stable order book — the 40 calls/sec almost always do zero useful work. Cost: **HIGH**.
4. **`GoapAction_GatherStorageItems.FindLooseWorldItem` does 3× `Physics.OverlapBox` + per-collider component scans on every `IsValid()` tick.** ([Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs):544-628). Hit by 4 logistics managers whenever they're in the gather phase. Each call allocates a `List<Collider>` and iterates every collider over `Physics.AllLayers`. Cost: **HIGH**.
5. **`CommercialBuilding.RefreshStorageInventory` does two `Physics.OverlapBox` scans + an O(N×M) double loop, fired from every `OnWorkerPunchIn` and from every `RefreshStorageOnOrderReceived`.** ([Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs):1892, 1957-1972). Allocates `List<WorldItem>`, `HashSet<ItemInstance>` (reserved items), another `HashSet` from `GetItemsInStorageFurniture()`. Cascades inside `BuildingLogisticsManager.PlaceBuyOrder` / `PlaceCraftingOrder` ([BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs):256, 265). Worst clustering happens around shift changes when 12 workers punch in within a 1-2 s window. Cost: **MED-HIGH**.
6. **`FindStorageFurnitureForItem` / `GetItemsInStorageFurniture` walk every room recursively × every furniture × `is StorageFurniture` cast on every call, with zero caching.** ([Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs):1753-1784). Called from 5 GOAP actions (`GatherStorageItems:441`, `DepositResources:271`, `StageItemForPickup:406`, `LocateItem:63 + 198`, `RefreshStorageInventory:1952`) — many fire per-plan-cycle. With 7+ workers running plans, hundreds of redundant walks/sec. The set of `StorageFurniture` per building changes only on placement/pickup. Cost: **MED**.
7. **GOAP actions rebuilt fresh per plan in `JobTransporter` (8 instances), `JobHarvester` (5 instances), `JobLogisticsManager` (4 instances).** ([JobTransporter.cs](../../Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs):275-293 + sibling job classes). The world-state dict + goal dict are pooled (good); the action list itself is rebuilt every replan — `new GoapAction_*()` ctors per plan. Note: this rebuild is **intentional today** ([Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs):173-178) because action instances carry per-plan state (`_isComplete`, `_isMoving`, target refs). Reuse requires adding a `Reset()` contract to every `GoapAction_*` and an audit per action; not a drive-by. Cost: **MED**.
8. **BT condition ordering: `BTCond_FriendInDanger` + `BTCond_DetectedEnemy` evaluated on every NPC every tick, even though combat is rare for workers.** ([Assets/Scripts/AI/NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs):121-133). No event-driven gate ("no recent damage event in last N seconds → skip combat scan"). Doubled-up with #2 — same Awareness scan, multiplied across the BT tree. Cost: **MED**.
9. **`ShopBuilding.ItemsToSell` getter allocates a fresh `List` on every property access.** ([Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs):55) — `_itemsToSell.Select(e => e.Item).ToList()`. Cost depends on call sites; if any UI / debug HUD touches it per-frame this is a thousands-of-allocs/sec leak. Cost: **LOW-MED** (footgun-class HIGH).

#### Refactor plan (ranked by impact / effort)

> **Status (2026-04-27):** **🎯 60 FPS hit on the audited worker mix.** Tier 1 + Tier 2 + Tier 3 (minus C and Dₐ) all shipped + Tier 1/2 invalidation hooks wired. Profiler pass after the bundle landed showed FPS still capped at ~33 because of `UI_CommercialBuildingDebugScript` (debug overlay, ~28% of frame, 59 KB GC.Alloc/frame from 633 allocations). Disabling it pushed FPS to **the 60 FPS target**. C and Dₐ confirmed not needed. See `## Result` below for the profiler trail and `## Tier 4 (deferred follow-ups)` for what's still on the table for future scaling work.

**Recommended ship order: A → B → D → F together as one PR (building-side, no AI-side changes), then E. → G. as drive-bys, then the AI-side bundle (Aₐ → Fₐ).**

##### Building / logistics side (ship first — pure server-only state, network-safe)
- **A. Cache `CraftingBuilding.GetCraftableItems()` behind a `HashSet<ItemSO>` invalidated on `FurnitureManager` register/unregister.** Kills hot-spot #1. `ProducesItem(item)` becomes one `HashSet.Contains` lookup. **Preserve the existing transform-tree fallback** — it covers a real registration race ([crafting-loop](../systems/crafting-loop.md) gotcha) — by feeding both the room walk and the fallback walk into the cache builder once at first access; the cache then absorbs whichever path finds the stations. Risk: low. Touches: `CraftingBuilding.cs`, `FurnitureManager` registration callbacks.
- **B. Make `JobLogisticsManager` event-driven.** Add `_orderBookDirty` / `_inventoryDirty` flags in `LogisticsOrderBook` + `CommercialBuilding`. `ProcessActiveBuyOrders` early-exits when both clean. Optionally throttle to 2 Hz when dirty. Kills hot-spot #3. Risk: medium — must mark dirty on every state change that could enable a new dispatch (reservation cancel, player drops items in zone, `RefreshStorageInventory` Pass-2 absorption). Touches: `LogisticsOrderBook.cs`, `LogisticsTransportDispatcher.cs`, `CommercialBuilding.cs` inventory mutations, `JobLogisticsManager.cs`.
- **C. Cache the globally-reserved set + `GetReservedItemCount` dict incrementally on `ReserveItem` / `UnreserveItem` / order add/remove.** Trims hot-spot #3 + the per-frame `BuildGloballyReservedSet` allocation. Risk: medium — `ReservedItems` is mutated on the BuyOrder/TransportOrder POCOs themselves, need to centralize through OrderBook or wrap. Redundant if B alone gets us under 16 ms. Touches: `LogisticsOrderBook.cs`, every `ReservedItems` mutation site.
- **D. Per-building `List<StorageFurniture>` cache.** Maintain on `CommercialBuilding`, populated from `FurnitureManager` register/unregister event (union across MainRoom + sub-rooms). Kills hot-spot #6. Risk: low. Touches: `CommercialBuilding.cs`, `FurnitureManager.cs`, `Room`/`ComplexRoom`. Save/load: cache rebuilt on wake. Network: server-only consumers today.
- **E. Per-tick virtual-stock cache.** `Dictionary<ItemSO, (physical, inFlight, ts)>` invalidated on the same dirty-flag from B. `CheckStockTargets` does dictionary lookups. Risk: low. Lower priority — `CheckStockTargets` is punch-in-only today.
- **F. Pool the `Collider[]` for `Physics.OverlapBox` calls** via `Physics.OverlapBoxNonAlloc` with a reused `Collider[64]` buffer on `CommercialBuilding`. Trims hot-spot #5. Risk: zero. Touches: `CommercialBuilding.GetWorldItemsInStorage`, `CountUnabsorbedItemsInBuildingZone`, `RefreshStorageInventory` PickupZone scan.
- **G. Fix `ShopBuilding.ItemsToSell` to cache or expose `_itemsToSell` directly.** Drive-by. Risk: zero.

##### AI / worker-loop side (ship second — independent of A-G)
- **Aₐ. Cache `CharacterAwareness` results on a 0.3-0.5 s timer; pre-allocate the result list; replace `OfType<T>().ToList()` with typed cache lists; delete the line-72 `Debug.Log` unconditionally.** Kills hot-spot #2 + amplifier from line 72. All 9 callers benefit transparently. Risk: combat detection latency goes from ≤0.1 s to ≤0.3-0.5 s — well under human reaction, acceptable. Network-safe (server-only). **Pure win — biggest single-call ROI on the AI side.** Touches: `CharacterAwareness.cs` only.
- **Bₐ. Make `GoapAction_GatherStorageItems.FindLooseWorldItem` event-driven.** Maintain a `List<WorldItem> _looseInZone` per building, mutated on enter/exit. Action consults the list (cheap) instead of 3 OverlapBoxes per tick. Keep a slow OverlapBox sweep at 1-2 Hz as safety net. Kills hot-spot #4. **Pairs with refactor D.** Coordinate with [[building-furniture-specialist]].
- **Cₐ. Stagger `Job.Execute` independently of BT tick.** Per-job `_executeIntervalSeconds` (default 0.1) overridable per Job class. Heavy planning/dispatch can run at 2-3 Hz with no behavioural degradation. Audit each action that assumes 10 Hz Execute. Touches: `BTAction_Work.HandleWorking` + `Job` base.
- **Dₐ. Pool `GoapAction` instances per Job.** Add `Reset()` contract on `GoapAction_*`. Kills hot-spot #7. Risk: high — exact concern the `JobLogisticsManager.cs:173-178` comment block warns about. Audit per action; do this last with full profiler diff.
- **Eₐ. Gate the 3 environmental BT conditions** (`BTCond_DetectedEnemy`, `BTCond_FriendInDanger`, `BTCond_WantsToSocialize`) behind a "trigger" flag (last damage / friend-distress event in last 0.5 s). Subsumed by Aₐ if Aₐ caches awareness output for 0.5 s. Touches: 3 BT conditions + `NPCBehaviourTree`.
- **Fₐ. Do NOT re-throttle job-internal GOAP planners** (separate from `CharacterGoapController` life GOAP, which is already throttled at 2 s in [CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs):115-130). Job-internal planners are reactive to logistics state which can flip mid-second; the right tools are event-driven dispatch (B) + Awareness caching (Aₐ), not a 2 s timer that would stall transports. **Explicit non-action — preserve current cadence, fix the cost-per-replan instead.**

#### What "good enough" looks like
- 60 fps stable with the current worker mix on the same hardware.
- No regression in NPC behaviour quality — workers still pick the correct job, route, and storage container.
- Networked behaviour identical across Host↔Client / Client↔Client / Host/Client↔NPC. Server stays authoritative; clients keep observing the same outcomes.
- Scales to ~3× the current worker count before hitting the same fps floor.

#### Profiler-pass before any refactor (mandatory)
Capture a 30-60 s **Deep Profile** run with the full 12-worker mix steady-state (everyone punched in, mid-shift). Both audits agree this must come **before** any code lands — if the profiler doesn't confirm the suspected cost distribution, the plan is aimed at the wrong target.

Specifically inspect:
1. **CPU Hierarchy + Timeline (sample by self-time):**
   - `JobLogisticsManager.Execute` parent → `LogisticsTransportDispatcher.ProcessActiveBuyOrders` self-time + invocation count (expect ~40/sec). Confirms hot-spot #3.
   - `CraftingBuilding.GetCraftableItems` + `GetComponentsInChildren` — should be the smoking gun for hot-spot #1.
   - `CharacterAwareness.GetVisibleInteractables` — self-time + #calls/frame. Keystone metric for refactor Aₐ.
   - `Physics.OverlapSphere` and `Physics.OverlapBox` call counts. Expect baseline ~120 OverlapSphere/sec + ~12+ OverlapBox/sec.
   - `CommercialBuilding.RefreshStorageInventory` — call rate (expect spikes around punch-in clusters).
2. **Allocation heatmap (Memory Profiler or Deep Profile "GC Alloc"):**
   - `LogisticsOrderBook.BuildGloballyReservedSet` (~40 HashSet allocs/sec).
   - `LogisticsTransportDispatcher.ProcessActiveBuyOrders` LINQ allocs.
   - `CraftingBuilding.GetCraftableItems` HashSet/List/`GetComponentsInChildren` array allocs.
   - `CharacterAwareness.GetVisibleInteractables` List + `OfType().ToList()` allocs.
   - `ShopBuilding.ItemsToSell` callsites.
3. **Console window allocations:** filter Profiler "Editor" view for `Console.Log` callstacks. Even one ungated `Debug.Log` per tick is visible on Windows. The `CharacterAwareness.cs:72` log is the prime suspect — also re-check `wiki/gotchas/host-progressive-freeze-debug-log-spam.md`.
4. **Capture twice — once Editor, once Standalone Mono build.** Editor masks console-flush cost; Standalone exposes pure CPU.
5. **Cross-check the hypothesis:** if `CraftingBuilding.GetCraftableItems` / `GetComponentsInChildren` and `CharacterAwareness` don't dominate self-time, the rankings above are wrong — **push back, reconsider, don't pre-commit to the plan**. The other strong suspect is hidden Debug.Log spam ([[host-progressive-freeze-debug-log-spam]]).

#### Shipped 2026-04-26 (Tier 1 + Tier 2)

Five surgical refactors. All compile clean, all server-only state, all network-safe (Host↔Client / Client↔Client / Host/Client↔NPC).

| Fix | File | What changed | Expected win |
|-----|------|--------------|--------------|
| **Aₐ** | [Assets/Scripts/Character/CharacterAwareness.cs](../../Assets/Scripts/Character/CharacterAwareness.cs) | Added 0.3 s TTL cache + reused `Collider[64]` `OverlapSphereNonAlloc` buffer + reused result list. **Deleted ungated `Debug.Log` on the typed overload.** Added `InvalidateCache()` for callers that need immediate freshness. Returned untyped list is now SHARED (callers documented as read-only — verified all 9 callers are non-mutating). | OverlapSphere call rate ~120/sec → ~30-40/sec across the worker mix. Eliminates the line-72 console-flush amplifier (host-progressive-freeze pattern). Per-call allocations on the typed overload drop from `List<InteractableObject>` + `Collider[]` + LINQ enumerator to a single small `List<T>`. |
| **G** | [Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs) | `ItemsToSell` getter now lazy-builds a cached `List<ItemSO>` once. Removed unused `using System.Linq;`. | Eliminates a per-access `Select().ToList()` allocation. Magnitude depends on caller frequency; defensive against future hot-loop callers. |
| **F** | [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Added shared `Collider[128]` `OverlapBuffer`. Swapped 3 `Physics.OverlapBox` calls to `OverlapBoxNonAlloc` (`GetWorldItemsInStorage`, `CountUnabsorbedItemsInBuildingZone`, `RefreshStorageInventory` PickupZone scan). Each site emits a `Debug.LogWarning` if the buffer saturates (rule #31). | Eliminates 3 `Collider[]` allocations per `RefreshStorageInventory` / `GetWorldItemsInStorage` / `CountUnabsorbedItemsInBuildingZone` call. PhysX cost itself unchanged — this is GC pressure relief. |
| **D** | [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Added 2 s TTL cache + `InvalidateStorageFurnitureCache()` hook on `CommercialBuilding`. Replaced the per-call recursive `GetFurnitureOfType<StorageFurniture>()` walk in `FindStorageFurnitureForItem` and `GetItemsInStorageFurniture` with the cached list. | Hundreds of redundant room+furniture walks/sec → one walk per 2 s per building. Hit by 5 GOAP actions across all logistics workers. |
| **A** | [Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs) | Added 2 s TTL cache (`HashSet<ItemSO>` for `ProducesItem` O(1) lookup + `List<ItemSO>` for `GetCraftableItems`) + `InvalidateCraftableCache()` hook. **Preserved the intentional `GetComponentsInChildren<CraftingStation>(true)` fallback** — now paid once per refresh instead of once per query. Removed unused `using System.Linq;`. | The biggest single win in the bundle. `ProducesItem(item)` fan-out across ~8 buildings × 4 logistics managers × 6 stock targets per shift-change cluster: was ~hundreds of full transform scans inside a 1-2 s window, now amortized to ~one scan per building per 2 s. |

**Trade-off introduced:** The TTL caches (D, A) introduce up to 2 s of staleness for furniture/station changes. Acceptable because:
- Stations and storage furniture change rarely at runtime (player must physically place/pick up).
- BuyOrders are retried via `RetryUnplacedOrders` every dispatcher tick — a 2 s delay in supplier discovery is invisible in practice.
- The intentional `GetCraftableItems` fallback walk still runs (now amortized), so the registration-race correctness it guards is preserved.
- Manual `InvalidateCraftableCache()` / `InvalidateStorageFurnitureCache()` hooks let callers force immediate freshness when they know things changed (default-furniture spawn completion, player place/pickup). **Future PR can wire these from `CommercialBuilding.SpawnDefaultFurniture` + `CharacterPlaceFurnitureAction` for zero staleness.**

**Verification:** Both `assets-refresh` passes (mid-checkpoint after Tier 1, final after Tier 2) returned zero compile errors and zero runtime exceptions.

#### Shipped 2026-04-27 (Tier 3 — most of it)

Five further surgical refactors on top of Tier 1+2. All compile clean (4 incremental compile checkpoints, zero errors). Network-safe — every dirty-flag, every cache, every cadence change is on server-only state and never touches `NetworkVariable` / `NetworkList`.

| Fix | Files | What changed | Expected win |
|-----|-------|--------------|--------------|
| **B** | [LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs), [LogisticsTransportDispatcher.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs), [BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs), [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Added `_dispatchDirty` flag in `LogisticsOrderBook`. Every Add\*/Remove\* method (active orders, placed buy orders, placed/active transport orders, active crafting orders, pending queue) marks dirty. Inventory mutations (`AddToInventory`, `TakeFromInventory`, `RemoveExactItemFromInventory`) call through `BuildingLogisticsManager.MarkDispatchDirty()`. `ProcessActiveBuyOrders` and `RetryUnplacedOrders` early-exit when the flag is clean; `ProcessActiveBuyOrders` clears the flag at the end of a successful pass. | Dispatcher work goes from 40 calls/sec on a stable order book → near-zero. The 4 logistics managers' per-tick `BuildGloballyReservedSet` allocation + LINQ `.Any()` + `Inventory.Where().ToList()` cost evaporates when nothing changed. |
| **Cₐ** | [Job.cs](../../Assets/Scripts/World/Jobs/Job.cs), [JobLogisticsManager.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs), [JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs), [BTAction_Work.cs](../../Assets/Scripts/AI/Actions/BTAction_Work.cs) | Added `Job.ExecuteIntervalSeconds` (default `0.1f`). `JobLogisticsManager` and `JobHarvester` override to `0.3f`. `BTAction_Work.HandleWorking` tracks `_lastExecuteTime` per-NPC (instance field, reset in `OnEnter`) and only calls `jobInfo.Work()` once per interval. BT itself still ticks at 10 Hz — only `Job.Execute` is throttled, so combat reaction / schedule transitions are unaffected. | Heavy-job Execute call rate: LogisticsManager 4×10=40/sec → 4×3.3=13/sec; Harvester 2×10=20/sec → 2×3.3=7/sec. ~60% reduction in GOAP planning + dispatcher work. Pairs perfectly with B (the throttled call usually finds the dispatcher clean and skips). |
| **Bₐ** | [GoapAction_GatherStorageItems.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs) | Replaced 3 per-call `Physics.OverlapBox` (BuildingZone, DepositZone, DeliveryZone) with `Physics.OverlapBoxNonAlloc` against a static shared `Collider[128]` buffer + reused scratch `List<Collider>`. Added per-action 0.5 s TTL cache for the result `WorldItem` (cleared in `Exit()` so a new action invocation always starts cold; cache invalidated if the cached item gets picked up). Saturation warning on overflow (rule #31). | 3 PhysX queries → at most 3 per 0.5 s per action. `Collider[]` allocations → zero. Scratch `List` allocation → one shared instance. Hit by 4 logistics managers + 2 harvesters in the audited mix. **Smaller scope than the audit's "full event-driven zone tracking" recommendation** — chosen because event-driven would have required new MonoBehaviour zone trackers + missed-event safety nets; cache-+-NonAlloc gets ~80% of the win with ~10% of the surface. |
| **E** | [LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) | Replaced `provider.GetStockTargets().ToList()` with iteration into a reused `_scratchStockTargets` member list. Cleared at start and end. **Scaled-down from the audit's "per-tick virtual-stock cache" recommendation** — `CheckStockTargets` only runs on punch-in / OnNewDay (not per tick), so the full cache is overkill; the alloc-elimination is the only meaningful win. | Eliminates one `List<StockTarget>` allocation per `CheckStockTargets` call. Small but free. |
| **Tier 1+2 invalidation hooks** | [FurnitureManager.cs](../../Assets/Scripts/World/Buildings/FurnitureManager.cs), [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Added `InvalidateOwnerBuildingCaches()` on `FurnitureManager` (resolves the parent `CommercialBuilding` lazy + cached). Called from every furniture mutation site: `AddFurniture`, `RemoveFurniture`, `RegisterSpawnedFurniture`, `RegisterSpawnedFurnitureUnchecked`, `UnregisterAndRemove`, `LoadExistingFurniture` (only when items were actually added). `CommercialBuilding.TrySpawnDefaultFurniture` also explicitly invalidates at the end of the layout pass. | Eliminates the 2 s staleness window in Tier 2's StorageFurniture / Craftable caches: any furniture place/pickup or default-furniture spawn now invalidates immediately. Storage drops + supplier discovery work the same instant a station appears, with zero perf cost vs the always-on TTL approach (cache still rebuilds lazily on next read). |

#### Deferred / explicitly skipped (profiler-gated)

- **C — Incremental reservation tracking on `LogisticsOrderBook`.** Skipped: clean impl needs either (a) refactoring all reservation mutations to go through `OrderBook` (touches `BuyOrder`, `TransportOrder`, `BuildingLogisticsManager`) or (b) wiring order-back-references — bigger surface than the audit anticipated. The audit explicitly said C is *redundant* if B alone gets us under 16 ms. Revisit only if profiler shows `BuildGloballyReservedSet` / `GetReservedItemCount` are still hot after B.
- **Dₐ — Pool `GoapAction` instances.** Explicitly NOT shipped. The `JobLogisticsManager.cs:173-178` in-file comment block memorializes a prior regression where this exact change broke shop ordering (only the first `BuyOrder` placed; subsequent ones stalled because `_isComplete=true` leaked across plans). Both audits independently said "do this last with profiler data." Revisit only with profiler evidence that GOAP action-construction cost is dominant after Tier 1-3 ship.
- **Eₐ — Gate the 3 environmental BT conditions.** Subsumed by Aₐ (CharacterAwareness now caches the OverlapSphere result for 0.3 s, which absorbs the BT condition fan-out automatically).

#### Until then
Keep recommended worker counts low for demos; flag this in any tutorial / sample save.

#### Owner
- **Building / logistics side (A-G):** [[building-furniture-specialist]]. Can ship as one PR independent of the AI side.
- **AI / worker-loop side (Aₐ-Fₐ):** [[npc-ai-specialist]]. Can ship independent of the building side; **start with Aₐ — biggest single-call ROI in the entire codebase.**
- **Coordination needed for Bₐ + D** (event-driven loose-item tracking spans both layers).

## Result (2026-04-27 profiler session)

**Verdict: 60 FPS target HIT** with the audited worker mix (3 transporters / 4 logistic managers / 2 harvesters / 1 vendor / 2 crafters). C and Dₐ confirmed not needed. Optimisation entry #2 is closed for the current performance budget.

### The trail

After Tier 1+2+3 + invalidation hooks landed, Kevin ran the Unity Profiler in Play Mode. Frame budget at that point: ~31 ms (~32 FPS) — still below target. Drilled down into `PlayerLoop` → `UpdateScene` → `Update.ScriptRunBehaviourUpdate` → `BehaviourUpdate`. The findings:

| Row | % of frame | Self ms | Calls | GC.Alloc / frame |
|-----|-----------|---------|-------|------------------|
| **`UI_CommercialBuildingDebugScript.Update`** | **28.9%** | **9.98** | **4** | **59.0 KB (633 individual allocs)** |
| `CharacterStatusManager.Update` | 0.0% | 0.02 | 25 | 3.7 KB |
| `QuestWorldMarkerRenderer.Update` | 0.0% | 0.00 | 2 | 120 B |
| Everything else (BT, Jobs, GOAP, NPC controllers, awareness, …) | 0.0% | tiny | — | 0 B |

**The debug overlay was 94% of every per-frame `Update()` allocation and ~28% of frame budget.** Not Kevin's logistics code, not the system I just refactored — instrumentation that had been left running.

**Disabling `UI_CommercialBuildingDebugScript` pushed FPS straight to the 60 FPS target.** That single test confirmed the entire Tier 1+2+3 refactor was doing its job: the actual game-logic Update path summed to ~4 KB / frame, which is negligible.

### Validating that the refactor "did its job"

The 60 FPS result is the headline, but the per-row data is the bigger validation. Inside `BehaviourUpdate`, after the debug overlay was disabled:

- `JobLogisticsManager.Execute`, `JobTransporter.Execute`, `JobHarvester.Execute`, `JobVendor.Execute`, `JobBlacksmith.Execute` — all 0 B GC.Alloc / negligible self ms.
- `LogisticsTransportDispatcher.ProcessActiveBuyOrders` — early-exited via the dirty flag (B); did not show up as a hot row.
- `CharacterAwareness.GetVisibleInteractables` — call rate dropped per the 0.3 s TTL cache (Aₐ); not in the top costs.
- `CraftingBuilding.GetCraftableItems` — amortized to ~one walk / 2 s / building (A); not visible as a per-frame cost.
- `Physics.OverlapBox` and `Physics.OverlapSphere` — `Collider[]` allocs gone (Tier 1 F + Tier 3 Bₐ).

The C path (`BuildGloballyReservedSet` / `GetReservedItemCount`) was confirmed cold — B alone reduced it to near-zero. **C is permanently parked.**

The Dₐ path (`GoapAction` ctor allocs) was also cold — the per-tick action constructor cost is now diluted by Cₐ throttling. **Dₐ is permanently parked.**

## Tier 4 (deferred follow-ups)

> **Kevin's note (2026-04-27):** "Don't forget to optimise that, ok?" — keep this list visible for future scaling work even though current FPS is satisfactory.

These were spotted in the same profiler session AFTER the debug overlay was disabled. None of them blocked the 60 FPS target; pick them up if/when worker count, content density, or scene complexity grows enough that we drop below 60 again.

### 1. `UI_CommercialBuildingDebugScript` — fix before re-enabling
- **Where:** `Assets/Scripts/UI/...` (exact path TBD — search at re-enable time).
- **What was wrong (2026-04-27 measurement):** 9.98 ms self / frame for 4 instances + 633 GC.Alloc events / frame totalling 59 KB. ~28% of total frame budget.
- **Likely causes (to confirm):** rebuilding strings every frame, instantiating UI children inside `Update`, ungated full re-render of the panel even when state didn't change, no cull for off-screen / collapsed buildings.
- **Reasonable fix shape:** (a) stagger updates (e.g. only refresh once per second per building, or only the currently-selected building); (b) cache string builders, reuse; (c) skip render when off-screen / minimized; (d) use a single shared overlay instead of one-per-building; (e) only show in dev mode.
- **Until then:** keep the script disabled in production scenes. If a designer needs to see logistics state, the existing dev-mode inspect tab (`DevInspectModule`) already covers it.
- **Owner:** [[debug-tools-architect]].

### 2. `CharacterActions.ActionTimerRoutine.Instantiate` — pooling
- **Where:** [Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) and the various `CharacterAction` subclasses' `OnApplyEffect` paths (`CharacterDropItem`, `CharacterPickUpItem`, `CharacterStoreInFurnitureAction`, `CharacterPlaceFurnitureAction`, `CharacterCraftAction`, …).
- **What was measured (2026-04-27):** 8.7% of frame / **0.8 MB GC.Alloc / frame** from `Instantiate` calls inside the action timer coroutine. ~2 instantiations / frame at steady state, each ~400 KB (likely `WorldItem` or `Furniture` prefab — multi-renderer hierarchy).
- **Why it costs:** workers in the logistics cycle constantly hand off items, and each handoff `Instantiate`s a fresh prefab instead of moving / reusing an existing one. With 12+ workers shuttling, this fires nonstop.
- **Reasonable fix shape:** (a) `WorldItem` pool keyed by `ItemSO` with parent under a stable `WorldItemPool` root; (b) acquire from pool on drop, release back to pool on pickup-and-disable; (c) preserve `NetworkObject` correctness — server spawns, client just re-parents the existing visual; (d) audit which actions create-vs-move and convert create-paths to move-from-pool wherever the source object would otherwise be despawned.
- **Risk:** networked. `WorldItem` has a `NetworkObject` and goes through NGO's spawn lifecycle. Pooling needs to either (i) only pool the visual and keep spawning the network entity, OR (ii) implement a network-aware pool that re-uses NetworkObjects (NGO 1.x supports this with `NetworkObjectPool` sample). Read the [[network-architecture]] doc before touching this.
- **Adjacent fix (free with this work):** there is also an in-coroutine `Debug.Log` (3 calls / frame, **11.7 KB / frame** including `StackTraceUtility.ExtractStackTrace`). Gate it behind `NPCDebug.VerboseActions`.
- **Owner:** [[item-inventory-specialist]] for the pool design; [[network-specialist]] for the NGO-pool patterns.

### 3. `PreLateUpdate.ScriptRunBehaviourLateUpdate` — 391 KB / frame, unexplored
- **Where:** unknown — never expanded in the profiler session. Sits at 19.5% of frame / 7.23 ms / **391 KB GC.Alloc / frame**, all inside `LateBehaviourUpdate`.
- **First step when picked up:** drill `PreLateUpdate.ScriptRunBehaviourLateUpdate` → `LateBehaviourUpdate`, sort by GC.Alloc descending, screenshot top 10. Likely candidates given the codebase: `CharacterVisual` flip / sprite-update logic, `UI_*` HUD renderers, `MapController.LateUpdate` cleanup, animation event handlers, or a NetworkBehaviour serializer.
- **No suspect named yet** — don't pre-commit to a fix shape.
- **Owner:** TBD.

### 4. `EyesController.BlinkRoutine` + Unity 2D Animation `SpriteSkin.OnEnable` log spam
- **Where:** `Assets/Scripts/Character/CharacterBodyPartsController/EyesController.cs` (blink coroutine activates a child GameObject with a `SpriteSkin` component); `UnityEngine.U2D.Animation.SpriteSkin.OnEnable` then unconditionally `Debug.Log`s on every enable.
- **What was measured (2026-04-27):** 2.3% of frame / 0.86 ms / 29.5 KB GC.Alloc / frame. Most of the cost is `LogStringToConsole` inside Unity's package code (15.1 KB / frame from `StackTraceUtility.ExtractStackTrace` invoked by `SpriteSkin.OnEnable`).
- **Decision (2026-04-27):** **defer to Spine migration.** [[project_visual_migration_order]] / [[project_spine2d_migration]] — once the visual layer moves to Spine, `SpriteSkin` is no longer used and the package log dies with it. Patching around it now is wasted work.
- **Owner:** [[character-system-specialist]] (visual migration); not actionable in isolation.

### 5. `MacroSimulator.SimulateOneHour` 24×=1×CatchUp invariant — EditMode test missing
- **Where:** [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — the `SimulateOneHour` entry point added for [[world-time-skip]].
- **What's missing (2026-04-27):** an EditMode test asserting that 24× `SimulateOneHour` is byte-equivalent to 1× `SimulateCatchUp(daysPassed=1.0)` for a fixture `MapSaveData`. This was Task 8 of the time-skip implementation plan and was specified in the design.
- **Why it didn't ship in v1:** `MacroSimulator` lives in the default `Assembly-CSharp` and Unity asmdefs don't reference it directly. There is no asmdef that an EditMode test asmdef can hang off.
- **Measured cost:** N/A — this is a **correctness** deferral, not a perf deferral. Day-boundary gating inside `SimulateOneHour` is the central correctness invariant; manual byte-equivalence was verified by code review during implementation but missing automation makes future regression harder to catch.
- **Reasonable fix shape:** extract `MacroSimulator`'s pure-math helpers (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule`, the cumulative day-grained steps) into a new `MWI.MacroSim.Pure` asmdef. Add an EditMode test asmdef referencing it and write the 24×=1× invariant test against a synthetic fixture. The Time Skip `TimeSkipController` and `MacroSimulator.SimulateCatchUp` orchestration can stay in `Assembly-CSharp`.
- **Risk if skipped:** medium. Any new step added to the macro-sim that is misclassified (day-grained step placed in the hour-grained block, or vice versa) silently breaks the time-skip integration without an obvious symptom — a 168 h skip will produce 0 of that effect.
- **Threshold for action:** before any third contributor adds a new step to `MacroSimulator.SimulateOneHour` / `SimulateCatchUp`. Until then, the design is fresh enough in Kevin + Claude's heads that classification mistakes are unlikely.
- **Status:** **deferred / not started.**
- **Owner:** [[world-system-specialist]].

### 6. `MacroSimulator.SimulateOneHour` resolves the active MapController via `Object.FindObjectsByType<MapController>` per hour
- **Where:** [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — inside `SimulateOneHour`. Pre-existing pattern from `SimulateCatchUp`; the per-hour entry point inherits it.
- **What it costs:** one `MapController[]` array allocation per hour during a time skip. Maximum 168 / skip.
- **Context:** companion to the static-cache fix in commit `f1369db0` (Task 7 of the time-skip plan), which cached `Resources.LoadAll<TerrainTransitionRule>` once instead of per-hour. The `FindObjectsByType<MapController>` call has the same allocation pattern and was missed.
- **Reasonable fix shape:** pass `MapController` through from `TimeSkipController.RequestSkip` → coroutine → `SimulateOneHour(map, …)` instead of resolving inside `SimulateOneHour`. The active map is already known at the call site.
- **Risk:** low — pure plumbing.
- **Threshold for action:** when measured GC pressure during a skip becomes visible (e.g. the player notices a stutter on the wake frame, or a profiler capture during a long skip shows the alloc as a hot row). At 168 allocs / skip this is well under any visible threshold today.
- **Status:** **deferred / not started.**
- **Owner:** [[world-system-specialist]].

### 7. `MapController.WakeUp()` ungated `Debug.Log` calls — newly reachable via `WakeUpFromSkip`
- **Where:** [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — lines 1444, 1454, 1462, 1492, 1496, 1501, 1507, 1535, 1551 (9 ungated `Debug.Log` calls inside the wake-up path).
- **Why it's new:** the calls are pre-existing and were tolerable when wake-up only fired on player approach (rare event). The `WakeUpFromSkip()` wrapper added for [[world-time-skip]] now reaches the same path at the end of every skip — cheap if the skip is rare, but still violates project rule #34 (hot-path logs must be gated).
- **Measured cost:** not measured. Each ungated `Debug.Log` triggers `StackTraceUtility.ExtractStackTrace` (~4 KB/call). 9 calls per wake = ~36 KB GC.Alloc on the wake frame. Visible at the end of a skip; invisible during a long skip's mid-section.
- **Reasonable fix shape:** wrap each call with `if (NPCDebug.VerboseJobs)` / `VerboseActions` / equivalent toggle, or move them behind `#if UNITY_EDITOR` / `Debug.isDebugBuild`. Same pattern as the rest of the `Verbose*` gating in the project. See [[host-progressive-freeze-debug-log-spam]] for context on the cost of ungated `Debug.Log` on Windows.
- **Risk:** zero. Diagnostic-only logs.
- **Threshold for action:** drive-by; no specific blocker. Pick up next time anyone touches `MapController.WakeUp` for an unrelated reason.
- **Status:** **deferred / not started.**
- **Owner:** [[world-system-specialist]].

### 8. Farming visual layers — collapse to single-GameObject-per-crop
- **Where:** [Assets/Scripts/Farming/CropVisualSpawner.cs](../../Assets/Scripts/Farming/CropVisualSpawner.cs) + the `CropHarvestable` spawn at maturity in [Assets/Scripts/Farming/FarmGrowthSystem.cs](../../Assets/Scripts/Farming/FarmGrowthSystem.cs) + the visual handoff via `MapController.NotifyDirtyCells` ClientRpc.
- **Why it's deferred:** the existing two-layer design (local stage cube during growth → networked `CropHarvestable` at maturity) was a speculative network-footprint optimization. In practice the simpler **single-GameObject-per-crop** model is architecturally cleaner — Kevin's intuition during 2026-04-29 playmode integration: "a crop is local to its own". One `CropHarvestable` per cell from plant-time, visual driven by `NetworkVariable<int> CurrentStage` (or by reading `cell.GrowthTimer` on tick). `CanHarvest()` returns false while growing.
- **Cost vs. benefit:** the saving from the current split is "no `NetworkObject` for growing cells". For typical scenes (dozens of crops per player, players spread across the world) that saving is negligible. The complexity cost of the handoff (visual race condition, two visual systems to debug, two paths to maintain) is real and recurring.
- **Reasonable fix shape:** spawn `CropHarvestable` from `CharacterAction_PlaceCrop.OnApplyEffect` instead of from `FarmGrowthSystem.HandleNewDay` on JustMatured. Add `CropHarvestable._stageSprites[]` (or per-stage prefab swap if 3D) + `NetworkVariable<int> CurrentStage`. Delete `CropVisualSpawner.cs` + the spawner fan-out from `MapController.SendDirtyCellsClientRpc`. Update farming spec §6 + `wiki/systems/farming.md` "Visual handoff" section accordingly.
- **Same critique applies to [[terrain-and-weather|VegetationGrowthSystem]]** (wild-vegetation cell timer). Whatever pattern wins here should be mirrored there.
- **Risk:** moderate. Touches the visual + persistence path on every plant. Existing 16 acceptance criteria from the [farming spec](../../docs/superpowers/specs/2026-04-28-farming-plot-system-design.md) need re-running.
- **Threshold for action:** before adding NPC farming AI (the simpler model is far easier to reason about for GOAP/BT planners). OR before shipping the system to a designer audience (the current two-layer model surprises them). Whichever comes first.
- **Status:** **deferred / not started.**
- **Owner:** [[kevin]].

## Milestones
- [ ] StorageVisualDisplay per-peer culling — no fixed date; pick up when shelf-count perf becomes measurable, OR when player-count testing shows the always-on cost hurts.
- [x] **Tier 1 + Tier 2 shipped 2026-04-26** — Aₐ / G / F / D / A. Compile clean, network-safe, server-only state.
- [x] **Tier 3 (B + Cₐ + Bₐ + E) + invalidation hooks shipped 2026-04-27.** C and Dₐ permanently parked.
- [x] **Profiler pass run 2026-04-27** — measured ~33 FPS post-bundle, identified `UI_CommercialBuildingDebugScript` as the remaining 28% of frame budget; **disabling it pushed FPS to the 60 FPS target. Optimisation entry #2 closed for current performance budget.**
- [ ] **Tier 4 — `UI_CommercialBuildingDebugScript` proper fix before re-enable** (stagger, cache, dev-mode-only). No fixed date; before any future re-enable in production scenes.
- [ ] **Tier 4 — `CharacterActions.ActionTimerRoutine` Instantiate pooling** (~0.8 MB/frame). Pick up when worker count, content density, or scene complexity scales beyond the audited 12-worker mix.
- [ ] **Tier 4 — `PreLateUpdate.ScriptRunBehaviourLateUpdate` 391 KB/frame** drill-down. First step is profiler expansion to identify the dominant LateUpdate scripts.
- [ ] **Tier 4 — EyesController + SpriteSkin.OnEnable log spam.** Defer until [[project_spine2d_migration]] — Spine migration deletes `SpriteSkin` and the cost dies with it.
- [ ] **Tier 4 — `MacroSimulator.SimulateOneHour` 24×=1×CatchUp EditMode test.** Threshold: before any third contributor adds a new step to `MacroSimulator`. Requires extracting pure-math helpers into `MWI.MacroSim.Pure` asmdef.
- [ ] **Tier 4 — `MacroSimulator.SimulateOneHour` per-hour `FindObjectsByType<MapController>`.** Threshold: when measured GC pressure during a skip becomes visible. Pass `MapController` through from `TimeSkipController` instead.
- [ ] **Tier 4 — `MapController.WakeUp()` ungated `Debug.Log` calls.** Drive-by. Gate the 9 logs behind `Verbose*` / `#if UNITY_EDITOR`. Newly reachable via `WakeUpFromSkip` from [[world-time-skip]].
- [ ] **Tier 4 — Farming visual layers collapse.** Single `CropHarvestable` per cell from plant-time; remove `CropVisualSpawner`. Threshold: before NPC AI farming OR before designer-facing rollout, whichever first. See [[farming]].

## Stakeholders
- [[kevin]] — decides when to invest.

## Links
- [[storage-furniture]]
- [[building-furniture-specialist]]
- [[npc-ai-specialist]]
- [[debug-tools-architect]]
- [[item-inventory-specialist]]
- [[network-specialist]]
- [[character-system-specialist]]
- [[project_spine2d_migration]]
- [[project_visual_migration_order]]
- [[host-progressive-freeze-debug-log-spam]]
- [[world-time-skip]]
- [[world-macro-simulation]]

## Change log
- 2026-04-25 — Initial deferral entry — `StorageVisualDisplay` per-peer culling. — claude
- 2026-04-26 — Added entry #2 (job logistics fps report) + dual-agent code audit + Tier 1 + Tier 2 shipped (Aₐ/G/F/D/A). — claude
- 2026-04-27 — Tier 3 shipped (B/Cₐ/Bₐ/E) + Tier 1/2 invalidation hooks wired through `FurnitureManager` + `CommercialBuilding.TrySpawnDefaultFurniture`. C and Dₐ permanently parked. — claude
- 2026-04-27 — Profiler session: identified `UI_CommercialBuildingDebugScript` (28% of frame, 633 GC.Allocs/frame) as remaining bottleneck. Disabling it reached the 60 FPS target. Entry #2 closed for current budget; Tier 4 deferrals captured (UI debug refactor, `CharacterActions.ActionTimerRoutine` Instantiate pooling, `PreLateUpdate.ScriptRunBehaviourLateUpdate` 391 KB drill-down, EyesController/SpriteSkin → defer to Spine migration). — claude
- 2026-04-27 — Added three Tier 4 deferrals from the Time Skip & Bed Furniture v1 implementation: (#5) `SimulateOneHour` 24×=1×CatchUp EditMode test pending asmdef extraction; (#6) `SimulateOneHour` per-hour `FindObjectsByType<MapController>` allocation; (#7) `MapController.WakeUp` ungated `Debug.Log` calls newly reachable via `WakeUpFromSkip`. — claude
- 2026-04-29 — Added Tier 4 deferral #8: collapse farming's two-layer visual model into a single `CropHarvestable`-per-cell after Kevin flagged "a crop is local to its own" during the [[farming]] integration playtest. Same critique echoed back to [[terrain-and-weather|VegetationGrowthSystem]]. — claude

## Sources
- 2026-04-25 conversation with Kevin — original deferral.
- [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) — class-level TODO comment.
- 2026-04-26 conversation with Kevin — job logistics fps report (3 transporters / 4 logistic managers / 2 harvesters / 1 vendor / 2 crafters → < 30 fps).
- 2026-04-26 dual-agent code audit ([[npc-ai-specialist]] + [[building-furniture-specialist]]) — read-only pass over `Assets/Scripts/World/Jobs/`, `Assets/Scripts/World/Buildings/Logistics/`, `Assets/Scripts/AI/GOAP/Actions/`, `Assets/Scripts/Character/CharacterAwareness.cs`, `Assets/Scripts/AI/NPCBehaviourTree.cs`. Findings cross-checked against the source by Claude.
- 2026-04-26 implementation pass (Claude, single session) — Tier 1 + Tier 2 shipped. Two `assets-refresh` checkpoints with zero compile errors / exceptions on either pass.
- 2026-04-27 implementation pass (Claude, single session) — Tier 3 shipped (minus C and Dₐ) + Tier 1/2 invalidation hooks wired. Five incremental `assets-refresh` checkpoints; one transient compile error caught on the first Cₐ pass (`MWI.Time` namespace clash with `UnityEngine.Time` — fixed via fully-qualified `UnityEngine.Time.time`); all subsequent passes clean.
- 2026-04-27 profiler session with Kevin (Unity Profiler, Play Mode, Deep Profile + Hierarchy view, ~170k frames captured). Headline measurements at the moment of the screenshots: frame ~31-37 ms, `PlayerLoop` 82-86 % of frame, total `GC.Alloc` ~145 KB-1.4 MB / frame across the session. `UI_CommercialBuildingDebugScript` identified as 28.9 % of frame / 9.98 ms self / 4 instances / 633 GC.Allocs / frame / 59 KB. After disabling it: FPS hit the 60 FPS target. Tier 4 follow-ups captured from the same screenshots (`CharacterActions.ActionTimerRoutine.Instantiate` 0.8 MB/frame; `PreLateUpdate.ScriptRunBehaviourLateUpdate` 391 KB/frame; `EyesController.BlinkRoutine` + `SpriteSkin.OnEnable` log spam 29 KB/frame).
- [[jobs-and-logistics]] / [[building-logistics-manager]] / [[character-job]] / [[ai-goap]] — wiki entries for the systems involved in the hot path.
- Verified files (with line ranges):
  - [Assets/Scripts/Character/CharacterAwareness.cs](../../Assets/Scripts/Character/CharacterAwareness.cs) — Tier 1 Aₐ.
  - [Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs) — Tier 2 A.
  - [Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs) — Tier 1 G.
  - [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — Tier 1 F + Tier 2 D + Tier 3 B (inventory dirty-marks) + Tier 1/2 invalidation hooks.
  - [Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs) — Tier 3 B (dirty flag).
  - [Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs) — Tier 3 B (early-exit gating).
  - [Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsStockEvaluator.cs) — Tier 3 E.
  - [Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs](../../Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs) — Tier 3 B (`MarkDispatchDirty` pass-through).
  - [Assets/Scripts/World/Buildings/FurnitureManager.cs](../../Assets/Scripts/World/Buildings/FurnitureManager.cs) — Tier 1/2 invalidation hooks.
  - [Assets/Scripts/World/Jobs/Job.cs](../../Assets/Scripts/World/Jobs/Job.cs) — Tier 3 Cₐ (`ExecuteIntervalSeconds`).
  - [Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs) — Tier 3 Cₐ override (0.3 s).
  - [Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs](../../Assets/Scripts/World/Jobs/HarvestingJobs/JobHarvester.cs) — Tier 3 Cₐ override (0.3 s).
  - [Assets/Scripts/AI/Actions/BTAction_Work.cs](../../Assets/Scripts/AI/Actions/BTAction_Work.cs) — Tier 3 Cₐ cadence gate.
  - [Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs) — Tier 3 Bₐ.
  - [Assets/Scripts/AI/NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs):46 — `_tickIntervalSeconds = 0.1f` (10 Hz BT tick).
