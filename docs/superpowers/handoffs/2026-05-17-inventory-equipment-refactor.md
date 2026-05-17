# Session bootstrap — Inventory / Equipment interface refactor

> Paste everything below the divider into a fresh Claude Code session at the project root.

---

## Context

I'm working on **My-World-Isekai-Unity** (Unity 6.x, NGO multiplayer, GameObject-based, branch `multiplayyer`). A combat action bar feature just shipped (latest commit `4e44679d`). It works end-to-end but is **gated by the inventory/equipment interface** not having a clean equip-weapon flow the player can actually drive.

This session: **refactor the inventory/equipment interface** so the combat bar's existing backend hooks (active weapon cursor, swap action, ammo replication) become consumable by a real player-facing equip flow.

Read `CLAUDE.md` first — 39 mandatory project rules apply, especially #18/#19/#19b (network), #22 (player↔NPC parity), #33 (input ownership), #28/#29/#29b (docs after every system change).

## Combat-bar context (what's already wired — DO NOT BREAK)

Spec + plan from the just-completed arc:
- `docs/superpowers/specs/2026-05-17-combat-action-bar-design.md`
- `docs/superpowers/plans/2026-05-17-combat-action-bar.md`
- `docs/superpowers/plans/2026-05-17-combat-action-bar-prefab-authoring.md`

Backend hooks the combat bar already consumes (must remain valid after refactor):

| Surface | Role |
|---|---|
| `CharacterEquipment.ActiveWeaponIndex` | Server-authoritative cursor int (`NetworkVariable<int>`). Indexes into `Inventory.GetWeaponInstances()`. |
| `CharacterEquipment.SwapToNextWeapon()` | Server-only. Advances cursor + re-equips. Called by `CharacterAction_SwapWeapon`. |
| `CharacterEquipment.RecomputeActiveWeaponSentinel()` | Server-only. Re-syncs `_activeAmmoNet` + `_isReloadingNet` NetworkVariables. Called after every equip change, ammo consume, reload state flip, swap. |
| `CharacterEquipment.ActiveAmmo` + `IsActiveReloading` | Replicated NetVar reads consumed by `UI_CombatActionMenu`. |
| `Inventory.GetWeaponInstances()` (returns `IReadOnlyList<WeaponInstance>`) | Drives the Swap cycle order + ammo lookup. |
| `Inventory.GetConsumables()` (returns `IEnumerable<ConsumableInstance>`) | Drives the Items sub-window. |
| `CharacterCombat.CurrentCombatStyleExpertise.Style` | Picks melee / ranged path in `Attack()`. **Must stay accurate after every equip change** — currently set externally; the refactor must keep this seam wired. |
| `CharacterRangedAttackAction.SpawnProjectile` | Calls `RecomputeActiveWeaponSentinel()` after consuming ammo. |

If your refactor renames / restructures any of these, you MUST update the callers in `Assets/Scripts/UI/UI_CombatActionMenu.cs`, `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs`, `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs`, and `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` to match.

## Pain points to fix (refactor scope)

1. **Bag-dependency.** `Inventory` only exists when a bag is equipped (`CharacterEquipment.HaveInventory()` gate). A character without a bag has no `Inventory` → `GetWeaponInstances()` returns null → `SwapToNextWeapon` silently no-ops. Decide: do every character need a baseline weapon-slot set even without a bag? Probably yes — most RPG mental models assume a player can always carry at least one weapon in-hand without needing a bag.

2. **Single `_weapon` field vs N carried weapons.** `CharacterEquipment._weapon` is one field representing "the active equipped weapon"; `Inventory.GetWeaponInstances()` returns N filtered from `ItemSlots`. The cursor (`ActiveWeaponIndex`) maps from N → 1. Refactor decision:
   - (a) Keep one "active" slot + treat all other carried weapons as "stored" — needs a clean enumeration order for Swap.
   - (b) Introduce an explicit "weapon belt" with named slots (`Primary`, `Secondary`, `Sidearm`).
   - (c) `LoadoutSO` data asset that defines slot schema per archetype.

3. **No player-facing equip flow.** Player picks up a weapon (`WorldItem` → `CharacterEquipment.Equip`?), but there's no UI for "this is my active weapon vs my backup, swap with Y." The combat bar's Swap button is wired to backend that has nothing to swap because the player has no way to put two weapons in carry.

4. **`EquipWeapon` is private.** No public surface NPCs / cinematics / orders can call. Rule #22 (player ↔ NPC parity) wants this routed through a `CharacterAction` or at least a public method that's NPC-callable.

5. **`UnequipWeapon` drops to world.** No "stow" / "holster" semantics — Unequip always spawns a `WorldItem`. Refactor must add a way to move a weapon from active slot to storage slot **without dropping**. The existing `SwapToNextWeapon()` already inlines this (it skips `UnequipWeapon` to avoid the drop) — the same pattern can be generalized.

6. **NetworkVariable coverage.** Only active-weapon ammo + reload state are replicated today. The carried-weapons list (which weapons + per-instance state like Sharpness / ChargeProgress) needs late-joiner correctness per rule #19b. Audit + extend if needed.

