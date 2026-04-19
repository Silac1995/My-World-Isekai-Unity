---
type: system
title: "World Items"
tags: [items, world, pickup, network, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[items]]"
  - "[[inventory]]"
  - "[[network]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: item-inventory-specialist
secondary_agents:
  - network-specialist
owner_code_path: "Assets/Scripts/Item/"
depends_on:
  - "[[items]]"
  - "[[character]]"
  - "[[network]]"
depended_on_by:
  - "[[items]]"
  - "[[world]]"
---

# World Items

## Summary
`WorldItem` is the physical, `NetworkObject`-backed presence of an `ItemInstance` on the ground. It hosts a `SortingGroup`, a visual root populated from the item's prefab, and an `ItemInteractable` child that AI + player systems use as the interaction target. Two rules are critical: **pickup destroys are exclusively through `CharacterPickUpItem.cs`**, and **network despawn runs through `CharacterActions.RequestDespawnServerRpc`**, never directly.

## Purpose
Let items exist in the world with spatial, physical, and network presence — while keeping the unique item state (`ItemInstance`) as the authoritative source of truth. The world GameObject is disposable; the instance's colors, sharpness, and custom names are what matter.

## Responsibilities
- Spawning world items from an `ItemInstance` at a position.
- Hosting the instance + visual + interaction trio.
- Performance: exposing `ItemInteractable` via serialized property (no `GetComponentInChildren`).
- Coordinating with `CharacterPickUpItem` for the single-destroy rule.
- Reserving for pickup via `TryCollect(character)` + releasing via `CancelCollect(character)` on failure.
- Saving/loading for map save data (with hibernation survival).

**Non-responsibilities**:
- Does **not** own item data — [[items]] `ItemSO` / `ItemInstance`.
- Does **not** directly despawn itself — server-side `CharacterActions.RequestDespawnServerRpc`.
- Does **not** handle pickup animation — see `CharacterPickUpItem`.

## Key classes / files

- [WorldItem.cs](../../Assets/Scripts/Item/WorldItem.cs) — the `NetworkBehaviour`.
- [CharacterPickUpItem.cs](../../Assets/Scripts/Character/CharacterActions/CharacterPickUpItem.cs) — sole destroy path.
- `ItemInteractable` (in `Assets/Scripts/Interactable/`) — child interaction target.

## Public API

- `WorldItem.SpawnFromInstance(ItemInstance, Vector3)` — materialize.
- `WorldItem.ItemInstance` — the live data.
- `WorldItem.ItemInteractable` — the interaction target (serialized property, no GetComponent).
- `WorldItem.TryCollect(character)` — reserve for pickup; returns true if available.
- `WorldItem.CancelCollect(character)` — release reservation on failure.

## Spawn/despawn flow

```
ItemInstance dropped from inventory (character.DropItem)
       │
       ▼
WorldItem.SpawnFromInstance(instance, pos)
       │
       ├── Set _instance
       ├── Build visual root from ItemSO.ItemPrefab
       ├── InitializeWorldPrefab (inject per-instance colors)
       └── NetworkObject.Spawn (server)
```

Pickup:
```
Character runs CharacterPickUpItem action
       │
       ├── worldItem.TryCollect(character)
       ├── if success: action proceeds
       │
       ▼
action.OnApplyEffect (on owner — can be client)
       │
       ├── character.Inventory.TryAddItem(worldItem.ItemInstance)
       └── character.CharacterActions.RequestDespawnServerRpc(worldItem.NetworkObject)
                │
                ▼
        Server validates → Despawn
```

## Performance optimization

`ItemInteractable` is exposed as a serialized public property:

```csharp
public ItemInteractable ItemInteractable { get; }   // set via [SerializeField]
```

Never `GetComponentInChildren<ItemInteractable>()` in AI navigation / interaction-check hot paths. The serialized reference is the canonical access.

## Dependencies

### Upstream
- [[items]] — carries an `ItemInstance`.
- [[network]] — is a `NetworkObject`; server-authoritative despawn.
- [[character]] — pickup actions live on `CharacterActions`.

### Downstream
- [[inventory]] — receives the instance on pickup.
- [[world]] — hibernation snapshots world items into `HibernatedItemData`.

## State & persistence

- Live: `ItemInstance` (colors, custom name), position, owner (temporary during carry).
- Saved: position + instance on the map's save data while the map is active; `HibernatedItemData` while hibernating.
- Reservations (`_reservedFor`) are transient.

## Known gotchas

- **Single destroy entry point** — only `CharacterPickUpItem.cs` destroys. Any other path = ghost item / duplication.
- **Server-only despawn** — `NetworkObject.Despawn()` from a client throws. Always route through `CharacterActions.RequestDespawnServerRpc`.
- **`TryCollect`/`CancelCollect` must balance** — failing to cancel on action abort leaves the item unpickable for the session.
- **`ItemInteractable` via property** — never `GetComponentInChildren`. Performance hit in AI.
- **Network position sync** — world items use standard NetworkTransform. If dropped rapidly, clients may see a brief mis-position — acceptable trade-off.

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md) §3.
- [[items]] parent.
- [WorldItem.cs](../../Assets/Scripts/Item/WorldItem.cs).
