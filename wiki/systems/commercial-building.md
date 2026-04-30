---
type: system
title: "Commercial Building"
tags: [building, commercial, jobs, tier-2]
created: 2026-04-19
updated: 2026-04-30
sources: []
related: ["[[building]]", "[[building-logistics-manager]]", "[[building-task-manager]]", "[[jobs-and-logistics]]", "[[shops]]", "[[crafting-loop]]", "[[worker-wages-and-performance]]", "[[quest-system]]", "[[tool-storage]]", "[[help-wanted-and-hiring]]", "[[dev-mode]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Buildings/"
depends_on: ["[[building]]"]
depended_on_by: ["[[jobs-and-logistics]]", "[[shops]]", "[[crafting-loop]]", "[[worker-wages-and-performance]]", "[[quest-system]]"]
---

# Commercial Building

## Summary
`Building` subclass adding a **`BuildingTaskManager`** (blackboard for GOAP workers) and a **`BuildingLogisticsManager`** (order queue brain — facade over `LogisticsOrderBook` / `LogisticsTransportDispatcher` / `LogisticsStockEvaluator`). `InitializeJobs()` instantiates the abstract `Job[]`; `AskForJob(character)` handles volunteer employment (gated by `HasOwner` or `HasCommunityLeader`); `GetWorkPosition(character)` returns a unique per-`InstanceID` offset so workers don't stack. Subclasses that need autonomous restock additionally implement `IStockProvider` (currently `ShopBuilding` and `CraftingBuilding`).

## Key classes / files

| File | Role |
|---|---|
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Abstract base. Owner, jobs, task manager, logistics manager, `GetWorkPosition`, `TimeClock` lookup, `RequestPunchAtTimeClockServerRpc`. |
| `Assets/Scripts/World/Furniture/TimeClockFurniture.cs` | Typed marker furniture — authored inside a CommercialBuilding's prefab/scene so the building can find its punch station via `GetComponentInChildren<TimeClockFurniture>()`. |
| `Assets/Scripts/Interactable/TimeClockFurnitureInteractable.cs` | Player + NPC entry point: `Interact` routes to a ServerRpc (client) or `RunPunchCycleServerSide` (server/NPC), which picks `Action_PunchIn` / `Action_PunchOut` from shift state. |
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

## Default furniture spawn (`_defaultFurnitureLayout`)

`CommercialBuilding` carries an authored `List<DefaultFurnitureSlot>` (each slot: `FurnitureItemSO`, `Vector3 LocalPosition` in building-local space, `Vector3 LocalEulerAngles`, `Room TargetRoom`). On the server-side branch of `OnNetworkSpawn`, `TrySpawnDefaultFurniture()` instantiates each slot's `InstalledFurniturePrefab`, calls `NetworkObject.Spawn()`, parents under the **building root** (the only NetworkObject in this hierarchy — see [[network]] §B and `.agent/skills/multiplayer/SKILL.md` §10), then calls `TargetRoom.FurnitureManager.RegisterSpawnedFurnitureUnchecked` to record grid + furniture-list membership without re-parenting.

**Why this exists:** baking a furniture instance whose prefab carries a `NetworkObject` into a runtime-spawned building prefab makes NGO half-register the child during the parent's spawn — the child ends up in `SpawnManager.SpawnedObjectsList` with a null `NetworkManagerOwner` and NRE's `NetworkObject.Serialize` during the next client-sync, breaking client approval entirely. The runtime-spawn path produces the same end-state without the half-spawn class.

**Authoring rule:** only NetworkObject-FREE furniture (e.g. `TimeClock`, which strips its NO via `m_RemovedComponents` in the prefab variant) may be nested directly in a building prefab. Anything network-bearing — `CraftingStation`, `Bed` — must move to `_defaultFurnitureLayout` with `TargetRoom` set. `Forge.prefab` is the canonical example: one slot pointing at the CraftingStation `FurnitureItemSO` at local position `(-7, 0, 4.9582)` with `TargetRoom = Room_Main`.

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
- 2026-04-30 — Hiring API: `_isHiring` NetworkVariable + `_helpWantedFurniture` reference + `TryOpenHiring` / `TryCloseHiring` / `CanRequesterControlHiring` / `GetVacantJobs` / `GetHelpWantedDisplayText` (virtual). `InteractionAskForJob.CanExecute` and `BuildingManager.FindAvailableJob` gate on `IsHiring` so closed buildings reject applications. `AssignWorker` + `CharacterJob.QuitJob` call `HandleVacancyChanged` to refresh the Help Wanted sign on hire/quit churn. `GetJobStableIndex(Job)` exposes stable indices for ServerRpc round-trip. See [[help-wanted-and-hiring]]. — claude
- 2026-04-29 — Added `_toolStorageFurniture` designer reference + `WorkerCarriesUnreturnedTools(Character, out List<ItemInstance>)` server-side scan + `NotifyPunchOutBlockedClientRpc` (targeted ClientRpc to player workers, raises `UI_ToolReturnReminderToast`) + `NotifyPunchOutBlockedToClient` server-side wrapper. Foundation for the [[tool-storage]] primitive (Plan 1 of Farmer rollout). — claude
- 2026-04-25 — Fixed `_defaultFurnitureLayout` registrations being silently wiped by `Room.Start` / `Room.OnNetworkSpawn`. `FurnitureManager.LoadExistingFurniture` is now additive: it prunes fake-null entries and merges any new transform child into `_furnitures` instead of replacing the list with `GetComponentsInChildren<Furniture>(true)`. Previous replace-style rescan would clobber `RegisterSpawnedFurnitureUnchecked` writes (the spawned furniture lives on the building root, not under the target room's transform — so the rescan saw an empty room and reset the list). Symptom on the Forge: `Room_Main.FurnitureManager.Furnitures` empty after placement; crafting only worked through `CraftingBuilding.GetCraftableItems`'s transform-tree fallback, which logged a one-shot warning. — claude
- 2026-04-25 — Added `_defaultFurnitureLayout` (`List<DefaultFurnitureSlot>`) + `TrySpawnDefaultFurniture()` server-side runtime-spawn path. Replaces the previous nested-furniture-with-NetworkObject pattern that was half-spawning during NGO sync and silently aborting client connection-approval. New `FurnitureManager.RegisterSpawnedFurnitureUnchecked(furniture, worldPos)` helper bypasses `CanPlaceFurniture` validation (server-authored content is trusted) and skips the SetParent step (caller must already have parented under a valid NetworkObject ancestor — Room_Main is a non-NO so SetParent under it would throw `InvalidParentException`). Forge prefab updated as the canonical example. — claude
- 2026-04-24 — Shift roster now single-sourced from the replicated `_activeWorkerIds` `NetworkList<FixedString64Bytes>`. Removed the parallel server-only `_activeWorkersOnShift : List<Character>` — it made `ActiveWorkersOnShift` return empty on remote clients, which silently broke the Time Clock UI / `UI_CommercialBuildingDebugScript` / `BTCond_NeedsToPunchOut` across peers. `ActiveWorkersOnShift` is now a materialiser that walks `_activeWorkerIds` via `Character.FindByUUID`; `IsWorkerOnShift` is the allocation-free containment check. `BTAction_Work`, `BTAction_PunchOut`, `BTCond_NeedsToPunchOut`, and `UI_CommercialBuildingDebugScript` were migrated to `IsWorkerOnShift` for both correctness on clients and fewer per-tick allocations. — claude
- 2026-04-24 — Physical punch-in: `TimeClockFurniture` + `TimeClockFurnitureInteractable` added. Both players and NPCs must interact with a Time Clock to punch; `BTAction_Work` / `BTAction_PunchOut` now target `workplace.TimeClock.GetInteractionPosition()` with a soft fallback to zone-punch when no clock is authored. New `CommercialBuilding.RequestPunchAtTimeClockServerRpc` routes player clients; `WorkerStartingShift` / `WorkerEndingShift` carry `!IsServer` defence-in-depth guards. Spec: `docs/superpowers/specs/2026-04-24-time-clock-furniture-design.md`. — claude
- 2026-04-23 — Quest aggregator: `GetAvailableQuests`, `GetQuestById`, `ResolveIssuer` (LogisticsManager Worker > Owner > null), `PublishQuest` stamps Issuer + OriginMapId. `WorkerStartingShift` auto-claims eligible quests for on-shift workers + subscribes for future publications. See [[quest-system]]. — claude
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
