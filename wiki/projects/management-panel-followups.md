---
type: project
title: "Management Panel Follow-ups"
tags: [management-panel, commercial-building, refactor, deferred-work]
created: 2026-05-08
updated: 2026-05-08
sources: []
related: ["[[shops]]", "[[building]]", "[[occupiable-furniture]]"]
status: active
confidence: high
start_date: 2026-05-08
target_date: null
---

# Management Panel Follow-ups

## Summary
Backlog of architectural / UX work on `UI_OwnerManagementPanel` and the `IManagementTab` system that was **deliberately deferred** to keep Phase 2b's shop catalog/shelves/cashiers/hiring loop shippable. Each entry names the gap, why it was deferred, and a sketch of the replacement.

## Non-goals
- Tracking general management-panel bugs (those go to GitHub issues).
- Tracking purely-cosmetic visual polish (those land in the designer's pass on `UI_OwnerManagementPanel.prefab` directly).

---

## 1. Unified Storage-Role Assignment System

**Filed 2026-05-08, Phase 2b shop work.**

### Why deferred
Phase 2b shipped two independent storage-role models that should logically be the same primitive:

- **`CommercialBuilding.ToolStorage` / `HelpWantedSign` / `ManagementFurniture`** — designer-time inspector-authored *singleton* role slots. 3-tier resolver (cached → snapshot rebind → first-of-type child). No runtime owner UI. See lines ~180–290 of [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs).
- **`ShopBuilding._sellShelves`** — runtime-mutable *multi-select* assignment, with `SetSellShelfFlagServerRpc`, save support, and a dedicated `ShopShelvesTab`. See lines ~49–53 + ~369–390 of [ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs).

Two different shapes, two different mental models, two different UI surfaces. The Tool/HelpWanted/Mgmt slots aren't owner-reassignable in playmode; the Sell-Shelf is. A new commercial-building subtype that wants its own role (e.g. `RawMaterialStorage` on `CraftingBuilding`) has to pick one of the two patterns and live with the asymmetry.

### Proposed shape (locked via sketch 001 + Kevin's cardinality call 2026-05-08)

**Cardinality:** Each `StorageFurniture` holds **exactly one role** (or `None`). Mutually exclusive across the role enum at the storage level. Multiple storages can independently share the same role — e.g., 3 storages can all be Sell-Shelves. No `IsMulti` flag needed; cardinality is single-per-storage by definition.

**UI:** One dropdown per storage row, listing the building's `SupportedStorageRoles`. Per-storage exclusivity is enforced by the widget itself. See [`.planning/sketches/001-storages-tab/`](../../.planning/sketches/001-storages-tab/) for the locked design (Variant B).

```csharp
public enum StorageRoleType
{
    None,                  // default — storage carries no role
    ToolStorage,
    InventoryStorage,
    SellShelf,             // Shop subtype only
    // future: RawMaterialStorage, OutputStorage, ...
}

public struct StorageRoleDescriptor
{
    public StorageRoleType Type;
    public string DisplayName;
    public string Icon;            // e.g. "⚒", "📦", "⛀" — surfaced in the dropdown row
}

// On StorageFurniture (NEW server-authoritative replicated field, default = None):
public StorageRoleType Role { get; private set; }

// On CommercialBuilding:
public virtual IReadOnlyList<StorageRoleDescriptor> SupportedStorageRoles { get; }
public IReadOnlyList<StorageFurniture> GetStoragesWithRole(StorageRoleType type);

// Owner-only mutation — assigning a new role auto-clears the old one (single-field semantics)
[ServerRpc(RequireOwnership=false)]
public void TrySetStorageRoleServerRpc(NetworkObjectReference furnitureRef, StorageRoleType newRole);

public event System.Action OnStorageRolesChanged;
```

`ShopBuilding.SupportedStorageRoles` extends base with `SellShelf`. Generic `CommercialBuilding` (Forge / House / etc.) exposes `[None, ToolStorage, InventoryStorage]` only.

**Existing `ToolStorage` getter** becomes a thin wrapper that returns the first storage with `Role == ToolStorage`, with the legacy inspector-authored field as fallback:

```csharp
public StorageFurniture ToolStorage =>
    GetStoragesWithRole(StorageRoleType.ToolStorage).FirstOrDefault()
    ?? _toolStorageFurniture                                  // inspector-authored fallback (existing 3-tier resolver)
    ?? GetComponentInChildren<StorageFurniture>();            // convention-fallback (existing)
```

All current call sites (`LogisticsStockEvaluator`, `WaterCropTask`, `FarmingBuilding.GetToolStockItems`, `CommercialBuilding.FindStorageFurnitureForItem`) keep working unchanged.

### UI shape
Generic `StorageRolesTab : IManagementTab` lives on `CommercialBuilding.GetManagementTabs()`. One row per `StorageFurniture` child. Each row: icon + name + slot count + a single dropdown listing `SupportedStorageRoles` (with `None` as the default). Picking a new option immediately fires `TrySetStorageRoleServerRpc` (silent commit, reactive UI re-render via `OnStorageRolesChanged`).

`ShopBuilding.GetManagementTabs()` drops the dedicated `ShopShelvesTab` (and the `ShopShelvesTabView` + row prefab) — Sell-Shelf becomes one option in the row dropdown.

### Save format
The role lives **on the storage**, not on the building. `StorageFurniture.Role` serializes as part of the storage's existing save entry — no new building-level `RoleAssignments` list needed. Even simpler than the original filing.

### Estimated scope
| Task | Files |
|---|---|
| `StorageRoleType` enum + `StorageRoleDescriptor` | +1 (new) |
| `StorageFurniture`: add `Role` field + `NetworkVariable<StorageRoleType>` replication + save serialization | 1 modified + 1 net-sync modified |
| `CommercialBuilding`: SupportedStorageRoles + GetStoragesWithRole + ServerRpc + OnStorageRolesChanged event | 1 modified |
| `ToolStorage` accessor → wraps GetStoragesWithRole(ToolStorage).FirstOrDefault, fallback to legacy resolver | 1 modified |
| `ShopBuilding.SellShelves` → wraps GetStoragesWithRole(SellShelf). Inspector `_sellShelves` and its ServerRpc deleted. | 1 modified |
| Save format: add `StorageRoleType role` to `StorageFurnitureSaveData` (or its equivalent — additive, default `None` for old saves) | 1 modified |
| New `StorageRolesTab` + view + row prefabs (1 dropdown widget per row) | +3 (new) |
| Drop `ShopShelvesTab`/View/Row + `ShopShelvesRow.prefab`/`ShopShelvesTab.prefab` | -2 prefabs, ~3 scripts |
| `ShopBuilding.GetManagementTabs()` — remove Shelves entry | 1 modified |
| SKILL.md updates (`building_system`, `shop_system`) | 2 modified |
| Wiki updates (`shops.md`, new `commercial-storage-roles.md`) | 1–2 modified |

~10–12 files, ~400 LOC code + prefab work. Smaller than the original estimate because per-storage role-on-the-furniture is structurally simpler than per-building role assignments.

### Acceptance criteria
- Owner of a Forge can reassign which `StorageFurniture` is the `ToolStorage` from the management panel without restarting the scene — by changing the dropdown on any storage row to "Tool Storage".
- Owner of a Clothing Shop sees the same generic Storages tab; the dropdown additionally exposes "Sell-Shelf" as an option.
- Picking "Tool Storage" on a row that previously held a different role (or "Sell-Shelf" / "Inventory Storage") **automatically clears the previous role** because the field is single-valued.
- Multiple storages can hold the same role (e.g., 3 Sell-Shelves). `GetStoragesWithRole(SellShelf)` returns the list; `BuildingLogisticsManager` / customer-buy flow reads from that list.
- All existing `ToolStorage` call sites work unchanged via the wrapper accessor.
- Save / load round-trip preserves the per-storage role assignment.
- No `ShopShelvesTab.prefab` / `ShopShelvesRow.prefab` / `ShopShelvesTabView.cs` / `_sellShelves` field on ShopBuilding remain.

---

## Change log
- 2026-05-09 — §1 functionally closed (multi-storage refactor). `ToolStorage` / `InventoryStorage` are now LISTS, not singletons. Added `CommercialBuilding.ToolStorages` / `InventoryStorages` accessors + `FindToolStorageContaining` / `FindToolStorageWithFreeSpace` / `HasToolInAnyToolStorage` / `IsToolStorage` helpers. All consumers iterate: `FindStorageFurnitureForItem`, `GoapAction_GatherStorageItems.DetermineStoragePosition`, `GoapAction_FetchToolFromStorage` (silent runtime-rebind cache bug fixed: dropped `_storageInteractable` cached at construction), `GoapAction_ReturnToolToStorage` (same), `JobFarmer.ProvideWorldState`, `CharacterJob.TryAutoReturnTools`, `StorageFurniture.AddItem` tool-stamp hook. Cross-client refresh fixed: `OnStorageRolesChanged` now fires on every peer via per-storage NetVar fan-out (`HandleChildStorageRoleChanged`), bound at `OnNetworkSpawn` and refreshed inside `GetStorageFurnitureCached`. Sync-component-missing case promoted from LogWarning to LogError. — claude
- 2026-05-08 — Refined design via sketch 001-storages-tab. Locked: per-storage role exclusivity (single role field on `StorageFurniture`, mutually exclusive across the enum). UI shape: one dropdown per storage row (Variant B). Save format moved from building-level role list to per-storage role field — simpler. Estimated scope dropped from ~500 LOC to ~400 LOC. — claude
- 2026-05-08 — Initial entry. Filed during Phase 2b shop integration session as deferred follow-up. — claude

## Sources
- [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs)
- [ShopBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs)
- [ShopShelvesTabView.cs](../../Assets/Scripts/UI/Management/ShopShelvesTabView.cs)
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md)
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — section "Furniture inheritance hierarchy" + "Furniture base"
- 2026-05-08 conversation with [[kevin]] — the architectural-symmetry observation that triggered this filing.
