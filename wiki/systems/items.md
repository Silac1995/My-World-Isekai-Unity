---
type: system
title: "Items, Inventory & Equipment"
tags: [items, inventory, equipment, gameplay, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[shops]]"
  - "[[jobs-and-logistics]]"
  - "[[world]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: item-inventory-specialist
secondary_agents:
  - combat-gameplay-architect
owner_code_path: "Assets/Scripts/Item/"
depends_on:
  - "[[character]]"
  - "[[network]]"
depended_on_by:
  - "[[combat]]"
  - "[[shops]]"
  - "[[jobs-and-logistics]]"
  - "[[dialogue]]"
---

# Items, Inventory & Equipment

## Summary
Four-layer item pipeline. `ItemSO` defines universal static data (name, icon, prefab, crafting recipe). `ItemInstance` is the in-memory incarnation with unique colors, custom names, durability — never shared between owners. `WorldItem` is the physical presence on the ground when dropped. `CharacterEquipment` attaches an `ItemInstance` to a character via typed layers (Underwear / Clothing / Armor / Bag) with socket-based weapon mounting. Keys are a specialized item type wired into the door/lock system.

## Purpose
Separate immutable item definitions from per-instance runtime state so one `WeaponSO` (e.g. "Iron Sword") can coexist as thousands of unique `ItemInstance`s with different colors, sharpness, and wear — without cross-contamination. Give the combat, crafting, inventory, shop, and door systems a single data contract to consume.

## Responsibilities
- Defining items as immutable ScriptableObjects (`ItemSO` and its subclasses).
- Incarnating items at runtime with per-instance state (`ItemInstance`).
- Spawning, picking up, and despawning world items (`WorldItem`).
- Equipping items to characters via layers and sockets (`CharacterEquipment`).
- Storing items in containers (`Inventory`, `ItemSlot`, `WeaponSlot`, `MiscSlot`).
- Defining weapon data (`WeaponSO` → `MeleeCombatStyleSO` / `RangedCombatStyleSO`) and per-instance weapon runtime state (`WeaponInstance`).
- Defining keys (`KeySO`, `KeyInstance`) with lock ID and tier for the door system.
- Preventing ghost-item duplication via single-entry-point destroy (`CharacterPickUpItem.cs`).
- Driving the UI_Inventory, UI_ItemSlot grid and drag/drop.

**Non-responsibilities**:
- Does **not** calculate damage — that's [[combat]] (formula reads `WeaponSO.DamageType`, `CombatStyleSO.BaseDamage`, and stat tertiaries).
- Does **not** own crafting stations — that's part of [[jobs-and-logistics]] `CraftingBuilding` / `JobCrafter`.
- Does **not** own shops or merchant logic — see [[shops]].
- Does **not** own dialogue books directly — books use `IAbilitySource` to teach abilities; see `CharacterBookKnowledge`.

## Key classes / files

### Static data (ScriptableObjects)
| File | Role |
|------|------|
| `Assets/Resources/Data/Item/ItemSO.cs` | Base item definition. |
| Specialized: `WeaponSO`, `KeySO`, `MiscSO`, `BagSO`, etc. | Subtypes with extra static data. |
| `Assets/Resources/Data/CombatStyle/*.cs` | `CombatStyleSO` hierarchy — `MeleeCombatStyleSO`, `ChargingRangedCombatStyleSO`, `MagazineRangedCombatStyleSO`. |

### Runtime instances
| File | Role |
|------|------|
| [ItemInstance.cs](../../Assets/Scripts/Item/ItemInstance.cs) | In-memory wrapper; holds `Color_Primary`, `Color_Secondary`, `_customizedName`. |
| [Assets/Scripts/Item/WeaponInstance.cs](../../Assets/Scripts/Item/) | Subclasses: `MeleeWeaponInstance` (Sharpness), `ChargingWeaponInstance` (ChargeProgress), `MagazineWeaponInstance` (CurrentAmmo). |
| [KeyInstance.cs](../../Assets/Scripts/Item/KeyInstance.cs) | Runtime `_runtimeLockId` override. |
| `BagInstance`, `MiscInstance`, etc. | Specialized runtime wrappers. |

### World presence
| File | Role |
|------|------|
| [WorldItem.cs](../../Assets/Scripts/Item/WorldItem.cs) | `NetworkBehaviour` on the ground; `ItemInteractable` child for AI/player pickup. |
| [CharacterPickUpItem.cs](../../Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs) | **Only** place that destroys a `WorldItem`. Calls `CharacterActions.RequestDespawnServerRpc`. |

### Inventory & equipment
| File | Role |
|------|------|
| [Inventory.cs](../../Assets/Scripts/Inventory/Inventory.cs) | Slot-based container. |
| [ItemSlot.cs](../../Assets/Scripts/Inventory/ItemSlot.cs), [WeaponSlot.cs](../../Assets/Scripts/Inventory/WeaponSlot.cs), [MiscSlot.cs](../../Assets/Scripts/Inventory/MiscSlot.cs) | Typed slot classes. |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | Layer system (Underwear / Clothing / Armor) + bag sockets. `FindKeyForLock(lockId, requiredTier)` scans inventory + hands. |
| [UI_Inventory.cs](../../Assets/Scripts/Inventory/UI_Inventory.cs), [UI_ItemSlot.cs](../../Assets/Scripts/Inventory/UI_ItemSlot.cs) | Inventory grid UI. |

