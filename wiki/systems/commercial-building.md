---
type: system
title: "Commercial Building"
tags: [building, commercial, jobs, tier-2]
created: 2026-04-19
updated: 2026-05-16
sources: []
related: ["[[building]]", "[[building-logistics-manager]]", "[[building-task-manager]]", "[[jobs-and-logistics]]", "[[shops]]", "[[crafting-loop]]", "[[worker-wages-and-performance]]", "[[quest-system]]", "[[tool-storage]]", "[[commercial-storage-roles]]", "[[commercial-treasury]]", "[[construction]]", "[[help-wanted-and-hiring]]", "[[management-panel-architecture]]", "[[dev-mode]]", "[[kevin]]"]
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

## Storage Roles (unified per-storage role system)

`CommercialBuilding` exposes a runtime, owner-driven storage-role system shared by every subtype (Forge, Shop, House, future workshops). Per-storage exclusivity: each `StorageFurniture` child carries one `StorageRoleType` (`None` / `ToolStorage` / `InventoryStorage` / optional subtype additions like `SellShelf`). Multiple storages can share the same role.

- `virtual IReadOnlyList<StorageRoleDescriptor> SupportedStorageRoles` — defaults to `StorageRoleCatalog.Generic`. `ShopBuilding` overrides to `StorageRoleCatalog.Shop` (adds `SellShelf`).
- `IReadOnlyList<StorageFurniture> GetStoragesWithRole(StorageRoleType type)` — walks every storage child (recursive, includes inactive).
- `IReadOnlyList<StorageFurniture> ToolStorages` / `InventoryStorages` — list accessors. `ShopBuilding.SellShelves` is a parallel wrapper for `SellShelf`.
- `FindToolStorageContaining(ItemSO)` / `FindToolStorageWithFreeSpace()` / `HasToolInAnyToolStorage(ItemSO)` / `IsToolStorage(StorageFurniture)` — multi-storage helpers used by logistics + GOAP.
- `event Action OnStorageRolesChanged` — fires on **every peer** when any child storage's role changes (driven by per-storage `OnRoleChanged` fan-out, bound at `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached`). UI subscribes for re-render.
- `[ServerRpc(RequireOwnership=false)] TrySetStorageRoleServerRpc(NetworkObjectReference, StorageRoleType)` — owner-only mutator. Validates: caller is the building's `Owner`, role is in `SupportedStorageRoles`, target resolves to a `StorageFurniture` child with a sibling `StorageFurnitureNetworkSync`. Writes through `sync.SetRoleServer(newRole)`. Rejected calls log a warning; missing-sync rejection logs an error.

See [[commercial-storage-roles]] for the full system page (data flow, persistence, UI tab).

## ToolStorage resolution (two-tier resolver, post-2026-05-09)

`CommercialBuilding.ToolStorage` is a singleton-shaped accessor backed by a two-tier resolver. Prefer the `ToolStorages` list / `FindToolStorage*` helpers in new code — `ToolStorage` exists for "first-found / convention" callers only.

1. **Role-tagged (Tier 0)** — first `StorageFurniture` child whose `Role == StorageRoleType.ToolStorage`. Set per-storage at design time via `_initialRole` on the storage prefab, or at runtime via the management panel dropdown. Multiple matches → first-found in child-walk order.
2. **First-crate convention fallback (Tier 1)** — `GetComponentInChildren<StorageFurniture>(includeInactive: false)`. The first storage child wins. Designers don't have to assign anything for buildings that just want "first crate" semantics — pre-role-system buildings keep working unchanged.

Returns null only when the building has no `StorageFurniture` children at all, in which case `HasToolStorage` is false and tool-needing GOAP actions fail-cleanly.

The legacy `_toolStorageFurniture` Inspector SerializeField + its snapshot/rebind machinery were removed 2026-05-09 (audit showed every prefab had it as `fileID: 0`). `HelpWantedSign` and `ManagementFurniture` still use the three-tier lazy-rebind pattern in `ResolveLazyFurnitureRef<T>` — only the tool-storage path was simplified.

`virtual IEnumerable<ItemSO> GetToolStockItems()` is the subclass extension point used by logistics routing. Default yields nothing (no tools). When a `JobLogisticsManager` worker drops off an item matching one of these and `ToolStorage` is available, the deposit is redirected from the loose `StorageZone` into the tool storage furniture — see `IsBuildingToolItem(ItemSO)` and the `FindStorageFurnitureForItem` / `GoapAction_GatherStorageItems.DetermineStoragePosition` routing in [[building-logistics-manager]]. `FarmingBuilding` overrides this and yields its `WateringCanItem`.

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

## Default furniture spawn

