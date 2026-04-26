---
type: project
title: "Optimisation Backlog"
tags: [optimisation, performance, backlog, deferred-work]
created: 2026-04-25
updated: 2026-04-26
sources: []
related: ["[[storage-furniture]]", "[[jobs-and-logistics]]", "[[building-logistics-manager]]", "[[character-job]]", "[[ai-goap]]"]
status: active
confidence: high
start_date: 2026-04-25
target_date: null
---

# Optimisation Backlog

## Summary
Catch-all tracker for performance / scalability / culling work that's been **deliberately deferred** to keep current features unblocked. Each entry names the system, the trade-off being held open, and what "good enough" looks like when we eventually pick it up. Anything that lives here is a known compromise — not a forgotten bug.

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

> **Status (2026-04-26):** **Tier 1 + Tier 2 SHIPPED.** Aₐ + G + F + D + A all landed and compile clean — the highest-confidence, lowest-risk subset that wins regardless of profiler outcome. **Mandatory profiler-pass NOW** to confirm the predicted gains and to decide whether Tier 3 (B, C, Bₐ, Cₐ, Dₐ, Eₐ) is worth the risk. See `## Shipped 2026-04-26` subsection below for what's in / what's out.

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

#### Deferred to Tier 3 (need profiler data first)

- **B** — Event-driven `ProcessActiveBuyOrders` (dirty-flag gating). Highest expected impact in the building layer but biggest integration surface — must mark dirty on every state change site (reservation cancel, player drops, Pass-2 absorption). Wait for profiler to confirm `ProcessActiveBuyOrders` self-time is dominant.
- **C** — Incremental reservation tracking on `LogisticsOrderBook`. Redundant if B alone gets us under 16 ms.
- **E** — Per-tick virtual-stock cache. Lower priority — `CheckStockTargets` is punch-in-only today.
- **Bₐ** — Event-driven loose-item tracking on `GoapAction_GatherStorageItems`. Pairs with D's invalidation hook; need to wire enter/exit zone events first.
- **Cₐ** — Stagger `Job.Execute` cadence (10 Hz → 2-3 Hz on heavy phases). Needs per-action audit (some assume 10 Hz).
- **Dₐ** — Pool `GoapAction` instances. Highest risk — exact concern the `JobLogisticsManager.cs:173-178` comment block warns against. Do this last with full profiler diff.
- **Eₐ** — Gate the 3 environmental BT conditions. Subsumed by Aₐ (already shipped).

#### Until then
Keep recommended worker counts low for demos; flag this in any tutorial / sample save.

#### Owner
- **Building / logistics side (A-G):** [[building-furniture-specialist]]. Can ship as one PR independent of the AI side.
- **AI / worker-loop side (Aₐ-Fₐ):** [[npc-ai-specialist]]. Can ship independent of the building side; **start with Aₐ — biggest single-call ROI in the entire codebase.**
- **Coordination needed for Bₐ + D** (event-driven loose-item tracking spans both layers).

## Milestones
- [ ] StorageVisualDisplay per-peer culling — no fixed date; pick up when shelf-count perf becomes measurable, OR when player-count testing shows the always-on cost hurts.
- [x] **Tier 1 + Tier 2 shipped 2026-04-26** — Aₐ (CharacterAwareness cache + Debug.Log delete), G (ShopBuilding.ItemsToSell cache), F (Physics.OverlapBoxNonAlloc swap), D (StorageFurniture cache), A (CraftingBuilding.GetCraftableItems cache + HashSet ProducesItem). Compile clean, network-safe, server-only state.
- [ ] Profiler pass on the same worker mix (3 transporters / 4 logistic managers / 2 harvesters / 1 vendor / 2 crafters) — measure before/after Tier 1 + Tier 2 to confirm gains and decide whether Tier 3 is worth the risk.
- [ ] Tier 3 — B / C / Bₐ / Cₐ / Dₐ. Only ship items the profiler proves are still costing real ms after Tier 1 + Tier 2.

## Stakeholders
- [[kevin]] — decides when to invest.

## Links
- [[storage-furniture]]
- [[building-furniture-specialist]]

## Sources
- 2026-04-25 conversation with Kevin — original deferral.
- [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) — class-level TODO comment.
- 2026-04-26 conversation with Kevin — job logistics fps report (3 transporters / 4 logistic managers / 2 harvesters / 1 vendor / 2 crafters → < 30 fps).
- 2026-04-26 dual-agent code audit ([[npc-ai-specialist]] + [[building-furniture-specialist]]) — read-only pass over `Assets/Scripts/World/Jobs/`, `Assets/Scripts/World/Buildings/Logistics/`, `Assets/Scripts/AI/GOAP/Actions/`, `Assets/Scripts/Character/CharacterAwareness.cs`, `Assets/Scripts/AI/NPCBehaviourTree.cs`. Findings cross-checked against the source by Claude.
- 2026-04-26 implementation pass (Claude, single session) — Tier 1 + Tier 2 shipped. Two `assets-refresh` checkpoints with zero compile errors / exceptions on either pass.
- [[jobs-and-logistics]] / [[building-logistics-manager]] / [[character-job]] / [[ai-goap]] — wiki entries for the systems involved in the hot path.
- Verified files (with line ranges):
  - [Assets/Scripts/Character/CharacterAwareness.cs](../../Assets/Scripts/Character/CharacterAwareness.cs):20-76 — line 72 ungated `Debug.Log`.
  - [Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs):49-115 — `GetCraftableItems` with intentional fallback walk.
  - [Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsTransportDispatcher.cs):63-119 — `ProcessActiveBuyOrders` per-tick path.
  - [Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobLogisticsManager.cs):121-161 — `Execute` calling dispatcher every BT tick unconditionally.
  - [Assets/Scripts/AI/NPCBehaviourTree.cs](../../Assets/Scripts/AI/NPCBehaviourTree.cs):46 — `_tickIntervalSeconds = 0.1f` (10 Hz BT tick).
