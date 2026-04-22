---
type: system
title: "Character Equipment"
tags: [character, equipment, items, tier-2]
created: 2026-04-19
updated: 2026-04-22
sources: []
related:
  - "[[items]]"
  - "[[character]]"
  - "[[combat]]"
  - "[[visuals]]"
  - "[[character-dismemberment]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: item-inventory-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterEquipment/"
depends_on:
  - "[[items]]"
  - "[[character]]"
  - "[[visuals]]"
depended_on_by:
  - "[[combat]]"
  - "[[items]]"
  - "[[character-dismemberment]]"
---

# Character Equipment

## Summary
Layer-based equipment on the character. Three clothing layers (`UnderwearLayer` / `ClothingLayer` / `ArmorLayer`) plus a `Bag` layer that, when worn, opens back-mounted sockets and renders the weapons stored inside the bag. Keys are looked up via `FindKeyForLock(lockId, requiredTier)` which scans inventory slots + `HandsController.CarriedItem`, matching on `KeyInstance.LockId` and `KeySO.Tier >= requiredTier`.

## Purpose
Give the character a clean layered clothing/armor/weapon architecture that plays well with [[visuals]] (sprite swaps / color injection) and [[combat]] (weapon type reads for ability gating).

## Responsibilities
- Maintaining typed layers + their current `ItemInstance`.
- Equipping/unequipping via `CharacterEquipment.Equip` / `Unequip`.
- Bag system: awakening back sockets, rendering bagged weapons.
- Key lookup for the door-lock system (`FindKeyForLock`).
- Synchronizing layer visuals over the network (observer replication).

**Non-responsibilities**:
- Does **not** own `ItemSO` / `ItemInstance` — see [[items]].
- Does **not** compute combat damage — see [[combat]] (weapon type is read here; damage math is there).
- Does **not** own Inventory slots — see `[[inventory]]`.

## Key classes / files

- `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` — root component.
- `Layer` subtypes (`UnderwearLayer`, `ClothingLayer`, `ArmorLayer`) — per-slot containers.
- `HandsController` (likely separate child) — owns `CarriedItem`.
- `WearableHandlerBase` — visual wiring for complex clothing.

## Public API

- `character.CharacterEquipment.Equip(ItemInstance, TargetLayer)`.
- `character.CharacterEquipment.UnequipToInventory(slot)`.
- `character.CharacterEquipment.FindKeyForLock(string lockId, int requiredTier)` — returns matching `KeyInstance` or null.
- `character.DropItem(slot)` — drop: strips layer visual + spawns `WorldItem`.

## Data flow

```
Player/NPC equips an ItemInstance
       │
       ▼
CharacterEquipment.Equip(inst, TargetLayer)
       │
       ├── layer.Set(inst)
       ├── Layer applies visual (sprite swap, color inject)
       │    └── uses WearableHandlerBase for complex clothing
       │
       ├── If inst.ItemSO is a BagSO:
       │    └── Awaken _bagSockets (back mounts)
       │         └── instantiate every weapon in the bag's Inventory visually on sockets
       │
       └── Broadcast layer state via NetworkVariable / RPC to observers
```

Key lookup:
```
Door.TryOpen(character)
       │
       ▼
character.CharacterEquipment.FindKeyForLock(door.LockId, door.RequiredTier)
       │
       ├── scan inventory slots
       ├── scan HandsController.CarriedItem
       └── return first KeyInstance where LockId matches & Tier >= required
```

## Dependencies

### Upstream
- [[items]] — wears `ItemInstance`s.
- [[character]] — subsystem.

### Downstream
- [[combat]] — reads `WeaponInstance`, `Sharpness`, `ChargeProgress`, `CurrentAmmo` via equipment.
- `door-lock-system` — uses `FindKeyForLock`.

## State & persistence

- Current layer contents + bag contents serialize via character profile.
- Back-socket runtime visuals are rebuilt on load.

## Known gotchas

- **Use `character.DropItem`**, not manual unequip + spawn — `DropItem` atomically strips the visual and spawns a `WorldItem`.
- **Bag socket awakening** — happens only when the bag is equipped; never when a bag is in inventory.
- **Key lookup scans hands** — a player carrying a key in hand (`CarriedItem`) can open doors without equipping it formally.
- **Layer ordering** — underwear rendered first, then clothing, then armor; breaking this order can cause Z-fighting on sprite clothing. In Spine backend, the slot order in the skeleton is the source of truth — see [[visuals]] §Slot naming convention.
- **Amputated slot equip** — equipping a glove / boot on a dismembered hand / foot must be refused. Equipment layers must consult [[character-dismemberment]] `IsFunctional(BodyPartId)` before accepting an equip request.
- **Cross-archetype accessories** — a cap equipped on a humanoid vs an animal archetype resolves differently. Equipment calls `visual.AttachToSocket("head", obj)` — the visual backend's `EquipmentSocketMap` resolves the logical socket to the concrete bone. Unsupported sockets (animal with no hand slot) must fail silently. See [[visuals]] §Cross-Archetype Equipment Sockets.

## Open questions

- [ ] `HandsController` — exact location and API.
- [ ] Layer sprite sorting rules — document when visuals children exist.
- [ ] Prosthetics as a pseudo-equipment layer vs owned entirely by [[character-dismemberment]] — decide where the equip flow lives (current plan: dismemberment owns it, but UI may route through the equipment panel).

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]
- 2026-04-22 — Added cross-refs to [[visuals]] (Spine skin composition + socket resolution) and [[character-dismemberment]] (prosthetics + amputated-slot guard). Bumped `depends_on` with [[visuals]]. — Claude / [[kevin]]

## Sources
- [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md) §4, §6, §7.
- [[items]] parent.
