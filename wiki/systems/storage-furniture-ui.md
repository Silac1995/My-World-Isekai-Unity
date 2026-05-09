---
type: system
title: "Storage Furniture — Player UI"
tags: [ui, hud, storage, furniture, inventory]
created: 2026-05-09
updated: 2026-05-09
sources:
  - Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs
  - Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs
  - Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab
  - docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md
related:
  - "[[storage-furniture]]"
  - "[[character-actions]]"
  - "[[player-ui-hud]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: [character-system-specialist]
owner_code_path: Assets/Scripts/UI/WorldUI/
depends_on:
  - "[[storage-furniture]]"
  - "[[character-equipment]]"
  - "[[character-actions]]"
depended_on_by: []
---

# Storage Furniture — Player UI

## Summary

Player-side HUD panel that opens when the local owner-player taps E on a `StorageFurniture` chest. Shows the player's bag inventory + hands on the left and the chest's slots on the right. Click-to-transfer in either direction, routing through the same `CharacterStoreInFurnitureAction` and `CharacterTakeFromFurnitureAction` that NPC GOAP already uses. No new RPCs — the UI is a thin shell over existing server-authoritative actions.

## Purpose

Close the API/UI gap on `StorageFurniture`: NPCs already store and retrieve items via GOAP, but no player-facing surface queues those same actions. This system is that surface.

## Responsibilities

- React to `Furniture.OnInteract` from the owner-player.
- Render the chest's slots and the player's bag inventory + hands item.
- Subscribe to `StorageFurniture.OnInventoryChanged` and `CharacterEquipment.OnEquipmentChanged` to repaint live.
- Poll `HandsController.CarriedItem` (no event fires for hands carry).
- Auto-close on ESC, walk-out-of-zone, target despawn, character incapacitated, or combat entry.
- Construct + queue `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction` instances on slot clicks.

## Key classes / files

- `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` — panel controller. Initializes from `(StorageFurniture, Character)`, owns the lifecycle, click handlers, and polling.
- `Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs` — generic slot-grid renderer used for both halves of the panel (player bag + chest).
- `Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab` — authored prefab.
- `Assets/UI/Player HUD/UI_StorageGridSlot.prefab` — slot button template.
- `Assets/Scripts/World/Furniture/StorageFurniture.cs` — `OnInteract(Character)` override is the open path.
- `Assets/Scripts/UI/PlayerUI.cs` — `OpenStoragePanel(StorageFurniture, Character)` / `CloseStoragePanel()` helpers.

## Public API / entry points

- `PlayerUI.Instance.OpenStoragePanel(StorageFurniture, Character)` — the only sanctioned way to open the panel. Called by `StorageFurniture.OnInteract`.
- `PlayerUI.Instance.CloseStoragePanel()` — programmatic close. ESC and auto-close paths invoke `UI_StorageFurniturePanel.Close()` directly.

## Data flow

```
Player taps E on chest (in InteractionZone)
  → PlayerController.HandleEKeyUp (owner-only)
  → nearest.Interact(player) on the chest's FurnitureInteractable
  → Furniture.OnInteract(Character) -- StorageFurniture override
  → PlayerUI.Instance.OpenStoragePanel(this, player)
  → UI_StorageFurniturePanel.Initialize(target, interactor)
  → SetActive(true), bind both grids, subscribe to events
  → repaint each frame as state changes via StorageFurniture.OnInventoryChanged
    + CharacterEquipment.OnEquipmentChanged + Update()-poll on HandsController

Player clicks bag slot or hands sub-slot
  → UI_StorageFurniturePanel.QueueStore(item)
  → new CharacterStoreInFurnitureAction(interactor, item, target)
  → interactor.CharacterActions.ExecuteAction(action)
  → existing client→server RPC inside CharacterActions
  → server runs OnApplyEffect: removes from inventory/hands, calls target.AddItem
  → fires StorageFurniture.OnInventoryChanged server-side
  → StorageFurnitureNetworkSync.HandleServerInventoryChanged rewrites NetworkList
  → all clients (including owner) mirror via ApplySyncedSlotsFromNetwork
  → local OnInventoryChanged fires → panel repaints

Player clicks chest slot
  → UI_StorageFurniturePanel.OnChestSlotClicked
  → new CharacterTakeFromFurnitureAction(interactor, item, target)
  → interactor.CharacterActions.ExecuteAction(action)
  → server: target.RemoveItem + HandsController.CarryItem
  → existing replication paths (storage NetworkList for chest, hands sync for character)
```

## Dependencies

- **Upstream:** `Furniture` / `StorageFurniture` / `FurnitureInteractable` (the open path), `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction` (the action layer), `StorageFurnitureNetworkSync` (replication), `Character.CharacterEquipment` + `HandsController` (left side data), `PlayerUI` + `UI_PlayerHUD` (host canvas).
- **Downstream:** none. This is a leaf consumer.

## State & persistence

- The panel itself is ephemeral. No save data. No NetworkVariables.
- All persisted state lives on `StorageFurniture` / `StorageFurnitureNetworkSync` (chest contents) and `CharacterEquipment` (bag inventory). The panel only renders.

## Known gotchas / edge cases

- **Owner gate is critical.** Without it, panels would pop on every replicated peer when any character calls `OnInteract`. Verified via `interactor.IsOwner && interactor.IsPlayer()`.
- **Hands has no event.** `HandsController.CarriedItem` changes are not signalled. Panel polls each frame in `Update()`. Same pattern as `CharacterEquipmentUI.RefreshHandsButton`.
- **`UI_Inventory` not reused.** `UI_ItemSlot` only handles right-click-drop, no left-click hook. Reusing it for the panel's bag side would require modifying the equipment-UI slot behaviour, so the panel uses its own `UI_StorageGrid` renderer for both halves.
- **ESC handling lives in the panel itself.** Rule #33's "input that targets the UI" carve-out applies — same pattern as `PauseMenuController`, `BuildingPlacementManager`, etc.
- **Re-bind on tap-E to a different chest works without explicit close.** `Initialize` calls `UnsubscribeAll` first.

## Open questions / TODO

- _(none currently)_

## Change log

- 2026-05-09 — Created. Initial implementation. — claude

## Sources

- `docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md`
- `docs/superpowers/plans/2026-05-09-storage-furniture-player-ui.md`
- `wiki/systems/storage-furniture.md`
- `wiki/systems/character-actions.md`
