---
title: Character Equipment UI rework — paper-doll + click-to-popup
date: 2026-05-19
status: approved
author: Kevin (Silac) + claude
related:
  - wiki/systems/character-equipment.md
  - wiki/systems/inventory.md
  - wiki/systems/player-hud.md
  - .agent/skills/ui-hud/SKILL.md
  - .agent/skills/item_system/SKILL.md
---

# Character Equipment UI rework — design

## 1. Context

The existing equipment window — script `Assets/Scripts/UI/CharacterEquipmentUI.cs`, prefab `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` — predates the rule #39 (UI HUD Prefab Architecture) and rule #22 (player↔NPC parity through `CharacterAction`) conventions and shows the strain. Concretely:

- **Script does not inherit `UI_WindowBase`** and uses `CharacterEquipmentUI` instead of the project's `UI_*` naming convention.
- **Toggle path** is `PlayerUI.ToggleEquipmentUI` calling `gameObject.SetActive(!active)` (line 264) rather than the canonical `Open<Name>Window` / `Close<Name>Window` pair with the rule #39 null-guard warning.
- **UX is layer-tabbed** (Armor / Clothing / Underwear): only one of the three simultaneous wearable layers is visible at a time, hiding the actual state.
- **Slots are text Buttons** ("None" / `ItemName`) — no icons, no tooltips, no item details.
- **Single "drop hands item" button** sits at the bottom, divorced from the other slots; mirrors the `G` hotkey but is its own surface.
- **Click-to-unequip drops to ground** (`CharacterEquipment.Unequip` calls `character.DropItem(instance)`). Items go to the world even when there is space in the bag — surprising and punishing.
- **No `CharacterAction` route** for equip / unequip / carry / use — the UI calls `CharacterEquipment.Equip` / `Unequip` / `CarryItemInHand` / `DropItemFromHand` directly from the player's local client. An NPC AI wanting to do "equip this wearable" today has no canonical action surface to enqueue.

This spec replaces the window with a **paper-doll + stacked-layer** view, a **state-aware action popup** that opens on every item click, a **smart hand-swap** rule that prefers the bag over the ground when displacing the currently carried item, and a set of **new `CharacterAction` subclasses** so both player UI and future NPC AI share one mutation path.

The visual exploration that converged on this design is captured in `.superpowers/brainstorm/2002-1779142520/` (v1 → v3 mockups). The v3 mockup is the locked design.

## 2. Scope

### In scope (v1)

