---
type: system
title: "Character Equipment"
tags: [character, equipment, items, tier-2]
created: 2026-04-19
updated: 2026-05-19
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

- Current layer contents + bag contents serialize via character profile (`EquipmentSaveData`, `SaveKey = "CharacterEquipment"`, `LoadPriority = 30`).
- The **carried in-hand item** (`HandsController.CarriedItem` — food, log, stone, key, …) is **distinct from the equipped weapon** and persists via its own contract: `HandsSaveData`, `SaveKey = "HandsController"`, `LoadPriority = 35` (deliberately runs after `CharacterEquipment` so the weapon slot is restored first and `AreHandsFree()` reflects the post-equip state). If the visual hand bones aren't initialized when `Deserialize` runs, the restore is deferred and consumed by `HandsController.Initialize()`.
- Back-socket runtime visuals are rebuilt on load.

## Replication contract — what's networked vs not

The `NetworkList<NetworkEquipmentSyncData> _networkEquipment` on `CharacterEquipment` replicates the **equipment slots only**:

| Slot id | Contents | Replicated |
|---------|----------|------------|
| 0 | Equipped weapon (`WeaponInstance`) | ✅ via `_networkEquipment` |
| 1 | Equipped bag *shell* (`BagInstance`) | ✅ via `_networkEquipment` |
| 100+ | Wearables (underwear=100+, clothing=200+, armor=300+) | ✅ via `_networkEquipment` |
| (inside the bag) | `_bag.Inventory.ItemSlots` items | ❌ **NOT replicated** |
| (in hands) | `HandsController.CarriedItem` | ❌ **NOT replicated** |

Bag-inventory contents and the carried-in-hand item are **deliberately not in any NetworkVariable / NetworkList**. They are persisted via `CharacterProfileSaveData` (loaded locally at session start) and mutated independently on each peer.

**Rule** (`[[host-only-state-blindspot]]` — see also `.agent/skills/item_system/SKILL.md` §"Bag-inventory replication authority"): any **server-side** code path that adds an item to a character's bag (shop delivery, chest take, quest reward) **must** branch on `character.IsSpawned && !character.IsOwnedByServer` and route delivery through `CharacterActions.ReceiveItemPickupClientRpc(NetworkItemData)` for remote-client characters; the owner reconstructs the item via `ItemSO.CreateInstance()` + `JsonUtility.FromJsonOverwrite` (preserves polymorphism) and runs `PickUpItem` locally. The inverse direction (client → server, e.g. store-to-chest) must include the item payload (`ItemSO.ItemId` + `JsonUtility.ToJson(instance)`) in the ServerRpc; the server reconstructs and the action then either runs server-side for host (slot lookup against server's own bag) or `[Rpc(SendTo.Owner)] RemoveFromInventoryAfterStoreClientRpc` back to a remote client owner.

Reference implementations:
- `WorldItem.RequestInteractServerRpc` — pickup, 2025.
- `CharacterAction_BuyFromShop.DeliverToCustomer` — shop delivery, 2026-05-14.
- `CharacterTakeFromFurnitureAction.OnApplyEffect` — chest take, 2026-05-14.
- `StorageFurnitureNetworkSync.RequestStoreFrom{Bag,Hands}ServerRpc` + `CharacterStoreInFurnitureAction.OnApplyEffect` — chest store, 2026-05-14.

## Known gotchas

- **Bag-inventory contents are not networked** — see "Replication contract" above. The most common shipping bug is calling `inv.AddItem` / `inv.RemoveItem` server-side on a remote-client character and seeing the change silently lost.
- **Use `character.DropItem`**, not manual unequip + spawn — `DropItem` atomically strips the visual and spawns a `WorldItem`.
- **Bag socket awakening** — happens only when the bag is equipped; never when a bag is in inventory.
- **Key lookup scans hands** — a player carrying a key in hand (`CarriedItem`) can open doors without equipping it formally.
- **Layer ordering** — underwear rendered first, then clothing, then armor; breaking this order can cause Z-fighting on sprite clothing. In Spine backend, the slot order in the skeleton is the source of truth — see [[visuals]] §Slot naming convention.
- **Amputated slot equip** — equipping a glove / boot on a dismembered hand / foot must be refused. Equipment layers must consult [[character-dismemberment]] `IsFunctional(BodyPartId)` before accepting an equip request.
- **Cross-archetype accessories** — a cap equipped on a humanoid vs an animal archetype resolves differently. Equipment calls `visual.AttachToSocket("head", obj)` — the visual backend's `EquipmentSocketMap` resolves the logical socket to the concrete bone. Unsupported sockets (animal with no hand slot) must fail silently. See [[visuals]] §Cross-Archetype Equipment Sockets.

