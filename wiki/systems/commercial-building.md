---
type: system
title: "Commercial Building"
tags: [building, commercial, jobs, tier-2]
created: 2026-04-19
updated: 2026-04-23
sources: []
related: ["[[building]]", "[[building-logistics-manager]]", "[[building-task-manager]]", "[[jobs-and-logistics]]", "[[shops]]", "[[crafting-loop]]", "[[worker-wages-and-performance]]", "[[dev-mode]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[shops]]", "[[crafting-loop]]", "[[worker-wages-and-performance]]"]
---

# Commercial Building

## Summary
`Building` subclass adding a **`BuildingTaskManager`** (blackboard for GOAP workers) and a **`BuildingLogisticsManager`** (order queue brain — facade over `LogisticsOrderBook` / `LogisticsTransportDispatcher` / `LogisticsStockEvaluator`). `InitializeJobs()` instantiates the abstract `Job[]`; `AskForJob(character)` handles volunteer employment (gated by `HasOwner` or `HasCommunityLeader`); `GetWorkPosition(character)` returns a unique per-`InstanceID` offset so workers don't stack. Subclasses that need autonomous restock additionally implement `IStockProvider` (currently `ShopBuilding` and `CraftingBuilding`).

## Key classes / files

| File | Role |
|---|---|
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Abstract base. Owner, jobs, task manager, logistics manager, `GetWorkPosition`. |
| `Assets/Scripts/World/Buildings/IStockProvider.cs` | Contract for autonomous restock + `StockTarget` struct. |
| `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs` | Implements `IStockProvider` by projecting `_itemsToSell`. |
| `Assets/Scripts/World/Buildings/CommercialBuildings/CraftingBuilding.cs` | Implements `IStockProvider` via Inspector-authored `_inputStockTargets`. |
| `Assets/Scripts/World/Buildings/BuildingTaskManager.cs` | See [[building-task-manager]]. |
| `Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs` | See [[building-logistics-manager]]. |

**In-flight physical-state helpers** (owned by `CommercialBuilding`, consumed by `BuildingLogisticsManager`):
- `GetWorldItemsInStorage()` — `Physics.OverlapBox` scan of the `StorageZone` collider. Drives `RefreshStorageInventory` and `GoapAction_LocateItem`'s fallback search.
- `RefreshStorageInventory()` — two-pass physical ↔ logical sync (remove ghosts / absorb orphans). Pass 1 protects any `ItemInstance` currently held by a `TransportOrder.ReservedItems` in `LogisticsManager.PlacedTransportOrders` to avoid killing in-flight transports during a transient `OverlapBox` miss.
- `CountUnabsorbedItemsInBuildingZone(ItemSO)` — counts matching WorldItems inside `BuildingZone` but not yet in `_inventory`, **plus** `ItemInstance`s currently held by this building's own assigned workers (equipment inventory + `HandsController.CarriedItem`). Used by `LogisticsTransportDispatcher.HandleInsufficientStock` to distinguish "items mid-transit to storage" from "items actually stolen" when a completed `CraftingOrder` exists without visible stock.

## IStockProvider contract

Any `CommercialBuilding` that wants autonomous restock implements `IStockProvider.GetStockTargets()`, returning `(ItemSO ItemToStock, int MinStock)` pairs. The logistics manager reads these on every `OnWorkerPunchIn` and places `BuyOrder`s when virtual stock (physical + in-flight) falls below the policy's reorder threshold. Yielding zero targets is fine — the evaluator no-ops.

- **`ShopBuilding`** projects its `_itemsToSell` (`ShopItemEntry { Item; MaxStock }`). Zero/negative `MaxStock` defaults to 5.
- **`CraftingBuilding`** exposes `[SerializeField] List<StockTarget> _inputStockTargets` and `IReadOnlyList<StockTarget> InputStockTargets`. Prior to the 2026-04-21 refactor, crafting buildings only requested materials after a `CraftingOrder` commission came in — the Layer A fix makes input restock proactive.

