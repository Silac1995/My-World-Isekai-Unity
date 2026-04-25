---
type: system
title: "Storage Furniture"
tags: [building, furniture, inventory, logistics, storage, tier-1]
created: 2026-04-25
updated: 2026-04-25
sources: []
related: ["[[furniture-grid]]", "[[building]]", "[[commercial-building]]", "[[building-logistics-manager]]", "[[inventory]]", "[[item-instance]]", "[[ai-goap]]", "[[jobs-and-logistics]]", "[[network]]", "[[save-load]]"]
status: wip
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: [npc-ai-specialist, item-inventory-specialist]
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on: ["[[furniture-grid]]", "[[building]]", "[[item-instance]]", "[[inventory]]", "[[ai-goap]]"]
depended_on_by: ["[[building-logistics-manager]]", "[[jobs-and-logistics]]", "[[commercial-building]]"]
---

# Storage Furniture

## Summary
A `Furniture` subclass that holds items inside authored slots — chest, shelf, barrel, wardrobe. Mirrors the player [[inventory]] pattern (typed `ItemSlot` subclasses, `OnInventoryChanged` event), exposes a strict-first `AddItem` priority, and is a **first-class logistics target**: the LogisticsManager and Transporter prefer slots over the loose `StorageZone` ground when one exists. Optional companion component `StorageVisualDisplay` renders contents on shelves; chests omit it for zero rendering cost.

## Purpose
Two things would be missing without it:

1. **Believable storage UX.** Crafted goods piling on the floor of a shop/forge looks broken. Slot-based containers let items live in chests and shelves like a real building.
2. **Spatial structure for logistics.** Without slots, every building has a single soup of "items in StorageZone" — couriers walk to the zone center and the LogisticsManager spreads drops randomly. Slots give buildings explicit content placement that authors and AI can reason about (e.g. wardrobe with `WearableSlot`s automatically rejects a sword without any extra code).

## Responsibilities
- Own a flat `List<ItemSlot>` initialized from four authored capacity ints (`_miscCapacity`, `_weaponCapacity`, `_wearableCapacity`, `_anyCapacity`).
- Provide a server-side `AddItem` / `RemoveItem` API that mirrors [[inventory]]'s `Inventory.cs`, fires `OnInventoryChanged` on every mutation, and respects an `IsLocked` flag.
- Apply **strict-first slot priority** in `AddItem`: wearables try `WearableSlot → MiscSlot → AnySlot`; weapons try `WeaponSlot → AnySlot`; everything else `MiscSlot → AnySlot`. Dedicated typed slots fill before the generic `AnySlot` catch-all.
- Be discoverable by the host's [[ai-goap|GOAP]] logistics actions through `CommercialBuilding.FindStorageFurnitureForItem(ItemInstance)` and `GetItemsInStorageFurniture()`.