7. **`WeaponSO._maxSharpness` is "Melee Specifics" but on every WeaponSO** — dead data on ranged-WeaponSO assets. Field structurally belongs on `MeleeWeaponSO` (which doesn't exist as a separate type today). Possible refactor: split `WeaponSO` → `MeleeWeaponSO : WeaponSO` + `RangedWeaponSO : WeaponSO`. **Optional** — flag as a stretch goal, not blocking.

## Explicitly OUT OF SCOPE

- **Visual bag anchor + weapon visual mounting.** `_bagScript.GetAllWeaponAnchors`, `UpdateWeaponVisualOnBag`, hand-bone mount points, weapon-prefab instantiation onto the character. The user is replacing the character visual stack with **Spine 2D** in a separate effort — whatever weapon-visual hooks exist today are temporary placeholders. **DO NOT TOUCH** anything visual. Keep the logical equip flow clean; visual hooks land later via the Spine integration.

## Suggested workflow

1. **Spawn the `item-inventory-specialist` agent** — that's the deep-dive specialist for this domain. Its `tools` permit Read/Edit/Write/Glob/Grep/Bash. Domain reads: `Assets/Scripts/Inventory/`, `Assets/Scripts/Item/`, `Assets/Scripts/Item/Equipment/`, `Assets/Scripts/Character/CharacterEquipment/`, `Assets/Resources/Data/Item/`.

2. **Brainstorm the interface shape first.** Use the `superpowers:brainstorming` skill. Key questions for Kevin during brainstorm:
   - "Belt slots" model (Primary/Secondary/Sidearm named) vs free-form N-carried list?
   - How does equipping work as gameplay action — drag from inventory? Hold-E on a `WorldItem`? Both?
   - Does every character carry weapons WITHOUT a bag? Bag becomes a stash-expander but not a prerequisite?
   - Public equip API on `CharacterEquipment` vs a `CharacterAction_EquipWeapon` (rule #22)?

3. **Spec → plan → execute** via `superpowers:writing-plans` then `superpowers:subagent-driven-development` (or the `gsd-*` equivalents — Kevin's used both flows in this project).

4. **When you ship, return to the combat-bar session** with results from this matrix:

| Equip state to test | Expected combat-bar behavior |
|---|---|
| Sword only carried | `Melee Attack` button visible, Swap greyed, no Reload, no AmmoBadge |
| Sword + Bow carried (bow active) | `Ranged Attack` button, Swap enabled showing `Mle/Rng`, no Reload |
| Sword + Pistol (pistol active, 6 ammo) | `Ranged Attack` + AmmoBadge `6/6` + `Reload` button visible |
| Fire 3 shots from pistol | AmmoBadge → `3/6`; bar updates within 1 frame on owner + remote clients (rule #19b) |
| Pistol empty (`0/6`) | Attack button greyed; Reload button pulses red |
| Press R (or click Reload) | 2s reload → AmmoBadge restores to `6/6` |
| Press Y (or click Swap) | 0.5s swap action → cluster re-renders for new active weapon |
| Press E in battle | Items popover opens above-right of bar |
| Click Attack with ranged equipped at any distance | Always fires ranged projectile, NEVER melee swing (post-`4e44679d`) |
| Multiplayer host fires 2 shots, client joins late | Client opens spawn → sees host's correct `4/6` ammo state (late-joiner repro per rule #19b) |

## Minimum viable refactor (success criteria)

1. Player can pick up a weapon → lands in their carry (with OR without a bag).
2. Player can equip a weapon as active → combat bar's `Attack` button reflects it (label + ammo badge + reload visibility).
3. Player can carry 2+ weapons → combat bar's `Swap` button works end-to-end.
4. NPCs can do the same flow via `CharacterAction` (rule #22).
5. Multiplayer late-joiner sees correct equipped state for every character (rule #19b — late-joiner repro is mandatory).
6. The `MaxSharpness` cleanup (split `WeaponSO`) is OPTIONAL — flag as stretch goal.
7. All 8 wiki/SKILL/agent docs are updated per rules #28 / #29 / #29b.

## Key files (read-only inventory of the touch surface)

```
Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs    ~700 lines, NetworkBehaviour, owner of refactor
Assets/Scripts/Inventory/Inventory.cs                                 ~300 lines, the bag-attached inventory class
Assets/Scripts/Inventory/ItemSlot.cs                                  slot abstraction
Assets/Scripts/Inventory/WeaponSlot.cs                                weapon-typed slot
Assets/Scripts/Item/ItemInstance.cs                                   base for runtime item state
Assets/Scripts/Item/Equipment/WeaponInstance.cs                       base for weapons
Assets/Scripts/Item/Equipment/MeleeWeaponInstance.cs                  + Sharpness
Assets/Scripts/Item/Equipment/RangedWeaponInstance.cs                 abstract
Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs               + CurrentAmmo/MagazineSize/IsReloading
Assets/Scripts/Item/Equipment/ChargingWeaponInstance.cs               + ChargeProgress/IsCharged
Assets/Scripts/Item/Equipment/BagInstance.cs                          bag (out-of-scope per user)
Assets/Resources/Data/Item/WeaponSO.cs                                weapon SO (with the _maxSharpness on every weapon)
Assets/Resources/Data/Item/ConsumableSO.cs                            now has IsUsableInCombat (added during combat-bar)
Assets/Scripts/UI/CharacterEquipmentUI.cs                             existing equipment UI (audit + likely modify)
Assets/Scripts/World/Item/WorldItem.cs                                pickup flow target
```

## Don't forget

- Run the late-joiner repro for EVERY new replicated field (rule #19b — six-question audit in `wiki/gotchas/host-only-state-blindspot.md`).
- Wire all SerializeField scene references via `SerializedObject` not reflection — see `wiki/gotchas/reflection-vs-serializedobject-persistence.md`.
- Audit MCP-authored TMP strings for unsupported glyphs — see `wiki/gotchas/tmp-font-glyph-fallback.md`.
- Update `.agent/skills/`, `wiki/systems/`, and `.claude/agents/` for the touched systems.

When you return, ping the combat-bar session with the validation matrix above. Good luck.
