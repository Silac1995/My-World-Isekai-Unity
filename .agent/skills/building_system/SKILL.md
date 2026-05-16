---
name: building-system
description: Architecture of buildings, rooms, zones, and the furniture grid system.
---

# Building & Zone Architecture

The building system in My-World-Isekai relies on a nested hierarchy of areas that define physical space, track character presence, and manage internal objects like furniture.

## Architectural Hierarchy

```mermaid
classDiagram
    Zone <|-- Room
    Room <|-- ComplexRoom
    ComplexRoom <|-- Building

    class Zone {
        +BoxCollider _boxCollider
        +HashSet<GameObject> _charactersInside
        +GetRandomPointInZone()
    }
    class Room {
        +HashSet<Character> _roomOwners
        +HashSet<Character> _roomResidents
        +FurnitureManager FurnitureManager
        +FurnitureGrid Grid
    }
    class ComplexRoom {
        +List<Room> _subRooms
        +GetRoomAt(Vector3)
        +FindAvailableFurniture()
    }
    class Building {
        +string buildingName
        +BuildingType _buildingType
        +BuildingState CurrentState
        +Collider _buildingZone
        +Zone _deliveryZone
        +AttemptInstallFurniture()
        +ContributeMaterial()
        +BuildInstantly()
    }
    class CommercialBuilding {
        +BuildingTaskManager TaskManager
        +BuildingLogisticsManager LogisticsManager
    }
```

### 1. Zone (`Zone.cs`)
The foundational class for any demarcated area.
- **Physical Representation:** Requires a `BoxCollider` and `NavMeshModifierVolume`. 
- **Tracking:** Automatically tracks characters inside (`_charactersInside`) using `OnTriggerEnter` and `OnTriggerExit`.
- **Utility:** Can return random valid NavMesh points within its bounds.

### 2. Room (`Room.cs`)
An enclosed space within the game world.
- **Inheritance:** Inherits from `Zone`.
- **Ownership:** Tracks specific `Owners` and `Residents`.
- **Furniture Management:** Acts as the root for furniture, requiring a `FurnitureManager` and `FurnitureGrid` component. Uses its `BoxCollider` to initialize the bounds of the `FurnitureGrid`.

### 3. ComplexRoom (`ComplexRoom.cs`)
A room that contains smaller nested sub-rooms.
- **Composition:** Maintains a list of sub-`Room`s (`_subRooms`).
- **Recursive Logic:** Overrides character tracking, ownership, and furniture queries to check its own components as well as all nested sub-rooms.

### 4. Building (`Building.cs`)
The top-level structure in the world.
- **Inheritance**: Inherits from `ComplexRoom`. Sub-rooms typically act as the specific floors or separated areas of the building.
- **Management**: Registers itself globally with the `BuildingManager` on `Start()`.
- **Construction & States**: Buildings manage a native `CurrentState` (`BuildingState.UnderConstruction` or `Complete`). They can require `_constructionRequirements` (a list of `CraftingIngredient`s) to be placed. The **Construction Loop** (see dedicated section below) drives gameplay-side completion: a server-only `ConstructionSiteScanner` watches items dropped in `_buildingZone` and updates `ConstructionProgress` + `DeliveredMaterials` NetworkVariables; the owner queues `CharacterAction_FinishConstruction` via `BuildingInteractable`, which consumes items per tick and calls `Building.Finalize()` when progress hits 1. `ContributeMaterial(ItemSO, amount)` is the underlying server-side ledger increment used by the action. `BuildInstantly()` bypasses the loop for debug. Empty `_constructionRequirements` skips construction entirely (spawns directly as `Complete`).
- **Logistics Integration**: Holds a reference to a `_deliveryZone` which is essential for the Logistics cycle.
- **Public access**: Has an outer `_buildingZone` (distinct from the main interior) for general traversal and random roaming around the property.
- **Dynamic Identity**: Buildings expose a unique `NetworkBuildingId` GUID used to link the building to its interior map record (`BuildingInteriorRegistry`) and any persisted state. **Generation strategy is split by origin:**
  - **Scene-authored buildings** (no `PlacedByCharacterId`): `OnNetworkSpawn` derives a *deterministic* GUID from `MD5(scene name + world position rounded to mm)` so the same scene building keeps the same `BuildingId` across reloads — without this, every reload would generate a fresh ID and orphan the saved interior record.
  - **Runtime-placed buildings** (`BuildingPlacementManager`): `BuildingPlacementManager.RequestPlacementServerRpc` sets `PrefabId` + `PlacedByCharacterId` **before** `netObj.Spawn()` so they ride in the initial NetworkVariable payload AND are observable inside `Building.OnNetworkSpawn`. With `PlacedByCharacterId` non-empty, `OnNetworkSpawn` rolls a fresh `Guid.NewGuid()` — that GUID then round-trips through `BuildingSaveData` on save.
- **Prefab ID**: The `PrefabId` string is used for registry lookups in `WorldSettingsData` but is NOT unique per instance.

### 5. Commercial Building (`CommercialBuilding.cs`)
A specialized structural entity handling jobs and economic tasks.

#### Furniture-reference resolution (three-tier lazy-rebind, 2026-05-02)

**Multi-storage tool/inventory accessors (2026-05-09).** The role system supports multiple storages per role. `CommercialBuilding` exposes both list accessors and a singleton fallback:

- `IReadOnlyList<StorageFurniture> ToolStorages` — every storage child whose `Role == StorageRoleType.ToolStorage`. Use this for any "iterate every tool storage" pattern (logistics, GOAP).
- `IReadOnlyList<StorageFurniture> InventoryStorages` — every storage with `Role == InventoryStorage`.
- `StorageFurniture FindToolStorageContaining(ItemSO tool)` — first tool storage holding the item; falls back to the convention singleton when no role-assigned tool storage matches.
- `StorageFurniture FindToolStorageWithFreeSpace()` — first non-full + non-locked tool storage (used by return-tool flow).
- `bool HasToolInAnyToolStorage(ItemSO tool)` — any-of predicate (used by `JobFarmer` worldState).
- `bool IsToolStorage(StorageFurniture s)` — predicate that returns true if `s.Role == ToolStorage` OR (no role-tagged tool storages exist AND `s` is the convention-resolved first-crate). Used by `StorageFurniture.AddItem`'s tool-stamp clearing hook.
- `StorageFurniture ToolStorage` (singleton, two-tier resolver) — returns first role-tagged tool storage, then falls back to first-crate convention. Prefer the list-based helpers in new code.

**`ToolStorage` two-tier resolver (2026-05-09 — `_toolStorageFurniture` Inspector field + snapshot/rebind machinery removed):**

1. **Role-tagged** — first `StorageFurniture` child whose `Role == StorageRoleType.ToolStorage`. Set per-storage at design time via `_initialRole` or at runtime via the management panel dropdown.
2. **First-crate convention** — `GetComponentInChildren<StorageFurniture>(includeInactive: false)`. The first storage child wins. Designers don't have to assign anything for buildings that just want "the first crate" semantics — pre-role-system buildings keep working unchanged.

`HelpWantedSign` and `ManagementFurniture` still use the three-tier lazy-rebind resolver below — they're not multi-instance and didn't need the role-system simplification.

**Three-tier lazy-rebind resolver** (used by `HelpWantedSign` and `ManagementFurniture`):

1. **Cached field still alive** — return the field directly when the reference is non-null.
2. **Snapshot-based rebind** — `Awake` snapshots the inspector-assigned furniture's `(FurnitureItemSO + buildingLocalPosition)` BEFORE `base.Awake` runs `ConvertNestedNetworkFurnitureToLayout` (which destroys the original nested children). The lazy resolver then scans children for the closest `(SO, localPos)` match within `FurnitureRefMatchEpsilon` and rebinds. Pattern lives in `CommercialBuilding.ResolveLazyFurnitureRef<T>`.
3. **No third tier for these** — they fail-cleanly to null when the building has no help-wanted sign / management furniture. Workers + UI handle the null gracefully.

`ToolStorages.Count == 0 && ToolStorage == null` is the only "no tool storage" state (i.e. no `StorageFurniture` child at all), in which case `HasToolStorage` is false and tool-needing GOAP actions (`GoapAction_FetchToolFromStorage`, `GoapAction_ReturnToolToStorage`) fail-cleanly.

**`virtual IEnumerable<ItemSO> GetToolStockItems()` extension point.** Default yields nothing. Override on subclasses that own tools — `FarmingBuilding` yields its `WateringCanItem`. When a `JobLogisticsManager` worker drops off an item matching one of these, `FindStorageFurnitureForItem` iterates `ToolStorages` (and falls back to the legacy singleton) so the deposit consolidates in the tool drawer(s) instead of getting first-fit-scattered into general inventory chests. `IsBuildingToolItem(ItemSO)` is the membership-check classifier wired into `FindStorageFurnitureForItem` and `GoapAction_GatherStorageItems.DetermineStoragePosition`. **Don't cache `building.ToolStorage.GetComponent<InteractableObject>()` at action construction** — the role can flip at runtime via the management dropdown. The cycle actions (`GoapAction_FetchToolFromStorage` / `GoapAction_ReturnToolToStorage`) re-resolve via `FindToolStorageContaining` / `FindToolStorageWithFreeSpace` per call.
- **Task Manager (`BuildingTaskManager`)**: Automatically attached module serving as a Blackboard. Manages a pool of `BuildingTask` objects. Instead of workers using expensive polling (raycasts/overlaps), tasks are registered here to be claimed sequentially using OCP-compliant logic (Open/Closed Principle) for dynamic behavior (e.g., Harvesters claiming trees).
- **Logistics Manager (`BuildingLogisticsManager`)**: Automatically attached **facade** over three plain-C# collaborators (`LogisticsOrderBook`, `LogisticsTransportDispatcher`, `LogisticsStockEvaluator`, all under `Assets/Scripts/World/Buildings/Logistics/`). The public API on the facade is stable — external callers (`JobLogisticsManager`, `InteractionPlaceOrder`, `GoapAction_PlaceOrder`, etc.) do not know about the split. Sub-components are reachable via `OrderBook`, `Dispatcher`, `Evaluator` properties for tests/tooling. See [`logistics-cycle` SKILL](../logistics_cycle/SKILL.md) for the order lifecycle, policy SO, and diagnostics details.
- **Stocking contract (`IStockProvider`)**: Any `CommercialBuilding` that wants autonomous restock implements `IStockProvider.GetStockTargets()`, returning `(ItemSO, MinStock)` pairs. The evaluator reads these on every `OnWorkerPunchIn` and places `BuyOrder`s when the virtual stock (physical + in-flight) falls below the pluggable `LogisticsPolicy`'s reorder threshold. Shipping implementers: `ShopBuilding` (projects `_itemsToSell`) and `CraftingBuilding` (`_inputStockTargets` — authored per-prefab in the Inspector, was added by the Layer A fix that stopped idle forges from sitting on empty input bins).

---

## The Furniture System

The interior of a `Room` is subdivided into a logical grid where objects can be placed.

### FurnitureGrid (`FurnitureGrid.cs`)
Provides a discrete coordinate system over a room's `BoxCollider`.
- **Initialization:** Determines grid bounds (`_gridWidth`, `_gridDepth`) using the room's collider size and a defined `_cellSize` (fixed at 1 unit = 1m).
- **Serialization:** Grid data (`_gridWidth`, `_gridDepth`, `_gridOrigin`, `_cells`) is serialized into the prefab via `[ContextMenu("Initialize Furniture Grid")]`. At runtime, `RestoreFromSerializedData()` rebuilds the 2D array from the flat list and recalculates cell world positions from the current transform (handles interior offset at y=5000).
- **Client sync:** On clients, `Awake()` fires before NGO sets the network position. `Room.OnNetworkSpawn()` calls `RestoreFromSerializedData()` again so the grid origin matches the actual runtime position. Without this, the grid is anchored at the prefab's origin (0,0,0) instead of the interior offset.
- **Placement Validation:** `CanPlaceFurniture()` checks: cell in bounds, not occupied, not IsWall, cell corners within BoxCollider bounds. The bounds Y-check uses `roomBounds.center.y` to avoid rejection by flat (height=0) colliders.
- **Ghost Snapping:** `GetPlacementPositions(cursorPos, sizeInCells)` returns grid-snapped anchor + visual center. Clamps the furniture footprint to grid bounds so it can't extend outside. The anchor is used for grid validation/registration, the visual center for ghost rendering.
- **Pathfinding:** Works alongside NavMesh but focuses purely on discrete object placement logic.

### Furniture registration / lazy bootstrap

`Room.Awake()` calls `FurnitureManager.LoadExistingFurniture()` to populate `Furnitures` from children. The scan uses `GetComponentsInChildren<Furniture>(true)` (includes inactive GameObjects). Because nested-prefab children can still be late-parented or late-activated — especially for network-spawned buildings where `NetworkObject` children arrive after the parent's Awake — `LoadExistingFurniture()` is **re-invoked in `Room.Start()` and `Room.OnNetworkSpawn()`**.

`Building.Start` ALSO calls `MainRoom.FurnitureManager.LoadExistingFurniture()` explicitly (2026-05-02). `Room.Start` runs the same defensive rescan but is `private`, so without the explicit call the Building's own MainRoom rescan never happened — the Building class itself IS its `MainRoom` via `ComplexRoom` inheritance, but the inherited `Start` method was hidden by `Building.Start`'s own override. Critical for spawned default furniture: `SpawnDefaultFurnitureSlot` parents furniture under the building root (NGO requires a NetworkObject ancestor), and the default-to-MainRoom registration (see below) relies on this rescan to catch any earlier authoring path that skipped it.

**The call is additive, not replace-style.** Each invocation prunes Unity fake-null entries, then merges any newly-discovered transform child into `_furnitures` (skipped if already present). The grid registration on top of that is itself idempotent (`FurnitureGrid.RegisterFurniture` just writes `cell.Occupant`). This matters because `CommercialBuilding._defaultFurnitureLayout` registers spawned furniture into `_furnitures` via `RegisterSpawnedFurnitureUnchecked` **without parenting it under the room** (the furniture sits on the building root — see "Default furniture spawn" below). A replace-style rescan would silently wipe those registrations: the room's transform tree never contained them, so `GetComponentsInChildren` returns an empty set and the list would collapse on the next `Start` / `OnNetworkSpawn` re-invocation.