**Non-responsibilities** (common misconceptions):
- Not responsible for the network wire format of slot contents — that lives on the sibling `StorageFurnitureNetworkSync` `NetworkBehaviour` which holds the `NetworkList<NetworkStorageSlotEntry>` and runs the server→client mirror. `StorageFurniture` itself stays a plain `MonoBehaviour`; the sync component reuses the parent `Furniture_prefab`'s `NetworkObject`.
- Not responsible for the on-disk save/load schema — that lives on `BuildingSaveData.StorageFurnitures` (one `StorageFurnitureSaveEntry` per storage, holding a sparse list of `StorageSlotSaveEntry`). `StorageFurniture` exposes `RestoreFromSaveData(IReadOnlyList<(int, ItemInstance)>)` as the server-only restore entry point that `MapController.SpawnSavedBuildings` / `WakeUp` invoke after the building's default-furniture spawn finishes.
- Not responsible for the visual placement of items on a shelf. That belongs to the optional [[storage-furniture#StorageVisualDisplay (renderer side)|StorageVisualDisplay]] component, which is a pure rendering layer. Storage data + rendering are intentionally split (SOLID).

## Key classes / files
| File | Role |
|------|------|
| [StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs) | Slot-based container — data + API + lock state. Adds `ApplySyncedSlotsFromNetwork` for the sync layer and `RestoreFromSaveData` for the save-restore path. |
| [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) | Defines `StorageSlotSaveEntry`, `StorageFurnitureSaveEntry`, `BuildingSaveData.StorageFurnitures`, and the static `BuildingSaveData.ComputeStorageFurnitureKey` helper used by both save and restore. |
| [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) | Calls `RestoreStorageFurnitureContents` from `SpawnSavedBuildings` (predefined-map load + dynamic wild-map respawn) and from `WakeUp` (post-hibernation restore). |
| [StorageFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs) | Sibling `NetworkBehaviour` — holds the `NetworkList<NetworkStorageSlotEntry>` and runs the server→client mirror. Authored on `Storage.prefab`, inherited by every variant. |
| [StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) | Optional renderer for shelves; uses sprite-only displays + per-component pool + distance gating. Now driven by the per-peer `OnInventoryChanged` fired through the sync layer. |
| [ItemSlot.cs](../../Assets/Scripts/Inventory/ItemSlot.cs) | Abstract base — `CanAcceptItem` decides which subtype each slot accepts. |
| [MiscSlot.cs](../../Assets/Scripts/Inventory/MiscSlot.cs) | Accepts any non-`WeaponInstance` (matches [[inventory]] convention — wearables fit too). |
| [WeaponSlot.cs](../../Assets/Scripts/Inventory/WeaponSlot.cs) | Weapons only. |
| [WearableSlot.cs](../../Assets/Scripts/Inventory/WearableSlot.cs) | Wearables only — added for storage furniture variants. |
| [AnySlot.cs](../../Assets/Scripts/Inventory/AnySlot.cs) | Permissive catch-all — added for "global" storage variants. |
| [CharacterStoreInFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterStoreInFurnitureAction.cs) | Worker → slot transfer. No `WorldItem` spawned. |
| [CharacterTakeFromFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterTakeFromFurnitureAction.cs) | Slot → worker hands transfer. |
| [Furniture.cs](../../Assets/Scripts/World/Furniture/Furniture.cs) | Base class — `GetInteractionPosition()` + new `GetInteractionPosition(Vector3 fromPosition)` overload + auto-create-on-Reset hook. |

## Public API / entry points
- `StorageFurniture.AddItem(ItemInstance)` → bool — strict-first slot priority insert. Fires `OnInventoryChanged` on success.
- `StorageFurniture.RemoveItem(ItemInstance)` / `RemoveItemFromSlot(ItemSlot)` — mutations also fire the event.
- `StorageFurniture.HasFreeSpaceForItem(ItemInstance)` / `HasFreeSpaceForItemSO(ItemSO)` / per-type variants.
- `StorageFurniture.IsLocked`, `Lock()`, `Unlock()`.
- `event Action OnInventoryChanged` — listeners are `StorageVisualDisplay` and any future networking layer.
- `CommercialBuilding.FindStorageFurnitureForItem(ItemInstance)` → `StorageFurniture` or null. Walks all sub-rooms; first-fit by furniture order; respects lock + per-slot `CanAcceptItem`.
- `CommercialBuilding.GetItemsInStorageFurniture()` → `IEnumerable<(StorageFurniture, ItemInstance)>` — used by outbound logistics to find reserved instances stored as logical-only slot data.
- `Furniture.GetInteractionPosition(Vector3 fromPosition)` — worker-aware overload that returns `InteractionZone.bounds.ClosestPoint(fromPosition)` when no `_interactionPoint` is authored, landing the target on the navmesh-walkable face of the furniture.

## Data flow

```
Crafter spawns WorldItem in BuildingZone
  ↓
LogisticsManager runs GoapAction_GatherStorageItems
  ↓
DetermineStoragePosition() → FindStorageFurnitureForItem(item)
  ├─ furniture found → walk to furniture.GetInteractionPosition(worker)
  │                    → CharacterStoreInFurnitureAction
  │                    → slot.ItemInstance = item ; OnInventoryChanged fires
  │                    → CommercialBuilding.AddToInventory(item) (logical stock)
  │                    → StorageVisualDisplay (if present) renders item
  └─ no furniture     → walk to StorageZone, CharacterDropItem (legacy)

Transporter pickup (outbound)
  ↓
GoapAction_LocateItem scans GetItemsInStorageFurniture() FIRST
  ├─ reserved in slot → JobTransporter.TargetSourceFurniture set
  │                     → GoapAction_TakeFromSourceFurniture
  │                     → CharacterTakeFromFurnitureAction → into hands
  │                     → continue to delivery (no WorldItem spawned)
  └─ not in slot      → fall back to CharacterAwareness scan +
                        GetWorldItemsInStorage (legacy WorldItem path)
```

**Server authority:** every slot mutation, `AddToInventory` call, and reservation lookup runs on the server. Clients never write to `_itemSlots` directly — the only client write path is `StorageFurniture.ApplySyncedSlotsFromNetwork`, called by `StorageFurnitureNetworkSync` after deserializing the replicated `NetworkList`. That method clears every slot, writes the supplied entries by index, and fires `OnInventoryChanged` so the local `StorageVisualDisplay` rebuilds. As a result `OnInventoryChanged` now fires on **every peer** (host + clients), driven by the sync layer's `OnListChanged` handler. Late-joiners get a one-shot catch-up call inside the sync component's `OnNetworkSpawn`.

## Dependencies

### Upstream (this system needs)
- [[furniture-grid]] — base `Furniture` MonoBehaviour, `_interactionPoint`, occupancy state machine, `FurnitureManager` registration.
- [[item-instance]] — slots store `ItemInstance` references; `WearableInstance` / `WeaponInstance` subtype gates drive slot routing.
- [[building]] / [[commercial-building]] — `_inventory` is the canonical "stock count" surface; furniture is a physical-storage strategy that the building tracks orthogonally.
- [[ai-goap]] — three GOAP actions read/write through the public API.

### Downstream (systems that need this)
- [[building-logistics-manager]] — `RefreshStorageInventory` Pass 1 protects slot-stored instances from being ghosted. `LogisticsStockEvaluator.GetItemCount(itemSO)` reads `_inventory` (unchanged) — slot contents and zone WorldItems both contribute to a single logical count.
- [[jobs-and-logistics]] — `GoapAction_GatherStorageItems` (LogisticsManager inbound), `GoapAction_DepositResources` (harvester opportunistic), `GoapAction_StageItemForPickup` (LogisticsManager outbound), `GoapAction_LocateItem` + `GoapAction_TakeFromSourceFurniture` (transporter pickup) all check the slot path first.

## State & persistence
- **Runtime state**: `List<ItemSlot> _itemSlots` constructed in `Awake()` from four authored capacities. Same plain C# data on every peer; on the server it's mutated through `AddItem` / `RemoveItem` / `RemoveItemFromSlot`; on clients it's mutated only through `ApplySyncedSlotsFromNetwork` driven by the sync layer.
- **Network sync (shipped 2026-04-25)**: `StorageFurnitureNetworkSync` (sibling `NetworkBehaviour`) carries a server-write `NetworkList<NetworkStorageSlotEntry>`. Each entry is `{ ushort SlotIndex, FixedString64Bytes ItemId, FixedString4096Bytes JsonData }`. Sparse — empty slots are absent. Server rewrites the list on every `OnInventoryChanged` (full clear + re-add of non-empty slots; O(Capacity), bounded ≤ 32 in the authored Crate). Clients listen on `OnListChanged` and rebuild their full local slot state through `StorageFurniture.ApplySyncedSlotsFromNetwork` regardless of `EventType` (the handler intentionally ignores the event type — see [[multiplayer]] §8 NetworkList event-type fan-out gotcha — and always rebuilds from the full list, which makes it robust against `Add` / `Insert` / `Value` / `Remove` / `RemoveAt` / `Clear` / `Full` alike). `IsLocked` is **not** replicated yet — clients always mirror server contents regardless of lock state.
- **Persisted state (shipped 2026-04-25)**: slot contents survive `MapController.Hibernate` / `WakeUp` and game-session reloads via `BuildingSaveData.StorageFurnitures` — a `List<StorageFurnitureSaveEntry>`, default-empty so older save files (no field) still deserialize cleanly. `BuildingSaveData.FromBuilding` walks `building.GetFurnitureOfType<StorageFurniture>()` and snapshots non-empty slots; each slot is serialized through the same `JsonUtility.ToJson(ItemInstance)` recipe the network-sync layer uses, so live, network, and disk paths share one serialization story. On load, `MapController.RestoreStorageFurnitureContents` matches each live storage to its persisted entry by composite key (see "FurnitureKey scheme" below), rehydrates each slot's `ItemInstance` (Resources lookup → `CreateInstance` → `JsonOverwrite` → re-bind `ItemSO`), and pushes the result through `StorageFurniture.RestoreFromSaveData`. The `OnInventoryChanged` fired at the end flows through `StorageFurnitureNetworkSync` (already subscribed in its server-side `OnNetworkSpawn`) which rewrites the replicated `NetworkList` — so late-joining clients see populated state on connect with no extra restore-side networking work. **Not yet persisted**: `IsLocked`. Tracked alongside lock-state replication in Open questions, since both gaps are addressed together.
- **FurnitureKey scheme**: `"{FurnitureItemSO.ItemId}@{x:F2},{y:F2},{z:F2}"` — composite of the authored `FurnitureItemSO.ItemId` and the building-local position rounded to 2 decimals, formatted with `CultureInfo.InvariantCulture` so saves round-trip identically across locales (avoids `1,23` vs `1.23` mismatches). Stable across `_defaultFurnitureLayout` reorders (the list ordering is irrelevant — keys depend only on the authored slot data) and supports multiple same-typed storages per building (two crates of the same kind at different positions get distinct keys). Falls back to `storage.name` when `FurnitureItemSO` is null (test/debug furniture spawned outside the default-layout path). One static helper, `BuildingSaveData.ComputeStorageFurnitureKey`, is the single authority — the same call computes the key on save AND on restore lookup so they cannot drift.

## Known gotchas / edge cases
- **Interaction point is mandatory in practice.** `Furniture.GetInteractionPosition()` falls back to `transform.position` when `_interactionPoint` is null, which sits inside the base `NavMeshObstacle` carve. Workers can't path inside the carve, so they loop. Mitigations in priority order:
  1. The auto-create-on-Reset hook adds an `InteractionPoint` child for new prefabs.
  2. The `[ContextMenu]` "Auto Create Interaction Point" action regenerates it for existing prefabs.
  3. The new `GetInteractionPosition(Vector3 fromPosition)` overload uses `InteractionZone.bounds.ClosestPoint` as the fallback when no point is authored — typically lands on a navmesh-walkable face.
  4. `GoapAction_GatherStorageItems` and `GoapAction_TakeFromSourceFurniture` carry a 5-second softlock guard that blacklists the furniture and falls back to the loose path.
- **`AddToInventory` was non-idempotent.** Calling it with an `ItemInstance` already in `_inventory` would double-count. Now guarded with `if (_inventory.Contains(item)) return;` — see [CommercialBuilding.AddToInventory](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs).
- **`RefreshStorageInventory` would ghost every slot-stored item** without explicit protection. Pass 1 builds `furnitureStoredInstances` from `GetItemsInStorageFurniture()` and skips them in the ghost check.
- **Default furniture nesting (`_defaultFurnitureLayout`)** — Storage variants have a `NetworkObject` of their own (the one inherited from `Furniture_prefab`), so they MUST be spawned through `_defaultFurnitureLayout` (`Instantiate → NetworkObject.Spawn() → SetParent`) and MUST NOT be nested directly inside another runtime-spawned prefab. A nested second `NetworkObject` half-spawns and silently breaks NGO sync — see [[furnituremanager-replace-style-rescan]] and `feedback_no_nested_networkobject_in_runtime_spawned_prefab.md` for the bug-class context.
- **NetworkBehaviour array index integrity (storage variants).** The Storage variant chain (`Storage.prefab` → `Storage Visible Items.prefab` → `Crate.prefab`) carries exactly one `NetworkBehaviour`: `StorageFurnitureNetworkSync` at index 0 of `NetworkObject.ChildNetworkBehaviours`. `FurnitureInteractable` and `StorageFurniture` are plain `MonoBehaviour`s and don't count. If a future component is added, it MUST be added on the same prefab in the variant chain so server and client agree on the array layout — never add a `NetworkBehaviour` only on the variant or only via a script that runs at runtime, or RPCs and NetworkVariable syncs will silently route to the wrong index. Never `Destroy()` the sync component — only `enabled = false`.
- **Don't clone `WorldItemPrefab` for visual-only purposes.** `StorageVisualDisplay` originally instantiated `ItemSO.WorldItemPrefab` (the full networked wrapper) and then `DestroyImmediate`d the cloned `NetworkObject` after `WorldItem.Initialize`. Worked on the host, **failed on clients** — NGO's stricter spawn-tracking on clients reverted parenting / left the GameObject in a non-rendering state, so visuals only appeared on the host. Resolved by switching to `ItemSO.ItemPrefab` (the visual sub-prefab — same content `WorldItem.AttachVisualPrefab` uses internally) and adding a `SortingGroup` at the spawned root. Generalisable rule: **never include a `NetworkObject` in a clone that isn't going through `NetworkObject.Spawn()`** — the symptom on clients is silent rendering failure, hard to diagnose without per-peer logs.
- **`_defaultFurnitureLayout` slot positions are part of the save schema.** The save-restore `FurnitureKey` is `"{ItemId}@{x:F2},{y:F2},{z:F2}"` derived from the storage's building-local position. Authoring `LocalPosition` on a `_defaultFurnitureLayout` slot **after** a world save was written is equivalent to renaming a save field — the saved entry's key won't match any live storage on next load, and contents are silently dropped (no log, no error: from the restore path's perspective, no save entry exists for that storage and the storage starts empty as if it were a brand-new piece). Treat `_defaultFurnitureLayout` slot positions as immutable once a build ships. If a layout repositioning is unavoidable, add migration code to `BuildingSaveData.FromBuilding` / `RestoreStorageFurnitureContents` that translates old keys to new.

## Open questions / TODO
- [x] ~~**Network sync of slot contents.**~~ Shipped 2026-04-25 via `StorageFurnitureNetworkSync` — see "State & persistence".
- [x] ~~**Save/restore.**~~ Shipped 2026-04-25 via `BuildingSaveData.StorageFurnitures` + `MapController.RestoreStorageFurnitureContents` + `StorageFurniture.RestoreFromSaveData`. Slot contents now survive `MapController.Hibernate` / `WakeUp` and game-session reloads. See "State & persistence".
- [ ] **Lock state replication + persistence.** `IsLocked` / `Lock()` / `Unlock()` is still server-only AND not persisted. Clients see contents but not the lock flag, and the lock resets every load. Both gaps will be addressed together: add a `NetworkVariable<bool>` on the sync component for replication, and add `IsLocked` to `StorageFurnitureSaveEntry` for persistence.
- [ ] **UI window.** Player-facing `UI_StorageWindow` opened by a `StorageFurnitureInteractable`. Today the `FurnitureInteractable.Use()` flow occupies the furniture but doesn't show contents.
- [ ] **Multi-furniture selection strategy.** First-fit is good enough for one or two furniture pieces per building. With many furniture pieces and high logistics throughput, consider closest-to-worker or load-balancing.
- [ ] **Sync churn under high mutation rates.** Current sync is a full-list clear+rebuild on every `OnInventoryChanged`. A single mutation produces 1+N events on the wire and 1+N rebuilds on the client (with visible per-event flicker as the visual display rebuilds each time). Acceptable today; if a future feature drives high mutation frequency, switch to a delta diff that emits a single `Value`-style update per changed slot.

## Change log
- 2026-04-25 — Initial documentation of the slot-based container, its visual display companion, and its first-class integration into the inbound, harvester, outbound, and transporter logistics paths. — Claude / [[kevin]]
- 2026-04-25 — Shipped `StorageFurnitureNetworkSync` (sibling NetworkBehaviour authored on `Storage.prefab`, inherited by every variant). Server now replicates slot contents via `NetworkList<NetworkStorageSlotEntry>`; clients mirror through new `StorageFurniture.ApplySyncedSlotsFromNetwork` and fire `OnInventoryChanged` locally so `StorageVisualDisplay` rebuilds on every peer. Lock state and save/restore are still TODO. — claude
- 2026-04-25 — `StorageVisualDisplay` switched from instantiating `WorldItemPrefab` (the full networked wrapper) to instantiating `ItemPrefab` (the visual sub-prefab) + adding a `SortingGroup` at the spawned root. Reason: the cloned `NetworkObject` interfered with parenting/visibility on clients, so visuals only appeared on the host. The new pipeline has zero `NetworkObject`s in the cloned chain — visuals render identically on host and clients. Distance gating fully removed (deferred to per-peer culling tracked in [[optimisation-backlog]]). — claude
- 2026-04-25 — Shipped save/restore for slot contents. New types `StorageSlotSaveEntry`, `StorageFurnitureSaveEntry`, and `BuildingSaveData.StorageFurnitures` (default-empty for backward-compat) in [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs). New static helper `BuildingSaveData.ComputeStorageFurnitureKey` for the composite (FurnitureItemSO.ItemId @ building-local-position) furniture key. `StorageFurniture.RestoreFromSaveData` is the new server-only restore entry point. `MapController.RestoreStorageFurnitureContents` is invoked from both `SpawnSavedBuildings` and `WakeUp` after the building's default-furniture spawn finishes. The same `OnInventoryChanged` flow used at runtime drives the network-sync rewrite, so late-joining clients see restored state on connect — no extra networking code needed. `IsLocked` persistence still pending alongside lock-state replication. — claude

## Sources
- [StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs)
- [StorageFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs)
- [StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs)
- [Furniture.cs](../../Assets/Scripts/World/Furniture/Furniture.cs)
- [ItemSlot.cs](../../Assets/Scripts/Inventory/ItemSlot.cs), [MiscSlot.cs](../../Assets/Scripts/Inventory/MiscSlot.cs), [WeaponSlot.cs](../../Assets/Scripts/Inventory/WeaponSlot.cs), [WearableSlot.cs](../../Assets/Scripts/Inventory/WearableSlot.cs), [AnySlot.cs](../../Assets/Scripts/Inventory/AnySlot.cs)
- [CharacterStoreInFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterStoreInFurnitureAction.cs), [CharacterTakeFromFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterTakeFromFurnitureAction.cs)
- [GoapAction_GatherStorageItems.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs), [GoapAction_DepositResources.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs), [GoapAction_StageItemForPickup.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_StageItemForPickup.cs), [GoapAction_LocateItem.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_LocateItem.cs), [GoapAction_TakeFromSourceFurniture.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_TakeFromSourceFurniture.cs)
- [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — `FindStorageFurnitureForItem`, `GetItemsInStorageFurniture`, `AddToInventory` idempotency, `RefreshStorageInventory` Pass 1 furniture protection.
- [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) — `BuildingSaveData.StorageFurnitures` schema, `StorageFurnitureSaveEntry` / `StorageSlotSaveEntry`, and `ComputeStorageFurnitureKey`.
- [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `RestoreStorageFurnitureContents` invoked from `SpawnSavedBuildings` and `WakeUp` after the default-furniture spawn settles.
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — procedural how-to for storage furniture authoring + visual display + logistics integration.
- [.agent/skills/logistics_cycle/SKILL.md](../../.agent/skills/logistics_cycle/SKILL.md) — procedural how-to for the deposit + pickup furniture-first paths.
- 2026-04-25 conversation with Kevin — design decisions (component-presence vs checkbox, strict-first slot priority, harvester ≤5u opportunistic diversion, transporter parallel pickup path).