- New `UI_CharacterEquipment : UI_WindowBase` script + new `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` authored as a Prefab Variant of `UI_WindowBase.prefab` (rule #39).
- Top-row of three special-slot cards: **Active Weapon · Hands Carry · Equipped Bag**.
- Paper-doll body: 5 body slots (Helm / Torso / Gloves / Pants / Boots), each rendered as a 3-cell stack (Underwear / Clothing / Armor) with item icons and layer-tinted backgrounds.
- Bag-inventory grid on the right.
- Single shared **action popup component** (`UI_EquipmentActionPopup`) anchored at the clicked cell, fed a state-aware verb list.
- Verbs: **Equip · Use · Carry in hand · Stash in bag · Unequip · Unequip bag · Drop on ground** — applied per the matrix in §5.
- **Smart hand-swap algorithm** (§6) for the "Carry in hand" verb when the hand is already occupied.
- Five new `CharacterAction` subclasses (§4 `Files`) routed through `Character.CharacterActions.ExecuteAction` per rule #22.
- `PlayerUI` façade upgrade: `_equipmentUI` field retyped, `OpenEquipmentWindow(Character)` / `CloseEquipmentWindow()` / `ToggleEquipmentWindow(Character)` introduced with the rule #39 null-guard warning. Old `ToggleEquipmentUI` kept as a thin shim that forwards to `ToggleEquipmentWindow` (so the wired `_buttonEquipmentUI.onClick` and the `PlayerController` Tab path keep working through the rename window) and removed once both call sites are migrated.
- ESC / close button / window-target-despawn close paths (no out-of-zone — equipment is self-owned, not a furniture window).
- Late-joiner audit per rule #19b: equipment state already replicates via `CharacterEquipment._networkEquipment` (`NetworkList<NetworkEquipmentSyncData>`); bag inventory is local-owner per the documented "Replication contract" gotcha. UI subscribes to `OnEquipmentChanged` and polls `HandsController.CarriedItem` (no event today — known gap, captured below).

### Out of scope (deferred)

- **Drag-and-drop** between cells. Pure click-to-popup model.
- **Right-click as a "default verb" shortcut** (e.g. right-click bag-wearable = quick equip). May be added later as polish.
- **Sorting / filtering / search in the bag grid.** Bag layout is the existing `Inventory.ItemSlots` order.
- **Party-member equipment inspection.** Window is self-owned only (one `Character` target).
- **NPC equipment view from dev-mode inspector.** Existing `CharacterInspectorView` text-based readout remains the inspection surface.
- **Tooltip stat fields beyond name + layer/slot.** Stat content depends on `WeaponInstance.Damage`-style accessors we'll refine when authoring; v1 surfaces what's already readable.
- **Networking the carried-in-hand item to remote observers.** Already an Open question on `wiki/systems/character-equipment.md`. Window observes the local owner only; remote-player observers see no carry today (unchanged from current behavior). The new UI does NOT make this gap worse.
- **Hands carry hotkey ergonomics changes.** `G` continues to drop the hand-carried item per `PlayerController` line 223 (project rule #33).

## 3. Decisions captured

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | Visual metaphor | **C · Hybrid paper-doll + stacked layers** | Only option that honestly surfaces all three layers at once (the defining feature of MWI equipment) while keeping the silhouette emotional payoff. Approved by Kevin during v1 mockup review. |
| 2 | Interaction model | **Click-to-popup, no drag-drop** | Discoverable for new players, uniform across all cells, single component to author, mirrors the existing `UI_InteractionMenu` (hold-E) pattern. Drag-drop is deferred. |
| 3 | Special-slot layout | **Top row of three cards** (Active Weapon · Hands · Bag) | Cleaner separation from the 15 wearable mini-cells, easier to label, room for badges ("swap: Y" on weapon, capacity on bag). Avoids cluttering the doll. |
| 4 | "Carry in hand" universal? | **Yes — appears on every item-bearing surface** | Carrying ≠ wielding. Active Weapon "Carry in hand" wields-off-to-loose. Only exceptions: Hands card (already carrying) and Bag card (container, not a hand-carry-able item). |
| 5 | Hand-swap collision | **Smart-swap per §6** | Tries bag (including the source's freshly-vacated slot); only drops the current hand item to ground as last resort. No "disabled when full" buttons. |
| 6 | Active-Weapon swap from this window? | **No — read-only** | Combat HUD owns swap (Y). Active Weapon card shows weapon + "swap: Y" badge but offers Stash / Carry / Drop, not Swap. Keeps responsibilities separated. |
| 7 | Route mutations through `CharacterAction`? | **Yes — five new action classes** | Rule #22 compliance. Future NPC behaviors (autonomous equip, "carry this item to X") get the same surface for free. |
| 8 | Window shape | **Centered modal panel ~720×520** | Default `UI_WindowBase.prefab` Canvas (ScreenSpaceCamera @1920×1080 per rule #39). No side-drawer / full-screen. |
| 9 | Opener | **`Tab` (keep current binding) + `_buttonEquipmentUI`** | No new input plumbing. `PlayerController` line 216 already binds Tab; `PlayerUI._buttonEquipmentUI` already wires the on-screen button. Both retarget to the new `ToggleEquipmentWindow`. |
| 10 | Naming convention | **`UI_CharacterEquipment`** (was `CharacterEquipmentUI`) | Matches project `UI_*` prefix. Same rename applied to the prefab. |
| 11 | Existing prefab/script disposition | **Delete both, replace with new** | Old script + prefab carry rule-#39 violations. Rather than retrofit the prefab (which would require reparenting under `UI_WindowBase.prefab` anyway), author fresh. Scene wiring on `PlayerUI._equipmentUI` retargets to the new prefab instance. |

## 4. Architecture

### Files

| File | Status | Purpose |
|---|---|---|
| `Assets/Scripts/UI/CharacterEquipmentUI.cs` | **delete** | Replaced. Old layer-tab implementation. |
| `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` | **delete** (or save aside as `.bak` outside `Assets/`) | Replaced by new Prefab Variant of `UI_WindowBase.prefab`. |
| `Assets/Scripts/UI/Equipment/UI_CharacterEquipment.cs` | **new** | The window root. `: UI_WindowBase`. Holds: 3 special-slot card refs, paper-doll container, bag grid container, single `UI_EquipmentActionPopup` child ref. `Initialize(Character)` subscribes to `OnEquipmentChanged` and starts an unscaled-time poll for `HandsController.CarriedItem` (no event today). Builds rows + cells once; updates labels/icons on event. |
| `Assets/Scripts/UI/Equipment/UI_EquipmentSpecialSlotCard.cs` | **new** | Leaf script for the three top-row cards (Weapon / Hands / Bag). Inits with a `SlotKind` enum + the live `Character` ref. Click → tells parent window to open the popup, fed with the kind-appropriate verb set. |
| `Assets/Scripts/UI/Equipment/UI_EquipmentWornCell.cs` | **new** | Leaf script for one mini-cell in a layer stack. Inits with `(WearableLayerEnum, WearableType)`. Click → tells parent window to open the popup, fed with worn-item verbs. Empty state = visual placeholder, no click handler. |
| `Assets/Scripts/UI/Equipment/UI_EquipmentBagCell.cs` | **new** | Leaf script for one bag slot. Inits with a `slotIndex` into `Character.CharacterEquipment.GetInventory().ItemSlots`. Click → opens popup with bag-item verbs (state determined from `ItemInstance` type at click time). Empty state = no click handler. |
| `Assets/Scripts/UI/Equipment/UI_EquipmentActionPopup.cs` | **new** | Single shared popup. `Show(RectTransform anchor, ItemActionContext ctx, List<EquipmentVerb> verbs)`. Renders a button per verb. ESC / click-outside dismisses. Each button calls back into `UI_CharacterEquipment.OnVerbSelected(verb, ctx)`. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_EquipWearable.cs` | **new** | Server-authoritative. Removes from bag → routes to `CharacterEquipment.Equip(instance)`. Existing `CharacterEquipment.Equip` already auto-swaps the displaced wearable into the bag (per the new behavior change in §6 below — see "Equip-side displacement" subsection). |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_UnequipWearable.cs` | **new** | Server-authoritative. Calls a new `CharacterEquipment.UnequipToBag(layer, slotType)` that stashes to bag instead of dropping (with ground-drop fallback when bag is full). Wraps existing `targetLayer.Unequip` + `UpdateNetworkSlot` plumbing. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_CarryInHand.cs` | **new** | Server-authoritative implementation of the §6 smart-swap algorithm. Source can be a bag slot, a worn layer/slot, or the active-weapon slot — all converge through one action. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_StashInBag.cs` | **new** | Server-authoritative. Hand-or-worn → bag with ground-drop fallback. The "Stash in bag" verb on the Hands card and Active Weapon card share this. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_UseItem.cs` | **new** | Server-authoritative. Dispatches to the item's "use" behavior (eat food, drink potion). v1 supports `FoodInstance`-style consumables; other consumable types verified during plan-phase. |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | **edit** | (a) Refactor `Equip` so the displaced wearable goes to the bag first, ground only on bag-full. (b) Add `UnequipToBag(WearableLayerEnum, WearableType)` returning success bool. (c) Add `WieldOffToHand()` (active-weapon → hand without dropping) supporting `CharacterAction_CarryInHand`. (d) Keep `Unequip` (drop-to-ground) for the existing escape-hatch / NPC drop callers. |
| `Assets/Scripts/UI/PlayerUI.cs` | **edit** | Retype `_equipmentUI` to `UI_CharacterEquipment`. Add `OpenEquipmentWindow(Character)` / `CloseEquipmentWindow()` / `ToggleEquipmentWindow(Character)` with rule #39 null-guard warning. Keep `ToggleEquipmentUI()` for one commit as a forwarding shim, then delete in a follow-up commit once `_buttonEquipmentUI.onClick` listener + any Tab caller are repointed. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | **edit** | Repoint Tab binding (line 216) from `PlayerUI.ToggleEquipmentUI()` to `PlayerUI.ToggleEquipmentWindow(_character)`. |

### Per-action UI → action wiring

```
[Click bag cell with Iron Helm spare]
  → UI_EquipmentBagCell.OnClick
  → UI_CharacterEquipment.OpenPopupFor(cellRect, bagItemCtx, [Equip, Carry, Drop])
  → user clicks Equip
  → UI_CharacterEquipment.OnVerbSelected(Equip, bagItemCtx)
  → owner-local: character.CharacterActions.ExecuteAction(new CharacterAction_EquipWearable(bagSlotIndex))
  → [SERVER tick] action.OnApplyEffect:
       validate item in bag slot still present + still wearable
       inventory.RemoveAt(slotIndex)
       character.CharacterEquipment.Equip(instance)  // auto-swaps existing wearable to bag (new behavior)
  → CharacterEquipment.OnEquipmentChanged fires on every peer (via _networkEquipment NetworkList)
  → UI repaints
```

### Bag-inventory replication note

Bag-inventory contents are NOT replicated to remote observers — only the bag *shell* is. The new UI subscribes to the local owner's bag inventory directly; no change to the replication contract. The matching gotcha in `wiki/systems/character-equipment.md` §"Replication contract" continues to apply: server-side B2B paths that mutate a remote-client character's bag MUST route through `CharacterActions.ReceiveItemPickupClientRpc`. The five new actions added here are all owner-local-triggered, so this gotcha is not hit by them.

## 5. Verb matrix

Source of truth: this table. The popup is constructed by feeding `UI_EquipmentActionPopup.Show` a `List<EquipmentVerb>` chosen per the row below.

| Clicked surface | Available verbs (top = primary) |
|---|---|
| Bag cell · wearable | **Equip** · Carry in hand · Drop on ground |
| Bag cell · consumable | **Use** · Carry in hand · Drop on ground |
| Bag cell · weapon | **Carry in hand** · Drop on ground *(active-swap via Y in combat HUD)* |
| Bag cell · misc / key / book / coin | **Carry in hand** · Drop on ground |
| Worn mini-cell (any U/C/A) | **Unequip** *(→ bag, fallback ground)* · Carry in hand · Drop on ground |
| Hands carry card | **Stash in bag** *(fallback ground)* · Use *(if consumable)* · Drop on ground |
| Active Weapon card | **Stash in bag** · Carry in hand · Drop on ground |
| Equipped Bag card | **Unequip bag** *(drops whole bag — preserves current `CharacterEquipment.UnequipBag` behavior)* |

Verb keyboard hotkeys (popup-scoped, optional polish): E · U · C · S · D. Bound only while the popup is open; PlayerController is unaffected.

## 6. "Carry in hand" smart-swap algorithm

Implemented inside `CharacterAction_CarryInHand.OnApplyEffect` (server tick). The "Equip" verb has a symmetric displacement path inside `CharacterEquipment.Equip` (see "Equip-side displacement" subsection at the end of this section).

```
// Inputs: source S (BagSlot | WornSlot | ActiveWeapon), item X (resolved from S)
// State: hand currently holds Y (HandsController.CarriedItem; may be null)

if Y is null:
    detachFromSource(X, S)         // removes X from S, no replacement
    hands.CarryItem(X)
    return

// Hand is occupied. Free X from its source first so its slot becomes available.
detachFromSource(X, S)             // bag slot or worn slot becomes empty;
                                   // for ActiveWeapon, calls WieldOffToHand-equivalent
                                   // but defers the hand assign until below

if bag.HasFreeSpaceForItem(Y):
    // covers BOTH the "slot was already free" case AND the "X's now-empty
    // slot accepts Y because they share a type" case. No special branch.
    bag.AddItem(Y)
    hands.DropCarriedItem()        // clears HandsController without spawning a WorldItem
    hands.CarryItem(X)
else:
    // Bag has no slot Y can fit into — drop Y to the world.
    DropToWorld(Y)                 // CharacterDropItem.ExecutePhysicalDrop
    hands.DropCarriedItem()
    hands.CarryItem(X)
```

**Why the check works without a special "same type" branch:** `Inventory.HasFreeSpaceForItem(Y)` already considers each slot's type filter and the slot's empty/full state. Once `detachFromSource(X, S)` has run, if `S` was a bag slot, that slot is empty and contributes to `HasFreeSpaceForItem(Y)` only if Y's type fits the slot's type filter. The "carrying a sword, click another sword in a WeaponSlot" case lands here automatically.

**Edge case — source is the Active Weapon card:** `detachFromSource` must call a new `CharacterEquipment.WieldOffToHand()` path that nulls `_weapon`, runs `UpdateWeaponVisual()` (socket hides, animator returns to civilian) and `UpdateNetworkSlot(0, null)` — but does NOT call `character.DropItem` like the existing `UnequipWeapon` does. The detached `WeaponInstance` is then handed back to the algorithm so the bag/ground/hand sink is consistent.

**Edge case — source is a worn layer/slot:** `detachFromSource` calls `targetLayer.Unequip(slotType)` + `UpdateNetworkSlot(slotId, null)`, then returns the instance. No additional displaced item to worry about (the worn slot just becomes empty).

### Equip-side displacement (new behavior)

`CharacterEquipment.Equip(WearableInstance)` currently does:

```csharp
EquipmentInstance existingInstance = targetLayer.GetInstance(data.WearableType);
if (existingInstance != null)
{
    character.DropItem(existingInstance);  // drops to ground
}
```

The rework changes this to:

```csharp
EquipmentInstance existingInstance = targetLayer.GetInstance(data.WearableType);
if (existingInstance != null)
{
    if (!TryStashInBag(existingInstance))
    {
        character.DropItem(existingInstance);  // ground fallback
    }
}
```

`TryStashInBag` is a small private helper using `Inventory.HasFreeSpaceForItem` + `AddItem`. The behavior is symmetric with the smart-swap above: bag first, ground only on full.

This is a behavior change to `CharacterEquipment.Equip` and may affect any non-UI caller. Sweep at plan-phase: all current callers of `Equip` (NPC `GoapAction_*` paths, `WorldItem.RequestInteractServerRpc` pickup flow, save-load `Deserialize`) — verify none depend on the "drop displaced item to ground" side-effect.

## 7. Late-joiner audit (rule #19b)

Six-question audit:

1. **Who writes / who reads.**
   - Writers: server (via the five new `CharacterAction` classes). Plus existing writers (`WorldItem.RequestInteractServerRpc`, save/load `Deserialize`, NPC GOAP equip paths). All converge through `CharacterEquipment.Equip` / `Unequip` / `UnequipBag` / `Equip` / `UnequipWeapon` and `HandsController.CarryItem` / `DropCarriedItem`.
   - Readers: the local owner's `UI_CharacterEquipment` window. No remote-client UI consumers (the window only ever opens for the local owner).

2. **Replication channel.**
   - Equipped slots (weapon, bag shell, 15 wearables): existing `NetworkList<NetworkEquipmentSyncData> _networkEquipment` on `CharacterEquipment`. UI subscribes via `OnEquipmentChanged` (fires after `ApplyEquipmentData` / `RemoveEquipmentData` on the client side). No new replicated fields.
   - Bag-inventory contents: deliberately not replicated (rule #18 / `[[host-only-state-blindspot]]`). UI reads from the local owner's bag only. The window never opens for a remote character, so the "client sees server-side shadow" gap does not apply.
   - Hands carry: local-owner only (`HandsController._carriedItem` is not networked). UI polls it at unscaled-time 4 Hz inside `Update`. Matches the existing approach in `CharacterEquipmentUI.RefreshHandsButton`. Captured Open question on `wiki/systems/character-equipment.md` — this UI doesn't widen the gap.

3. **Late-joiner repro (mandatory before claiming done).**
   - **Repro steps:** host the session, equip a helmet + drop a sword + put an apple in the bag → client joins late → client opens equipment window on its own Player. Expect: window paints client's own state. Then host opens its own equipment window on the host's character. Expect: each peer sees their own loadout correctly. (The window does not surface remote characters, so the cross-peer correctness test reduces to "each peer paints its own owner state on a late join".)
   - The existing `CharacterEquipment.OnNetworkSpawn` already calls `FullSyncFromNetwork()` for joining clients — the equipment payload is delivered before the window can open. No new code required for this gate; only verify behavior with the new UI script.

4. **Client-side pre-gate.**
   - Window opens only via `PlayerController.HandleEKey…` Tab path → `PlayerUI.ToggleEquipmentWindow(_character)`. The Character ref is the owning player's Character (local owner). No remote-client pre-gate to consider.

5. **`GetComponentInParent` in `Awake` (spawn-race risk).**
   - No new `GetComponentInParent` introduced. `UI_CharacterEquipment` lives as a `SerializeField` sibling on `PlayerUI`, bound at scene authoring. The popup is a child of the window.

6. **Proximity gate (rule #36).**
   - Not applicable. Equipment is self-owned; there is no world-space interactable to gate on. No `IsCharacterInInteractionZone` check needed.

**Replication channel chosen:** existing `_networkEquipment` `NetworkList` for slots. Bag-contents and hands-carry remain local-owner only — UI reads only the local owner's state, so the existing un-networked gap is not made worse.

## 8. NPC parity (rule #22)

The five new `CharacterAction` subclasses ARE the canonical surface for "equip a wearable" / "unequip a wearable" / "carry an item in hand" / "stash an item from hand to bag" / "use a consumable" — for both player UI clicks and future NPC AI.

- `CharacterAction_EquipWearable(int bagSlotIndex)` — NPC autonomously equipping a found garment.
- `CharacterAction_UnequipWearable(WearableLayerEnum, WearableType)` — NPC autonomously stashing armor mid-narrative.
- `CharacterAction_CarryInHand(EquipmentSourceRef source)` — already used by player; future NPC "fetch this item and carry it to X" flows.
- `CharacterAction_StashInBag(EquipmentSourceRef source)` — symmetric.
- `CharacterAction_UseItem(EquipmentSourceRef source)` — NPC eating food (today GOAP `GoapAction_BuyFood` ends with an `Eat` step; this is the action it would enqueue).

`EquipmentSourceRef` is a tiny serializable discriminated value: `{ Kind: BagSlot | WornSlot | ActiveWeapon | HandsCarry, BagIndex?: int, Layer?: WearableLayerEnum, Slot?: WearableType }`. Used by Carry / Stash / Use to identify the source uniformly.

The raw `CharacterEquipment` API (`Equip`, `Unequip`, `UnequipBag`, `EquipWeapon`, `WieldOffToHand`) continues to serve B2B paths (save-load, pickup, NPC GOAP). Those callers do NOT route through the actions because they're side-effects of other actions / lifecycle paths, not character intentional acts. Same precedent as `CharacterAction_DepositToSafe` vs `safe.Credit`.

## 9. Error handling

| Failure | Server behavior | Client UX |
|---|---|---|
| Bag slot empty by the time action runs (race) | Action no-ops + logs | Popup already closed; no toast (silent — race is rare and self-correcting via next `OnEquipmentChanged`) |
| Wearable slot already empty by the time Unequip runs | No-op + log | Same |
| `EquipWearable` on a non-wearable in the bag slot (corrupt input) | RPC validates type, rejects | Logged anti-cheat warning; silent client-side |
| `CarryInHand` and bag has no room AND hand drop fails (e.g. world is full?) | Defensive: keep Y in hand, refuse to detach X; log | Toast "can't carry — drop something first" |
| `UseItem` on an item with no Use behavior (e.g. accidentally on misc) | Action no-ops + log | Verb shouldn't appear on this item type; reaching here is a UI bug |
| `UnequipBag` when wearing nothing (bag = null) | Existing `CharacterEquipment.UnequipBag` already guards with warning | No toast |
| Window opened while owner Character is despawned | `PlayerUI.ToggleEquipmentWindow` guards on `character != null` | Silent return |

All `Debug.Log` calls in hot paths (4 Hz hands-carry poll, `OnEquipmentChanged` repaint) MUST be gated behind a verbose toggle per rule #34. Popup state transitions (Open / Close / button-click) log only at `Debug.isDebugBuild`.

## 10. Open questions / risks

- **Consumable detection.** v1 needs a clean predicate for "is this item Use-able?" Options: a `IUsable` marker interface on `ItemInstance`, an `ItemSO.IsConsumable` flag, or a runtime type check (`item is FoodInstance || item is PotionInstance || ...`). Decide at plan-phase. Default proposal: a `IUsable` marker on `ItemInstance` subclasses that implement a `Use(Character user)` method — keeps dispatch polymorphic.
- **`CharacterAction_UseItem` payload for food.** Probably calls into existing `CharacterNeeds.NeedHunger.Satiate(amount)` via the food instance's recipe. Verify path during plan-phase against `wiki/systems/needs.md` / `wiki/systems/food.md` if those exist.
- **Equip-side displacement behavior change.** Sweeping all current callers of `CharacterEquipment.Equip` to confirm none depend on the "displaced wearable drops to ground" side-effect. NPC GOAP equip paths (clothing job, armor pickup), `WorldItem.RequestInteractServerRpc` pickup, save-load `Deserialize` — verify at plan-phase.
- **Bag-inventory `UI_Inventory` widget reuse vs rebuild.** Current implementation embeds `UI_Inventory` as a child. New design proposes rebuilding inline (one `UI_EquipmentBagCell` per slot) so click behavior is uniform with the doll. If `UI_Inventory` has callers elsewhere we want to preserve, it stays untouched — we just don't use it inside `UI_CharacterEquipment`. Verify at plan-phase that no other panel depends on the embedded shape.
- **Numeric width / overflow.** A long item name in the special-slot card (e.g. "Reinforced Plate Shoulder Guard") can overflow the card label. Truncate-with-ellipsis or shrink-to-fit; resolved at prefab authoring time.
- **Future combat-bar interplay.** When the player is in combat and the equipment window is open, the combat HUD's items popover may overlap. v1 closes the equipment window automatically if combat starts (defensive). Confirm with combat HUD owner that this is desirable; otherwise leave both open and let z-order handle it.

## 11. Testing matrix (multiplayer mandatory)

| Scenario | Expected |
|---|---|
| Open via Tab, close via X, close via ESC | All three work; no console warnings. |
| Open via Tab on a freshly-spawned character | All worn slots show empty placeholders; special cards labeled "(empty)"; bag grid populates from save data. |
| Click bag wearable → Equip → instantly worn | Item appears in the matching layer/slot mini-cell within one frame after `OnEquipmentChanged`. Bag slot becomes empty. |
| Click bag wearable → Equip — slot already had a different item in same layer | Existing item moves to bag (first free compatible slot); new item appears in worn slot. Bag-full edge → existing item drops to ground; verify visual on the world. |
| Click worn cell → Unequip → goes to bag | Wearable disappears from doll, appears in bag's first free compatible slot. |
| Click worn cell → Unequip — bag full | Wearable disappears from doll, spawns as `WorldItem` at character feet. |
| Click bag item → Carry in hand — hands free | Item moves from bag slot to hand visual; bag slot empties. |
| Click bag item → Carry in hand — hands occupied, bag has space | Hand item stashes to bag; new item moves to hand. |
| Click bag item → Carry in hand — hands occupied, bag full | Hand item drops to world; new item moves to hand. |
| Click bag item → Carry in hand — same type as hand item (both swords) | New sword in hand; old sword in the freshly-vacated weapon slot. |
| Click Hands card → Stash in bag | Hand item moves into bag; ground if bag full. |
| Click Hands card → Use (consumable) | Consumable's Use behavior fires (e.g. NeedHunger satiated); item destroyed. |
| Click Active Weapon → Stash in bag | Weapon socket hides; weapon goes to bag weapon slot; combat HUD reflects no active weapon. |
| Click Active Weapon → Carry in hand | Weapon socket hides; weapon moves to HandsController; combat HUD reflects no active weapon. |
| Click Bag card → Unequip bag | Whole bag drops as `WorldItem` with contents (current `UnequipBag` behavior). Window closes (no bag = no inventory grid to show). |
| Popup dismissal: ESC | Popup closes; window stays open. |
| Popup dismissal: click outside the popup | Popup closes; window stays open. |
| Popup dismissal: click another cell while popup open | Old popup closes; new popup opens for the new cell. |
| Two players in same session, each opens own equipment window | Each window paints its own owner; no cross-talk. |
| Late-joiner: host changes equipment then client joins → opens window | Client's window shows the client's own state (host's state is not shown). |
| Save + load with mid-game loadout | Window state matches loaded `EquipmentSaveData` after one `OnEquipmentChanged` fire. |
| Equipment window open when GameSpeed = 0 (paused) | Window still works (unscaled time used per rule #26). |
| `_equipmentWindow` field on `PlayerUI` is null | `Open/Toggle…` log the rule #39 orange directive warning + early-return. No null-deref. |
| Character incapacitated mid-window-open | `CharacterEquipment.HandleIncapacitated` drops carried item — UI reflects via hands poll within ~250ms. |

## 12. Documentation updates (rule #28 / #29 / #29b)

After implementation:

- **`wiki/systems/character-equipment.md`** — bump `updated:`, append change log line. Move the "click-to-unequip drops to ground" gotcha to Resolved (replaced by bag-first + ground-fallback). Refresh `depended_on_by` if the new actions/UI introduce new dependencies.
- **`wiki/systems/player-hud.md`** — bump `updated:`, append change log line. Add `UI_CharacterEquipment` to the "Key classes / files" table. Add to the PlayerUI surface API listing.
- **`wiki/systems/character-actions.md`** (if exists; else `wiki/systems/character.md` actions section) — register the five new actions.
- **`wiki/systems/inventory.md`** — bump `updated:` if any signature changes leak into Inventory; otherwise skip.
- **`.agent/skills/ui-hud/SKILL.md`** — append a "Click-to-popup pattern" section under existing patterns; note the shared popup-component-fed-per-state model.
- **`.agent/skills/item_system/SKILL.md`** — append a "Equipment-window action surface" subsection describing the five new actions and the smart-swap algorithm. Cross-reference `wiki/systems/character-equipment.md` for replication contract continuity.
- **`.claude/agents/character-system-specialist.md`** — extend description to include the five new actions.
- **`.claude/agents/item-inventory-specialist.md`** — extend description to include the new UI window + the smart-swap algorithm.
- **`.claude/agents/ui-hud-specialist.md`** — extend description to include `UI_CharacterEquipment` + the click-to-popup component pattern.

## 13. References

- `Assets/Scripts/UI/CharacterEquipmentUI.cs` (to be deleted)
- `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` (to be deleted)
- `Assets/Scripts/UI/PlayerUI.cs` (lines 43, 182, 264 — current wiring)
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` (line 216 — Tab binding)
- `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` (Equip / Unequip / EquipBag / UnequipBag / EquipWeapon / UnequipWeapon / PickUpItem / CarryItemInHand / DropItemFromHand / DropItemFromInventory / HasFreeSpaceForItemSO)
- `Assets/Scripts/UI/UI_WindowBase.cs` (base every closable window inherits)
- `Assets/UI/Player HUD/UI_WindowBase.prefab` (Prefab Variant base — rule #39)
- `Assets/Scripts/UI/Furniture/UI_SafePanel.cs` + `Assets/UI/Player HUD/UI_SafePanel.prefab` (closest UI precedent shipped 2026-05-16)
- `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` (window lifecycle precedent)
- `Assets/Scripts/UI/WorldUI/UI_InteractionMenu.cs` (popup-component precedent for hold-E)
- `Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs` (CharacterAction subclass pattern)
- `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs` + `CharacterAction_SwapWeapon.cs` (combat-bar precedent for equipment-mutation actions)
- `wiki/systems/character-equipment.md` (architecture + replication contract)
- `wiki/systems/inventory.md` (slot model)
- `wiki/systems/player-hud.md` (UI_WindowBase + PlayerUI façade architecture)
- `.agent/skills/ui-hud/SKILL.md` (authoring recipe)
- `.agent/skills/item_system/SKILL.md` (bag-inventory replication authority)
- `wiki/gotchas/host-only-state-blindspot.md`
- `CLAUDE.md` rules #16 (event/coroutine cleanup), #19 / #19b (late-joiner audit), #22 (player↔NPC parity), #26 (Time vs unscaled), #33 (input ownership), #34 (perf), #39 (UI HUD prefab architecture)
- `.superpowers/brainstorm/2002-1779142520/equipment-directions.html` (v1 — direction selection)
- `.superpowers/brainstorm/2002-1779142520/equipment-design.html` (v1 — first proposed layout)
- `.superpowers/brainstorm/2002-1779142520/equipment-design-v2.html` (v2 — click-popup model)
- `.superpowers/brainstorm/2002-1779142520/equipment-design-v3.html` (v3 — locked design)
