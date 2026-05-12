# Network Audit — Interact Deduplication (`e56ce30c..b2e32123`)

> **Plan task:** Task 8 of `docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication.md`
> **Spec:** `docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md` Section 4
> **Auditor:** `network-validator` agent, 2026-05-09
> **Verdict:** **PASS WITH NOTES** (no blockers)

## Summary

The fix consolidates all `Input.GetKey*(KeyCode.E)` reads into `PlayerController`
per Rule #33 and renames `PlayerInteractionDetector.TriggerInteract → TriggerTapInteract`.
The detector's `Update()` was deleted and replaced by a proximity-only
`LateUpdate()`. Two new helpers (`TriggerHoldMenu`, `SetPromptHoldProgress`) were
added. `PlayerInteractCommand` was updated to call `TriggerTapInteract`. A
defensive Canvas/GraphicRaycaster `Awake()` was added to `UI_ShopBuyPanel`.

The double-`.Interact()` on E tap is eliminated: exactly one input path exists
now — `PlayerController.HandleEKeyUp` → `_detector.TriggerTapInteract(target)`
→ `ExecuteNormalInteract()` → `target.Interact(Character)`.

## Per-subclass audit table

| Subclass | Path verified | Host↔Client | Client↔Client | Host/Client↔NPC | Findings |
|---|---|---|---|---|---|
| `CashierInteractable` (flagship) | PASS | PASS | PASS | PASS | Tap-E fires `RequestStartBuyServerRpc` **once**. Server pre-gate at `CashierNetSync.cs:139-144` validates `IsAvailableForCustomer` and sends targeted `SendBusyToastClientRpc` to sender only. `OpenBuyPanelClientRpc` is `ClientRpcParams`-targeted to `customer.OwnerClientId` (`CharacterAction_BuyFromShop.cs:79-80`) and the receiving side also defensively gates with `if (!customer.IsOwner) return;` (`CashierNetSync.cs:103`). |
| `FurnitureInteractable` (base) | PASS | PASS | PASS | PASS | Single dispatch via `_furniture.OnInteract(interactor)`. `IsOccupied` pre-flight is read-only; mutation happens server-authoritatively inside `Furniture.OnInteract` / `IOccupiable.Use`. |
| `ChairFurnitureInteractable` | PASS | PASS | PASS | PASS | `OnFurnitureUsed` calls `user.CharacterMovement?.SetDestination` and `user.Controller?.Freeze()`. *Pre-existing concern, out of scope: `_isSeated` / `_seatedCharacter` are local-only, not replicated — latent multiplayer bug not introduced by this change.* |
| `CraftingFurnitureInteractable` | PASS | PASS | PASS | PASS | `OnFurnitureUsed` opens a local crafting UI for the caller; subsequent crafting goes through `CharacterAction` and existing ServerRpcs. Single fire confirmed. |
| `BedFurnitureInteractable` | PASS | PASS | PASS | PASS | `Interact` branches on `interactor.IsOwner && interactor.IsPlayer()`. Player path opens local UI prompt → `RequestSleepOnFurnitureServerRpc`. NPC/server path enqueues directly. Single dispatch per tap. |
| `TimeClockFurnitureInteractable` | PASS | PASS | PASS | PASS | Routes via `_building.RequestPunchAtTimeClockServerRpc` (client) or direct server call (host/NPC). |
| `BuildingInteractable` | PASS | PASS | PASS | PASS | Server-relay only: `Building.RequestStartFinishConstructionServerRpc(NetworkBehaviourReference)`. |
| `Harvestable` | PASS | PASS | PASS | PASS | Tap-E enqueues `CharacterHarvestAction` once; mutations occur server-authoritatively inside the action + `HarvestableNetSync` NetworkVariables. Hold-E menu unchanged. |
| `CharacterInteractable` (dialogue NPC) | PASS | PASS | PASS | PASS | Freeness gate at `PlayerInteractionDetector.cs:243-260` inside `ExecuteNormalInteract` is **reached identically** — `TriggerTapInteract` sets `_currentInteractableObjectTarget = target` then calls `ExecuteNormalInteract()`, which still type-checks `is CharacterInteractable` and runs `IsFree()` before `Interact`. End-dialogue toggle (`CurrentTarget == targetChar → EndInteraction()`) still works. |
| `MapTransitionDoor` | PASS | PASS | PASS | PASS | Door lock/key/jiggle checks go through `doorLock.RequestUnlockServerRpc()` / `RequestJiggleServerRpc()`. Toast guarded by `interactor.IsOwner && interactor.IsPlayer()`. |
| `BuildingInteriorDoor` (`: MapTransitionDoor`) | PASS | PASS | PASS | PASS | Mirrors `MapTransitionDoor.Interact` with interior resolution; identical single-fire path through `CharacterMapTransitionAction`. |
| `ItemInteractable` | PASS | PASS | PASS | PASS | `WorldItem.RequestInteractServerRpc(interactor.NetworkObjectId)` — single ServerRpc per tap. Local `_wasCollected` is a server-only optimisation; not corrupted by single fire. |