See [[building#Default furniture layout]] for the system (it now lives at the `Building` level). `CommercialBuilding` overrides `OnDefaultFurnitureSpawned()` to invalidate the storage furniture cache after the layout spawns AND to fire the Treasury seed (see below).

## Treasury seed flow

The `BaseTreasury` integer on `BuildingCommercialSO` seeds the building's Treasury-role
[[commercial-treasury|SafeFurniture]] once, on construction-complete, via `CommercialBuilding.OnDefaultFurnitureSpawned`
calling the `SeedTreasuryIfNeeded()` helper. Currency is resolved at that moment from
`MapController.NativeCurrency` (which reads `CommunityData.NativeCurrency`); buildings
placed outside any `MapController` fall back to `CurrencyId.Default`. The seed runs in
all four spawn paths: cooperative finalize (see [[construction]]), `_spawnAsComplete`
designer flag, debug `BuildInstantly`, and `RestoreFromSaveData` Complete-branch.
Re-credit on reload is prevented by `BuildingSaveData.TreasurySeeded`, a server-only
boolean flag mirroring the `_treasurySeeded` runtime field — clients never read it.
Crediting itself delegates to [[commercial-treasury|CreditTreasury]] which picks the
largest Treasury-role safe (first-found ordering today).

`Building.SpawnDefaultFurnitureSlot` defaults `slot.TargetRoom` to `MainRoom` when null — without this, late-authored slots silently spawned under the building root without grid registration, and the LogisticsManager + crafting pipeline relied on `_furnitures` registration for storage lookups. Designers can still set `slot.TargetRoom` explicitly. `Building.Start` also explicitly invokes `FurnitureManager.LoadExistingFurniture()` for the building's `MainRoom` because `Room.Start` runs the same defensive rescan but is `private` — without the explicit call the Building's own MainRoom rescan never happens (the Building class is itself the MainRoom via `ComplexRoom` inheritance).

## Quest auto-claim — player-only path

`WorkerStartingShift` calls `TryAutoClaimExistingQuests(worker)` and `SubscribeWorkerQuestAutoClaim(worker)` ONLY when `worker.IsPlayer()`. NPC workers use GOAP's `ClaimBestTask<T>` on demand instead. Without this gate, the first NPC to subscribe hoarded every newly-published `BuildingTask` via the multicast `OnQuestPublished` event order, leaving subsequent NPCs idle (a single `JobFarmer` would scoop every `PlantCropTask` and `HarvestResourceTask` the moment they were registered, and the second farmer's worldState would report zero work). Players still get auto-claim so quests they accept by punching in show up in their `CharacterQuestLog` UI without an extra interaction step.

## Owner management panel — polymorphic tabs

- `building.GetManagementTabs()` → `IReadOnlyList<MWI.UI.Management.IManagementTab>` (virtual). Returns `[HiringTab]` on the base. Subtypes override to append more owner-only admin tabs (Open/Closed Principle, rule #10). Allocates per-panel-open only.

Subtypes append tabs without modifying the [[management-panel-architecture|UI_OwnerManagementPanel]]:

```csharp
public override IReadOnlyList<IManagementTab> GetManagementTabs()
{
    var tabs = new List<IManagementTab>(base.GetManagementTabs());
    tabs.Add(new MyFeatureTab(this));
    return tabs;
}
```

Always call `base.GetManagementTabs()` first so the Hiring tab is preserved. See [[management-panel-architecture]] for the full polymorphic contract; see [.agent/skills/management-panel/SKILL.md](../../.agent/skills/management-panel/SKILL.md) for procedural how-to.

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
- 2026-05-16 — Treasury seed flow: BuildingCommercialSO.BaseTreasury → OnDefaultFurnitureSpawned override → CreditTreasury. Currency resolved from MapController.NativeCurrency at credit time. Persisted via BuildingSaveData.TreasurySeeded. — claude
- 2026-05-14 — Added private `DoSetStorageRole(StorageFurniture, StorageRoleType)` server-only mutator + `internal bool TrySetStorageRoleServer(...)` validated wrapper. Both `TrySetStorageRoleServerRpc` (player UI) and `BuildingLogisticsManager.AssignStorageRolesForShift` (NPC shift-punch) funnel through `DoSetStorageRole`, eliminating future-divergence risk between the two role-write paths. `SupportedStorageRoles` subtype filter lives inside `TrySetStorageRoleServer` — same rule the RPC enforces. Playtest-confirmed. See [[commercial-storage-roles]]. — claude
- 2026-05-14 — `WorkerStartingShift` now invokes `LogisticsManager.AssignStorageRolesForShift()` (server-only, wrapped in try/catch per rule #31) inside the `!IsWorkerOnShift` branch — one call per actual shift entry. Applies the unified storage-role rule (tool-priority → shelf-priority on shops → inventory default). Idempotent re-runs are zero-cost. Also added `CommercialBuilding.GetStorageFurnitureOrdered()` public accessor (thin read-only wrapper around the existing `GetStorageFurnitureCached`) so the logistics layer can address the same cached/ordered set without re-walking rooms. See [[commercial-storage-roles]] for the full rule + idempotency contract. — claude
- 2026-05-09 (later) — Owner / employee restoration split. The combined `RestoreFromSaveData(List<string> ownerIds, List<EmployeeSaveEntry> employees)` was decomposed: the **owner** half lifted to base [[building]] (`Building.RestoreOwnersFromSaveData` + virtual `BindRestoredOwner` hook), commercial-side override calls `SetOwner(owner, ownerJob, autoAssignJob:false)` AND consumes the matching saved `EmployeeSaveEntry` from `_pendingEmployees` to recover the boss's actual job slot; the **employee** half stays here as `RestoreEmployeesFromSaveData(List<EmployeeSaveEntry>)`. Both subscribe to `Character.OnCharacterSpawned` AND `Character.OnCharacterIdReassigned` (the latter closes the host-player UUID timing window — see [[host-player-uuid-timing-on-load]]). Routed by `MapController.ApplyDynamicSaveDataToBuilding(Building, BuildingSaveData)` — owners FIRST so the override can claim the boss's employee entry before the employee pass runs. Order is critical for the auto-assigned-job suppression. — claude
- 2026-05-09 — Removed dead `_toolStorageFurniture` Inspector SerializeField + its snapshot/rebind machinery (audit showed every prefab had it as `fileID: 0`). `ToolStorage` resolver simplified from four tiers to two (role-tagged → first-crate convention). `HelpWantedSign` and `ManagementFurniture` still use the three-tier lazy-rebind pattern. — claude
- 2026-05-09 — Multi-tool-storage refactor: tool/inventory storages are now LISTS, not singletons. Added `ToolStorages` / `InventoryStorages` accessors + `FindToolStorageContaining` / `FindToolStorageWithFreeSpace` / `HasToolInAnyToolStorage` / `IsToolStorage` helpers. All consumers iterate the lists. `OnStorageRolesChanged` now fires on every peer via per-storage `OnRoleChanged` fan-out (`HandleChildStorageRoleChanged`), bound at `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached`. — claude
- 2026-05-08 — Added unified storage-role API: `SupportedStorageRoles` virtual + `GetStoragesWithRole` walker + `OnStorageRolesChanged` event + `[ServerRpc] TrySetStorageRoleServerRpc` (owner-only mutator). Base `GetManagementTabs()` now appends `StorageRolesTab` so every commercial subtype (Forge, Shop, House, …) gets the same per-storage role-assignment UI. `ToolStorage` getter promoted to a four-tier resolver — Tier 0 is now `GetStoragesWithRole(StorageRoleType.ToolStorage).FirstOrDefault()`, demoting the legacy `_toolStorageFurniture` Inspector field to Tier 1 fallback. See [[commercial-storage-roles]] for the full system page. — claude
- 2026-05-07 — Added `GetManagementTabs()` virtual — polymorphic surface for the new owner management panel. Subtypes append tabs without modifying the panel. See [[management-panel-architecture]]. — claude
- 2026-05-02 — Farmer end-to-end rollout (cascade, IsValid corrections, softlock guards, race detection, etc.) — claude. Touchpoints with `CommercialBuilding`: (a) `ToolStorage` becomes a three-tier resolver — cached field → snapshot lazy-rebind → first-`StorageFurniture` child fallback. Designers no longer have to assign anything in the inspector. Same lazy-snapshot pattern formalised on `_helpWantedFurniture` and `_managementFurniture` via the new `ResolveLazyFurnitureRef<T>` helper. (b) New `virtual IEnumerable<ItemSO> GetToolStockItems()` extension point + `IsBuildingToolItem(ItemSO)` classifier — drives the tool-aware logistics routing in `FindStorageFurnitureForItem` and `GoapAction_GatherStorageItems.DetermineStoragePosition`. Default yields nothing; `FarmingBuilding` yields its `WateringCanItem`. (c) `WorkerStartingShift` quest auto-claim is now player-only (`worker.IsPlayer()` gate) — NPCs use GOAP's `ClaimBestTask` on demand, so the first NPC to subscribe no longer hoards every newly-published task. (d) `Building.SpawnDefaultFurnitureSlot` defaults `slot.TargetRoom` to `MainRoom` when null and `Building.Start` calls `FurnitureManager.LoadExistingFurniture()` explicitly — together they guarantee spawned default furniture is FurnitureManager-registered. — claude
- 2026-05-01 — `_defaultFurnitureLayout` system hoisted up to `[[building]]`. `CommercialBuilding` now only carries the `OnDefaultFurnitureSpawned` override (storage cache invalidation). See [[building#Default furniture layout]]. — claude
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
