---
type: system
title: "Commercial Storage Roles"
tags: [building, furniture, storage, owner, management, network, tier-2]
created: 2026-05-08
updated: 2026-05-14
sources: []
related:
  - "[[commercial-building]]"
  - "[[storage-furniture]]"
  - "[[tool-storage]]"
  - "[[shops]]"
  - "[[shop-building]]"
  - "[[management-panel-architecture]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - item-inventory-specialist
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on:
  - "[[storage-furniture]]"
  - "[[commercial-building]]"
  - "[[management-panel-architecture]]"
depended_on_by:
  - "[[tool-storage]]"
  - "[[shops]]"
  - "[[jobs-and-logistics]]"
---

# Commercial Storage Roles

## Summary
Per-storage role tagging for `StorageFurniture` children of any `CommercialBuilding`. Each storage carries one `StorageRoleType` value (`None` / `ToolStorage` / `InventoryStorage` / subtype-extension `SellShelf`). Multiple storages can share the same role independently. The role is owner-mutable at runtime via the management panel's `StorageRolesTab` dropdown, replicated through a per-storage `NetworkVariable<StorageRoleType>` on the sibling `StorageFurnitureNetworkSync`, and persisted in `StorageFurnitureSaveEntry.Role`. Replaces three pre-2026-05-08 systems with one unified API: the Inspector-only `_toolStorageFurniture` field on `CommercialBuilding`, the dedicated `_sellShelves : NetworkList<NetworkObjectReference>` on `ShopBuilding`, and the Phase 2b `ShopShelvesTab` UI.

## Purpose
Before this refactor, storage-role assignment was scattered across three different mechanisms with three different APIs:

1. **Tool storage** — designer-only Inspector field `_toolStorageFurniture` on `CommercialBuilding`. No runtime override; the owner couldn't pick a different crate after build time.
2. **Sell-shelves** — Phase 2b's `ShopShelvesTab` UI flipped a `bool` flag through `ShopBuilding.SetSellShelfFlagServerRpc`, persisted as a list of composite keys on `BuildingSaveData.SellShelfFurnitureKeys`. Shop-only.
3. **Inventory storage** — implicit in "any non-tool, non-shelf storage". No explicit role.