This bootstrap matters because `CraftingBuilding.GetCraftableItems()` walks `Rooms → FurnitureManager.Furnitures → station.CraftableItems`. If the list is empty, `ProducesItem(item)` returns false for every item and `LogisticsStockEvaluator.FindSupplierFor` can't route to the building. `GetCraftableItems` carries a transform-tree fallback (`GetComponentsInChildren<CraftingStation>` on the building) that recovers crafting capability when the room list is empty AND emits a one-shot warning — so a regression to replace-style would be caught loudly even if it didn't break crafting outright. Any other system that queries a room's furniture at runtime depends on the same `_furnitures`-list invariant.

> **Note:** The additive note above refers to `Building._defaultFurnitureLayout` (hoisted from `CommercialBuilding` — now lives on the base `Building` class and applies to all subclasses).

### Furniture inheritance hierarchy (post-2026-05-08 ISP refactor)

```
Furniture                       (base — placement, grid, interaction point, item ref)
│   public virtual bool OnInteract(Character)   — universal E-press dispatch (default no-op)
│
├── OccupiableFurniture : Furniture, IOccupiable
│   │   _occupant + _reservedBy state
│   │   virtual Reserve / Use / Release / IsFree / IsOccupied
│   │   override OnInteract → calls Use(c)
│   ├── BedFurniture           (multi-slot — slot-aware overrides preferred)
│   ├── ChairFurniture         (single-occupant)
│   ├── Cashier                (vendor occupant + customer lock + till)
│   ├── CraftingStation        (occupied during CharacterCraftAction)
│   └── TimeClockFurniture     (occupied during Action_PunchIn / Action_PunchOut)
│
├── StorageFurniture           (no occupancy — slot-based container)
├── ManagementFurniture        (no occupancy — opens UI on E-press)
└── DisplayTextFurniture       (no occupancy — read-only sign)
```

**`IOccupiable`** is the contract for "this surface holds one Character at a time" — `Reserve`, `Use`, `Release`, `Occupant`, `ReservedBy`, `IsOccupied`, `IsFree`. Future non-Furniture occupiables (mounts, vehicles) implement the interface directly without inheriting `Furniture`.

**Why interface AND abstract class:** the interface lets call-sites that hold a generic `Furniture` reference do `if (f is IOccupiable occ) occ.Reserve(c);` cleanly (Open/Closed friendly). The abstract base shares the actual `_occupant`/`_reservedBy` state + standard `Use`/`Release` body so the five subclasses don't each reimplement them. Pure-interface (no abstract base) would force five copies of the same field/methods; pure-abstract-class would block future mounts/vehicles that aren't furniture.

