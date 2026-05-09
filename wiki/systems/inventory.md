---
type: system
title: "Inventory"
tags: [items, inventory, ui, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[items]]"
  - "[[character-equipment]]"
  - "[[player-ui]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: item-inventory-specialist
owner_code_path: "Assets/Scripts/Inventory/"
depends_on:
  - "[[items]]"
  - "[[character]]"
depended_on_by:
  - "[[items]]"
  - "[[character-equipment]]"
  - "[[shops]]"
  - "[[jobs-and-logistics]]"
---

# Inventory

## Summary
Slot-based container for `ItemInstance`s. Typed slots (`ItemSlot`, `WeaponSlot`, `MiscSlot`) enforce what can go where. UI views (`UI_Inventory`, `UI_ItemSlot`) render the grid and handle drag/drop. Used by characters, buildings (commercial stock), and bags (which themselves are `ItemInstance`s containing an inner Inventory).

## Purpose
Give every container (character inventory, bag, building stock) the same slot semantics so items move between them with uniform rules: reserve on `TryCollect`, release on `CancelCollect`, transfer on `InteractionBuyItem` / `InteractionPlaceOrder`, drop via `character.DropItem`.

## Responsibilities
- Holding slots (typed containers, fixed or dynamic count).
- Enforcing per-slot type constraints (a `WeaponSlot` rejects a Misc item).
- Reserve/release API (`TryCollect` / `CancelCollect`) to prevent race conditions.
- UI drag/drop wiring.
- Serializing slot contents for [[save-load]].
- Network sync (server-authoritative mutations).

**Non-responsibilities**:
- Does **not** own world items — see `[[world-items]]`.
- Does **not** own equipment layers — see [[character-equipment]].
- Does **not** handle sale transactions — see [[shops]].

## Key classes / files

- [Inventory.cs](../../Assets/Scripts/Inventory/Inventory.cs) — root container.
- [ItemSlot.cs](../../Assets/Scripts/Inventory/ItemSlot.cs) — base slot.
- [WeaponSlot.cs](../../Assets/Scripts/Inventory/WeaponSlot.cs) — weapons only.
- [MiscSlot.cs](../../Assets/Scripts/Inventory/MiscSlot.cs) — misc items / keys.
- [UI_Inventory.cs](../../Assets/Scripts/Inventory/UI_Inventory.cs), [UI_ItemSlot.cs](../../Assets/Scripts/Inventory/UI_ItemSlot.cs) — grid UI.

## Public API

- `inventory.TryAddItem(ItemInstance)` / `RemoveItem(slotIndex)`.
- `inventory.TryCollect(ItemInstance)` — reserve for pickup; must be released with `CancelCollect` on failure.
- `inventory.CancelCollect(ItemInstance)` — free a reserved item.
- `inventory.FindFirstMatching(ItemSO)` / `FindKeyForLock(...)` (delegated to [[character-equipment]] for key lookup).
- `inventory.Count`, `inventory.IsFull`.

## Data flow

Pickup:
```
CharacterPickUpItem action targets a WorldItem
       │
       ▼
worldItem.TryCollect(character)       ──► reserves for this character only
       │
       ▼
action.OnApplyEffect (on owner)
       │
       ├── character.Inventory.TryAddItem(instance)
       └── character.CharacterActions.RequestDespawnServerRpc(worldItem.NetworkObject)
       │
       ▼
WorldItem despawned (server)
```

On failure path:
```
action cancelled mid-run
       │
       └── MUST call worldItem.CancelCollect(character) or the world item stays reserved forever
```

## Dependencies

### Upstream
- [[items]] — the things being contained.
- [[character]] — each character has an `Inventory` (may be several: carried + bag-internal).

### Downstream
- [[character-equipment]] — reads inventory for key lookup.
- [[shops]] — transfers items on sale.
- [[jobs-and-logistics]] — transporter moves items between inventories.
- [[player-ui]] — `UI_Inventory` renders the grid.

## State & persistence

- Every slot's `ItemInstance` serializes to character or map save data.
- Bag-internal inventories serialize as nested save data of the bag's `BagInstance`.
- Ghost prevention: reserved `TryCollect` state is transient; resets on load.

## Known gotchas

- **Single destroy path** — only `CharacterPickUpItem.cs` calls the despawn RPC. Everything else goes through inventory transfer, not raw Destroy.
- **Reserve/release balance** — failing to `CancelCollect` after a failed pickup leaks the item for the session.
- **Slot type gate** — `WeaponSlot.CanAccept(instance)` must be checked before insert. Missing the check lets a hat land in a sword slot.
- **UI drag/drop** — drops onto a typed slot that rejects the item should bounce back cleanly; don't leak into a network mutation unless the slot accepted.

## Open questions

- [ ] Are stack sizes supported (consumables, food)? Current model looks 1-item-per-slot. Confirm.
- [ ] Inventory max size per character — hardcoded, profile-driven, or skill-driven?

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md)
- [[items]] parent.