## NPC-driven callsites (rule #19 — Host/Client↔NPC)

Outside `PlayerController` `.Interact()` calls — all server/AI-driven, never through input:

- **`MapTransitionDoor.cs:155`** — `Action = () => door.Interact(interactor)` — `InteractionOption.Action` callback used by the hold-E menu's "Enter" button. User clicks menu manually; `PlayerController` does not read input for this call. **PASS** — unaffected.
- **`CharacterParty.cs:1143`** — `door.Interact(_character)` inside `_portalFollowCoroutine`. Party leader auto-interact when a follower reaches the door zone. **PASS** — NPC-driven.
- **`CharacterDoorTraversalAction.cs:153, 167`** — `door.Interact(character)`. NPC traversal of a non-transition door. **PASS** — server-side action.
- **`BTAction_Work.cs:164` and `BTAction_PunchOut.cs:216`** — `interactable.Interact(self)`. NPC behaviour-tree actions. **PASS** — server-driven.

## Two-player parity (rule #19 — Client↔Client)

`PlayerInteractionDetector.LateUpdate` owner gate:
```csharp
if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;
```
- In a 2-player scene, only the owning client runs `UpdateClosestTarget()` on its own detector. Remote-player detectors short-circuit before `nearbyInteractables` is queried.
- `OnTriggerEnter` / `OnTriggerExit` repeat the same owner gate — remote players don't accumulate proximity state; no spurious prompts on other peers.
- `_detector.CurrentTarget` is read on each `PlayerController.Update()`; because `PlayerController.Update()` is `IsOwner`-gated, only the local owner reads it. Each player has an independent `(PlayerController, PlayerInteractionDetector)` pair scoped to their own `Character` facade.
- `IsLocalPlayerCharacter()` (requires `IsPlayer() && (!IsSpawned || IsOwner)`) correctly prevents dialogue-menu spam on remote players inside `HandleInteractionStateChanged`.

## Frame ordering: `LateUpdate` proximity vs `Update` input

`UpdateClosestTarget()` moved from `Update()` to `LateUpdate()`. The XML comment claims this lets `PlayerController.Update()` "see the previous frame's proximity snapshot — stable, no input/render race." Correct for the steady-state case (Unity runs all `Update()` before all `LateUpdate()`).

**Edge case (low severity)**: On the **first frame** an interactable enters the zone, `OnTriggerEnter` fires during FixedUpdate; then `PlayerController.Update()` reads `_detector.CurrentTarget` which is still null (population happens in this frame's `LateUpdate`). A tap-E on this exact frame would drop. This was also true pre-refactor (with deterministic script-execution-order timing). One-frame lag ≤16ms at 60Hz — negligible.

## Warnings / future cleanup

None block the dedup objective. All flagged items are pre-existing or out-of-scope for this fix.

1. **`UI_ShopBuyPanel.Awake` does not set `Canvas.renderMode`.** Mirrors `UI_StorageFurniturePanel.cs:58-71`, which also doesn't set it. Parity preserved; not a regression introduced by this branch. UI hygiene only — review if either panel exhibits wrong-space rendering in any build configuration.

2. **Per-frame LINQ allocation in `UpdateClosestTarget`** (lines 342-344: `nearbyInteractables.OrderBy(...).FirstOrDefault()`). Violates Rule #34. **Pre-existing**, not introduced by this change. Defer to optimisation backlog if not already tracked.

3. **`TriggerTapInteract` permanently mutates `_currentInteractableObjectTarget`** (lines 200-207). The proximity field is overwritten with the caller-supplied target until the next `LateUpdate`. Minimal risk because `LateUpdate` reconciles before the next `Update`, but the mutation is non-obvious. Pre-existing pattern from `TriggerInteract`. Optionally scope into a `try/finally` so the field is restored after `ExecuteNormalInteract`.

4. **`FurniturePlacementManager.cs:185` rule #33 outlier** (potential future cleanup). Reads `Input.GetKeyDown(KeyCode.E)` for ghost rotation during placement. When placement is active and a consumable is in hand, E both rotates the ghost AND falls through to `PlayerController.HandleEKeyDown`'s priority-4 consumable check — could double-trigger. `HandleEKeyDown` already gates on `_character.CropPlacement.IsActive` (lines 358-362) but not on `FurniturePlacementManager.IsPlacementActive`. **Pre-existing, latent** — out of scope for the dedup fix. Recommend extending the priority-3 placement-active gate in a follow-up.

## Overall verdict

**PASS WITH NOTES.** The interact-deduplication objective is achieved cleanly: every `InteractableObject` subclass fires exactly one effect per E tap across Host↔Client, Client↔Client, and Host/Client↔NPC scenarios. Server authority preserved; no NetworkVariable/RPC paths regressed; dialogue-NPC freeness gate reached identically; late-joiner gate in `OpenBuyPanelClientRpc` is correct (and additionally protected by `ClientRpcParams` targeting). Two-player owner gate in `LateUpdate` + `OnTriggerEnter`/`OnTriggerExit` is correct.

Notes 1-4 above are pre-existing or out-of-scope. None block merging the dedup branch.

---

*End of audit.*