## Employment gating

`CommercialBuilding.AskForJob(character)` accepts a volunteer only if:
- `HasOwner` (a boss is already in place) **or** `HasCommunityLeader()` (a macro leader exists).
- The requested role is instantiated in the abstract `Jobs[]` (`InitializeJobs()` in the subclass).
- The role is vacant.
- `character.CharacterJob.DoesScheduleOverlap(requestedJob)` returns false.

Force-assignment bypasses consent: `CommunityTracker.ImposeJobOnCitizen()` → `CharacterJob.ForceAssignJob()` dissolves any overlapping job to make room.

## Work positioning

`GetWorkPosition(Character)` is virtual. Defaults to `GetRandomPointInBuildingZone()` with a per-`InstanceID` offset so multiple workers don't physically stack. `ShopBuilding` overrides for its vendor role to return the counter `VendorPoint`; all other roles wander.

## Zones (authored Inspector fields)

| Zone | Role |
|---|---|
| `StorageZone` | Interior inventory plot. `_inventory` physically sits here; `GetWorldItemsInStorage()` scans its collider. |
| `DeliveryZone` | Reachable destination-side drop point for incoming transporters. `GoapAction_MoveToDestination` targets this (falls back to `MainRoom` if null). |
| `PickupZone` (optional) | Reachable source-side pickup point for outgoing transporters. If authored, source-side `JobLogisticsManager` stages reserved items here via `GoapAction_StageItemForPickup`, and `GoapAction_MoveToItem` targets it instead of the raw WorldItem position. If null, transporter walks directly to the item (legacy behaviour). See [[building-logistics-manager]] for the staging flow and the NavMesh safety net. |

## Dependencies

### Upstream
- [[building]] — inheritance.

### Downstream
- [[shops]] — `ShopBuilding` subclass.
- [[crafting-loop]] — `CraftingBuilding` subclass.
- [[jobs-and-logistics]] — every commercial building holds its own logistics manager.

## Gotchas

- A commercial building with **no** owner and **no** community leader rejects every `AskForJob`. Predefined-map communities with empty `LeaderIds` still pass the gate (treated as "open city").
- `InitializeJobs()` must be called in `Awake()` / `OnNetworkSpawn()` path before any worker tries to punch in; subclasses that forget to populate `_jobs` break employment silently.
- If a subclass wants autonomous restock, **implementing `IStockProvider` is mandatory** — declaring `_itemsToSell` or `_inputStockTargets` alone does nothing until the contract is wired.

## Change log
- 2026-04-23 — Added optional `PickupZone` field for transporter pickup routing + NavMesh-based reachability safety net. Transporter no longer stalls when `StorageZone` is unreachable. See [[building-logistics-manager]] for the full flow. — claude
- 2026-04-22 — Wage and worklog hooks added: `WorkerStartingShift` records punch-in time + calls `CharacterWorkLog.OnPunchIn`; `WorkerEndingShift` calls `FinalizeShift` + `WageSystemService.ComputeAndPayShiftWage`; new owner-gated `TrySetAssignmentWage`. See [[worker-wages-and-performance]] — claude
- 2026-04-22 — Documented in-flight physical-state helpers (`GetWorldItemsInStorage`, `RefreshStorageInventory` with reserved-item protection, new `CountUnabsorbedItemsInBuildingZone` covering loose + worker-carried stock) — claude
- 2026-04-21 — Expanded from stub: IStockProvider contract, InputStockTargets on CraftingBuilding, facade reference. — claude
- 2026-04-19 — Stub. — Claude / [[kevin]]

## Sources
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) §5 — procedure.
- [.agent/skills/job_system/SKILL.md](../../.agent/skills/job_system/SKILL.md) §3.
- [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs).
- [IStockProvider.cs](../../Assets/Scripts/World/Buildings/IStockProvider.cs).
- [[building]] + [[jobs-and-logistics]].