## Public API / entry points

Spawning:
- `ItemSO.CreateInstance(...)` — create a brand-new instance with per-item colors/parameters.
- `WorldItem.SpawnFromInstance(instance, position)` — materialize on ground.

Pickup / drop:
- `CharacterPickUpItem` action — **the only path** to remove a `WorldItem` from the world.
- `character.DropItem(slot)` — hand-off: strips the layer visual, spawns a `WorldItem`.

Equipment:
- `CharacterEquipment.Equip(instance, TargetLayer)` — layer-aware.
- `CharacterEquipment.UnequipToInventory(slot)`.
- `CharacterEquipment.FindKeyForLock(string lockId, int requiredTier)` — scans inventory + hands.

Combat:
- `WeaponInstance.CanFire()` / `ChargeProgress` / `CurrentAmmo` — combat reads these.
- `MeleeWeaponInstance.Sharpness` — durability read, reduce on hit.

Network despawn:
- **Never** call `NetworkObject.Despawn()` directly on a `WorldItem` — always route through `CharacterActions.RequestDespawnServerRpc(netObj)` (server only).

## Data flow

```
ItemSO (ScriptableObject, immutable)
        │
        ▼
ItemSO.CreateInstance(colors, name)
        │
        ▼
ItemInstance (per-owner, mutable)
        │
        ├──► WorldItem (dropped) ◄── CharacterPickUpItem destroys
        ├──► Inventory.AddItem
        └──► CharacterEquipment.Equip(layer)
                │
                ├──► WeaponInstance (runtime state)  ──► combat reads
                ├──► BagInstance (opens back sockets) ──► weapons render on back
                └──► layer VFX / sprite swap
```

Color flow (unique per `ItemInstance`, never shared):
```
ItemInstance.Color_Primary ──► InitializePrefab searches child nodes tagged "Color_Primary"
                          └──► pushes color into Material Property Block (no batching break)
```

## Dependencies

### Upstream
- [[character]] — equipment, inventory, and world item actions live on the character.
- [[network]] — `WorldItem` is a `NetworkObject`; server-only despawn; keys and item state sync through `NetworkVariable` / RPCs on `CharacterEquipment`.
- Crafting data lives on `ItemSO` (`CraftingRecipe`, `CraftingDuration`).

### Downstream
- [[combat]] — consumes `WeaponSO`, `CombatStyleSO`, `WeaponInstance`.
- [[shops]] — trades `ItemInstance`s.
- [[jobs-and-logistics]] — `CraftingOrder` produces `ItemInstance`s via `CraftingStation`; `TransportOrder` moves them.
- `door-lock-system` — consumes `KeySO.LockId` + `Tier` via `CharacterEquipment.FindKeyForLock`.
- `character-book-knowledge` — books implement `IAbilitySource` to teach abilities.

## State & persistence

- Every `Inventory` and `CharacterEquipment` slot serializes to the character profile (see [[save-load]]).
- `ItemInstance` colors and custom names travel with the character (portable profile → any session).
- `WorldItem`s on an active map persist via `MapSaveData`. During hibernation, world items serialize to `HibernatedItemData` so they survive map sleep.

## Known gotchas / edge cases

- **Never store mutable state on `ItemSO`** — e.g. color / sharpness on the ScriptableObject. That mutates **every** sword in the world.
- **Single destroy entry point** — only `CharacterPickUpItem.cs` destroys `WorldItem`. Any other path = ghost item / duplication.
- **Server-only despawn** — route through `CharacterActions.RequestDespawnServerRpc(netObj)`.
- **`TryCollect` must be balanced with `CancelCollect`** — if you reserve an item but fail to execute the pickup, free it.
- **`ItemInteractable` accessed via property**, not `GetComponentInChildren` (performance rule; exposed on `WorldItem` directly).
- **Weapon DamageType precedence** — `WeaponSO.DamageType` first, fallback to `CombatStyleSO.DamageType` for barehands.

## Open questions / TODO

- [ ] `Assets/Scripts/Items/` (plural) exists alongside `Assets/Scripts/Item/` (singular). Is one legacy? Flag for cleanup.
- [ ] `ItemMaterial.cs` (in `Assets/Scripts/Items/`) is newly added and has no SKILL coverage. Tracked in [[TODO-skills]].

## Child sub-pages (to be written in Batch 2)

- [[item-data]] — `ItemSO` hierarchy, ScriptableObject structure.
- [[item-instance]] — runtime state, colors, custom names.
- [[world-items]] — ground presence, pickup rules, network despawn.
- [[character-equipment]] — layer system, bag sockets, key lookup.
- [[inventory]] — slots, UI grid, drag/drop, save format.
- [[keys-and-locks]] — `KeySO`, `KeyInstance`, door integration.

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md)
- [.claude/agents/item-inventory-specialist.md](../../.claude/agents/item-inventory-specialist.md)
- [ItemInstance.cs](../../Assets/Scripts/Item/ItemInstance.cs)
- [WorldItem.cs](../../Assets/Scripts/Item/WorldItem.cs)
- [Inventory.cs](../../Assets/Scripts/Inventory/Inventory.cs)
- [CharacterPickUpItem.cs](../../Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs)
- 2026-04-18 conversation with [[kevin]].
