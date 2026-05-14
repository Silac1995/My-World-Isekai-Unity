# Furniture Occupancy via CharacterAction — Design

**Date:** 2026-05-14
**Branch:** `claude/quirky-swirles-fd0ed4` off `multiplayyer` @ `60d6c269`
**Status:** Approved
**Driving principle (user):** Uniformise character actions — switching player↔NPC must be a no-op for the seating state.

## Goal

Replace `Cashier.ServerTickAutoOccupy` (proximity-driven server tick — architecturally wrong; bypasses the action system) with a `CharacterAction_OccupyFurniture` pipeline. Same action for player and NPC. Generalises to every `OccupiableFurniture` that doesn't already have a domain-specific action wrapping `Use` (Cashier, Chair, future).

## Non-goals

- Rename `Use` / `Release` (Kevin: keep).
- Change `CashierNetSync` replication channel (already correct).
- Touch `BedFurniture` sleep path (`CharacterAction_SleepOnFurniture` already wraps Use internally).
- Touch `CraftingStation` craft path (`CharacterCraftAction` already wraps Use internally).
- Storage / TimeClock / Management furniture (not Occupiable).

## Architecture

### 1. `Character.OccupyingFurniture` becomes server-replicated

**Problem.** Spec literal: *"`OccupyingFurniture != null` ⇒ no movement"*. Today `OccupyingFurniture` is a plain auto-property mutated only inside server-side `OccupiableFurniture.Use`. Remote-client peers of a seated player read `null` and the gate breaks (rule #19b: host-only state blindspot).

**Fix.** Add `NetworkVariable<ulong> _occupyingFurnitureNetId` on `Character` (server-write only). Server's `SetOccupyingFurniture` writes both the in-memory field and the NetVar. The `OccupyingFurniture` getter:

- On the server (or unspawned): returns the in-memory field (cheap, authoritative).
- On clients: resolves the NetworkObject via `NetworkManager.Singleton.SpawnManager.SpawnedObjects[id].GetComponentInChildren<Furniture>()`, returns the matching Furniture or null.

`OnOccupyingFurnitureChanged` event fires on clients via the NetVar `OnValueChanged` hook (resolve prev/current the same way) so `CharacterSystem.HandleOccupyingFurnitureChanged` stays accurate cross-peer.

Mirrors two existing precedents:
- `Cashier.Occupant` override (Cashier.cs:60-67) — resolves via `CashierNetSync.OccupantNetworkObjectId`.
- `Character.IsSleeping` (Character.cs:348) — `NetworkVariable<bool> NetworkIsSleeping`.

### 2. `CharacterAction_OccupyFurniture : CharacterAction_Continuous`

Single new action file. Server-only execution (continuous actions are rejected on clients at `CharacterActions.cs:73`).

- **Constructor:** `(Character, OccupiableFurniture target)`.
- **CanExecute:** `target != null && target.GetComponent<InteractableObject>().IsCharacterInInteractionZone(character) && (!target.IsOccupied || target.Occupant == character)`.
- **OnStart:** `_seatingFailed = !target.Use(character);`. If failed, OnTick returns true on first call.
- **OnTick (1 Hz):** if `_seatingFailed` or `target == null` or `target.Occupant != character` → call `target?.Leave(character)` defensively and return `true`. Else return `false`.
- **OnCancel:** server-side `target?.Leave(character)` (idempotent — `Leave` returns false if not occupant).
- **Replication knobs:** `IsReplicatedInternally = false` (use the standard 600s-sentinel visual proxy); `ShouldPlayGenericActionAnimation = false` (the character idles at the StandingPoint — no `isDoingAction` trigger).
- **ActionName:** `"OccupyFurniture"` (generic; HUD label only).

### 3. Two ServerRpcs on `CharacterActions` (player paths)

Mirror `RequestSleepOnFurnitureServerRpc` (CharacterActions.cs:480) — same validation shape.

- `RequestOccupyFurnitureServerRpc(NetworkBehaviourReference furnitureRef)` — resolves the furniture, casts to `OccupiableFurniture`, validates proximity via `IsCharacterInInteractionZone`, then `ExecuteAction(new CharacterAction_OccupyFurniture(_character, target))`.
- `RequestLeaveOccupiedFurnitureServerRpc()` — if `_character.OccupyingFurniture != null`, calls `ClearCurrentAction()`.

### 4. Interaction routing

**`OccupiableFurniture.OnInteract` (default tap-E path).** Currently calls `Use(interactor)` directly. Replace with:
- Server (NPC path): `interactor.CharacterActions.ExecuteAction(new CharacterAction_OccupyFurniture(interactor, this))`.
- Client (player path): `interactor.CharacterActions.RequestOccupyFurnitureServerRpc(new NetworkBehaviourReference(this))`.

`ChairFurniture` and any future Occupiable inherit this. **`BedFurniture` and `CraftingStation` are not affected** — they override `OnInteract` and route through their own actions.

**`CashierInteractable.Interact` (Cashier's bespoke E-press override).** Replace the current "always RequestStartBuyServerRpc" with three-branch routing:
1. `_cashier.Occupant == interactor` → request Leave.
2. `interactor.CharacterJob?.CurrentJob is JobVendor jv && jv.Workplace == _cashier.LinkedBuilding` → request Occupy.
3. Else → existing buy/shop path (unchanged).

Branch 1 uses `_cashier.Occupant`, which is already client-resolved via CashierNetSync. Works on every peer.

### 5. `JobVendor` (NPC parity)

`Execute()`:
1. Already seated (`_heldCashier.Occupant == _worker`) → return.
2. Lost the seat → release reservation, null `_heldCashier`, fall through.
3. Have reservation + in interaction zone + `CharacterActions.CurrentAction == null` → server-side `ExecuteAction(new CharacterAction_OccupyFurniture(_worker, _heldCashier))`. Set `_isMovingToCashier = false`.
4. No reservation → existing reserve+move flow.

`Unassign()`:
- If `_worker.OccupyingFurniture == _heldCashier` → `_worker.CharacterActions.ClearCurrentAction()` (releases via OnCancel).
- Existing reservation cleanup stays.

Schedule-out-of-Work hook: wire from the existing `WorkerEndingShift` flow (TimeClock / `Action_PunchOut` chain). Concrete location verified during implementation; same `ClearCurrentAction` call.

### 6. Movement gates

- **`PlayerController.Move()`** — add early-return at top: `if (_character.OccupyingFurniture != null) return;`. (Works on every peer thanks to §1 replication.)
- **`CharacterMovement.SetDestination(...)`** — add early-return at top: `if (_character.OccupyingFurniture != null) return;`. Same gate covers NPCs (server-side) and any player-initiated SetDestination path.
- **`CharacterActions.ExecuteAction`** — no new gate. The existing `_currentAction != null` check at line 31 already blocks new actions while the occupy action is current.

### 7. Cashier.cs deletions

Remove `Update()`, `ServerTickAutoOccupy()`, `_autoSeatTimer`, `AUTO_SEAT_TICK_INTERVAL`, `_scratchColliders`, `_autoSeatRadius` serialised field. Cashier becomes a pure react-to-actions component.

## Player↔NPC parity invariants

Switching control of a seated Character between PlayerController and NPCController must be a no-op for occupancy. Verified by:

- The action runs on `CharacterActions`, which lives on the Character itself — survives controller swap.
- `OccupyingFurniture` lives on Character — survives controller swap.
- Cashier's authoritative state lives on CashierNetSync — replicated to every peer regardless of who controls the character.
- Forced-leave triggers (`SetCombatState(true)`, `SetUnconscious(true)`, `Die()`) live on Character — survive controller swap.

## Audit (rule #19b — mandatory)

Six-question audit at the end of implementation:

1. **Who writes / who reads:** Character.`_occupyingFurnitureNetId` written by server in `SetOccupyingFurniture`; read by every peer via the property override and the OnValueChanged event.
2. **Replication channel:** new `NetworkVariable<ulong>` on Character (additive — no existing channel changed).
3. **Late-joiner repro:** host seats vendor → fresh client joins → joining client sees `cashier.Occupant == vendor` (via existing CashierNetSync) AND `vendor.OccupyingFurniture == cashier` (via new Character NetVar). Repro is mandatory before claiming done.
4. **Client pre-gate:** `CashierInteractable.Interact` reads `_cashier.Occupant` (replicated). Three-branch decision resolves identically on every peer.
5. **GetComponentInParent spawn-race:** the new Character NetVar uses the existing NetworkBehaviour spawn pipeline — no new `GetComponentInParent` call sites added.
6. **Proximity:** every player path routes through `InteractableObject.IsCharacterInInteractionZone` (2D X-Z). No inline distance math.

## File map

**New:**
- `Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs`

**Modified:**
- `Assets/Scripts/Character/Character.cs` — add NetVar + getter override + OnValueChanged hook.
- `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — two ServerRpcs.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — Move() gate.
- `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs` — SetDestination gate.
- `Assets/Scripts/World/Furniture/OccupiableFurniture.cs` — OnInteract routing.
- `Assets/Scripts/World/Furniture/Cashier.cs` — delete ServerTickAutoOccupy + scaffolding.
- `Assets/Scripts/World/Furniture/CashierInteractable.cs` — three-branch routing.
- `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs` — queue action on arrival, clear on Unassign / shift end.

**Docs:**
- `.agent/skills/character_core/SKILL.md`
- `.agent/skills/shop_system/SKILL.md` (delete deferred-refactor callout)
- `.agent/skills/job_system/SKILL.md`
- `wiki/systems/character.md`
- `wiki/systems/shop-vendor.md`
- `.claude/agents/character-system-specialist.md` (continuous action knowledge + Character NetVar)

## Build sequence

1. Character NetVar + getter override + OnValueChanged hook.
2. `CharacterAction_OccupyFurniture` class.
3. CharacterActions ServerRpcs.
4. OccupiableFurniture.OnInteract routing.
5. CashierInteractable three-branch routing.
6. Cashier deletions.
7. JobVendor wiring + shift-end hook.
8. Movement gates (PlayerController + CharacterMovement).
9. Late-joiner repro + rule #19b audit (mandatory before commit-as-done).
10. Skill / wiki / agent docs.
