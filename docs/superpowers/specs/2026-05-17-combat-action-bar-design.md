---
title: Combat action bar
date: 2026-05-17
status: draft
author: Kevin (Silac) + claude
related:
  - wiki/systems/combat.md
  - wiki/systems/character-combat.md
  - wiki/systems/player-hud.md
  - .agent/skills/combat_system/SKILL.md
  - .agent/skills/ui-hud/SKILL.md
---

# Combat action bar — design

## 1. Context

Today's combat UI is [UI_CombatActionMenu.cs](../../Assets/Scripts/UI/UI_CombatActionMenu.cs) — a single "Melee Attack" / "Ranged Attack" button that auto-shows when `CharacterCombat.IsInBattle == true`. The button text flips between "Melee" and "Ranged" based on `CurrentCombatStyleExpertise.Style is RangedCombatStyleSO`, queues a `PlannedAction` via `SetActionIntent`, and turns blue with `[Queued]` text while waiting for initiative.

That covers one verb. The actual combat surface needs more:

- **Abilities** — `CharacterAbilities` holds 6 active slots (`PhysicalAbilityInstance` / `SpellInstance`) + 4 passive slots. No player UI surfaces them today.
- **Items** — `ConsumableInstance` exists, `ConsumableSO._destroyOnUse` is wired, but consumables can't be triggered from combat. No filter for "usable in combat."
- **Reload** — `MagazineWeaponInstance` has `_currentAmmo` / `_magazineSize` / `_isReloading` + `StartReload()` / `FinishReload()` methods. `MagazineRangedCombatStyleSO._reloadTime` defaults to 2s. **No `CharacterAction_Reload` exists; no caller invokes `StartReload`.**
- **Swap weapon** — `CharacterEquipment` exists; swap mechanics are TBD (need to verify `GetCarriedWeapons()` exists).
- **Initiative visibility** — `CharacterCombat.UpdateInitiativeTick` fires `OnInitiativeFull`, but the player has no visual indication of "how full is my initiative right now."

This spec covers the visual surface (bar layout, sub-window, chrome) **and** the backend wiring required to make the new buttons functional (Reload action, Swap action, `IsUsableInCombat` filter, network sync of ammo + reload state, hotkey routing through `PlayerController`).

## 2. Scope

### In scope (v1)