## Open questions

- [ ] Layer sprite sorting rules — document when visuals children exist.
- [ ] Prosthetics as a pseudo-equipment layer vs owned entirely by [[character-dismemberment]] — decide where the equip flow lives (current plan: dismemberment owns it, but UI may route through the equipment panel).
- [ ] **Networking the carried item.** `HandsController._carriedItem` and its visual are local-only — remote clients never see another player carrying a log, key, or food. Picking up, save-loading, and dropping all work on the owner; observers see nothing. Scope of fix: a `NetworkVariable<NetworkItemRef>` on a networked component (or a new RPC pair) that mirrors the carry state to all observers, plus visual reattach on each client.

## Change log
- 2026-04-19 — Initial pass. — Claude / [[kevin]]
- 2026-04-22 — Added cross-refs to [[visuals]] (Spine skin composition + socket resolution) and [[character-dismemberment]] (prosthetics + amputated-slot guard). Bumped `depends_on` with [[visuals]]. — Claude / [[kevin]]
- 2026-04-26 — `CharacterEquipmentUI` now exposes a "drop carried item" button (`_dropHandsItemButton`) for the `HandsController.CarriedItem` slot, alongside the existing inventory + unequip slots. The same drop is also bound to **G** in `PlayerController` (project rule #33 — input ownership). Both routes enqueue the existing `CharacterDropItem` action; no new gameplay path. — Claude / [[kevin]]
- 2026-04-27 — `HandsController` now implements `ICharacterSaveData<HandsSaveData>` (`SaveKey = "HandsController"`, `LoadPriority = 35`). Fixes a save/load bug where the in-hand carried item silently disappeared on reload — `_carriedItem` was runtime-only and not covered by `EquipmentSaveData`. Restore is deferred to `Initialize()` if the hand visual isn't ready when `Deserialize` runs. Bag inventory persistence verified unchanged (already serialized via `EquipmentSaveData.bagInventoryItems`). Networking gap (carry state not replicated to observers) noted in Open questions. — Claude / [[kevin]]
- 2026-05-14 — Documented the **bag-inventory replication contract** (new "Replication contract" section). The `_networkEquipment` `NetworkList` replicates equipment slots only; bag-inventory contents and `HandsController.CarriedItem` are deliberately not networked. Three shipping flows (shop buy delivery, chest take, chest store) had been silently mutating the server-side shadow copy for remote clients and now route through `CharacterActions.ReceiveItemPickupClientRpc` (server→owner) or include the item payload in their ServerRpc + use `RemoveFromInventoryAfterStoreClientRpc` for the owner-side removal (client→server). Same pattern as `WorldItem.RequestInteractServerRpc`. See [[host-only-state-blindspot]] §"Bag-inventory state not replicated" and `.agent/skills/item_system/SKILL.md` §"Bag-inventory replication authority". — Claude / [[kevin]]
- 2026-05-19 — **Equipment UI rework (script-side complete; prefab + scene wiring deferred)**. Replaced the layer-tabbed `CharacterEquipmentUI` (still on disk pending prefab cleanup) with `UI_CharacterEquipment : UI_WindowBase` (paper-doll + 3-stacked-layer mini-cells + top-row Weapon/Hands/Bag cards + click-to-popup verbs). Five new server-authoritative `CharacterAction` subclasses route the verbs (`CharacterAction_EquipWearable` / `_UnequipWearable` / `_CarryInHand` / `_StashInBag` / `_UseItem`). Client UI ↔ server bridges through one consolidated `CharacterActions.RequestEquipmentVerbServerRpc(byte verbId, byte sourceKind, int bagIndex, int layer, int slot)` since direct `ExecuteAction` on the client runs locally only. Added `EquipmentSourceRef` (BagSlot / WornSlot / ActiveWeapon / HandsCarry discriminator), `HandsController.OnCarriedItemChanged` event (replaces UI polling), and three new helpers on `CharacterEquipment`: `TryStashInBag` (private), `UnequipToBag(layer, slot)`, `WieldOffToHand()`, `DetachWornToCaller(layer, slot)`. **Behavior change**: `Equip()` displacement is now bag-first with ground fallback (was: always drop to ground). Symmetric `CharacterAction_CarryInHand` smart-swap algo for hand collisions. Callers swept and unaffected: `EquipmentInstance.EquipToCharacter`, `CharacterEquipAction` (called by `WorldItem` wearable pickup + 2 GOAP equip paths — all benefit from less litter). Deferred: `UI_CharacterEquipment.prefab` authoring (Variant of `UI_WindowBase.prefab`), `UI_EquipmentBagCell.prefab` leaf, scene wiring of `PlayerUI._equipmentUI`, deletion of legacy `CharacterEquipmentUI.cs` + old prefab. The window is inaccessible in-game until those land — `OpenEquipmentWindow` no-ops with the rule #39 orange `[PlayerUI]` warning. See [spec](../../docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md) + [plan](../../docs/superpowers/plans/2026-05-19-character-equipment-ui-rework.md). — claude / [[kevin]]
- 2026-05-19 — **Equipment UI rework — final state shipped to multiplayyer (12 polish commits `5b1fecd3` → `5860aec1`)**. Prefabs authored, scene wired, legacy script deleted, then iteratively polished through playtest feedback:
  - **`CharacterAction_UseItem` deleted; Use verb now routes through the existing `CharacterUseConsumableAction`** (1.5s duration, `Trigger_Consume` animator trigger, `Character.UseConsumable` on apply which handles `ApplyEffect` + removal from hands/inventory). The originally-planned wrapper was redundant — the canonical consumable-use action already existed.
  - **Initialize no longer auto-opens the window.** Split into `Initialize` (data bind only) + `InitializeAndOpen` (user-driven open). `PlayerUI.cs:184` auto-calls Initialize during character setup; previously the bundled `OpenWindow()` auto-popped the panel at game start.
  - **Tab is NOT the equipment opener.** Spec said "keep Tab binding" but `PlayerController.cs:216` Tab is `HandleTabTargeting()`. Only the HUD `_buttonEquipmentUI` button opens the window. The Open/Close/Toggle methods exist on PlayerUI for future hotkey wiring.
  - **15 worn cells authored on the doll** (5 body rows × 3 layer cells U/C/A each). Without them, equipped items had no display surface — `OnEquipmentChanged` had no targets to repaint, so equipping a tshirt appeared "nowhere".
  - **New-item badge restored on bag cells** via `IPointerEnterHandler` mirroring the old `UI_ItemSlot.OnPointerEnter` pattern: hover an item with `IsNewlyAdded=true` → clear flag → tell window to call `CharacterEquipment.ClearInventoryNotification()` if no items remain new. Added `_newBadge` + `_fallbackLabel` SerializeFields to `UI_EquipmentBagCell` and `_fallbackLabel` to `UI_EquipmentWornCell`. Fallback shows first 3 chars of item name when `ItemSO.Icon` is null.
  - **Several UGUI gotchas documented in `.agent/skills/ui-hud/SKILL.md`**: Panel_Main_Background 50%-alpha inheritance, layout-group `childControl*` flags ignoring `LayoutElement`, card-label overflow from runtime-instantiated GameObjects' default `sizeDelta=(100,100)`, `Button_Close` canvas-center anchoredPosition per variant, `RectangleContainsScreenPoint` mis-compute under ScreenSpaceCamera, scene-instance + `UI_PlayerHUD.prefab` nested-instance dedupe. — claude / [[kevin]]

## Sources
- [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md) §4, §6, §7.
- [[items]] parent.
- [[host-only-state-blindspot]] — bag-inventory replication is the canonical example of the audit's "not actually a single replicated field" sub-case.