Two future workshop subtypes (Forge with a "material bin" role, Mill with a "grain silo" role, etc.) would have meant one more bespoke field + ServerRpc + save column each. Open/Closed Principle violation (root rule #10): adding a new role required modifying existing classes.

The unified system collapses all three (and any future role) into one enum + one NetworkVariable + one ServerRpc + one save field, with subclass extension via `SupportedStorageRoles`. Designers still pre-assign roles per storage via the Inspector field (`_initialRole`) for default behavior, and owners can re-assign at runtime through the polymorphic management panel that every commercial subtype inherits.

## Responsibilities
- Defining the canonical role taxonomy (`StorageRoleType` enum + `StorageRoleDescriptor` UI metadata + `StorageRoleCatalog` static lookups).
- Holding per-storage runtime role state, replicated server-authoritatively to every peer.
- Owner-only runtime mutation via the management panel, server-validated against the building's `Owner` and `SupportedStorageRoles`.
- Persistence across `MapController.Hibernate`/`WakeUp` and game-session reload.
- Migrating legacy save data (`SellShelfFurnitureKeys` from Phase 2b) into the new `Role` field on first load.
- Subclass extension: `ShopBuilding` widens the catalog to include `SellShelf`; future subtypes can add new roles without touching the base.

**Non-responsibilities:**
- **Does not** drive logistics routing per role — consumers (`ToolStorage` getter, `ShopBuilding.SellShelves`) query `GetStoragesWithRole(...)` themselves. The role system is a tagging/replication layer, not a router.
- **Does not** enforce role-aware item filtering (e.g. "only tools go in a ToolStorage"). The legacy `StorageFurniture` slot-type filters still apply (a wardrobe rejects a sword regardless of role).
- **Does not** allow multiple roles per storage. Each storage holds exactly one `StorageRoleType` — per-storage exclusivity is part of the data shape (single enum field).

## Key classes / files

| File | Purpose |
|---|---|
| [Assets/Scripts/World/Furniture/StorageRoleType.cs](../../Assets/Scripts/World/Furniture/StorageRoleType.cs) | Enum + `StorageRoleDescriptor` struct + `StorageRoleCatalog` static catalogs (`Generic`, `Shop`). |
| [Assets/Scripts/World/Furniture/StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs) | Adds `_initialRole` Inspector field + `_runtimeRole` field + `Role` getter + `OnRoleChanged` event + `ApplyRoleFromNetwork` internal mutator. |
| [Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs) | Adds `NetworkVariable<StorageRoleType> _networkRole` + `SetRoleServer(StorageRoleType)` mutator + `OnValueChanged` callback that writes through to `_storage.ApplyRoleFromNetwork`. |
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | `virtual SupportedStorageRoles` + `GetStoragesWithRole` walker + `OnStorageRolesChanged` event + `[ServerRpc] TrySetStorageRoleServerRpc`. `ToolStorage` getter promoted to a four-tier resolver, Tier 0 = first storage with `Role == ToolStorage`. |
| [Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs) | Override `SupportedStorageRoles → StorageRoleCatalog.Shop`. `SellShelves` is now a wrapper around `GetStoragesWithRole(SellShelf)`. `OnFurnituresLoaded()` migrates legacy `_pendingSellShelfKeys` → `Role = SellShelf`. |
| [Assets/Scripts/UI/Management/StorageRolesTab.cs](../../Assets/Scripts/UI/Management/StorageRolesTab.cs) | `IManagementTab` impl, takes `CommercialBuilding`, loads `Resources/UI/Management/StorageRolesTab.prefab`. |
| [Assets/Scripts/UI/Management/StorageRolesTabView.cs](../../Assets/Scripts/UI/Management/StorageRolesTabView.cs) | View MonoBehaviour: lists all storage children, rebuilds rows on `OnStorageRolesChanged`, handles empty state. |
| [Assets/Scripts/UI/Management/StorageRolesTabRow.cs](../../Assets/Scripts/UI/Management/StorageRolesTabRow.cs) | Per-row label + TMP_Dropdown bound to `SupportedStorageRoles`. Subscribes to `storage.OnRoleChanged` for off-band writes (save-restore, migration). |
| [Assets/Scripts/World/MapSystem/MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) | `StorageFurnitureSaveEntry.Role` field + `BuildingSaveData.FromBuilding` capture. |
| [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) | `RestoreStorageFurnitureContents` writes saved `Role` onto each storage's `StorageFurnitureNetworkSync` after slot restore. |

## Public API / entry points

**Type catalog:**

```csharp
public enum StorageRoleType { None = 0, ToolStorage = 1, InventoryStorage = 2, SellShelf = 3 }
public struct StorageRoleDescriptor { public StorageRoleType Type; public string DisplayName; public string Icon; }
public static class StorageRoleCatalog
{
    public static readonly StorageRoleDescriptor None, ToolStorage, InventoryStorage, SellShelf;
    public static readonly IReadOnlyList<StorageRoleDescriptor> Generic; // None, Tool, Inventory
    public static readonly IReadOnlyList<StorageRoleDescriptor> Shop;    // Generic + SellShelf
}
```

**Storage-side (`StorageFurniture`):**

```csharp
[SerializeField] StorageRoleType _initialRole;          // designer seed
public StorageRoleType Role { get; }                     // runtime getter
public event Action<StorageRoleType> OnRoleChanged;      // fires on every peer when the role changes
internal void ApplyRoleFromNetwork(StorageRoleType v);   // called by NetSync OnValueChanged
```

**Replication (`StorageFurnitureNetworkSync`):**

```csharp
public void SetRoleServer(StorageRoleType newRole);      // server-only mutator; writes _networkRole.Value
```

**Building-side (`CommercialBuilding`):**

```csharp
public virtual IReadOnlyList<StorageRoleDescriptor> SupportedStorageRoles { get; } // default: Generic
public IReadOnlyList<StorageFurniture> GetStoragesWithRole(StorageRoleType type);  // walker
public event Action OnStorageRolesChanged;                                          // fires on EVERY peer when any child storage's role changes
[ServerRpc(RequireOwnership=false)]
public void TrySetStorageRoleServerRpc(NetworkObjectReference furnitureRef, StorageRoleType newRole, ServerRpcParams p = default);

// Multi-storage list accessors + helpers (2026-05-09).
// Tools / inventory storages are LISTS, not singletons — multiple chests can share a role.
public IReadOnlyList<StorageFurniture> ToolStorages       { get; }   // == GetStoragesWithRole(ToolStorage)
public IReadOnlyList<StorageFurniture> InventoryStorages  { get; }   // == GetStoragesWithRole(InventoryStorage)
public StorageFurniture FindToolStorageContaining(ItemSO tool);      // first that holds the item
public StorageFurniture FindToolStorageWithFreeSpace();              // first non-full + non-locked
public bool             HasToolInAnyToolStorage(ItemSO tool);        // any-of predicate
public bool             IsToolStorage(StorageFurniture storage);     // role-tagged OR legacy fallback
```

**UI (`MWI.UI.Management`):** `StorageRolesTab(CommercialBuilding)` → `StorageRolesTabView.Bind(building)` → instantiates `StorageRolesTabRow` per storage child.

## Data flow

```
Designer authors _initialRole on each StorageFurniture in the prefab Inspector
        │
        ▼
StorageFurniture spawns (Awake builds slots, preserves _initialRole)
        │
        ▼
StorageFurnitureNetworkSync.OnNetworkSpawn (server)
   ├─ if _networkRole.Value == None: _networkRole.Value = _storage.InitialRole
   └─ subscribe storage.OnInventoryChanged for slot replication

StorageFurnitureNetworkSync.OnValueChanged (every peer)
   └─ _storage.ApplyRoleFromNetwork(newValue)  ─►  fires storage.OnRoleChanged

       ┌────────── consumer paths (every peer) ──────────┐
       ▼                                                  ▼
StorageRolesTabRow.HandleRoleChanged           CommercialBuilding.HandleChildStorageRoleChanged
   └─ dropdown.SetValueWithoutNotify(newIdx)         └─ OnStorageRolesChanged?.Invoke()
                                                        ─►  StorageRolesTabView.Refresh
                                                ToolStorages list:
                                                   GetStoragesWithRole(ToolStorage)
                                                ShopBuilding.SellShelves:
                                                   GetStoragesWithRole(SellShelf)


        Owner clicks dropdown in StorageRolesTabRow
        │
        ▼
TrySetStorageRoleServerRpc(furnitureRef, newRole)
   ├─ resolve caller via NetworkManager.ConnectedClients[senderClientId].PlayerObject.GetComponent<Character>()
   ├─ if caller != Owner → log warning, return (rejected)
   ├─ if newRole not in SupportedStorageRoles → log warning, return (rejected)
   ├─ resolve furnitureRef → StorageFurniture + sibling StorageFurnitureNetworkSync
   └─ sync.SetRoleServer(newRole)  ─►  _networkRole.Value = newRole  ─►  fan out to every peer
                                       (per-peer OnValueChanged fires the consumer paths above)


Save:
   BuildingSaveData.FromBuilding walks GetFurnitureOfType<StorageFurniture>()
        for each storage: entry.Role = storage.Role
   (entry.Role default for old saves: StorageRoleType.None)

Restore:
   MapController.RestoreStorageFurnitureContents
        for each saved entry:
           storage.RestoreFromSaveData(entry.Slots);
           if (entry.Role != None) sync.SetRoleServer(entry.Role);

Legacy migration (one-time on load):
   ShopBuilding.RestoreShopFromSaveData stages _pendingSellShelfKeys
   ShopBuilding.OnFurnituresLoaded() walks _pendingSellShelfKeys
        for each key: resolve to live StorageFurniture
                      sync.SetRoleServer(StorageRoleType.SellShelf)
   New saves don't write SellShelfFurnitureKeys.
```

## Dependencies

### Upstream
- [[storage-furniture]] — owns the per-storage runtime field and the network sync component.
- [[commercial-building]] — owns `SupportedStorageRoles`, `GetStoragesWithRole`, the `ServerRpc`, and the `OnStorageRolesChanged` event.
- [[management-panel-architecture]] — base `GetManagementTabs()` appends `StorageRolesTab`. Subtypes inherit it for free.
- [[network-architecture]] — server-authoritative `NetworkVariable<StorageRoleType>` replication.
- [[save-load]] — `StorageFurnitureSaveEntry.Role` field on `BuildingSaveData`.

### Downstream
- [[tool-storage]] — `CommercialBuilding.ToolStorage` getter's Tier 0 lookup.
- [[shops]] — `ShopBuilding.SellShelves` query.
- [[jobs-and-logistics]] — sell-shelves drive shop logistics; tool storage drives the punch-out gate + tool-aware deposit routing.

## State & persistence

- **Per-storage runtime state** lives on `StorageFurniture._runtimeRole` and `StorageFurnitureNetworkSync._networkRole`. The runtime field is server-authoritative; clients see the value only via the OnValueChanged callback that calls `ApplyRoleFromNetwork`.
- **Designer seed** is `_initialRole` on `StorageFurniture` (Inspector field). Used once on `OnNetworkSpawn` (server) when `_networkRole.Value` is still default.
- **Persistence** is via `StorageFurnitureSaveEntry.Role` field on `BuildingSaveData` (default `None` for backward compat). Captured in `BuildingSaveData.FromBuilding`, restored in `MapController.RestoreStorageFurnitureContents` after slot contents are restored.
- **Legacy migration**: `BuildingSaveData.SellShelfFurnitureKeys` (Phase 2b) still deserializes; `ShopBuilding.OnFurnituresLoaded()` translates each entry into a `Role = SellShelf` write. New saves don't populate the legacy list (the load-side keeps reading it for backward-compat).

## Known gotchas / edge cases

- **Owner-validation race after building transfer.** `TrySetStorageRoleServerRpc` reads `Owner` at call-time. If a building changes owner mid-RPC (extremely unlikely — owner mutation goes through `AddOwner`/`RemoveOwner`), the new owner could see their click rejected. Acceptable; no retry logic needed.
- ~~**`OnStorageRolesChanged` fires only from the building's ServerRpc path.**~~ **Resolved 2026-05-09.** The building-level event now fans out from a per-storage `OnRoleChanged` subscription (`CommercialBuilding.HandleChildStorageRoleChanged`), driven by `StorageFurnitureNetworkSync._networkRole.OnValueChanged`. Fires on every peer and on every code path that mutates the role (ServerRpc, save-restore, ShopBuilding migration, future programmatic writes). Subscriptions are bound at `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached` so runtime-placed storages are picked up automatically.
- **Role-write rejected → optimistic UI doesn't roll back automatically**, because the ServerRpc is fire-and-forget. The dropdown's local visual change persists until the user reopens the tab. Mitigation: the ServerRpc writes `_networkRole` only on success and the per-storage `OnValueChanged` callback's `SyncDropdownToCurrentRole` resets the dropdown selection if the authoritative value differs. So if the user picks a role that's silently rejected (e.g. caller isn't owner), no state change propagates — but the visual stays wrong until next role-change event. Future polish: server should send a targeted toast on rejection.
- **`SupportedStorageRoles` widening at runtime is unsafe.** The catalog is read once per Tab opening to populate the dropdown; if a subclass mutates its `SupportedStorageRoles` after the panel opens, the existing dropdown won't reflect the change. In practice subclass catalogs are static (one per type), so this is a non-issue.
- ~~**Multiple ToolStorage role-holders → first wins.**~~ **Resolved 2026-05-09.** All consumers now iterate the `ToolStorages` / `InventoryStorages` lists directly (logistics: `FindStorageFurnitureForItem` / GOAP: `GoapAction_FetchToolFromStorage` + `GoapAction_ReturnToolToStorage` + `GoapAction_GatherStorageItems` / jobs: `JobFarmer.HasToolInAnyToolStorage` / lifecycle: `CharacterJob.TryAutoReturnTools`). Multi-storage is now first-class. The singleton `ToolStorage` getter is preserved for callers that genuinely want "any one" (legacy fallback only).
- **No catalog refresh hook.** Adding a new `StorageRoleType` enum value at runtime (e.g. via mods) isn't supported; the enum is compile-time. Future modding may need a registry layer.

## Open questions / TODO

- **Role cardinality enforcement**. Today every role allows multiple storages to share it. Should `ToolStorage` and similar singleton roles reject the second assignment server-side? Or is "first wins" via the getter sufficient? Sketch design favoured "first wins" for simplicity; revisit if it confuses owners.
- **Per-role contents filter**. Should a storage tagged `ToolStorage` reject non-tool items? The legacy `_toolStorageFurniture` didn't filter — workers still drop loose materials in the tool crate. Filter logic would be a separate subsystem on `StorageFurniture` keyed off `Role` + `SupportedStorageRoles[role].AllowedItemFilter`.
- **Role-filtered logistics routing**. `JobLogisticsManager` could prefer `Role == InventoryStorage` storages over `Role == ToolStorage` for general-purpose deposits, instead of falling through to `FindStorageFurnitureForItem`'s first-fit. Defer until a profiling-driven need surfaces.
- **Toast on rejected ServerRpc**. Add `SendUnauthorizedToastClientRpc` (matching `ShopBuilding`'s pattern) so the owner gets visible feedback when the RPC silently rejects.
- **TMP_Dropdown sub-prefab on `StorageRolesRow.prefab`** — the placeholder prefab shipped with the Toggle hierarchy stripped + the `_dropdown` field exposed for designer wiring. Author the dropdown in the Editor before playmode testing the role-assignment UI.

## Change log

- 2026-05-14 — **Shift-punch storage-role assignment pass.** New `BuildingLogisticsManager.AssignStorageRolesForShift()` is called from `CommercialBuilding.WorkerStartingShift` for every worker punching in (server-only). Walks every `StorageFurniture` in deterministic order (MainRoom first, then SubRooms; FurnitureManager registration order within each). Applies a unified rule: (a) if `GetToolStockItems()` yields anything → first storage = `ToolStorage`, rest = `InventoryStorage`; (b) else if `ShopBuilding` → first = `SellShelf`, rest = `InventoryStorage`; (c) else → all = `InventoryStorage`. Tool-storage priority overrides shelf priority on shops. Idempotent (skip-write when current role matches verdict), server-only via `SetRoleServer`, fans out through the existing `OnStorageRolesChanged` event. Storages whose desired role isn't in `SupportedStorageRoles` are skipped with a `VerboseJobs`-gated warning. NEW `CommercialBuilding.GetStorageFurnitureOrdered()` public accessor exposes the existing private `GetStorageFurnitureCached()` list under a read-only contract so the logistics layer can address the same cached set. Note: this rule overrides owner choice on every punch-in by design — currently flagged as a behaviour the user accepted explicitly; revisit if it conflicts with management-panel intent in playtests. — claude
- 2026-05-09 — Removed dead `_toolStorageFurniture` Inspector SerializeField on `CommercialBuilding` (audit showed every building prefab had it as `fileID: 0` — null). Also removed the snapshot/rebind machinery (`_toolStorageRefSO` / `_toolStorageRefLocalPos` / `_toolStorageRefSnapshotted` + the corresponding `SnapshotFurnitureRef` call in `Awake`) that supported the now-deleted Tier 1-2 resolver branches. `ToolStorage` getter simplified to two tiers: (1) role-tagged, (2) first-crate convention fallback. `HelpWantedSign` and `ManagementFurniture` still use their three-tier lazy-rebind machinery — only the tool-storage path was simplified. — claude
- 2026-05-09 — Multi-tool-storage refactor: tool/inventory storages are now LISTS, not singletons. Added `CommercialBuilding.ToolStorages` / `InventoryStorages` accessors + `FindToolStorageContaining` / `FindToolStorageWithFreeSpace` / `HasToolInAnyToolStorage` / `IsToolStorage` helpers. All consumers iterate the lists: `FindStorageFurnitureForItem`, `GoapAction_GatherStorageItems.DetermineStoragePosition`, `GoapAction_FetchToolFromStorage` (dropped `_storageInteractable` cache; resolves per call — silent runtime-rebind bug fixed), `GoapAction_ReturnToolToStorage` (same fix), `JobFarmer.ProvideWorldState`, `CharacterJob.TryAutoReturnTools`, `StorageFurniture.AddItem` tool-stamp hook (`IsToolStorage(this)`). Cross-client refresh: building-level `OnStorageRolesChanged` now fires from a per-storage `OnRoleChanged` subscription (`HandleChildStorageRoleChanged`), driven by `StorageFurnitureNetworkSync._networkRole.OnValueChanged` — fires on every peer + every mutation path (RPC, save-restore, migration). Subscriptions bound in `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached` so runtime-placed storages auto-subscribe. Sync-component-missing case promoted from LogWarning to LogError so future regressions surface. — claude
- 2026-05-08 — Initial system shipped: `StorageRoleType` + `StorageRoleCatalog`, `StorageFurniture._initialRole` / `_runtimeRole` / `OnRoleChanged`, `StorageFurnitureNetworkSync._networkRole` / `SetRoleServer`, `CommercialBuilding.SupportedStorageRoles` / `GetStoragesWithRole` / `TrySetStorageRoleServerRpc` / `OnStorageRolesChanged`, `StorageRolesTab` + view + row, `BuildingSaveData → StorageFurnitureSaveEntry.Role` save column, `MapController.RestoreStorageFurnitureContents` role restore, `ShopBuilding` migration of `SellShelfFurnitureKeys` → `Role = SellShelf`. Deletes: `ShopBuilding._sellShelves`, `ShopBuilding.OnSellShelvesChanged`, `ShopBuilding.SetSellShelfFlagServerRpc`, `ShopShelvesTab` + view + row + prefabs. — claude

## Sources

- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) §"Storage Roles" — procedural how-to.
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md) §1 — shop-side migration notes.
- [Assets/Scripts/World/Furniture/StorageRoleType.cs](../../Assets/Scripts/World/Furniture/StorageRoleType.cs)
- [Assets/Scripts/World/Furniture/StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs)
- [Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs)
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs)
- [Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs)
- [Assets/Scripts/UI/Management/StorageRolesTab.cs](../../Assets/Scripts/UI/Management/StorageRolesTab.cs)
- [Assets/Scripts/UI/Management/StorageRolesTabView.cs](../../Assets/Scripts/UI/Management/StorageRolesTabView.cs)
- [Assets/Scripts/UI/Management/StorageRolesTabRow.cs](../../Assets/Scripts/UI/Management/StorageRolesTabRow.cs)
- [Assets/Scripts/World/MapSystem/MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) — `StorageFurnitureSaveEntry.Role`.
- [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `RestoreStorageFurnitureContents` role restore.
- [.planning/sketches/001-storages-tab/](../../.planning/sketches/001-storages-tab/) — Variant B winner (dropdown-per-row).
- [wiki/projects/management-panel-followups.md](../projects/management-panel-followups.md) §1 — locked design decisions.
- 2026-05-07 → 2026-05-08 conversation with [[kevin]] driving the unification (sketch round + implementation).