### Furniture base (`Furniture.cs`)
The base class for any object inside a room.
- **Space Occupation:** Holds a `_sizeInCells` (Vector2Int) dictating how many grid cells it consumes. This is often auto-calculated via renderer bounds.
- **Interaction Point:** Dictates where characters must stand to interact with it (`_interactionPoint`). Resolution chain in `GetInteractionPosition(Vector3 fromPosition)`: (1) authored Transform, (2) `FurnitureInteractable.InteractionZone.bounds.ClosestPoint(fromPosition)`, (3) `transform.position` (last-resort, **inside the `NavMeshObstacle` carve** — workers stall in front of the furniture, GOAP softlock fires after 5s with an orange `[GOAP Storage]` console warning). **Authoring guarantees (in order):** (a) the editor `Reset()` hook auto-creates the child when the component is first added or right-clicked → Reset. (b) The `[ContextMenu]` "Auto Create Interaction Point" regenerates it on demand for older prefabs. (c) **`Furniture.Awake` runtime safety net (2026-05-14)** — if both `_interactionPoint == null` AND no `FurnitureInteractable.InteractionZone`, `Awake` auto-spawns the child at instantiation time. Covers legacy prefabs and runtime-placed / save-restored furniture that bypass `Reset()`. Local-only, no network sync (deterministic geometry on both peers).
- **Universal interaction surface:** `virtual bool OnInteract(Character)` — called by `FurnitureInteractable.Interact` on every E-press, regardless of whether the furniture is occupiable. Default returns `true` (no-op). `OccupiableFurniture` overrides to delegate to `Use(c)`; bespoke types (sign / management desk) override directly.
- **No occupancy state.** `_occupant`, `_reservedBy`, `Reserve`, `Use`, `Release`, `IsFree`, `IsOccupied`, `Occupant`, `ReservedBy` were extracted to `OccupiableFurniture` + the `IOccupiable` interface on 2026-05-08 per ISP (rule #12) — pure-display or pure-storage furniture no longer carries machinery it doesn't use.

### OccupiableFurniture (`OccupiableFurniture.cs`)
Abstract base for any furniture a Character can drive (sit / sleep / stand / craft / punch-clock).
- **Availability State:**
  - `_reservedBy`: A character is walking to it.
  - `_occupant`: A character is currently using it physically.
  - Both prevent other characters from using the furniture simultaneously.
- **Reservation is advisory** — `Use(c)` only checks `IsOccupied`, not `_reservedBy`. Whoever calls `Use` first wins; loser detects stale local state on next tick and re-picks. Canonical race-friendly pattern (used by `JobVendor` pool model — see `shop_system/SKILL.md`).
- **Subclass override pattern:** override `Use` / `Release` to add side-effects (network broadcast, animation, slot-aware logic), call `base.Use` / `base.Release` to keep the lock state in sync. `BedFurniture` is the exception — its overrides delegate entirely to slot-aware methods (`UseSlot`/`ReleaseSlot`), so the inherited base `_occupant`/`_reservedBy` fields are intentionally unused (multi-occupant beds need per-slot tracking).
- **Generic queries:** `FurnitureManager.FindAvailableFurniture<T>() where T : Furniture, IOccupiable` is the canonical "find a free X" lookup. Call-sites that hold a `Furniture` reference and need occupancy semantics use `if (furniture is IOccupiable occ) occ.Reserve(c);`.

#### Typed subclasses (occupiable)
- **`ChairFurniture` / `ChairFurnitureInteractable`** — sit-and-stay seating; `Release()` ends occupation.
- **`CraftingStation` + `CraftingFurnitureInteractable`** — opens the crafting window for a worker. Crafter occupies the station for the duration of `CharacterCraftAction` so two artisans can't queue against the same anvil simultaneously.
- **`TimeClockFurniture` + `TimeClockFurnitureInteractable`** — punch-in / punch-out station for the parent `CommercialBuilding`. Acts as a one-shot interaction: `OccupiableFurniture.Use(...)` → `Action_PunchIn` / `Action_PunchOut` → `OccupiableFurniture.Release()` fires in the action's `OnActionFinished`. Players hop through `CommercialBuilding.RequestPunchAtTimeClockServerRpc` (client-side `Interact` detects `!IsServer` and routes); NPCs target the clock from `BTAction_Work` / `BTAction_PunchOut` directly on the server. Eligibility: the interactor must have a `JobAssignment` where `Workplace == this building`. Missing clock → one-shot warning + legacy zone-punch fallback.
- **`BedFurniture`** — multi-slot occupant container. Single-bed prefab = 1 slot, double-bed = 2, family-bed = 4, etc. `ReserveSlot` / `UseSlot` / `ReleaseSlot` are preferred over the inherited single-slot API.
- **`Cashier`** — vendor occupancy + customer lock + till. See `shop_system/SKILL.md`.

#### Typed subclasses (non-occupiable)
- **`StorageFurniture`** — slot-based container (chest, shelf, barrel, wardrobe). Mirrors the player `Inventory` pattern: a flat `List<ItemSlot>` initialized from four authored capacity ints (`_miscCapacity`, `_weaponCapacity`, `_wearableCapacity`, `_anyCapacity`). API: `AddItem(ItemInstance)`, `RemoveItem`, `RemoveItemFromSlot`, `GetItemSlot(int)`, `HasFreeSpaceFor*`, plus `Lock()` / `Unlock()` and `OnInventoryChanged` event. `AddItem` uses **strict-first slot priority** — wearables try `WearableSlot → MiscSlot → AnySlot`, weapons try `WeaponSlot → AnySlot`, everything else `MiscSlot → AnySlot` — so dedicated typed slots fill before generic ones. New slot types `WearableSlot` (wearables only) and `AnySlot` (any item) live alongside the existing `MiscSlot` / `WeaponSlot`. **Storage contents are now server-authoritative replicated** — see "Storage network sync" below. **Visual display is opt-in** via the optional `StorageVisualDisplay` component (see below) — chests don't add it, shelves do.

#### Storage network sync (`StorageFurnitureNetworkSync`)
Sibling `NetworkBehaviour` added to `Assets/Prefabs/Furniture/Storage/Storage.prefab` (and inherited by every variant: `Storage Visible Items.prefab`, `Crate.prefab`). Reuses the `NetworkObject` already on the `Furniture_prefab` base — no separate `NetworkObject` is added on the storage GameObject (rule: never nest a second `NetworkObject` on a runtime-spawned prefab; see `wiki/gotchas/host-progressive-freeze-debug-log-spam.md` neighbours).

- Holds `NetworkList<NetworkStorageSlotEntry>` — each entry is `{ ushort SlotIndex, FixedString64Bytes ItemId, FixedString4096Bytes JsonData }`. Sparse: empty slots are simply absent from the list.
- **Server-side flow:** `OnNetworkSpawn` (server) subscribes to `_storage.OnInventoryChanged` and runs an initial `RebuildNetworkListFromStorage` so the list is in sync the moment NGO finishes the spawn handshake. Each subsequent inventory change clears the list and re-adds one entry per non-empty slot. Strict-first slot priority logic still runs only inside `StorageFurniture.AddItem` on the server — the sync layer just snapshots the result.
- **Client-side flow:** `OnNetworkSpawn` (client) subscribes to `OnListChanged` AND immediately calls `ApplyFullStateOnClient` for late-joiner safety. Each `OnListChanged` event (any `EventType` — `Add`, `Insert`, `Value`, `Remove`, `RemoveAt`, `Clear`, `Full` — see `feedback_network_client_sync.md` and `.agent/skills/multiplayer/SKILL.md` §8 on the NetworkList event-type fan-out gotcha) triggers a full rebuild of the local slot state via `StorageFurniture.ApplySyncedSlotsFromNetwork`. That method clears every slot and writes the supplied entries by index, then fires the local `OnInventoryChanged` so `StorageVisualDisplay` re-renders on this peer.
- **`ItemSO` resolution** mirrors `WorldItem.ApplyNetworkData`: `Resources.LoadAll<ItemSO>("Data/Item")` → `Array.Find` by `ItemId` → `so.CreateInstance()` → `JsonUtility.FromJsonOverwrite` → re-bind `instance.ItemSO = so` (lost during JSON overwrite). Each step is wrapped in `try/catch` per rule #31 — one bad entry never blocks the rest.
- **Spawn-timing invariant:** `StorageFurniture._itemSlots` is built in `Awake()` (capacities authored on the prefab). `Awake` runs before the first `OnNetworkSpawn` call on the same GameObject, so the server's initial rebuild always sees a fully-initialized slot list. Client capacity is computed identically from the same authored ints, so server and client agree on slot count from frame 0.
- **What still lives only server-side:** `IsLocked` (the `Lock()` / `Unlock()` flag). If lock state ever needs to be visible to clients, extend the sync component — do not lift the lock check into `ApplySyncedSlotsFromNetwork`.
- **Cost:** O(Capacity) per server-side mutation. With the authored Crate capacity (32 slots) this is fine. NetworkList delta-sync sends one `Clear` event followed by one `Add` per non-empty entry, so a single mutation produces 1+N events on the wire and 1+N rebuilds on the client. Visual flicker is per-mutation only and invisible at typical storage churn rates. If profiling later shows hot churn, replace the clear+rebuild with a delta diff.
- **Multiplayer test plan (rule #19):** validated across (a) Host stores via `StorageFurniture.AddItem` → all clients see the item; (b) Client triggers a server-side store via NPC AI / `CharacterStoreInFurnitureAction` → server runs `AddItem`, sync layer fires, every other client mirrors; (c) Host↔NPC: NPC store on host fires the same path, every client (including host) sees the item.

#### Storage save/restore (`BuildingSaveData.StorageFurnitures`)
Slot contents survive `MapController.Hibernate` / `WakeUp` AND game-session reloads via per-furniture entries on `BuildingSaveData`. The schema lives in [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) and the restore wiring lives in [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs).

- **Save-side (`BuildingSaveData.FromBuilding`):** walks `building.GetFurnitureOfType<StorageFurniture>()` (recurses through every sub-room because `Building` extends `ComplexRoom`). For each storage, builds a `StorageFurnitureSaveEntry { FurnitureKey, List<StorageSlotSaveEntry> Slots }` keyed by `BuildingSaveData.ComputeStorageFurnitureKey(storage, building.transform)`. Each non-empty slot contributes a `StorageSlotSaveEntry { SlotIndex, ItemId, JsonData }` — same `JsonUtility.ToJson(ItemInstance)` recipe the network-sync layer uses. Per-slot try/catch: a single corrupt instance is logged and skipped, never blocks the rest of the save (rule #31). The entry is added even when `Slots` is empty so that emptying a previously-stocked storage actually persists the empty state on the next save.
- **Restore-side (`MapController.RestoreStorageFurnitureContents`):** invoked from BOTH `SpawnSavedBuildings` (predefined-map load + `RespawnDynamicMaps`) and `WakeUp` (post-hibernation). Runs immediately after the building's `bNet.Spawn()` returns — `CommercialBuilding.OnNetworkSpawn` synchronously fires `TrySpawnDefaultFurniture` which synchronously instantiates+spawns each storage furniture, so live storages exist by the time restore runs. The method walks `building.GetFurnitureOfType<StorageFurniture>()`, looks up each storage's entry by composite key, rehydrates each slot's `ItemInstance` via `Resources.LoadAll<ItemSO>("Data/Item")` → `Array.Find` by ItemId → `so.CreateInstance()` → `JsonUtility.FromJsonOverwrite` → re-bind `inst.ItemSO = so` (same pattern as `StorageFurnitureNetworkSync.TryDeserializeEntry`), and pushes the result through `StorageFurniture.RestoreFromSaveData`. Per-slot AND per-furniture try/catch — one corrupt entry never blocks others (rule #31).
- **FurnitureKey scheme:** `"{FurnitureItemSO.ItemId}@{x:F2},{y:F2},{z:F2}"` formatted with `CultureInfo.InvariantCulture`, where (x,y,z) is `building.transform.InverseTransformPoint(storage.transform.position)`. Stable across `_defaultFurnitureLayout` reorders, supports multiple same-typed storages per building, locale-independent. The static helper `BuildingSaveData.ComputeStorageFurnitureKey` is the single authority used by BOTH save and restore so they cannot drift.
- **Network sync interaction:** `StorageFurniture.RestoreFromSaveData` ends by firing `OnInventoryChanged`. The sibling `StorageFurnitureNetworkSync` is already subscribed (subscribed in its server-side `OnNetworkSpawn`, which ran inside the same synchronous spawn-handshake chain), so `RebuildNetworkListFromStorage` runs immediately and rewrites the replicated `NetworkList`. **Late-joining clients see populated state on connect with no extra restore-side networking.** No race: by the time `RestoreFromSaveData` is called, the storage's `OnNetworkSpawn` has already returned and the subscription is live.
- **Backward compatibility:** `BuildingSaveData.StorageFurnitures` defaults to an empty list, so save files written before this feature deserialize cleanly (the missing field is treated as an empty list, restore is a no-op for those buildings). No migration code needed.

**Adding a new storage subclass:** if you subclass `StorageFurniture` (e.g. `EncryptedChest` with a passcode field), the save/restore path picks it up automatically because the discovery is via `GetFurnitureOfType<StorageFurniture>()` and the serialization is per-slot. The **only** thing to add is subclass-specific state — for example, persisting a passcode would need a new field on `StorageFurnitureSaveEntry` plus subclass-aware capture/apply logic. Slot contents themselves require no change. Do NOT route subclass-specific state through `RestoreFromSaveData(IReadOnlyList<(int, ItemInstance)>)` — that contract is intentionally narrow ("clear and write slots, fire one event"); add a separate API on the subclass (mirroring how `IsLocked` will eventually be handled).

**Authoring rule for storages with `_defaultFurnitureLayout`:** the FurnitureKey depends on the storage's building-local position. If you change `LocalPosition` on a `_defaultFurnitureLayout` slot **after** a world save was written, the saved entry's key won't match any live storage on next load — its contents will be silently dropped. This is the same brittleness as renaming a save field. Treat `_defaultFurnitureLayout` slot positions as part of the save schema once a build ships.

#### Placed-furniture roster (`BuildingSaveData.PlacedFurnitures`, 2026-05-13)
**The complement to `StorageFurnitures`.** `StorageFurnitures` persists slot CONTENTS keyed by furniture position; `PlacedFurnitures` persists the furniture EXISTENCE itself for anything spawned at runtime via `CharacterPlaceFurnitureAction` (player path or NPC path). Without this, every player-placed chest / crafting station / cashier / etc. silently disappears on save→load because `Building.TrySpawnDefaultFurniture` only re-instantiates items in `_defaultFurnitureLayout`.

- **Save-side (`BuildingSaveData.FromBuilding`):** walks `building.GetFurnitureOfType<Furniture>()` and skips anything matched by `Building.IsDefaultLayoutFurniture(furniture)` (same FurnitureItemSO + LocalPosition within 0.05u epsilon). Surviving entries become `PlacedFurnitureSaveEntry { ItemId, LocalPosition, LocalEulerAngles }`. Per-furniture try/catch (rule #31). Furniture absorbed into `_defaultFurnitureLayout` at Awake via `ConvertNestedNetworkFurnitureToLayout` is automatically excluded because the absorption happens before save runs.
- **Restore-side (`MapController.RestorePlacedFurnitureForBuilding`):** invoked from `ApplyDynamicSaveDataToBuilding` **BEFORE** `RestoreStorageFurnitureContents` so per-storage key lookup finds the freshly-spawned instances. Server-only, gated to `building.CurrentState == BuildingState.Complete` (don't restore furniture into a still-under-construction building). For each entry: resolve `ItemId` → `FurnitureItemSO` via `Resources.LoadAll<ItemSO>("Data/Item")`, `Instantiate(InstalledFurniturePrefab)` at the saved world position, `NetworkObject.Spawn()`, parent under the building root (NGO requires a NetworkObject ancestor — same parenting rule as `Building.SpawnDefaultFurnitureSlot`), register via `FurnitureManager.RegisterSpawnedFurnitureUnchecked` on `MainRoom` (server-authoritative restore — bypasses `CanPlaceFurniture`). Same cleanup-on-failure path as `SpawnDefaultFurnitureSlot`: half-spawned NetworkObjects get Despawn'd to avoid corrupting `SpawnManager.SpawnedObjectsList`.
- **No duplicate-spawn risk:** `TrySpawnDefaultFurniture` only iterates `_defaultFurnitureLayout`, which excludes the player-placed roster. `RestorePlacedFurnitureForBuilding` iterates only `PlacedFurnitures`. The two paths spawn disjoint sets.
- **Refresh-path parity:** both `MapController.SnapshotActiveBuildings` (mid-game save) and `MapController.Hibernate` (player-leaves wake-cycle) copy `refreshed.PlacedFurnitures` onto the existing save entry — same trap that bit `ConstructionProgress` / `DeliveredMaterials` on 2026-05-07 (commit `ff98c2b7`). Without this, mid-game saves would drop the field.
- **Backward compatibility:** `BuildingSaveData.PlacedFurnitures` defaults to an empty list, so save files written before this feature deserialize cleanly (the missing field is treated as an empty list — no migration code).

**Why this matters (2026-05-13 bug fix):** Before this addition, HarvestingBuilding / CraftingBuilding / FarmingBuilding lost every player-placed storage on save→load. Shop buildings appeared to work only because their SellShelves are nested NetworkObject children of the prefab, absorbed into `_defaultFurnitureLayout` at Awake by `ConvertNestedNetworkFurnitureToLayout`. Player-placed storages in Shop buildings had the same bug. This fix makes player placement first-class across every CommercialBuilding subclass.

#### Save-restore ordering + subscription-timing fix (2026-05-13)
`MapController.ApplyDynamicSaveDataToBuilding` orchestrates the per-building restore. Order is **load-bearing** — getting it wrong silently drops state. The full sequence:

1. `building.RestoreOwnersFromSaveData(bSave.OwnerCharacterIds)` — owner UUIDs queued + bound via `Character.OnCharacterSpawned` / `OnCharacterIdReassigned`.
2. `commercial.RestoreEmployeesFromSaveData(bSave.Employees)` — only for `CommercialBuilding`.
3. **`building.RestoreFromSaveData(bSave)`** — flips state to `bSave.State` (e.g. `Complete`) AND manually invokes `TrySpawnDefaultFurniture()` + `ConfigureNavMeshObstacles()` when state ends Complete and `_isStarted == false`. **This step now happens BEFORE the per-furniture restore steps** (was last in old order).
4. `RestorePlacedFurnitureForBuilding` — spawns player-placed furniture (state is Complete by now).
5. `RestoreStorageFurnitureContents` — per-storage key lookup against the now-live default + placed furniture.
6. `RestoreCashierContents` — same shape for `Cashier`.
7. ShopBuilding hooks (`RestoreShopFromSaveData`, `OnFurnituresLoaded`).

**Why the manual cascade inside `RestoreFromSaveData`?** `Building.OnNetworkSpawn` auto-derives `_currentState.Value = UnderConstruction` for any prefab with non-empty `_constructionRequirements` and `_spawnAsComplete = false`. The state-flip inside `RestoreFromSaveData` to `Complete` would normally fire `_currentState.OnValueChanged → HandleStateChanged` which runs the post-Complete cascade (`TrySpawnDefaultFurniture`, NavMesh carve, `OnConstructionComplete`, leftover evict). **But that subscription is wired in `Start()`, which Unity hasn't dispatched yet** — `SpawnSavedBuildings → Spawn → ApplyDynamicSaveDataToBuilding → RestoreFromSaveData` is all one synchronous frame. Without a subscriber, the cascade silently no-ops. Default furniture vanishes (Lumberyard crate, Forge anvil, etc.), NavMesh isn't carved, and the per-storage content restore in step 5 finds zero matching live storages.

The fix mirrors the BuildInstantly subscription-timing pattern (see [[buildinstantly-pre-start-lifecycle-race]] in the wiki) but uses a **manual cascade** instead of coroutine-defer — because steps 4-7 below run synchronously in the same call and need furniture live NOW, not next frame. `_defaultFurnitureSpawned` guards make the manual call idempotent against the legitimate Start-subscribed path. We deliberately do NOT call `EvictLeftoversToPerimeter` (one-time construction-loop side effect — saved WorldItems restore separately) or fire `OnConstructionComplete` (would falsely tell quest hooks the building just completed).

**Gotcha catalogue:** [[save-restore-state-flip-no-subscriber]] documents this in the wiki for cross-system reference.

#### Storage Roles (unified per-storage role system, 2026-05-08)
Every `StorageFurniture` carries one runtime `StorageRoleType` value (`None` / `ToolStorage` / `InventoryStorage` / `SellShelf`). Per-storage exclusivity: a storage can hold **exactly one** role, but multiple storages can independently share the same role (e.g. three sell-shelves, one tool bin, two inventory bins). The role is owner-mutable at runtime through the management panel and persists in save data.

**Type catalog (`Assets/Scripts/World/Furniture/StorageRoleType.cs`):**
- `StorageRoleType` enum — `None = 0, ToolStorage = 1, InventoryStorage = 2, SellShelf = 3`.
- `StorageRoleDescriptor` — `{ Type, DisplayName, Icon }` for UI rendering.
- `StorageRoleCatalog` — static catalogs `Generic` (None / Tool / Inventory) and `Shop` (Generic + SellShelf). Subclasses pick the catalog they expose by overriding `CommercialBuilding.SupportedStorageRoles`.

**Storage-side state (`StorageFurniture`):**
- `_initialRole : StorageRoleType` — designer-authored seed (Inspector field). Used by the network sync layer on `OnNetworkSpawn` (server) when no save data has overwritten it.
- `_runtimeRole : StorageRoleType` — server-authoritative state. Set only by `ApplyRoleFromNetwork` (called from `StorageFurnitureNetworkSync` on both server and client when the role NetVar value changes).
- `Role : StorageRoleType` — public getter; returns `_runtimeRole`.
- `event Action<StorageRoleType> OnRoleChanged` — fires on every peer when the role changes. UI listeners (the `StorageRolesTab` row) subscribe to this for live re-render.

**Replication (`StorageFurnitureNetworkSync`):**
- Adds a `NetworkVariable<StorageRoleType> _networkRole` (server write, everyone read). Default `None`.
- Server seeds `_networkRole.Value = _storage.InitialRole` in `OnNetworkSpawn` if the value is currently default.
- `OnValueChanged` callback calls `_storage.ApplyRoleFromNetwork(newValue)` on every peer — fires the local `OnRoleChanged` event.
- `SetRoleServer(StorageRoleType newRole)` — server-only mutator; writes `_networkRole.Value`. Used by `CommercialBuilding.DoSetStorageRole` (the canonical helper that both `TrySetStorageRoleServerRpc` and `BuildingLogisticsManager.AssignStorageRolesForShift` route through — see below), `MapController.RestoreStorageFurnitureContents` (save-restore), and `ShopBuilding.OnFurnituresLoaded` (legacy sell-shelf migration).

**Building-side API (`CommercialBuilding`):**
- `virtual IReadOnlyList<StorageRoleDescriptor> SupportedStorageRoles` — defaults to `StorageRoleCatalog.Generic`. Override per subclass to widen (`ShopBuilding` returns `StorageRoleCatalog.Shop`).
- `IReadOnlyList<StorageFurniture> GetStoragesWithRole(StorageRoleType type)` — walks every storage child (recursive, includes inactive) and returns those whose `Role` matches. Allocates a fresh list per call — not a hot path (called on UI refresh + logistics re-evaluation, both rare).
- `event Action OnStorageRolesChanged` — fires on **every peer** when any child storage's role changes. Driven by per-storage `StorageFurniture.OnRoleChanged` subscriptions installed at `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached` (so runtime-placed storages are picked up automatically). Old behavior — fire only from inside the ServerRpc body — was host-only and was replaced 2026-05-09.
- `[ServerRpc(RequireOwnership=false)] TrySetStorageRoleServerRpc(NetworkObjectReference furnitureRef, StorageRoleType newRole)` — owner-only mutator (player UI). Validates: caller is the building's `Owner`, `newRole` appears in `SupportedStorageRoles`, target NetworkObject resolves to a `StorageFurniture` child with a sibling `StorageFurnitureNetworkSync`. Then calls `DoSetStorageRole(storage, newRole)` (2026-05-14: routes through the canonical helper instead of calling `sync.SetRoleServer` directly so the NPC path can converge here). The per-storage NetVar `OnValueChanged` fan-out then fires `OnStorageRolesChanged` on every peer — no manual invoke from the ServerRpc body. **Hard-fails with LogError when the sync sibling is missing** (was LogWarning pre-2026-05-09). Rejected owner / role-out-of-catalog calls still log a warning.
- `private void DoSetStorageRole(StorageFurniture storage, StorageRoleType newRole)` — **canonical** server-only mutator (2026-05-14). Every programmatic role write — player RPC + NPC shift-punch auto-assignment — funnels through this method so side-effects converge. Responsibilities: idempotency guard (skip when current == desired), resolve sibling `StorageFurnitureNetworkSync` (LogError + abort on miss), call `sync.SetRoleServer(newRole)`. **Future-proof:** any new invariant (cache invalidation when a storage flips to/from `ToolStorage`, audit log, broadcast) lands here once and reaches both paths.
- `internal bool TrySetStorageRoleServer(StorageFurniture storage, StorageRoleType newRole)` — non-RPC server-only entry (2026-05-14). Performs the `SupportedStorageRoles` subtype filter (same rule the player RPC enforces) then calls `DoSetStorageRole`. Returns `true` if the write happened, `false` on same-state (idempotent) or rejected. Called by `BuildingLogisticsManager.AssignStorageRolesForShift`.
- Multi-storage list helpers (above) — `ToolStorages`, `InventoryStorages`, `FindToolStorageContaining`, `FindToolStorageWithFreeSpace`, `HasToolInAnyToolStorage`, `IsToolStorage`. All consumers (logistics + GOAP + jobs) iterate the lists.

**Save/restore:**
- `StorageFurnitureSaveEntry.Role : StorageRoleType` field captures the role per-storage at save time (`BuildingSaveData.FromBuilding`).
- `MapController.RestoreStorageFurnitureContents` writes the saved role onto each storage's `StorageFurnitureNetworkSync` after slot-content restore — guaranteed to happen after `OnNetworkSpawn` so `SetRoleServer` is safe.
- Old saves: `Role` defaults to `None` for entries that predate the field, and `ShopBuilding._pendingSellShelfKeys` (legacy) is migrated by `ShopBuilding.OnFurnituresLoaded()` after restore to write `SellShelf` onto the matching storages.

**Owner-driven UI (`StorageRolesTab` family):**
- `StorageRolesTab` — `IManagementTab` impl on the base `CommercialBuilding`. Loaded from `Resources/UI/Management/StorageRolesTab.prefab`. Replaces the deleted `ShopShelvesTab`.
- `StorageRolesTabView` — lists every `StorageFurniture` child, one row each. Subscribes to `building.OnStorageRolesChanged` for re-render. Empty state copy: "Place a storage furniture inside the building to assign roles."
- `StorageRolesTabRow` — per-row label + TMP_Dropdown wired to `building.SupportedStorageRoles`. Selecting an option fires `building.TrySetStorageRoleServerRpc(...)`. Subscribes to `storage.OnRoleChanged` so save-restore / migration writes (which bypass the building's ServerRpc) still refresh the row's selection.

**Wider impact / migration notes:**
- `ShopBuilding.SellShelves` is now `GetStoragesWithRole(StorageRoleType.SellShelf)`. The dedicated `_sellShelves : NetworkList<NetworkObjectReference>`, `OnSellShelvesChanged` event, and `SetSellShelfFlagServerRpc` ServerRpc were deleted on 2026-05-08.
- Subclassing `CommercialBuilding` with new role catalogs (e.g. `WorkshopBuilding` adding `MaterialBin`): extend `StorageRoleType` enum + `StorageRoleCatalog` + override `SupportedStorageRoles`. The dropdown UI auto-renders the new option.

**Shift-punch storage-role assignment (2026-05-14):**
`BuildingLogisticsManager.AssignStorageRolesForShift()` is called from `CommercialBuilding.WorkerStartingShift` (server-only, inside the `!IsWorkerOnShift` branch — one call per actual shift entry). It walks `_building.GetStorageFurnitureOrdered()` (deterministic: MainRoom first → SubRooms; FurnitureManager registration order within each room) and applies the unified rule:

1. If `GetToolStockItems()` yields anything → storage[0] = `ToolStorage`, storage[1..] = `InventoryStorage`.
2. Else if `_building is ShopBuilding` → storage[0] = `SellShelf`, storage[1..] = `InventoryStorage`.
3. Else → all storages = `InventoryStorage`.

Tool-storage priority **overrides** shelf priority — a shop that ever returns tool items from `GetToolStockItems()` gets the tool branch.

**Idempotency:** per-storage write routes through `CommercialBuilding.TrySetStorageRoleServer(storage, desired)` which early-outs on `storage.Role == desired`. Replays cost zero replication traffic. **Server-only:** the helper calls into `DoSetStorageRole` → `StorageFurnitureNetworkSync.SetRoleServer` — same path the player RPC uses (2026-05-14: both paths converged through `DoSetStorageRole`); the NetVar fans out via `OnValueChanged` → `OnRoleChanged` → `CommercialBuilding.OnStorageRolesChanged`. **Subtype-safe:** the `TrySetStorageRoleServer` filter rejects roles outside `SupportedStorageRoles` (same rule the player RPC enforces), returns `false`, and the NPC pass skips logging — the helper logs its own warning. **Owner-override caveat:** today the rule re-runs on every shift-punch and will **overwrite** an owner's manual role choice on the next punch-in. If you want a "designer-locked" or "owner-locked" mode, layer a flag on `StorageFurniture` (e.g. `_roleLocked`) and have the assignment pass skip locked storages. Not implemented yet — flag in the open-questions follow-up if it becomes a problem.

To extend the rule for new subclasses, override `GetToolStockItems()` on the building (yields the items it treats as tools) — the tool branch is triggered by any non-empty result. The `ShopBuilding` branch is hard-coded by `is ShopBuilding`; if you add a parallel role family in the future, extend `AssignStorageRolesForShift` to accept a polymorphic "role priority resolver" (sketch: `virtual StorageRoleType ResolveFirstStorageRole()` on `CommercialBuilding`).

#### Treasury & SafeFurniture (2026-05-09)

Per-building currency reserve that funds B2B shop purchases. Parallel furniture family to `StorageFurniture` — see [[commercial-treasury]] for the full architecture page.

- **`SafeFurniture`** holds per-`CurrencyId` integer balance + a `SafeRoleType` (None / Treasury). Sibling `SafeFurnitureNetworkSync` replicates both via `NetworkVariable<SafeRoleType>` + `NetworkList<BuildingTreasuryEntry>`. Designer Inspector field `_initialRole` defaults to `Treasury` so pre-placed safes are usable immediately.
- **Aggregator on `CommercialBuilding`** — `Safes` / `TreasurySafes` getters + `GetTreasuryBalance` / `CanAffordFromTreasury` / `TryDebitTreasury` (drains largest treasury safe first) / `CreditTreasury` (credits largest treasury safe). `OnTreasuryChanged` event fans out per-safe `OnBalanceChanged` / `OnRoleChanged` via `RefreshSafeSubscriptions` set up in `OnNetworkSpawn`. **No NetworkList on the building itself** — replication lives on each safe's sync component.
- **Auto-assign** — `BuildingLogisticsManager.AssignSafeRolesForShift()` (sibling of `AssignStorageRolesForShift`, called from `WorkerStartingShift`) flips every `Role == None` safe to `Treasury` on every NPC LogisticsManager shift-punch. Idempotent.
- **Save / load** — `SafeFurnitureSaveEntry { FurnitureKey, Balances, Role }` on `BuildingSaveData.Safes`. Default-empty for back-compat. Captured in `BuildingSaveData.FromBuilding` for every Building subtype (homes can hold safes). Restored in `MapController.RestoreSafeContents` after `RestoreCashierContents` — balances via `SafeFurniture.RestoreFromSaveData`, role via the sibling `SafeFurnitureNetworkSync.SetRoleServer`.
- **B2B spending path** — `LogisticsStockEvaluator.TryB2BPurchaseFromShop` consults `GetTreasuryBalance` / `TryDebitTreasury` before posting a producer BuyOrder. See [[logistics_cycle]] and [[shop_system]] SKILL files for the spend-side details.

#### `StorageVisualDisplay` (optional renderer)
Add this component next to `StorageFurniture` only when contents should be visible (shelves, open crates, weapon racks). Configure:
- `_displayAnchors` — `List<Transform>`. Anchors are consumed by the **first non-empty slots iterated in slot order** (misc → weapon → wearable → any), so a shelf with 5 anchors over an 8-misc + 8-wearable storage will display the first 5 stored items regardless of slot index. Authors don't need to match anchor count to capacity — extras stay unused; fewer-than-capacity is fine.
- `_itemScale` — uniform scale applied to each spawned item visual (default 0.7). Items are usually authored at full world size; shelves want them shrunk.

**Visual pipeline mirrors `WorldItem.Initialize` directly** but instantiates `ItemSO.ItemPrefab` (the visual sub-prefab) instead of the full `WorldItemPrefab` wrapper:
1. Instantiate `ItemSO.ItemPrefab` — the same content `WorldItem.AttachVisualPrefab` uses internally as the inner visual. Pure visuals, no `NetworkObject`, no `NetworkTransform`, no physics.
2. Add a `SortingGroup` to the spawned root if it doesn't already have one — this is the only thing the `WorldItemPrefab` wrapper provided beyond raw visuals, and 2D sprites need it to layer correctly against the rest of the building.
3. `StripRuntimeComponents`: disable `Collider`s, `Rigidbody.isKinematic = true` + `useGravity = false`, disable `NavMeshObstacle`s. Static shelf items can't push workers, fall, or carve the navmesh.
4. Reparent + zero local transform + apply `_itemScale`.
5. `ApplyItemVisual` — same wearable-handler / simple-item config logic as `WorldItem.Initialize`: find `WearableHandlerBase` in children → `Initialize(SpriteLibraryAsset)` + `SetLibraryCategory(CategoryName)` + primary/secondary colors from the `EquipmentInstance` + `SetMainColor(Color.white)`. Else `ItemInstance.InitializeWorldPrefab(go)` for simple items (apple, potion). Re-applies `ShadowCastingMode` from `ItemSO.CastsShadow` to every Renderer.

**Critical client-side gotcha:** earlier versions instantiated `WorldItemPrefab` (the full wrapper with a `NetworkObject`). On the host, NGO tolerated the "homeless" cloned `NetworkObject` long enough to `DestroyImmediate` it. **On clients, NGO's stricter spawn-tracking either reverted parenting or left the GameObject in a non-rendering state** — visuals never appeared. The `ItemPrefab` approach has zero `NetworkObject`s in the cloned chain, so there's nothing for NGO to interfere with on either peer. Same reason applies whenever you clone a prefab purely for visual purposes — never include the `NetworkObject` in the clone if you don't intend to `Spawn()` it.

Performance contract:
- Per-`ItemSO` object pool (`Dictionary<ItemSO, Stack<GameObject>>`) — taking and re-storing the same item type doesn't allocate after the first time. Different SOs never share a pooled instance. `InitializeWorldPrefab` runs only on first spawn (color injection on a sprite that already has it would be wrong); wearable handlers re-apply per-acquire because `EquipmentInstance` palettes can differ.
- Event-driven via `StorageFurniture.OnInventoryChanged`; no `Update` loop, no coroutines.
- Static items don't run physics / pathfinding — the strip pass guarantees this.
- **No distance gating today.** A coroutine-based squared-distance check was removed because it was a global host-side decision that flipped client displays out of phase with the storage state. Per-peer local culling is tracked in [`wiki/projects/optimisation-backlog.md`](../../wiki/projects/optimisation-backlog.md) for future work.

`StorageVisualDisplay` is network-agnostic. As of the `StorageFurnitureNetworkSync` work it correctly receives `OnInventoryChanged` on every peer (host and clients), because the sync layer fires the local event at the end of `ApplySyncedSlotsFromNetwork`. No display-side authority checks needed — the display just listens to the local event regardless of who's running.

#### Logistics-cycle integration
`CommercialBuilding` exposes two helpers used by GOAP actions to prefer slot storage over the loose `StorageZone` drop:
- `FindStorageFurnitureForItem(ItemInstance)` — first-fit search across all sub-rooms (via `GetFurnitureOfType<StorageFurniture>()`); returns the first unlocked furniture with a compatible free slot, or null. Type-affinity (a wardrobe rejecting a sword) falls out of `StorageFurniture.HasFreeSpaceForItem` for free.
- `GetItemsInStorageFurniture()` — yields every `(furniture, item)` pair currently held in any storage slot. Used by the outbound staging path so reserved transport instances stored as logical-only slot data can still be located.

Two character actions wire the slot transfer:
- `CharacterStoreInFurnitureAction(character, item, furniture)` — removes the item from the worker's inventory or hands and inserts it into the slot. **No `WorldItem` is spawned** — the item lives logical-only inside the slot. Re-validates lock + free-space at `OnApplyEffect` (another worker may have filled the slot during travel) and rolls back to hands on slot-insert failure.
- `CharacterTakeFromFurnitureAction(character, item, furniture)` — mirror; pulls the item out of the slot and places it in the worker's hands. Used by `GoapAction_StageItemForPickup` when a reserved instance is in a slot rather than a loose `WorldItem`.

`GoapAction_GatherStorageItems` (LogisticsManager inbound) tries furniture first and re-targets per-item across multiple furniture pieces; falls back to the zone drop when nothing fits. `GoapAction_DepositResources` (harvester) only opportunistically diverts to a furniture within ~5 Unity units (≈0.76 m) of the deposit zone — preserves throughput. `GoapAction_StageItemForPickup` (outbound) checks slot-stored reserved instances after the loose-WorldItem scan. **Transporter pickup also runs furniture-first**: `GoapAction_LocateItem` scans `GetItemsInStorageFurniture()` before the WorldItem search; on a hit it sets new `JobTransporter.TargetSourceFurniture` + `TargetItemFromFurniture` fields and the new `GoapAction_TakeFromSourceFurniture` walks the worker straight to the slot — bypassing the LogisticsManager staging dance entirely. `GoapAction_MoveToItem` and `GoapAction_PickupItem` early-out when `TargetSourceFurniture != null` so the two pickup paths are mutually exclusive. See `.agent/skills/logistics_cycle/SKILL.md` for the full state-machine diff.

**`RefreshStorageInventory` guard**: Pass 1 (the ghost-detector) builds a `furnitureStoredInstances` HashSet from `GetItemsInStorageFurniture()` and skips any logical instance present in it. Without this protection, every furniture-stored item would be silently ghosted on the next punch-in (no matching `WorldItem` in `StorageZone` → flagged as ghost → removed from `_inventory`).

### Furniture Placement & Pickup (Player + NPC)

Furniture has two forms:
- **Portable:** `FurnitureItemSO` (ScriptableObject in `Resources/Data/Item/`) + `FurnitureItemInstance` (carried in hands as a crate)
- **Installed:** `Furniture` MonoBehaviour (placed on grid or freestanding)

Bidirectional link: `FurnitureItemSO._installedFurniturePrefab` → Furniture prefab; `Furniture._furnitureItemSO` → back to `FurnitureItemSO`.

#### Placement Flow
- **Player:** Carries `FurnitureItemInstance` in hands → presses F → `FurniturePlacementManager` shows ghost → left-click confirms → queues `CharacterPlaceFurnitureAction`
- **NPC:** AI decision → queues `CharacterPlaceFurnitureAction(character, room, prefab)` directly
- **Action (shared):** `CharacterPlaceFurnitureAction.OnApplyEffect()` — calls `CharacterActions.RequestFurniturePlaceServerRpc()` to have the server instantiate + spawn + register on grid. Client-side: consumes item from hands (player path). NPC path (no FurnitureItemSO): direct server spawn.

#### Pickup Flow
- **Player:** Hold E on furniture → "Pick Up" option via `FurnitureInteractable.GetHoldInteractionOptions()` → queues `CharacterPickUpFurnitureAction`
- **NPC:** AI decision → queues `CharacterPickUpFurnitureAction(character, furniture)` directly
- **Action (shared):** `CharacterPickUpFurnitureAction.OnApplyEffect()` — creates `FurnitureItemInstance`, puts in hands, calls `CharacterActions.RequestFurniturePickUpServerRpc()` to have the server unregister from grid + despawn. Non-networked furniture: direct `Destroy()`.

#### Key Methods on FurnitureManager
- `AddFurniture(prefab, position)` — instantiates + registers (non-networked, NPC legacy)
- `RegisterSpawnedFurniture(furniture, position)` — registers already-spawned networked furniture (no instantiation)
- `UnregisterAndRemove(furniture)` — unregisters from grid + removes from list (no destroy, caller handles despawn)
- `RemoveFurniture(furniture)` — unregisters + destroys (non-networked legacy)

#### Debug Mode
`DebugScript` button calls `FurniturePlacementManager.StartPlacementDebug(FurnitureItemSO)` — bypasses carry requirement, enters ghost placement mode directly.

#### FurnitureGrid Editor Tools
- `[ContextMenu("Initialize Furniture Grid")]` — bakes grid data into prefab from BoxCollider + floor renderers
- `_floorRenderers` list — defines walkable floor planes for non-rectangular rooms (L-shapes, etc.)
- Cells over void (no floor) are marked `IsWall = true` and rejected by `CanPlaceFurniture()`
- Gizmo colors: green = free, red = occupied, gray = wall/no floor

### Default furniture authoring (Building-level system)

Every `Building` (any subclass — Commercial, Residential, Harvesting, Transporter)
has a `_defaultFurnitureLayout : List<DefaultFurnitureSlot>` SerializeField. Slots
become live Furniture instances on first `OnNetworkSpawn` via
`TrySpawnDefaultFurniture` (server-only).

#### Mode A — Visual authoring (recommended)

Drop the Furniture prefab as a nested child of the building prefab, in the room
hierarchy you want it associated with (e.g. `Room_Main/CraftingStation`). At
runtime, `Building.Awake()` calls `ConvertNestedNetworkFurnitureToLayout()` on
every peer:

- Each network-bearing Furniture child → captured into a fresh
  `DefaultFurnitureSlot` (ItemSO + local pose + nearest Room ancestor) and
  appended to `_defaultFurnitureLayout`.
- The child GameObject is `Destroy()`d, so NGO never half-spawns it.
- Server-only `TrySpawnDefaultFurniture` then re-spawns each entry as a
  top-level NetworkObject parented under the building.

Plain-MonoBehaviour Furniture (no NetworkObject — e.g. TimeClock variant with NO
stripped) is LEFT IN PLACE and dedup'd by ItemSO inside `TrySpawnDefaultFurniture`.

#### Mode B — Manual layout (legacy / opt-in)

Author each slot directly in the Inspector list. Same runtime behavior post-spawn.
Valid for cases where the slot has no canonical scene location yet, or for
scripted spawns. If both Mode A and Mode B target the same `ItemSO`, the Mode A
nested child wins (its pose replaces the manual slot's, with a log) — remove the
manual slot to silence the log.

#### Save schema gotcha

`DefaultFurnitureSlot.LocalPosition` feeds `FurnitureKey =
"{ItemId}@{x:F2},{y:F2},{z:F2}"` for `StorageFurniture` save/restore. Moving a
slot's local position between save and load silently drops storage contents. With
Mode A, this means **moving a Furniture child in the prefab** has the same
effect — treat slot poses as part of the on-disk schema once a build ships with
stocked storages.

#### Subclass cache hook

`OnDefaultFurnitureSpawned()` is the virtual hook fired at the tail of
`TrySpawnDefaultFurniture` when the layout had entries to process. Override to
invalidate subclass-owned caches that depend on the just-spawned furniture
(storage cache on `CommercialBuilding`, craftable cache on `CraftingBuilding`).
Always chain `base.OnDefaultFurnitureSpawned()`.

#### Runtime spawn mechanics

`TrySpawnDefaultFurniture` (server-only) runs once per building instance (gated
by `_defaultFurnitureSpawned`). For each slot:

1. `Instantiate(slot.ItemSO.InstalledFurniturePrefab, worldPos, worldRot)` where `worldPos = transform.TransformPoint(slot.LocalPosition)`.
2. `NetworkObject.Spawn()` (instance is still at scene-root at this point).
3. `instance.transform.SetParent(this.transform, worldPositionStays: true)` — parents under the **building root**, the only NetworkObject in this hierarchy. **Not** under `slot.TargetRoom` — see "Why parenting under the room throws" below.
4. `slot.TargetRoom.FurnitureManager.RegisterSpawnedFurnitureUnchecked(instance, worldPos)` — records grid occupancy and adds to the room's `_furnitures` list. **No transform reparent**.

**Default-to-MainRoom registration (2026-05-02).** When `slot.TargetRoom` is null, `SpawnDefaultFurnitureSlot` defaults `registerInto` to `MainRoom` (the Building itself, via `ComplexRoom` inheritance). Authoring previously demanded an explicit `TargetRoom` reference and silently skipped registration when null — meaning the spawned furniture sat under the building root without grid occupancy AND was missing from any room's `_furnitures` list (the LogisticsManager + crafting pipeline rely on `_furnitures` for storage / station lookups). With the default, registration is the rule; designers can still set `slot.TargetRoom` explicitly to land into a specific subroom. If MainRoom has no FurnitureManager, the slot still spawns under the building root but logs a one-shot warning.

A per-slot match by `FurnitureItemSO` against existing children skips a slot if any current Furniture child of the building has the same `FurnitureItemSO` reference. This handles: (a) baked NO-free furniture like TimeClock is detected and doesn't block other slots; (b) save-restore finds no `BuildingSaveData`-tracked furniture and re-spawns the defaults; (c) future restore paths that pre-populate furniture children block the matching slot from re-spawning.

**Why this exists:** baking a furniture instance whose prefab carries a `NetworkObject` directly into a runtime-spawned building prefab makes NGO half-register the child during the parent's spawn — the child ends up in `SpawnManager.SpawnedObjectsList` with a null `NetworkManagerOwner` and NRE's `NetworkObject.Serialize` during the next client-sync, breaking client approval. See `.agent/skills/multiplayer/SKILL.md` §10.

**Why parenting under the room throws:** NGO's `OnTransformParentChanged` raises `InvalidParentException` when a NetworkObject is reparented under a GameObject without its own `NetworkObject` component. `Room_Main` is a `NetworkBehaviour` on a non-NO GameObject (only the building root carries the NO). The building root is therefore the closest valid NO ancestor. Logical room-membership lives in `FurnitureManager._furnitures` rather than transform parenting.

**`FurnitureManager.RegisterSpawnedFurnitureUnchecked(furniture, worldPos)`** bypasses `CanPlaceFurniture` (level-designer-authored slots are trusted) and deliberately does **not** call `SetParent`. Adds grid occupancy + appends to `_furnitures`.

**Authoring rule:** only NetworkObject-FREE furniture (e.g. TimeClock, which strips its NO via the prefab's `m_RemovedComponents`) may be nested directly in a building prefab and left in place. Anything network-bearing — `CraftingStation`, `Bed` — must either use Mode A (drop as nested child, auto-converted in `Awake`) or Mode B (manual slot in Inspector). The Forge prefab is the canonical Mode B example (one slot: CraftingStation in Room_Main). Furniture prefabs that should block NPC navigation should also carry a `NavMeshObstacle` (carve=true, carveOnlyStationary=true).

## Best Practices
- Always ensure `Zone` colliders have `isTrigger = true` and perfectly encapsulate their interior visual meshes, as their size dictates the generated `FurnitureGrid`.
- Room BoxColliders must have **non-zero height** — flat colliders (height=0) cause `Bounds.Contains()` to reject valid grid cells.
- Query `Furniture` availability starting from the `ComplexRoom` or `Building` level to let recursive logic find the nearest or first-available furniture in the entire property.
- When an NPC needs to drop items off or use the shop, rely on the properties like `_deliveryZone` stored natively on the `Building` component.
- Player-placed furniture prefabs must have `NetworkObject`, `Furniture` (with `_furnitureItemSO`), and `FurnitureInteractable` components.
- All gameplay effects (place, pickup) go through `CharacterAction` — player HUD is UI-only, never spawns directly.
- **Network gotcha:** Any system that caches world positions in `Awake()` (like FurnitureGrid) must recalculate in `OnNetworkSpawn()` for clients, because NGO sets the network position after `Awake()`. Interior rooms at y=5000 are the primary case where this matters.

---

## Building Placement System

Player and NPC building placement follows a shared validation pipeline.

### Key Files
| File | Purpose |
|---|---|
| `BuildingPlacementManager.cs` | Ghost visual, mouse positioning, validation, `RequestPlacementServerRpc` |
| `UI_BuildingPlacementMenu.cs` | Lists unlocked blueprints, instant mode toggle |
| `UI_BuildingEntry.cs` | Single entry row: icon + name + click handler |
| `CharacterBlueprints.cs` | Stores `UnlockedBuildingIds` and `MaxPlacementRange` |
| `WorldSettingsData.cs` | `BuildingRegistry` (PrefabId → BuildingPrefab mapping) |

### Placement Flow (Player)
1. Player opens `UI_BuildingPlacementMenu` via the HUD "Build" button.
2. Selects a building → `BuildingPlacementManager.StartPlacement(prefabId)`.
3. Ghost prefab follows mouse cursor (raycast on `_groundLayer`).
4. Ghost material changes (valid = green, invalid = red) based on `ValidatePlacement()`.
5. **Left-Click** confirms → `RequestPlacementServerRpc` spawns the building server-side.
6. **Right-Click / Escape** cancels placement.

### Validation Rules
- **Range**: `Vector3.Distance(character, target) <= CharacterBlueprints.MaxPlacementRange`.
- **Obstacle overlap**: `Physics.OverlapBox` using the building's `BuildingZone` collider against `_obstacleLayer`.
- `ValidatePlacement(Vector3)` is **public** so NPC AI systems can call it directly.

### Instant Build Mode
- Toggled via `SetInstantMode(bool)` on `BuildingPlacementManager`.
- UI exposes this as a `Toggle` in the placement menu.
- When active, the ServerRpc calls `building.BuildInstantly()` after spawning, bypassing construction requirements.

### State Management & Interactions
Building mode is integrated into the core `Character` state machine to ensure consistency:
- **Character State**: `Character.IsBuilding` flag and `OnBuildingStateChanged` event.
- **Busy Logic**: When building, `Character.IsFree()` returns `false` with `CharacterBusyReason.Building`. This prevents overlapping actions (e.g. starting a craft while placing).
- **Auto-Interruption**: `BuildingPlacementManager` inherits from `CharacterSystem`. It automatically calls `CancelPlacement()` if the character enters combat or becomes incapacitated.
- **UI Sync**: `UI_BuildingPlacementMenu` subscribes to `OnBuildingStateChanged`. If the state is cancelled externally (combat), the menu automatically closes.

### Camera Integration
The camera system reacts to building state changes for improved UX:
- **Auto Zoom**: When a character enters building mode, `CameraFollow` smoothly zooms out to the maximum allowed distance (`_targetZoom = 1f`).
- **Scroll Lock**: Manual mouse wheel zoom is disabled during placement to prevent accidental perspective shifts.
- **Restore Zoom**: Upon exiting building mode (completion or cancellation), the camera restores the previous zoom level.

### Server Authority
- The ghost is client-local only (NetworkObject disabled on the ghost prefab).
- Actual building spawn happens exclusively on the Server via `RequestPlacementServerRpc`.
- The Server re-validates the prefab ID against `WorldSettingsData.BuildingRegistry` before instantiation.

---

## Construction Loop (Phase 1 — Cooperative)

Authoritative spec: [docs/superpowers/specs/2026-05-06-building-construction-loop-design.md](../../docs/superpowers/specs/2026-05-06-building-construction-loop-design.md). Architecture wiki: [wiki/systems/construction.md](../../wiki/systems/construction.md). This section is the procedural how-to for authoring the loop into a building prefab and reasoning about its lifecycle.

**Phase 1 is cooperative finalize — no owner gate.** The placer-only gate proposed in the original 2026-05-06 design spec was dropped 2026-05-06/07 during PlayMode-MP testing because it blocked co-op partners from helping. Any character standing inside the building's `BuildingZone` can drive the Construct action. `BuildingInteractable.IsOwner` survives but is reserved for Phase 2 hold-menu options (Abandon, Sell). Spatial gate (Core Rule #1) stays on every tick.

### Lifecycle (state machine)

```
Placement (BuildingPlacementManager)
  → Building.Awake: _constructionRequirements.Count > 0 ?
                      → _currentState.Value = UnderConstruction
                      → _constructionVisualRoot.SetActive(true)
                      → _completedVisualRoot.SetActive(false)
                      → TrySpawnDefaultFurniture DEFERRED
                    else
                      → state = Complete (instant — preserves legacy behaviour)

UnderConstruction (server-only)
  ConstructionSiteScanner ticks 2 Hz:
    items = building.GetPhysicalItemsInCollider(building.BuildingZone)
    bucket by ItemSO; clamp delivered[i] = min(bucket[req.Item], req.Amount)
    write Building.ConstructionProgress + DeliveredMaterials NetworkList
    NEVER consumes items (purely observational)

  Owner clicks site → BuildingInteractable.TryQueueInteraction(FinishConstruction, actor)
    → CharacterAction_FinishConstruction (extends CharacterAction_Continuous)
    → CharacterActions.ExecuteAction → ActionContinuousTickRoutine (1 Hz default)
    → OnTick: per pending requirement, consume up to (1 + builderSkill/N) WorldItems
              by despawning matching NetworkObjects in BuildingZone, then call
              Building.ContributeMaterial(itemSO, count). Stalls 5 ticks on no-progress.

  Building.ComputeProgress() >= 1f:
    → Building.Finalize() (server) — STATE FLIP FIRST then side-effects:
        1. _currentState.Value = Complete            (atomic, replicates via NV)
        2. ConstructionProgress.Value = 1f
        3. HandleStateChanged on every peer:
             - _constructionVisualRoot.SetActive(false)
             - _completedVisualRoot.SetActive(true)
        4. TrySpawnDefaultFurniture (server-only, gated on Complete)
        5. EvictLeftoversToPerimeter (server-only) — repositions remaining
           WorldItems to NavMesh-valid points just outside _buildingZone
        6. OnConstructionComplete?.Invoke()

  Crash safety: state-flip is the FIRST line of Finalize. Crashing between (1) and
  (5) leaves Complete + a few items still on the footprint — never "paid but no
  building."
```

### Authoring a building prefab for the construction loop

Required prefab structure:

```
Building (root — Building.cs + NetworkObject)
├─ ConstructionSiteScanner          (server-only [RequireComponent(Building)])
├─ BuildingInteractable             ([RequireComponent(Building)])
├─ ConstructionVisual               ← assigned to Building._constructionVisualRoot
│  └─ scaffolding renderers, NO NavMeshObstacle on footprint (so owner can walk in to drop items)
├─ CompletedVisual                  ← assigned to Building._completedVisualRoot
│  └─ final renderers, full collider/NavMeshObstacle setup
├─ BuildingZone (BoxCollider, isTrigger=false)   ← Building._buildingZone (footprint = drop area)
├─ DeliveryZone (Zone child)        ← Building._deliveryZone (post-build logistics — unchanged)
└─ Rooms / sub-rooms with FurnitureGrid (unchanged)
```

Wiring checklist:
1. `[SerializeField] _constructionVisualRoot` and `_completedVisualRoot` on Building — assign in the Inspector.
2. `[SerializeField] _constructionRequirements` (list of `CraftingIngredient` — each `(ItemSO Item, int Amount)`). Empty list = spawns Complete instantly.
3. Add `ConstructionSiteScanner` and `BuildingInteractable` as siblings on the building root. Both `[RequireComponent(typeof(Building))]`.
4. `_buildingZone` is the **footprint drop area** — `WorldItem`s that land inside it count toward delivery. Make it large enough to cover the entire scaffolded outline.
5. The construction visual must NOT block pedestrian traffic onto the footprint — designers should use scaffold sprites without a `NavMeshObstacle` carve.

### Public API surface added to `Building`

| Member | Authority | Purpose |
|---|---|---|
| `NetworkVariable<float> ConstructionProgress` | Read=Everyone, Write=Server | UI meter, persistence pre-warm. Updates only when delta > 0.001f. |
| `NetworkList<DeliveredMaterialEntry> DeliveredMaterials` | Read=Everyone, Write=Server | Per-requirement-index delivered counts. Replicates incrementally. |
| `IReadOnlyList<CraftingIngredient> ConstructionRequirements` | All peers | Read-only view of the prefab's `_constructionRequirements`. |
| `Dictionary<ItemSO, int> ContributedMaterials` | Server-only | Server-side ledger; never replicated (clients use `DeliveredMaterials`). |
| `bool IsUnderConstruction` | All peers | `_currentState.Value == UnderConstruction`. |
| `Collider BuildingZone` | All peers | Public accessor for the footprint collider. |
| `float ComputeProgress()` | Server-only | Recomputes progress from `ContributedMaterials` against requirements. |
| `void Finalize()` | Server-only | State-flip-first finalization. **Note: shadows `object.Finalize` (the GC finalizer hook) — Building's `new void Finalize()` returns void; the GC slot is unaffected.** |
| `void EvictLeftoversToPerimeter()` | Server-only | Moves leftover `WorldItem`s to NavMesh-valid points outside `_buildingZone`. |
| `List<WorldItem> GetPhysicalItemsInCollider(Collider, List<WorldItem>)` | Any | Refactored sibling of `GetPhysicalItemsInZone`. Caller passes a reused buffer to satisfy Rule #34 (zero per-tick alloc). |
| `void ContributeMaterial(ItemSO, int)` | Server-only | Existing — bumps `_contributedMaterials` ledger. Now called from `CharacterAction_FinishConstruction.OnTick`. |

### Persistence (`BuildingSaveData` extension)

```
BuildingSaveData
├─ ConstructionProgress : float                              [2026-05-07]
├─ DeliveredMaterials   : List<DeliveredMaterialEntryDTO>    [2026-05-07]
│                         { string ItemAssetGuid, int Delivered }
└─ PlacedFurnitures     : List<PlacedFurnitureSaveEntry>     [2026-05-13]
                          { string ItemId, Vector3 LocalPosition, Vector3 LocalEulerAngles }
```

- DTO references `ItemSO` by **AssetGuid** so the snapshot survives a designer-time edit to `_constructionRequirements` ordering.
- AssetGuid resolution is editor-only (`AssetDatabase.AssetPathToGUID` is `UNITY_EDITOR`-gated). Runtime saves on a built player will write empty GUIDs — Phase 1 hibernation pre-warm only matters in-editor; the next scanner tick after wake authoritatively recomputes the meter from actual physical `WorldItem`s in the zone.
- The construction snapshot is a UX pre-warm (so the meter doesn't blink to 0 between map-wake and the next scanner tick). The next scanner tick is the source of truth and overwrites it.
- `PlacedFurnitures` carries the player/NPC-placed furniture roster — see `Placed-furniture roster` section above for the full save/restore contract. Identified by `Furniture.FurnitureItemSO.ItemId`; resolved on load via `Resources.LoadAll<ItemSO>("Data/Item")`.

### Networking & multiplayer matrix (Rule #18 / #19)

| Path | Behaviour |
|---|---|
| Host places building | Scanner runs locally on host. NetworkVariables replicate to clients. |
| Client places building | `BuildingPlacementManager.RequestPlacementServerRpc` → server spawns; same loop. |
| Host drops item / Client drops item | Existing `CharacterAction_DropItem` — `WorldItem` spawns server-side, replicates. |
| Any character clicks Finish from inside `BuildingZone` | `BuildingInteractable.Interact` → `Building.RequestStartFinishConstructionServerRpc` (legacy `[ServerRpc(RequireOwnership=false)]`); server queues `CharacterAction_FinishConstruction`; per-tick re-validates state + position (no ownership check); `Finalize` runs server-side. |
| Character clicks Finish from outside the zone | `BuildingInteractable.IsCharacterInInteractionZone` (2D X-Z) returns false → silent no-op. Server never sees the RPC. |
| Late-join | NGO spawn payload carries `_currentState`, `ConstructionProgress`, `DeliveredMaterials` — meter renders correctly on first frame. |
| Crash mid-Finalize | State flip is FIRST. Worst case: Complete building with a few un-evicted items. Never "paid but no building." |
| Two clients race Finish | Server processes serially via `_currentAction` gate in `CharacterActions`. Second is silent no-op. |
| Save/load mid-construction | `MapController.SnapshotActiveBuildings` + `Hibernate` refresh paths copy `ConstructionProgress` + `DeliveredMaterials` so progress survives. `WorldItem`s on the footprint persist via the existing world-item save pipeline. The action itself does not persist — re-engages on reload. |

### Gotchas

- **`CraftingIngredient` is a struct** — only its `Item` field can be null. `req == null` does not compile. Always check `req.Item == null`.
- **`WorldItem` is non-stacking** — each `WorldItem` instance counts as 1 unit toward a requirement. Bucketing in the scanner increments by 1 per item, and `CharacterAction_FinishConstruction.ConsumeFromZone` despawns `take` items.
- **Scanner tick rate is 2 Hz; action tick rate is 1 Hz.** They are independent. All peers see pre-action meter movement at 2 Hz; once any character triggers Finish, consumption updates appear at 1 Hz (driven by `ContributeMaterial` writing `ContributedMaterials` and `OnTick` writing `ConstructionProgress`).
- **The 2 Hz scanner is purely observational** — never consumes. Item consumption only happens inside `CharacterAction_FinishConstruction.OnTick`.
- **Theft remains possible** — `WorldItem`s in `_buildingZone` are normal interactable items. A thief can steal items the owner has not yet consumed (each tick the owner converts items into permanent progress, so the thief race shrinks per tick).
- **Phase 1 cooperative finalize — no owner gate.** Any character in `BuildingZone` can drive the Construct action. `BuildingInteractable.IsOwner` is kept for Phase 2 hold-menu options (Abandon, Sell), but the finalize path itself never calls it. **Spatial gate stays** (Core Rule #1) — actor must be inside `BuildingZone` every tick.
- **2D X-Z proximity check** — `BuildingInteractable.IsCharacterInInteractionZone` (override) and `CharacterAction_FinishConstruction.IsActorInsideBuildingZone` both drop the Y axis. 3D `Bounds.Contains` was false-negativing on `NetworkTransform`-replicated Y precision (NavMesh agent height / floor offset noise) — character standing on the footprint, but Y rounded just below `bounds.min.y`. Both client and server use the same 2D check so they stay in sync. **Don't reintroduce `bounds.Contains(charPos)` for construction zone tests.**
- **`[ServerRpc(RequireOwnership=false)]` legacy attribute** — `Building.RequestStartFinishConstructionServerRpc` uses the old `[ServerRpc]` form. Attempting `[Rpc(SendTo.Server)]` failed to dispatch in our NGO version. Building NetworkObject is server-owned, so any client invoking the RPC is by definition not the owner — `RequireOwnership=false` is the standard escape. Method name MUST end in `ServerRpc` for the legacy attribute to dispatch.
- **Continuous action visual proxy uses a 600s sentinel** — `CharacterActions.ExecuteAction` calls `BroadcastActionVisualsClientRpc(duration=600f)` for `CharacterAction_Continuous`, because continuous actions don't have a real duration. On finish (Finalize, stall timeout, manual cancel) the server **must** call `CancelActionVisualsClientRpc` so peers tear down the proxy immediately — without it the proxy lingers 600s.
- **HUD progress bar reads `Progress`, not `Duration`** — `CharacterAction_Continuous.Progress` is a virtual getter (default 0). Override on `CharacterAction_FinishConstruction` returns `Building.ConstructionProgress.Value`. `CharacterActions.GetActionProgress` checks the override BEFORE falling back to `elapsed/duration` (which would div-by-0 or read the 600s sentinel — both wrong).
- **Save/load progress restoration through refresh paths** — `MapController.SnapshotActiveBuildings` (manual save) and `MapController.Hibernate` (player-leaves wake-cycle) both walk the registered building list and refresh existing `BuildingSaveData` entries from the live `Building`. Both paths must copy `ConstructionProgress` AND `DeliveredMaterials` AND `PlacedFurnitures` from the refreshed entry. Without this (the 2026-05-07 fix `ff98c2b7` extended on 2026-05-13 for `PlacedFurnitures`), mid-build progress / player-placed furniture reset to 0 / disappear on every save/load cycle.
- **`_spawnAsComplete` designer checkbox** — `[SerializeField] bool _spawnAsComplete` on Building. When true, `OnNetworkSpawn` flips state directly to `Complete` regardless of `_constructionRequirements` content. Use for scene-authored buildings that should ship as already-built environment (player home, NPC shops, tutorial structures). Empty `_constructionRequirements` already auto-promotes to Complete; the checkbox is for prefabs that DO have requirements but don't want to load as scaffolds.
- **`Building.Finalize()` shadows `object.Finalize`** — declared as `public new void Finalize()`. The GC finalizer slot is untouched (Building has no `~Building()`). Don't add one without renaming this.
- **Default furniture is deferred until `Complete`** — `TrySpawnDefaultFurniture` early-exits during `UnderConstruction`. The state-change handler invokes it once on the transition; subsequent state changes (Damaged, Demolished, future) must not re-spawn.
- **Rule #34: zero per-tick allocation.** The scanner reuses `_scratchItems` (`List<WorldItem>`) and `_bucketCache` (`Dictionary<ItemSO, int>`); the action reuses `_scratch` (`List<WorldItem>`). `GetPhysicalItemsInCollider` accepts a caller-supplied buffer.

### Skill hook (seated for Phase 2)

`CharacterAction_FinishConstruction.OnTick` computes consume budget as:
```
int budget = 1 + (actor.GetSkillLevelOrZero(SkillId.Builder) / SkillBudgetDivisor);
```
`SkillId` enum lives at `Assets/Scripts/Character/Skills/SkillId.cs`. `Character.GetSkillLevelOrZero(SkillId)` is a Phase 1 stub returning 0 (so `budget = 1` for everyone). When the actual `BuilderSkill` system lands, this method becomes the integration point — no action signature change.

### Dev tools

- `BuildingInspectorView` (in `Assets/Scripts/Debug/DevMode/Inspect/`) surfaces live `ConstructionProgress`, per-requirement `DeliveredMaterials` breakdown, owner display name, and a **Force Finish** dev button that calls `Building.Finalize()` directly (bypasses the action).
- `BuildingPlacementManager._isInstantMode` (debug toggle) preserves the existing one-click instant-build path — bypasses scaffolding visual entirely.

---

## Building Interiors

Interiors use the **Spatial Offset Architecture** (placed at `y=5000` via `WorldOffsetAllocator.GetInteriorOffsetVector()`). They are **lazy-spawned** on first entry and **hibernate independently** when empty.

### Key Files
| File | Purpose |
|---|---|
| `BuildingInteriorDoor.cs` | Exterior entrance door (inherits `MapTransitionDoor : InteractableObject`) |
| `BuildingInteriorRegistry.cs` | Server singleton mapping BuildingId → InteriorRecord, ISaveable |
| `BuildingInteriorSpawner.cs` | Static helper that instantiates + configures interior prefabs |
| `CharacterMapTracker.cs` | Server-side lazy-spawn in `ResolveInteriorPosition()`, `WarpClientRpc` |
| `CharacterMapTransitionAction.cs` | Client-side fade + warp action, uses `ForceWarp` |
| `ScreenFadeManager.cs` | Client-only fade-to-black overlay (uses `Time.unscaledDeltaTime`) |

### 1. Linking Exterior to Interior
The connection is established via **`BuildingInteriorDoor.cs`** on the exterior building.
- **Auto-detection:** The door derives `BuildingId` and `PrefabId` from `GetComponentInParent<Building>()`. The `ExteriorMapId` is auto-detected from the interactor's `CurrentMapID`, a parent `MapController`, or falls back to `"World"`.
- **Deterministic ID:** Interior `MapId` = `"{ExteriorMapId}_Interior_{BuildingId}"`. Both client and server can compute this independently.
- **Lazy Spawning:** The interior is only spawned when the first player interacts with the door. The server handles this in `CharacterMapTracker.ResolveInteriorPosition()`.
- **Scene hierarchy:** After `netObj.Spawn(true)`, `BuildingInteriorSpawner` calls `MapController.GetByMapId(record.ExteriorMapId)` and `netObj.TrySetParent(exteriorMap.transform, worldPositionStays: true)` so the interior MapController becomes a child of its exterior in both server and client hierarchies. Valid because both are NetworkObjects — cross-NetworkObject parenting is NGO-safe. If `TrySetParent` fails, a warning is logged and the interior stays at scene root (still fully networked, just visually disconnected).

### 2. Interior Prefab Requirements
Every Interior Prefab root must contain:
- `MapController` (spawner sets `IsInteriorOffset = true` and `MapId` at runtime)
- `NetworkObject`
- `NavMeshSurface` (must be baked in the prefab relative to root)
- One or more `Room` components (for furniture placement)
- A plain `MapTransitionDoor` for the exit door (**NOT** `BuildingInteriorDoor`)
  - The exit door can have a `TargetSpawnPoint` in the prefab for editor preview, but `BuildingInteriorSpawner` **clears it at runtime** and uses `TargetPositionOffset` instead (computed as `exteriorReturnPos - exitDoor.transform.position`)

### 3. Transition Flow (Enter)
1. Player interacts with `BuildingInteriorDoor`.
2. Door computes `interiorMapId` and `targetPosition` (Vector3.zero on first visit, real position on repeat visits).
3. `CharacterMapTransitionAction.OnStart()` fades to black (`ScreenFadeManager`).
4. `OnApplyEffect()`: Client calls `ForceWarp` if position is known, then sends `RequestTransitionServerRpc`.
5. **Server** (`ResolveInteriorPosition`): On first visit, registers the interior in `BuildingInteriorRegistry`, spawns via `BuildingInteriorSpawner`, resolves the interior offset position.
6. If the server resolved a different position than the client sent, it sends `WarpClientRpc` back to the owning client.
7. `WarpClientRpc` calls `CharacterMovement.ForceWarp()` on the client (owner-authoritative via `ClientNetworkTransform`).

### 4. Transition Flow (Exit)
1. Player interacts with the plain `MapTransitionDoor` inside the interior.
2. `MapTransitionDoor.Interact()` computes `dest = transform.position + TargetPositionOffset` (which resolves to the exterior return position).
3. Same `CharacterMapTransitionAction` flow: fade, `ForceWarp`, `RequestTransitionServerRpc`.
4. Interior `MapController` hibernates when player count reaches 0.

### 5. ForceWarp (Cross-NavMesh Teleport)
`CharacterMovement.ForceWarp(Vector3)` is required for all interior transitions because the source and destination have separate NavMesh surfaces.
- Disables `NavMeshAgent` before teleporting (prevents snap-back to old NavMesh).
- Sets `transform.position` and `Rigidbody.position` directly.
- Sets Rigidbody to kinematic during teleport to prevent gravity interference.
- Re-enables the agent after **2 frames** (coroutine) so the destination NavMesh is ready.
- Regular `Warp()` must NOT be used for cross-map teleports — `NavMeshAgent.Warp` silently fails if the destination has no NavMesh.

### 6. Important Gotchas
- **FixedString size:** Map IDs use `FixedString128Bytes` (not 32) because interior IDs can be 50+ chars.
- **CharacterMovement location:** Use `_character.GetComponentInChildren<CharacterMovement>()`, not `TryGetComponent`, as it may be on a child GameObject.
- **ClientNetworkTransform:** Characters use owner-authoritative networking. The server cannot move the client — it must send `WarpClientRpc` for the client to move itself.
- **Exit door TargetSpawnPoint:** `BuildingInteriorSpawner` must null out any prefab-assigned `TargetSpawnPoint` on exit doors, otherwise it overrides the computed `TargetPositionOffset`.
- **Building.HasInterior / GetInteriorMap():** Helpers on `Building` to query the registry. Used by NPC systems to check if a building has a spawned interior.

### 7. BuildingInteriorRegistry (ISaveable)
- Singleton with `Dictionary<string, InteriorRecord>` keyed by `BuildingId`.
- `InteriorRecord`: `BuildingId`, `InteriorMapId`, `SlotIndex`, `ExteriorMapId`, `ExteriorDoorPosition`, `PrefabId`.
- On `RestoreState()`, respawns all interior MapControllers via `BuildingInteriorSpawner`.
- Allocates spatial slots via `WorldOffsetAllocator.AllocateSlotIndex()`.
- **Door persistence fields**: `InteriorRecord` includes `bool IsLocked = true` and `float DoorCurrentHealth = -1f` (negative = use prefab default). Read AND write paths are wired:
  - **Read**: `DoorLock`/`DoorHealth.OnNetworkSpawn` prefer the persisted record over field defaults. `BuildingInteriorSpawner` re-applies after `Spawn()` (defensive).
  - **Write**: `DoorLock.SetLockedStateWithSync` and `DoorHealth.OnCurrentHealthChanged` (server) push state into the record on every change.
  - **Restore-race fix**: `BuildingInteriorRegistry.RestoreState` calls `DoorLock.ApplyLockState` + `DoorHealth.ApplyHealthState` for each record so exterior doors (already spawned from the scene) get patched after restore.
  - **Pre-record snapshot**: `RegisterInterior` snapshots live door state via `DoorLock.GetCurrentLockState` + `DoorHealth.GetCurrentHealth` when first creating a record so changes done before first entry persist.

### 8. Door Lock / Door Health on Building Doors

Building doors (both `BuildingInteriorDoor` on the exterior and the exit `MapTransitionDoor` inside the interior) can have optional `DoorLock` and `DoorHealth` components. See the **door-lock-system** skill for full details.

**Key integration points:**
- **LockId auto-generation**: `DoorLock._lockId` must be **empty on building door prefabs**. At runtime, `DoorLock.OnNetworkSpawn()` auto-derives it from `GetComponentInParent<Building>().BuildingId` (unique GUID per building instance). This means same prefab, different lock per instance.
- **Interior exit door LockId**: Set by `BuildingInteriorSpawner` via `exitLock.SetLockId(record.BuildingId)` **before** `NetworkObject.Spawn()`, so both exterior and interior doors share the same LockId and auto-pair.
- **Paired door sync**: All doors with the same LockId are linked via a static registry. Lock/unlock/jiggle on one propagates to all paired doors.
- **No nested NetworkObjects**: `DoorLock` and `DoorHealth` sit on the door child GameObject but use the parent building's `NetworkObject`. **Never** add a separate `NetworkObject` to the door child.
- **IsSpawned guards**: All `NetworkVariable` reads and RPC calls on `DoorLock`/`DoorHealth` must be guarded with `doorLock.IsSpawned` to handle cases where the `NetworkObject` hasn't spawned yet.

### Programmatic NPC interior entry / exit

Two reusable `CharacterAction`s let any caller (BT, GOAP, party, quest, future order system) order an NPC to enter or leave a building interior:

```csharp
// Enter a specific building
npc.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(npc, targetBuilding));

// Leave whatever interior the NPC is currently inside
npc.CharacterActions.ExecuteAction(new CharacterLeaveInteriorAction(npc));
```

Both walk the NPC to the appropriate door and call `door.Interact(npc)`, which triggers the existing `BuildingInteriorDoor` lock/key/rattle/transition pipeline. Failure modes (no door, locked-no-key, timeout, unreachable) cancel cleanly with a `Debug.LogWarning` so the caller can observe and react.

Authority: the actions run server-side for NPCs (rule #18). For player actors the action runs on the owning client — currently no UI surfaces it, but it is queueable.

Both inherit an internal abstract `CharacterDoorTraversalAction` that owns the shared walk-loop (freeze NPC controller, repath every 2 s, 15 s timeout, locked-with-key two-step retry, unfreeze on cancel). Subclasses only override `ResolveDoor()` and `IsActionRedundant()`.

---

## Building-Map Registration & Hibernation

> **STATUS: HIBERNATION DISABLED** — `MapController._hibernationEnabled` is `false` by default.
> The NPC/Building despawn-on-exit and respawn-on-enter system is fully implemented but disabled
> due to unresolved issues with NPC visual restoration (2D Animation bone corruption) and
> combat knockback falsely triggering OnTriggerExit → full hibernate cycle mid-fight.
> When re-enabling, ensure: (1) NPC identity/visual data is restored correctly from
> `HibernatedNPCData.RaceId/CharacterName/VisualSeed`, (2) a grace period prevents
> knockback-triggered hibernation, (3) Spine2D visual system is integrated.

Player-placed buildings are registered with the `MapController` they're placed inside, ensuring they survive map hibernation.

### Key Flow
1. **On placement** (`BuildingPlacementManager.RequestPlacementServerRpc` → `RegisterBuildingWithMap`):
   - `MapController.GetMapAtPosition(position)` tries the containing map.
   - **Bounds fallback** — iterates all exterior `MapController`s and tests `BoxCollider.bounds.Contains`.
   - **Must be inside a Region** — `ValidatePlacement` rejects out-of-Region clicks via `IsInsideRegion` (client ghost goes red + toast). Server re-validates in `RequestPlacementServerRpc` as authority.
   - **Expand nearby map** — if still no enclosing map, `MapRegistry.FindNearestMapInRegion(position)` returns the closest same-region exterior map within `WorldSettingsData.MapMinSeparation` (default 150 Unity units ≈ 23 m). If found → `MapController.ExpandBoundsToInclude(position, footprintSize, regionBounds)` grows that map's BoxCollider to envelop the new building, clamped to the Region's bounds. Building joins the expanded map.
   - **Create wild map** — if no nearby map either, `MapRegistry.CreateMapAtPosition(position)` spawns a new exterior MapController centered on the placement, then `MapController.ClampBoundsToRegion(regionBounds)` shrinks the new map to fit inside the Region. Registers a fresh `CommunityData` (Tier=Settlement, no leaders, no biome, MapId = `Wild_<guid8>`), allocates a `WorldOffsetAllocator` slot. **No rejection on MinSep** — it routes to expansion above.
   - Building is parented to the resolved MapController via `SetParent()`.
   - A `BuildingSaveData` entry is added to `CommunityData.ConstructedBuildings`.
   - `Building.PlacedByCharacterId` is set to the placing character's UUID.
2. **On hibernation** (`MapController.Hibernate()`): Buildings are synced to save data and despawned (matching the NPC pattern)
3. **On wake-up** (`MapController.WakeUp()`): Buildings are re-instantiated from `ConstructedBuildings`, with `BuildingId` restored (not regenerated) to prevent duplication
4. **Construction completion** (`Building.HandleStateChanged`): State is synced back to the matching `ConstructedBuildings` entry

### Known Issues (For When Re-Enabling)
- **NPC visual data**: `HibernatedNPCData` now saves `RaceId`, `CharacterName`, `VisualSeed` but restoration was untested with current 2D Animation system. Spine2D migration should fix bone deformation crashes.
- **Combat knockback**: Player can be knocked outside MapController trigger → OnTriggerExit → player count 0 → Hibernate fires mid-combat. Fix: add a 2-3s grace period in `CheckHibernationState` before calling `Hibernate()`.
- **Community auto-creation**: `EnsureCommunityData()` creates a CommunityData with no leaders for predefined maps. Permission check allows everyone to build when `LeaderIds.Count == 0`.
- **Predefined map OriginChunk**: Auto-created CommunityData now uses the map's actual world position for `OriginChunk` (not default `(0,0)`).
- **Spawn-then-reparent ordering in `SpawnSavedBuildings`** — current order is `bNet.Spawn()` → `bObj.transform.SetParent(this.transform)`. NGO prefers parent-before-spawn; the current order has been observed to produce half-spawned `NetworkObject`s whose internal `NetworkManagerOwner` ends up null, causing `NetworkObject.Serialize` to NRE during a client-join approval handshake and silently break every join against a loaded save. Fresh worlds are unaffected. Defensive purge lives in `GameSessionManager.PurgeBrokenSpawnedNetworkObjects` but the root fix is to reorder to `SetParent` first, `Spawn()` second. See [[network]] in the wiki for the full diagnostic write-up.

### `MapController.GetMapAtPosition(Vector3)`
Static utility that iterates `_mapRegistry`, skips interiors, returns the first map whose `_mapTrigger.bounds.Contains(position)`. Returns null for open world.

### `MapController.GetNearestExteriorMap(Vector3, float maxDistance)`
Static utility that iterates `_mapRegistry`, skips interiors, and returns the map whose trigger's `ClosestPoint(position)` is within `maxDistance`. Used by `BuildingPlacementManager` to "join" a nearby existing map before falling back to creating a new wild map.

### `MapRegistry.CreateMapAtPosition(Vector3)`
(`CommunityTracker` was renamed to `MapRegistry` in Phase 1 — ADR-0001.) Server-only. Instantiates the MapController prefab at `worldPosition`, allocates a unique MapId (`Wild_<guid8>`) + `WorldOffsetAllocator` slot, pre-registers a `CommunityData` (Tier=Settlement, no leaders, no biome, `IsPredefinedMap=false`), then spawns the `NetworkObject`. Returns the new MapController or null on failure. **Enforces `WorldSettingsData.MapMinSeparation`** — rejects if another `MapController` (exterior) or `WildernessZone` center is within the configured distance, returns null with a warning log. **Caveat:** `MapController.MapId` is a plain `string`, not a NetworkVariable — clients will not learn the MapId of dynamically spawned maps without a dedicated sync. Tracked as a broader follow-up; the wild-map path inherits this behavior.

### `BuildingSaveData.FromBuilding(Building, Vector3 mapCenter)`
Static factory creating a save entry with position **relative** to map center. Also captures:
- `OwnerCharacterIds` — `List<string>` from `Room.OwnerIds` (raw NetworkList read; works for both Residential and Commercial; preserves hibernated owners). Replaces the deprecated `OwnerNpcId` single-string field.
- `Employees` — `List<EmployeeSaveEntry>` (`CharacterId`, `JobType`) for CommercialBuilding crews. Iterates `commercial.Jobs` and emits one entry per assigned job.

`MapController.SnapshotActiveBuildings()` and `MapController.Hibernate()` always *replace* the dynamic fields (`OwnerCharacterIds`, `Employees`, `State`, `Position`, `Rotation`) on existing entries — do not patch fields individually or stale ownership leaks across saves.

### Ownership Sync Invariant (`_ownerIds` ↔ `CharacterLocations.OwnedBuildings`)

Ownership lives on **both** sides and must stay in sync:

- **Building side** — `Room._ownerIds` (NetworkList<FixedString64Bytes>) replicates to clients. Mutated by `Room.AddOwner`, `Room.RemoveOwner`, and `CommercialBuilding.SetOwner` / `ResidentialBuilding.SetOwner` (which clear + refill).
- **Character side** — `CharacterLocations.OwnedBuildings` (plain `List<Building>`, server-only today). Mirrors which buildings this character owns. Used by permission logic (`AddOwnerToBuilding`, `AddResidentToRoom`) and home resolution (`GetHomeBuilding`, `GetAssignedBed`).

Two entry points keep both sides consistent:

1. `CharacterLocations.ReceiveOwnership(Building)` — character-first path. Adds to `OwnedBuildings`, then calls `building.AddOwner(_character)`. Used by `SpawnManager` / purchase flows.
2. `Building.SetOwner(Character)` — building-first path. Unregisters the **old** owners (`oldOwner.CharacterLocations.UnregisterOwnedBuilding(this)`), clears `_ownerIds`, calls `AddOwner(newOwner)`, then calls `newOwner.CharacterLocations.RegisterOwnedBuilding(this)`. Used by dev-mode Assign-Building, `CharacterJob.BecomeBoss`, save/restore, and residential ownership transfer.

`RegisterOwnedBuilding` / `UnregisterOwnedBuilding` are lightweight character-side mirrors — they do **not** call back into `building.AddOwner`, which avoids circular calls and double-inserts into `_ownerIds`.

**Known gap:** `OwnedBuildings` is not networked. Remote clients see an empty list for their own character unless they are also the host. If a non-host client needs to query its own ownership, read `building.IsOwner(character)` (replicated via `_ownerIds`) instead. A future refactor could replace `OwnedBuildings` with a derived `BuildingManager.GetAllBuildings().Where(b => b.IsOwner(_character))` getter.

### Building Ownership/Employee Restoration
Restoration is split across two layers so that **every** Building subclass — Residential, Commercial, Harvesting, plain Building — gets owner restoration on save/load. Pre-2026-05-09 only CommercialBuilding had a restoration path; ResidentialBuilding owners were silently dropped on load.

**Owner restoration (base `Building`):**
- `Building.RestoreOwnersFromSaveData(List<string> ownerIds)` — server-only, populates `_pendingOwnerIds`, tries `Character.FindByUUID` on each, and subscribes to **two** static events for unresolved IDs:
  - `Character.OnCharacterSpawned` — catches NPC owners (their persistent UUID is set inside `SpawnNPCsFromSnapshot → ImportProfile` BEFORE `OnNetworkSpawn` fires).
  - `Character.OnCharacterIdReassigned` — catches the **host's player Character** (it spawns with a fresh `Guid.NewGuid()` in GameLauncher Step 4, then has its persistent profile GUID overwritten by `CharacterDataCoordinator.ImportProfile` in Step 6 — AFTER the spawn event already fired with the wrong ID). Without this second hook, host-owned buildings come back un-owned on every load. Fired from `CharacterDataCoordinator.ImportProfile` only when `NetworkCharacterId.Value` actually changes.
- The handler `HandleCharacterIdentityResolvedForOwnerRestore` is identical for both events: walk the pending list, bind anything that now resolves, unsubscribe both events when the list drains.
- Per-owner binding goes through `protected virtual void BindRestoredOwner(Character owner)`:
  - **Default** (plain `Building`) — `AddOwner(owner)` (Room) + `owner.CharacterLocations.RegisterOwnedBuilding(this)` for the character-side mirror.
  - **`ResidentialBuilding`** — calls `SetOwner(owner)` so the residency mirror, CharacterLocations link, and old-owner unregister all run.
  - **`CommercialBuilding`** — calls `SetOwner(owner, ownerJob, autoAssignJob: false)` AND looks up the matching saved `EmployeeSaveEntry` in `_pendingEmployees` to recover the boss's actual job slot (LogisticsManager / Cashier / etc.) instead of the auto-pick stealing a slot another saved employee owns.
- Cleanup: `Building.OnNetworkDespawn` calls `UnsubscribeOwnerRestoreListener` (idempotent, safe across re-hibernation cycles).

**Employee restoration (`CommercialBuilding`-only):**
- `CommercialBuilding.RestoreEmployeesFromSaveData(List<EmployeeSaveEntry>)` — populates `_pendingEmployees`, resolves via `Character.FindByUUID` + `worker.CharacterJob.TakeJob(job, building)` for the bidirectional link (building.Jobs ↔ character._activeJobs).
- Same async pending+OnCharacterSpawned pattern; cleaned up in `CommercialBuilding.OnNetworkDespawn` → `UnsubscribeEmployeeRestoreListener`.

**Call ordering (in `MapController.ApplyDynamicSaveDataToBuilding`):**
1. `building.RestoreOwnersFromSaveData(bSave.OwnerCharacterIds)` — owners FIRST so the CommercialBuilding override of `BindRestoredOwner` can consume the boss's matching employee entry before the employee pass.
2. `(building as CommercialBuilding)?.RestoreEmployeesFromSaveData(bSave.Employees)`.

The split lives in `MapController.ApplyDynamicSaveDataToBuilding(Building, BuildingSaveData)` — the single helper used by both `SpawnSavedBuildings` (new spawn + preplaced overlay) and `WakeUp` (new spawn from hibernation).

### Preplaced building dynamic-state overlay
Pre-2026-05-09, `MapController.SnapshotActiveBuildings` filtered out scene-authored ("preplaced") buildings via `if (building.PlacedByCharacterId.Value.IsEmpty) continue;`, and `SpawnSavedBuildings` further skipped any building whose `BuildingId` already existed in the scene. The combined effect: **owners, employees, storage contents, cashier state, and shop catalog applied to a preplaced building at runtime were silently dropped on every save→load cycle.**

Current behavior:
- `SnapshotActiveBuildings` snapshots ALL buildings. Preplaced buildings have a deterministic BuildingId (from `Building.DeriveDeterministicSceneBuildingId(sceneName, position)` — MD5 of scene name + mm-rounded position) that round-trips reliably.
- `SpawnSavedBuildings` keeps the "don't re-spawn what's already in scene" guard but routes the matched save entry through `ApplyDynamicSaveDataToBuilding(existing, bSave)` instead of a bare `continue`. Owners, employees, storage, cashier, shop, and construction state all overlay onto the live scene instance.

Result: assigning an owner to a scene-authored Tavern via dev mode now persists across save/load.

### `Building.PlacedByCharacterId`
`NetworkVariable<FixedString64Bytes>` tracking who originally placed the building. Distinct from `CommercialBuilding.Owner` (business operator). **Always restored** by `MapController.SpawnSavedBuildings()` / `WakeUp()` from `BuildingSaveData.PlacedByCharacterId` — early implementations dropped it on load.

### `BuildingManager.OnBuildingRegistered`
`static event Action<Building>` fired by `BuildingManager.RegisterBuilding`. Used by `CharacterJob` to lazily re-bind to a saved workplace when the building's map wakes up (event-driven; works for hibernated workplaces).

---

## Community Territory & Build Permits

### Leadership Model
- `CommunityData.LeaderIds`: `List<string>` of all leader character IDs (primary leader is first)
- `CommunityData.LeaderNpcId`: The primary leader (backward-compatible)
- `CommunityData.IsLeader(characterId)`: Checks if a character is any leader
- `CommunityData.AddLeader(characterId)`: Adds a leader, sets primary if first

### Placement Permissions
Buildings can only be placed inside a community zone by:
1. **Community leaders** (in `LeaderIds`) — always allowed
2. **Permit holders** — characters who obtained a `BuildPermit` from a leader

Non-leaders without a permit see a **red ghost** (placement denied). Open world has no restrictions.

### Build Permit System
- `BuildPermit`: `CharacterId`, `GrantedByLeaderId`, `RemainingPlacements`, `MapId`
- `CommunityData.GrantPermit()` / `HasPermit()` / `ConsumePermit()` methods
- Permits stack if granted multiple times
- Consumed on the server in `RequestPlacementServerRpc` after successful placement

### `InteractionRequestBuildPermit`
Extends `InteractionInvitation`. A non-leader asks a community leader for permission to build. NPC leaders evaluate based on relationship score. On acceptance, a `BuildPermit` is granted.

---

## Community Expansion & Building Adoption

`MapRegistry.AdoptExistingBuildings(MapController, CommunityData)` is the preserved API for discovering existing buildings inside a newly-created MapController's bounds (Phase 1 ADR-0001 removed its only caller — `PromoteToSettlement` — but the method remains for future wiring into `CreateMapAtPosition` when the placement landing lands near orphan buildings).

### Adoption Rules
- **Unowned buildings** (`PlacedByCharacterId` empty): Auto-claimed immediately
- **Owned buildings (owner present)**: Leader sends `InteractionNegotiateBuildingClaim` invitation
- **Owned buildings (owner absent)**: Queued as `PendingBuildingClaim` with 7-day timeout

### `InteractionNegotiateBuildingClaim`
Extends `InteractionInvitation`. Community leader negotiates with the building owner. NPC evaluation is relationship-based. On acceptance, building is parented to the MapController and added to `ConstructedBuildings`.

### Pending Building Claims
- `PendingBuildingClaim`: `BuildingId`, `OwnerCharacterId`, `DayClaimed`, `TimeoutDays`
- Processed daily in `MapRegistry.HandleNewDay()` (renamed from `CommunityTracker.HandleNewDay`)
- If owner returns: negotiation invitation triggered
- If timeout expires: auto-claimed into community
- `BuildingManager.FindBuildingById(id)` resolves live Building instances

---

## BuildingSO Blueprint API (2026-05-16)

- `BuildingSO` — base SO at `Assets/Scripts/World/Data/BuildingSO.cs`, namespace `MWI.WorldSystem`. Holds PrefabId / BuildingName / Icon / BuildingPrefab / InteriorPrefab / CommunityPriority / BuildingType / ConstructionRequirements / DefaultFurnitureLayout. One asset per building type under `Assets/Resources/Data/Buildings/`.
- `BuildingCommercialSO : BuildingSO` — adds `int BaseTreasury` (seed amount credited at construction-complete).
- `Building._blueprint` — `[SerializeField] BuildingSO _blueprint` on every Building prefab. Replaces the 5 legacy duplicated prefab fields (`_prefabId` / `buildingName` / `_buildingType` / `_constructionRequirements` / `_defaultFurnitureLayout`), all deleted in Task 6 of this migration.
- `WorldSettingsData.Blueprints` — `List<BuildingSO>` is the new lookup surface. Legacy `BuildingRegistry` marked `[Obsolete]`, deleted in Task 18.
- `WorldSettingsData.GetBuildingBlueprint(string prefabId)` — new helper returning `BuildingSO` by PrefabId (the existing `GetBuildingPrefab` / `GetInteriorPrefab` still work, now blueprint-first).
- **PrefabId strings are the cross-session save key — NEVER rename them after authoring.** Renaming silently invalidates every existing save file.
- **Construction requirements lifted from prefab to SO.** Positional index contract preserved (`DeliveredMaterialEntry.RequirementIndex`).
- `CommunityData.NativeCurrency` — new `CurrencyId` field on CommunityData (server-driven save data). `MapController.NativeCurrency` is the convenience getter (falls back to `CurrencyId.Default`).
- `CommercialBuilding.OnDefaultFurnitureSpawned` is the canonical seeding hook: extends the existing override to call `SeedTreasuryIfNeeded()` which credits the Treasury safe in the local map's NativeCurrency. Idempotent via `_treasurySeeded` flag (server-only).
- `BuildingSaveData.TreasurySeeded` — persists the idempotency flag across save/reload. Default-false on old saves so legacy data re-seeds exactly once on next load.
- Migration tool: `Assets/Editor/BuildingRegistryToBuildingSOMigration.cs` — Menu: `MWI > Migration > Convert BuildingRegistry → BuildingSO assets`. Already run on this branch; idempotent.