- Replace `UI_CombatActionMenu` with a multi-cluster action bar: **weapon · abilities · utility**.
- Active-weapon-only verbs (no melee-while-ranged secondary attack).
- Per-weapon-instance UI variants: Melee · Charging (bow) · Magazine (pistol) — with Ranged Attack + ammo readout + Reload + charge-progress visual.
- Player-local **Initiative bar** + **queued-action label** anchored above the action bar (Option A from brainstorm).
- 6 ability icons (active slots from `CharacterAbilities`) inline in the bar, with cooldown/resource state + empty-slot affordance.
- **Items sub-window** — `UI_WindowBase` Prefab Variant per rule #39 — anchored above-right of the Items button. Auto-closes on use, ESC, combat end, second-click toggle, out-of-zone (if combat ends mid-window).
- Filter: `IsUsableInCombat` flag on `ConsumableSO` (`FoodSO` overrides to `false`). Food items still listed but disabled with reason.
- **Weapon Swap** — cycle to next entry from `CharacterEquipment.GetCarriedWeapons()`. Greyed when only one weapon carried. Swap preview shows current → next icon.
- **New `CharacterAction` classes** — `CharacterAction_Reload` (continuous, duration = `MagazineRangedCombatStyleSO.ReloadTime`) and `CharacterAction_SwapWeapon` (continuous, ~0.5s). Both NPC-callable per rule #22.
- **Network sync** — replicate `MagazineWeaponInstance.CurrentAmmo` + `IsReloading` so clients see the active weapon's ammo state. Channel TBD in §5.
- **Hotkey map** — Space (active attack), R (reload), Y (swap), 1–6 (abilities), E (items). All wired in `PlayerController.Update()` per rule #33.
- Multiplayer correctness: Host↔Client + Host/Client↔NPC validated. Late-joiner repro for ammo + swap state (mandatory per rule #19b).

### Concrete API additions required (in scope)

The spec depends on APIs that don't exist today. Collecting them here so the planner has a single inventory:

| Surface | New member | Why |
|---|---|---|
| `Inventory.cs` | `IEnumerable<ConsumableInstance> GetConsumables()` | Items window data source. Filters `ItemSlots` for `ItemInstance is ConsumableInstance`. (There is **no `CharacterInventory` class**; inventory lives on `CharacterEquipment.GetInventory()` returning `Inventory`.) |
| `Inventory.cs` | `IReadOnlyList<WeaponInstance> GetWeaponInstances()` | Swap source. Filters `ItemSlots` for `slot is WeaponSlot && !slot.IsEmpty()`. The existing `UpdateWeaponVisualOnBag` at `CharacterEquipment.cs:495` already does this filter inline — refactor into a reusable helper. |
| `CharacterEquipment.cs` | `int ActiveWeaponIndex { get; }` + `void SwapToNextWeapon()` (server-only) | Tracks which entry of `GetWeaponInstances()` is currently equipped. `SwapToNextWeapon` advances index modulo count, calls existing equip/unequip pair to actually swap. **No new "secondary slot" concept** — reuses the existing inventory + equip API entirely (see §4.1 "Carried weapons data model"). **Do not invoke directly from client input** — Y-hotkey + UI button route through `CharacterAction_SwapWeapon` (§4 data flow) so the swap respects initiative pacing + anti-spam delay. |
| `CharacterEquipment.cs` | `NetworkVariable<int> _activeAmmoNet` + `NetworkVariable<bool> _isReloadingNet` | Replicates the active magazine weapon's per-shot state. `-1` ammo sentinel = not a magazine. See §5. |
| `CharacterAbilities.cs` | `bool TryUseSlot(int slotIndex, Character target)` | Combat hotkeys 1-6 + UI ability slot click. Wraps `_activeSlots[i].TryTrigger(target)` with bounds + null check. |
| `CharacterCombat.cs` | `event Action<float> OnInitiativeChanged` (pct 0–1) | Drives `UI_CombatInitiativeBar`. Fired inside the existing `UpdateInitiativeTick` after the change. |
| `CharacterCombat.cs` | `event Action OnActionIntentCleared` | Drives `UI_CombatQueuedLabel.Hide()`. Fired inside the existing `ClearActionIntent` after the assignment. |
| `CharacterCombat.cs` | `bool TryQueueReload()` | Player UI + hotkey entry. Validates `WeaponInstance is MagazineWeaponInstance && !IsReloading && CurrentAmmo < MagazineSize`, queues `CharacterAction_Reload` via `CharacterActions.ExecuteAction`. |
| `CharacterCombat.cs` | `bool TryQueueSwapWeapon()` | Validates `GetWeaponInstances().Count >= 2 && no swap-action in flight`, queues `CharacterAction_SwapWeapon`. |
| `CharacterCombat.cs` | `bool TryQueueUseItem(ConsumableInstance, Character target)` | Validates target (self vs throw), queues item use via existing or new consumable action. |
| `MagazineWeaponInstance.cs` | `void CancelReload()` | Resets `_isReloading = false` without setting ammo. Called by `CharacterAction_Reload.OnInterrupt`. |
| `ConsumableSO.cs` | `[SerializeField] bool _isUsableInCombat = true;` + property | Items window filter. |
| `FoodSO.cs` | override `IsUsableInCombat => false` (or set the SerializeField on existing assets) | Food is bench-only. |
| `CharacterAction_Reload.cs` | new file | Continuous action; see §4 file table. |
| `CharacterAction_SwapWeapon.cs` | new file | Continuous action; see §4 file table. |

**Verification deferred to planning (not blockers, but worth confirming):**
- Whether `CharacterAction_UseItem` (or similar) already exists for non-combat consumable use; if yes, `TryQueueUseItem` reuses it; if no, add. The existing E-dispatcher uses `CharacterUseConsumableAction` (`PlayerController.cs:379`) — likely the right reuse.
- Whether `CharacterEquipment` equip-change replicates to clients today. If not, audit + fix as part of this work — otherwise Swap will work for the swapping player but remote players won't see the weapon change.
- Whether `CharacterStats.Initiative` is replicated. Under Option A chrome (player-only init bar) this only matters for the owner's local UI, which reads from the local server-or-owner copy.

### Out of scope (deferred)

- **"Melee while ranged equipped" gameplay capability.** Removed by user directive 2026-05-17. Ranged-weapon-can-melee-attack is being eliminated from `WeaponSO` and combat logic in a **separate follow-up** (see §13). UI never shows a secondary attack button.
- **Hold-Y radial weapon picker.** Ship cycle now; radial for 3+ weapon loadouts is a v2.
- **"Pin item to ability slot."** Drag a Health Potion onto slot 5 to bind it permanently. Deferred — bigger feature, separate design.
- **Multi-use mode in Items sub-window.** Shift-click to use without closing. Defer; v1 closes on use.
- **Ability loadout UI** (out-of-combat: choose which 6 abilities to slot). Separate spec.
- **Party-member action ordering** (click ally row to queue their action). Option C's chrome anticipated this; Option A is single-player and the panel-with-allies UI is deferred.
- **Combat XP / level-up display.** `UI_CombatExpBar.cs` exists separately and is untouched by this spec.
- **Enemy initiative bars / world-space combatant UI.** Option B (head-bars) and Option C (party panel) rejected for v1. Deferred until enemy threat-readout becomes a felt need.
- **Hotkey rebinding UI.** Bindings are constants in v1; settings UI is a separate concern.

## 3. Decisions captured

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | Bar layout shape | **Option B from brainstorm — always-visible action bar** | All 6 ability slots + verbs visible. 1-click ability use. Hotkeys feel native. JRPG ATB pacing + MMO speed. |
| 2 | Dual attack (melee + ranged when ranged equipped) | **No — active-weapon only** | User directive. Melee-from-ranged is being removed from gameplay; UI mirrors. Player swaps to melee weapon to bash. |
| 3 | Reload button visibility | **Always visible when magazine weapon equipped; greyed when ammo full** | Stable layout > shifting widths. Predictable. |
| 4 | Auto-queue Reload at 0 ammo when player attempts Attack | **Yes — auto-queue with "click again to confirm" toast** | Modern-shooter convention (Borderlands / Destiny). The strict "Attack does nothing on empty" punishes the player for a UI state the game can see. |
| 5 | Bow charge bar location | **Inside the Attack button (sub-bar fill)** | Initiative bar stays dedicated to its one job. Button-internal bar reads as "this action is being charged." |
| 6 | Swap mechanic | **Tap Y = cycle to next carried weapon; hold Y = radial picker (deferred)** | Cycle covers 2-weapon loadouts perfectly. Radial added later if 3+ loadouts become common. |
| 7 | Sword swap — does carried pistol still show Reload? | **No — only active weapon's verbs render** | Real-time-shooter "reload while sheathed" convention doesn't fit ATB pacing. Swap to pistol to reload. |
| 8 | Items sub-window placement | **Anchored above-right of Items button** | World view stays visible — combat doesn't pause; you need to see initiative + enemy moves. |
| 9 | Items window auto-close after use | **Yes** | One-shot selection. Shift-click multi-use deferred. |
| 10 | Items hotkeys inside the sub-window | **1–9 select row N (window-scoped binding)** | Doesn't conflict with the global 1–6 ability map because PlayerController gates by `_combatItemsWindow.IsOpen`. |
| 11 | Food items shown in Items window | **Listed but disabled with reason ("Not usable in combat")** | Discoverability beats hiding — players ask "why isn't my Roast Meat here?" |
| 12 | `IsUsableInCombat` filter mechanism | **`bool _isUsableInCombat = true` on `ConsumableSO`. `FoodSO` overrides to `false`.** | Smallest data change. Matches existing structure. |
| 13 | Chrome (initiative + queued) placement | **Option A — compact player-only, centered above action bar** | User directive. Zero clutter; smallest authoring; no per-character world UI. |
| 14 | Initiative bar shows party / enemy bars? | **No — player only** | Follows from #13. Enemy threat-readout deferred. |
| 15 | Queued-action label content | **`▶ Queued: <icon> <action name> → <target name>`** | Tells player what fires + at whom. Matches the in-button blue glow + replaces today's "[Queued]" text suffix. |
| 16 | NPC parity for Reload + Swap | **Yes — rule #22 mandatory. Both actions are no-owner-gated `CharacterAction`s.** | A future combat AI that reloads / swaps weapons uses the same surface. |
| 17 | Hotkey ownership | **`PlayerController.Update()` per rule #33** | All hotkeys read in one place, gated by `IsOwner`. UI button onClick handlers call the same `Character` subsystem methods the hotkeys do. No parallel input paths. |
| 18 | Replication of `CurrentAmmo` + `IsReloading` | **Lift to `NetworkVariable<int>` (ammo) + `NetworkVariable<bool>` (isReloading) on `CharacterEquipment`** — exposed per active weapon slot | See §5 — fewest moving parts. WeaponInstance objects stay POCO. |

## 4. Architecture

Pattern:
- **Action bar** = leaf HUD element (no close button → not a `UI_WindowBase` variant per rule #39). Replaces `UI_CombatActionMenu` 1-for-1; rebuilt as multi-cluster with composable sub-elements.
- **Items sub-window** = `UI_WindowBase` Prefab Variant (has close affordance → window per rule #39). Mirrors `UI_SafePanel` precedent.
- **Initiative bar + queued label** = leaf sub-elements parented inside the action bar's `_menuContainer`. Show/hide together with the bar based on `IsInBattle`.
- **Backend actions** = continuous `CharacterAction`s with no owner gate (NPC-callable per rule #22).

### Files

| File | Status | Purpose |
|---|---|---|
| `Assets/Scripts/UI/UI_CombatActionMenu.cs` | rewrite | Multi-cluster bar (weapon · abilities · utility). Drives all visual state from `Character.CharacterCombat`, `Character.CharacterAbilities`, `Character.CharacterEquipment`. Owns the inline init bar + queued label children. |
| `Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs` | new | Per-slot leaf prefab (×6). Renders ability icon + hotkey + cooldown overlay + resource readout. `Initialize(int slotIndex, Character)`. |
| `Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs` | new | Leaf prefab. Subscribes to `Character.CharacterStats.OnInitiativeChanged` (add if missing) — driven by existing `_initiative01` field. |
| `Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs` | new | Leaf prefab. Subscribes to `Character.CharacterCombat.OnActionIntentDecided` (show + paint) + `OnActionIntentCleared` (hide). Renders `"▶ Queued: <icon> <name> → <target>"`. |
| `Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs` | new | `UI_WindowBase` subclass. Opens via `PlayerUI.OpenCombatItemsWindow(Character)`. Builds row list from `character.CharacterEquipment.GetInventory().GetConsumables()` (new helper — see §2). Auto-closes on use / combat end / ESC / second-click toggle. |
| `Assets/Scripts/UI/Combat/UI_CombatItemRow.cs` | new | Leaf row prefab. Renders icon + name + qty + effect + hotkey badge. `Initialize(ConsumableInstance, Character, int hotkeyNumber)`. Click → `OnUseClicked`. |
| `Assets/Scripts/UI/PlayerUI.cs` | edit | Add `[SerializeField] private UI_CombatItemsWindow _combatItemsWindow;` + `OpenCombatItemsWindow(Character)` + `CloseCombatItemsWindow()`. Null-guard warning per rule #39. |
| `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` | new | Prefab Variant of `UI_WindowBase.prefab`. Wired into `PlayerUI._combatItemsWindow`. |
| `Assets/UI/Player HUD/UI_CombatItemRow.prefab` | new | Leaf row prefab (not a UI_WindowBase variant — no self-close button). |
| `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab` | new | Leaf, ×6 instances inside the action bar. |
| `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab` | new | Leaf, single instance inside the action bar. |
| `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab` | new | Leaf, single instance inside the action bar. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs` | new | Continuous action (extends `CharacterAction_Continuous`). Duration = `MagazineRangedCombatStyleSO.ReloadTime`. OnStart → `magInstance.StartReload()`. OnComplete → `magInstance.FinishReload()`. OnInterrupt → `magInstance.CancelReload()` (see §2 API table + §11 interrupt row). No owner gate. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs` | new | Continuous action, ~0.5s. OnComplete → server-side `CharacterEquipment.SwapToNextWeapon()` (new method, see below). No owner gate. |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | edit | Adds `ActiveWeaponIndex` + `SwapToNextWeapon()` server-only method. Adds the two NetworkVariables for ammo + reload sync. Delegates "list of weapons" to `Inventory.GetWeaponInstances()`. See §2 Concrete-API table + §4.1 Carried-weapons data model. |
| `Assets/Scripts/Item/Inventory.cs` (or wherever `Inventory` lives) | edit | Adds `GetConsumables()` + `GetWeaponInstances()` helpers (refactored from the inline filter at `CharacterEquipment.cs:500-509`). Used by `UI_CombatItemsWindow` + the Swap cluster. |
| `Assets/Resources/Data/Item/ConsumableSO.cs` | edit | Add `[SerializeField] private bool _isUsableInCombat = true;` + `public bool IsUsableInCombat => _isUsableInCombat;`. |
| `Assets/Resources/Data/Item/FoodSO.cs` | edit | Override `IsUsableInCombat => false` (FoodSO already extends from the consumable chain — verify inheritance path). |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | edit | Add in-battle hotkey block: Space / R / Y / 1–6 / E read inside `Update()` gated by `IsOwner && _character.CharacterCombat.IsInBattle`. Routes to existing combat action queueing surface. |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | edit | Add `event Action<float> OnInitiativeChanged` if not present (drives `UI_CombatInitiativeBar`). Add helper `TryQueueReload()` (validates active weapon is magazine type + not already reloading, queues `CharacterAction_Reload`). Add helper `TryQueueSwapWeapon()`. Add helper `TryQueueUseItem(ConsumableInstance, Character target)`. |

### 4.1 Carried-weapons data model

Today's `CharacterEquipment` + `Inventory` does not have a "secondary weapon slot" or an explicit "active vs holstered" split. Weapons live as `WeaponInstance`s inside `Inventory.ItemSlots` (filtered for `WeaponSlot && !IsEmpty()` — see `CharacterEquipment.cs:500-509` for the existing inline filter). The "active" weapon is the one currently held in hand and reflected by `CombatStyleExpertise`; everything else in a `WeaponSlot` is "carried but not active" (visible on the bag visual via `UpdateWeaponVisualOnBag`).

**Design choice (no new slot system):** Swap reuses the existing equip path entirely. Concretely:

```
SwapToNextWeapon (server)
  → carriedList = inventory.GetWeaponInstances()   // ordered list of WeaponInstances
  → if (carriedList.Count < 2) return
  → activeIdx = (ActiveWeaponIndex + 1) % carriedList.Count
  → unequip current active   (existing CharacterEquipment.UnequipWeapon path)
  → equip carriedList[activeIdx]   (existing CharacterEquipment.EquipWeapon path)
  → ActiveWeaponIndex = activeIdx
  → re-evaluate _activeAmmoNet sentinel (§5)
  → fires existing OnEquipChanged event (verify replicates — §2 verification note)
```

Cycle order = `Inventory.ItemSlots` order (the same order `UpdateWeaponVisualOnBag` already uses). No new "primary/secondary" semantics — if a player carries three weapons, Swap cycles through all three (matches decision #6: tap Y cycles, radial picker deferred to v2).

**`ActiveWeaponIndex` is server-authoritative.** It can be derived on the client from "which `WeaponInstance` matches the currently-held one" but adding the explicit index avoids reconstruction work on every UI refresh and gives the swap action a stable rotation cursor across save/load.

### Cluster ordering (left → right)

```
┌─ weapon ───────────────┬─ abilities ──────────────┬─ utility ────┐
│ [Attack] [Reload?]    │ [1][2][3][4][5][6]      │ [Swap] [Items▾] │
└────────────────────────┴──────────────────────────┴─────────────────┘
```

Visual separators (1px vertical line) divide the three clusters. `Reload` slot only renders when active weapon is `MagazineWeaponInstance`. `Swap` is always visible; greyed when `GetCarriedWeapons().Count < 2`.

### Data flow (Attack queued, then fires)

```
[Player presses Space or clicks Attack button]
  → PlayerController.Update reads Space, gated on IsOwner && IsInBattle
  → CharacterCombat.SetActionIntent(() => Attack(PlannedTarget), target)
     (existing path — unchanged)
  → fires OnActionIntentDecided
     → UI_CombatQueuedLabel paints "▶ Queued: <Melee Attack> → <target name>"
     → Attack button gets `.queued` style (existing blue-glow path)

[Initiative bar fills via existing CharacterCombat.UpdateInitiativeTick]
  → CharacterStats.Initiative cresses threshold → OnInitiativeFull fires
  → existing CombatAILogic / PlannedAction.Invoke()
  → Attack closure runs → CharacterCombat.Attack(target) → existing damage path
  → CharacterCombat.ConsumeInitiative()
  → UI_CombatQueuedLabel.Hide() on PlannedAction == null
```

### Data flow (Reload)

```
[Player presses R or clicks Reload]
  → PlayerController routes to CharacterCombat.TryQueueReload()
  → validates: WeaponInstance is MagazineWeaponInstance && !IsReloading && CurrentAmmo < MagazineSize
  → enqueues CharacterAction_Reload via CharacterActions.ExecuteAction
  → [SERVER] action.OnStart → magInstance.StartReload() → _isReloadingNet = true
     → ClientRpc fans out via NetworkVariable
     → UI repaints: Attack disabled, Reload slot shows ↻ <timer>s + fill bar
  → [SERVER] action ticks down for ReloadTime seconds
  → [SERVER] action.OnComplete → magInstance.FinishReload()
     → _activeAmmoNet = MagazineSize; _isReloadingNet = false
     → UI repaints: Attack re-enabled with full ammo
```

### Data flow (Swap weapon)

```
[Player presses Y or clicks Swap]
  → PlayerController routes to CharacterCombat.TryQueueSwapWeapon()
  → validates: GetCarriedWeapons().Count >= 2 && no swap already in flight
  → enqueues CharacterAction_SwapWeapon
  → [SERVER] action ticks ~0.5s (anti-spam) — character visibly stows / unsheathes
  → [SERVER] action.OnComplete → CharacterEquipment.SwapToNextWeapon()
     → activeIndex = (activeIndex + 1) % carriedWeapons.Count
     → triggers existing equip-changed event + CombatStyleExpertise re-select
     → _activeAmmoNet syncs from the new active weapon's MagazineWeaponInstance (or 0 if not magazine)
  → UI re-evaluates cluster: which verbs render (Reload appears/disappears, Attack icon flips)
```

### Data flow (Use item)

```
[Player clicks Items button or presses E]
  → PlayerController routes to PlayerUI.Instance.OpenCombatItemsWindow(character)
  → UI_CombatItemsWindow.Initialize(character)
     → reads character.CharacterEquipment.GetInventory().GetConsumables()   // new helper, §2
     → builds rows: enabled if cs.Data is ConsumableSO so && so.IsUsableInCombat, disabled otherwise
     → first 9 enabled rows get hotkey badges 1–9

[Player clicks Smoke Bomb row OR presses 2 inside window]
  → UI_CombatItemRow.OnUseClicked
  → target resolution: self-target items use character; throw items use CharacterCombat.PlannedTarget
  → CharacterCombat.TryQueueUseItem(consumableInstance, target)
  → window closes (auto-close on use)
  → behavior identical to Attack/Ability queue: fires when initiative full
  → on fire: ConsumableSO._destroyOnUse path consumes inventory entry
```

## 5. Network sync of weapon state (rule #19b prep)

Today `MagazineWeaponInstance._currentAmmo` / `_magazineSize` / `_isReloading` are `[SerializeField]` POCO fields on the `WeaponInstance`. The `WeaponInstance` itself is held inside `CharacterEquipment` — but the per-instance fields are not replicated.

**Channel chosen:** add two `NetworkVariable`s on `CharacterEquipment`:

```csharp
private readonly NetworkVariable<int> _activeAmmoNet =
    new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
private readonly NetworkVariable<bool> _isReloadingNet =
    new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

`-1` sentinel = active weapon is not a magazine type. Otherwise `_activeAmmoNet` mirrors `((MagazineWeaponInstance)activeWeapon).CurrentAmmo`. Server writes:

- On equip-change → re-evaluate sentinel.
- On Attack consume (server-side combat path) → `_activeAmmoNet--`.
- On `StartReload` → `_isReloadingNet = true`.
- On `FinishReload` → `_isReloadingNet = false; _activeAmmoNet = MagazineSize`.
- On Swap → reset both based on new active weapon.

Why not lift `WeaponInstance` to a fully replicated POCO? Because `WeaponInstance` is also saved via `ICharacterSaveData<T>` for inventory persistence, and the `Sharpness` / `ChargeProgress` / `MaxDurability` fields don't need per-frame replication. Surgical NetworkVariables on the *active* slot only is enough for the UI and combat math; non-active carried weapons replicate via the inventory snapshot path (next equip-change re-syncs).

**Trade-off acknowledged:** swap-to-pistol shows ammo correctly because Swap re-runs the sentinel evaluation. Carried (non-active) pistol's ammo is not visible while sheathed — fine, the UI never shows it (decision #7).

## 6. UI_CombatItemsWindow — `UI_WindowBase` variant per rule #39

Authoring follows the canonical recipe in [.agent/skills/ui-hud/SKILL.md](../../.agent/skills/ui-hud/SKILL.md):

| Concern | Choice |
|---|---|
| Prefab variant base | `Assets/UI/Player HUD/UI_WindowBase.prefab` |
| Asset path | `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` |
| Backing script | `UI_CombatItemsWindow : UI_WindowBase` |
| Canvas renderMode | `ScreenSpaceCamera` (rule #39 — inherited from base, never overridden) |
| Sort order | Above the action bar (the bar is `sortingOrder: 50`; this window is `sortingOrder: 60`) |
| Anchor | Right edge of screen, vertically offset to sit above the Items button (~74 px) |
| Size | 280 × ~280 (auto-fit on row count, but `Content` has `ContentSizeFitter` vertical only — rule #39 forbids it elsewhere) |
| Close affordance | Inherited `_buttonClose` (top-right) + ESC + second-click toggle on Items button + auto-close on `OnBattleLeft` + auto-close on `_combatItemsWindow == null`-safe `OnDisable` |
| Row prefab | `UI_CombatItemRow.prefab` (leaf — NO close button, per rule #39 litmus) |
| Scroll | `ScrollView/Viewport/Content` with `ContentSizeFitter` (vertical fit only — load-bearing for overflow) |

PlayerUI surface:

```csharp
[SerializeField] private UI_CombatItemsWindow _combatItemsWindow;

public void OpenCombatItemsWindow(Character character)
{
    if (_combatItemsWindow == null)
    {
        Debug.LogWarning("<color=orange>[PlayerUI]</color> OpenCombatItemsWindow called but _combatItemsWindow SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._combatItemsWindow in the Inspector.");
        return;
    }
    _combatItemsWindow.Initialize(character);
    _combatItemsWindow.OpenWindow();
}

public void CloseCombatItemsWindow()
{
    if (_combatItemsWindow == null) return;
    _combatItemsWindow.CloseWindow();
}
```

## 7. Initiative bar + queued label (Option A)

Both are leaf sub-elements parented inside `UI_CombatActionMenu._menuContainer`. They show/hide with the bar (`IsInBattle` toggle).

**UI_CombatInitiativeBar:**
- 200 × 6 px bar, anchored center-top of the bar with a 4 px gap above.
- Background `rgba(0,0,0,0.7)`. Fill gradient orange → yellow (matches existing initiative aesthetic from existing wiki/screenshots).
- Subscribes to `CharacterCombat.OnInitiativeChanged(float pct01)` (new event — fired by `UpdateInitiativeTick`).
- Hidden when not in battle (parent container hides; no per-element gate).

**UI_CombatQueuedLabel:**
- Pill: rounded background `rgba(26,58,107,0.95)`, border `#3a78c8`, text `#cce`, 10 px font.
- Format: `▶ Queued: <icon> <action name> → <target name>`.
- Anchored centered, 3 px above the initiative bar (above-bar stack: queued label / init bar / action row).
- Subscribes to `CharacterCombat.OnActionIntentDecided(target, action)`. Hides when `PlannedAction == null` (subscribe to a new `OnActionIntentCleared` event — add to `CharacterCombat.ClearActionIntent`).
- Action name + icon: a small registry keyed by intent type — Attack ("⚔ Melee Attack" / "🏹 Ranged Attack"), Reload ("↻ Reload"), Swap ("⇄ Swap"), Ability (ability's `AbilitySO.AbilityName + AbilityIcon`), UseItem (consumable's `ItemSO.ItemName + Icon`). Resolution lives on the queued-label script (small switch / type-dispatch).

## 8. Hotkey map (PlayerController routing per rule #33)

All combat hotkeys read inside `PlayerController.Update()` gated by `IsOwner && _character.CharacterCombat.IsInBattle`. Block lives in a new helper method `HandleCombatHotkeys()` called inside `Update`.

**Coexistence with the existing `PlayerController` E + Space dispatchers:**

- **E** today routes through `HandleEKeyDown` (`PlayerController.cs:335`) — a 5-priority chain (placement-active item → consumable in hand → interactable intent → consumable execute → fall-through to hold-menu). The new in-battle E binding **preempts** this dispatcher: at the very top of `HandleEKeyDown`, add `if (_character.CharacterCombat.IsInBattle) { PlayerUI.Instance.ToggleCombatItemsWindow(_character); _eMenuOpened = true; return; }`. Out of battle, the existing 5-priority chain is unchanged. The "consumable in hand → eat" branch is intentionally unreachable in combat — combat consumable use routes exclusively through the Items window (which queues `TryQueueUseItem` for initiative-paced firing instead of the immediate `CharacterUseConsumableAction` the field-eat path uses).
- **Space** today is gated on `!_character.CharacterCombat.IsInBattle` (`PlayerController.cs:295`) — out-of-battle Space directly calls `CharacterCombat.Attack(null)`. The new in-battle binding adds the inverse branch (an `else` against the same gate): in-battle Space queues an attack via `SetActionIntent` instead of firing immediately. **Existing out-of-battle Space behavior is preserved** — this design adds the missing in-battle case.
- **1-6 + Y + R** are new; no existing conflict.
- Inside the Items window: `PlayerController` skips its global 1-6 binding when `PlayerUI.Instance.IsCombatItemsWindowOpen` is true. The window owns 1-9 hotkeys for row selection while open.

| Key | Action | Route |
|---|---|---|
| `Space` | Attack (active weapon's verb) | `_character.CharacterCombat.SetActionIntent(() => Attack(PlannedTarget), PlannedTarget ?? bm.GetBestTargetFor(_character))` — mirror existing `OnAttackClicked` |
| `R` | Reload | `_character.CharacterCombat.TryQueueReload()` |
| `Y` | Swap weapon | `_character.CharacterCombat.TryQueueSwapWeapon()` |
| `1`–`6` | Activate ability slot N | `_character.CharacterAbilities.TryUseSlot(n, PlannedTarget)` (canonical name locked in §2 API table) |
| `E` | Toggle Items window | `if (window.IsOpen) PlayerUI.Instance.CloseCombatItemsWindow() else PlayerUI.Instance.OpenCombatItemsWindow(_character)` |
| `1`–`9` *(window-scoped)* | Use item row N | Listened by `UI_CombatItemsWindow.Update` when its `IsOpen` is true. `PlayerController` skips its global 1–6 binding when the items window is open (gate: `!PlayerUI.Instance.IsCombatItemsWindowOpen`). |
| `Esc` | Close items window (if open), then cancel queued action (if any) | Listened in `UI_CombatItemsWindow.Update` when open; falls through to `CharacterCombat.ClearActionIntent()` otherwise |

UI button onClick handlers call the **same** `CharacterCombat` / `CharacterAbilities` / `PlayerUI` methods — no parallel input paths, no duplicated input handling.

## 9. Late-joiner audit (rule #19b)

The mandatory six-question audit:

1. **Who writes / who reads.**
   - Writers: server (combat path consumes ammo, `CharacterAction_Reload` flips `_isReloadingNet`, `CharacterAction_SwapWeapon` updates equipped weapon + re-evaluates sentinel).
   - Readers: every client (action bar reads `_activeAmmoNet` + `_isReloadingNet` for Attack/Reload button states).

2. **Replication channel.**
   - Ammo + reload state: NEW `NetworkVariable<int> _activeAmmoNet` + `NetworkVariable<bool> _isReloadingNet` on `CharacterEquipment`. Server-write / everyone-read.
   - Active weapon equip change: existing `CharacterEquipment` equip-change event (verify replication path; if equip change is host-only state today, audit and fix as part of this work).
   - Carried weapons list: existing inventory replication path. Snapshot fans out on equip-change, not per-frame.
   - Initiative: existing `CharacterStats.Initiative` (audit — confirm this is replicated; if not, the player's own UI works because the player owns their character, but other players see no initiative bar for remote characters — acceptable since Option A is player-only).
   - Planned action: existing `CharacterCombat.PlannedAction` is a `Func<bool>` closure (not network-replicable). The queued label is **owner-local only** — driven by the owner's local `OnActionIntentDecided` event. Other players don't see a remote player's queued action. Acceptable per Option A scope.

3. **Late-joiner repro (mandatory before claiming done).**
   - **Repro steps**: host the session, equip a pistol on host's character, fire 3 of 6 rounds via Attack, join a fresh second client, second client looks at host's character. **Note**: second client only sees host's ammo if a UI surfaces it for remote characters — under Option A scope that's not built. The repro for *the host's own* late-joiner state: host saves, closes, re-launches as host, expect ammo = 3/6 (persistence path, not network — covered by inventory save).
   - **More directly relevant**: client connects, equips their own pistol, fires 2 rounds, host's view of client should not crash. Server-side state is correct; nothing remote-visible breaks.
   - **Swap repro**: host carries sword + pistol, swaps to pistol, joins fresh client, fresh client should see host's equipped visual = pistol (existing equip-replication path — verify before this spec ships).

4. **Client-side pre-gate.**
   - Action bar reads `_activeAmmoNet.Value` + `_isReloadingNet.Value` for Attack/Reload button state. Matches authoritative state.
   - `TryQueueReload` client-side pre-gate (button disable state) reads the same NetworkVariables. Server re-validates inside `CharacterAction_Reload.OnStart`.

5. **`GetComponentInParent` in `Awake` (spawn-race risk).**
   - `UI_CombatActionMenu` resolves `Character` via existing `Initialize(Character)` call from `PlayerController`. No `GetComponentInParent`.
   - `UI_CombatItemsWindow` similar — `Initialize(Character)` passed by PlayerUI.
   - `UI_CombatAbilitySlot` / `UI_CombatInitiativeBar` / `UI_CombatQueuedLabel` all receive their `Character` reference from `UI_CombatActionMenu.Initialize`. No spawn-race risk.
   - The two new `CharacterAction` classes have no `GetComponentInParent` dependencies.

6. **Proximity gate (rule #36).**
   - Combat action bar does not gate on proximity — combat already established by `IsInBattle` (the BattleManager's geofence).
   - Items window also does not gate on proximity — combat is the only gate. Closes on `OnBattleLeft`.
   - No raw `Vector3.Distance` calls introduced.

**Replication channel chosen:** new `NetworkVariable<int> _activeAmmoNet` + `NetworkVariable<bool> _isReloadingNet` on `CharacterEquipment`. Late-joiner verified for host's own state via inventory persistence; remote-character UI is out of scope per Option A.

## 10. NPC parity (rule #22)

The four new `CharacterAction` surfaces are NPC-callable:

- `CharacterAction_Reload` — a future combat AI that reloads when low on ammo calls `npc.CharacterActions.ExecuteAction(new CharacterAction_Reload(magInstance))`. Same code path the player UI invokes.
- `CharacterAction_SwapWeapon` — an NPC switching to a melee weapon for close-range engagement calls the same action.
- `CharacterAction_UseItem` (likely already exists for non-combat consumable use — verify; if not, add): a combat AI healing itself with a Health Potion calls it.

No additional player-only logic in the action classes themselves.

## 11. Error handling / edge cases

| Failure | Server behavior | Client UX |
|---|---|---|
| `TryQueueReload` called with active weapon not a magazine | Action factory returns null; no action queued | Hotkey/button no-op silently |
| `TryQueueReload` called while already reloading | Same | Same |
| `TryQueueReload` called at full ammo | Same — but if triggered by "auto-queue on empty Attack" path (decision #4), shouldn't fire — auto-queue is gated on `CurrentAmmo == 0` |
| Reload action interrupted (knockback / death / forced action) | `CharacterAction_Reload.OnInterrupt` calls `magInstance.CancelReload()` (new method — resets `_isReloading = false` without setting ammo) | `_isReloadingNet = false`, ammo stays at pre-reload value, UI re-enables Attack with original ammo |
| Swap action interrupted | `CharacterAction_SwapWeapon.OnInterrupt` no-ops (no partial state); player still has active weapon, retry possible | UI doesn't flip Swap state |
| Items window open when combat ends | `UI_CombatItemsWindow` subscribes to `OnBattleLeft`; auto-closes | Window snaps shut, no error |
| Items window open when active weapon swapped | Window stays open (orthogonal); rows re-evaluate filter only when item list changes | Acceptable |
| Player clicks ability slot N with cooldown active | `UseSlot` returns false; UI shows shake animation + transient "On cooldown 2.4s" tooltip | Same shape as today |
| Player presses 1–6 while items window open | Items window's row N selected; global ability binding skipped via `IsCombatItemsWindowOpen` gate | Discoverable: window has hotkey badges |
| Player presses E to open items, then second E to close | `_combatItemsWindow.IsOpen` toggle; second E calls `CloseCombatItemsWindow` | Toggle works as expected |
| Player uses item with no target needed (self-Potion) | `TryQueueUseItem(instance, target: _character)` — self-target | Queued label shows "Health Potion → self" |
| Player uses thrown item with `PlannedTarget == null` | `TryQueueUseItem` returns false; UI shows "Pick a target first" toast | Same shape as Attack-with-no-target |

All `Debug.Log` calls in hot paths (Update-frequency, BT-tick-frequency) gated behind `if (NPCDebug.VerboseActions)` per rule #34. RPC validation logs gated behind `if (Debug.isDebugBuild)`.

## 12. Open questions / risks

### Blocking-for-planning (resolve before plan-phase)

- **Equip-change replication audit.** `CharacterEquipment` exposes `EquipWeapon` / `UnequipWeapon` (private surfaces called from inventory-side flows). Need to confirm clients are notified when the host swaps weapons (active `CombatStyleExpertise` + visual weapon visible on the swapping character to remote viewers). If equip-change is host-local-state-only today, the plan must include surgical NetworkVariables on `CharacterEquipment.ActiveWeaponIndex` (or equivalent) — otherwise Swap works for the swapping player but the remote view of a host swapping mid-battle is stale. **Scope estimate**: ~1-2 hours to audit + decide; small or no-op fix if already replicated.

- **`CharacterUseConsumableAction` shape.** Used today at `PlayerController.cs:379` for the out-of-battle E-eat path. Verify whether it's already initiative-aware / queueable, or whether it executes immediately. For combat use we need the queue-then-fire shape (matches Attack / Ability). Two paths: (a) reuse the existing action with an `enqueue: true` flag, or (b) add a thin `CharacterAction_UseCombatItem` wrapping the existing fire-now logic. Decision deferred to planning.

### Deferrable (resolve during implementation or after v1)

- **Auto-queue Reload on empty Attack (decision #4) — failure mode for future spare-ammo system.** Today `MagazineWeaponInstance.FinishReload` always refills to `MagazineSize` (effectively infinite spare ammo). When a real spare-ammo / pouch system ships, the auto-queue logic must check spare availability before firing. Out of scope here; tracked as "auto-queue Reload assumes refill-to-max".

- **Ranged-melee gameplay cleanup (post-UI).** User flagged 2026-05-17. Tracked in §13 as out-of-band follow-up. UI design is consistent with the post-cleanup state regardless.

- **Items window animation on combat end.** Today the window snaps shut via `OnBattleLeft`. A short fade would feel softer but is pure polish — defer.

- **Future ally action queueing surface.** Option C chrome (party panel) was rejected; if it returns later, the queued-label component generalizes to per-row state. No spec change needed today.

## 13. Out-of-band follow-up (user directive 2026-05-17)

After this spec ships:

> **Ranged weapons should only do ranged attacks.** Remove melee-attack damage from ranged weapons in `WeaponSO` and related code paths. Update `wiki/systems/combat.md`, `wiki/systems/items.md` (or wherever), and `.agent/skills/combat_system/SKILL.md` to reflect "ranged weapon = ranged only; for melee, swap to a melee weapon."

This work is **out of scope for this spec** but design choices here (active-weapon-only UI, no secondary attack button, no `MeleeBash` backend routing) are forward-compatible with that cleanup.

## 14. Testing matrix (multiplayer mandatory)

| Scenario | Expected |
|---|---|
| Sword equipped, click Attack | Existing Melee Attack flow; queued label appears; fires on initiative full. |
| Pistol equipped, fire 3 shots | Ammo readout updates 6→5→4→3 via `_activeAmmoNet` replication. |
| Pistol empty, click Attack | Attack greyed; auto-queue Reload triggers; toast "Click again to confirm" appears. |
| Pistol empty, click Attack twice | First click toasts; second click within 2s queues Reload. |
| Reload completes | `_isReloadingNet` true → false; ammo restores to MagazineSize; Attack re-enables. |
| Reload interrupted by knockback | `_isReloadingNet = false`, ammo unchanged, Attack re-enables at pre-reload ammo. |
| Bow equipped, hold Attack to charge | Sub-bar fills inside Attack button via `ChargingWeaponInstance.ChargeProgress`; button text flips to "Ready" on `IsCharged`. |
| Swap with 2 weapons carried | 0.5s swap action; active weapon flips; cluster re-renders (Reload disappears if swapping to sword). |
| Swap with 1 weapon carried | Swap button greyed; tooltip "No other weapon equipped"; hotkey Y no-op. |
| Items window opened in combat | World stays visible (no dim); combat ticks; window appears above-right of Items button. |
| Items window: click Health Potion (self-target) | Window auto-closes; queued label "Health Potion → self"; fires on initiative full; potion consumed. |
| Items window: click Smoke Bomb with no target | Window stays open; row shows "Pick a target first" toast. |
| Items window: click Food row | Row disabled with reason; click does nothing. |
| Items window open + combat ends | Window auto-closes via `OnBattleLeft`. |
| Items window open + ESC | Window closes; if no queued action, ESC also closes ambient menus per existing pattern. |
| Two-client session: client fires their own pistol | Their own ammo readout updates correctly; host's view of client's character correct (existing equip visual + ground circles). |
| Two-client session: host swaps weapons | Client sees host's visual weapon change (verify equip replication); client's "remote character" view doesn't show ammo readout (out of scope per Option A). |
| Hotkey 3 (ability slot) while items window open | Items window's row 3 used; ability slot 3 NOT triggered (gate by window IsOpen). |
| Press Y (swap) mid-Attack-animation | Swap action queued; runs after current attack consumes initiative. |
| Save game with pistol at 3/6 ammo | Load: pistol still at 3/6 via existing `WeaponInstance` serialization. |
| Player leaves combat zone (BattleManager teardown) | Action bar hides via `IsInBattle` flip; queued label hides; init bar hides; Items window closes. |

## 15. Documentation updates (rule #28 / #29 / #29b)

After implementation:

- **`.agent/skills/combat_system/SKILL.md`** — append section on `CharacterAction_Reload` + `CharacterAction_SwapWeapon`. Document hotkey map. Document the new `_activeAmmoNet` / `_isReloadingNet` replication channel.
- **`.agent/skills/ui-hud/SKILL.md`** — append example of `UI_CombatItemsWindow` as a `UI_WindowBase` variant. Document the leaf init-bar + queued-label pattern inside a non-window HUD parent.
- **`wiki/systems/combat.md`** — bump `updated:`, append change log line, add a "Player action bar" subsection under Public API, refresh `depended_on_by` to include the new UI files.
- **`wiki/systems/character-combat.md`** — bump `updated:`, document the new helper methods (`TryQueueReload`, `TryQueueSwapWeapon`, `TryQueueUseItem`) and the new events (`OnInitiativeChanged`, `OnActionIntentCleared`).
- **`wiki/systems/player-hud.md`** — bump `updated:`, add `[[combat-action-bar]]` to `depended_on_by` (or add a new system page if scope warrants).
- **`wiki/systems/items.md`** (if it exists) — note `ConsumableSO.IsUsableInCombat` flag.
- **`.claude/agents/combat-gameplay-architect.md`** — extend description to mention `CharacterAction_Reload` + `CharacterAction_SwapWeapon`.
- **`.claude/agents/ui-hud-specialist.md`** — extend description to mention `UI_CombatItemsWindow` as a canonical variant example + the leaf-inside-HUD pattern for init bar / queued label.

## 16. References

- `Assets/Scripts/UI/UI_CombatActionMenu.cs` (rewrite target)
- `Assets/Scripts/UI/UI_WindowBase.cs`
- `Assets/Scripts/UI/PlayerUI.cs`
- `Assets/UI/Player HUD/UI_WindowBase.prefab`
- `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`
- `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`
- `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`
- `Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs`
- `Assets/Scripts/Item/Equipment/ChargingWeaponInstance.cs`
- `Assets/Resources/Data/Item/WeaponSO.cs`
- `Assets/Resources/Data/Item/ConsumableSO.cs`
- `Assets/Resources/Data/Item/FoodSO.cs`
- `Assets/Resources/Data/CombatStyle/MagazineRangedCombatStyleSO.cs`
- `Assets/Scripts/Item/ConsumableInstance.cs`
- `docs/superpowers/specs/2026-05-16-safe-furniture-deposit-withdraw-ui-design.md` (UI window pattern precedent)
- `docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md` (UI window pattern precedent)
- `CLAUDE.md` rules #18 (NGO authority), #19 / #19b (late-joiner audit), #22 (player↔NPC parity), #26 (Time), #28 / #29 / #29b (docs), #33 (input ownership), #34 (perf), #36 (interaction zone — not directly applied here but mentioned for consistency), #39 (UI HUD prefab architecture)
- Brainstorm mockups: `.superpowers/brainstorm/1092-1779031305/` — `layout-options.html`, `layout-b-weapon-states.html`, `layout-b-v3-dual-attack-swap.html`, `layout-b-v4-active-weapon-only.html`, `items-sub-window.html`, `initiative-chrome.html`
