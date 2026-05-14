---
type: system
title: "Shop Vendor"
tags: [shops, jobs, tier-2, stub]
created: 2026-04-19
updated: 2026-05-14
sources: []
related: ["[[shops]]", "[[jobs-and-logistics]]", "[[host-only-state-blindspot]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: building-furniture-specialist
owner_code_path: "Assets/Scripts/World/Jobs/"
depends_on: ["[[shops]]"]
depended_on_by: ["[[shops]]"]
---

# Shop Vendor

## Summary
`JobVendor` is the face of the shop. BT keeps them behind the counter. Constantly scans the queue; when non-empty, calls the next customer via `CallNextCustomer`. Customer walks up, executes `InteractionBuyItem`, money + item transfer server-side.

## Shift end
If the vendor is the **last** one working, `ShopBuilding.ClearQueue()` kicks remaining customers. Vendor then `WorkerEndingShift`.

## Seat eviction (forced) — central `Character.AutoLeaveOccupiedFurniture`

Forced seat release on combat / incapacitate / death is centralised on `Character` (see `[[character]]` §3.b and `character_core/SKILL.md`). The cashier no longer polls for these conditions.

## Occupy via CharacterAction (2026-05-14)

Vendor seating is action-driven. The previous `Cashier.ServerTickAutoOccupy` proximity tick has been **deleted**; the same `CharacterAction_OccupyFurniture` (continuous, server-only) is queued by both player and NPC paths — controller swaps are no-ops for seating state (rule #22 player↔NPC parity).

**NPC path** — `JobVendor.Execute` step 3: reserved cashier + worker in interaction zone + no current action → server-side `ExecuteAction(new CharacterAction_OccupyFurniture(_worker, _heldCashier))`. `JobVendor.Unassign` routes the seated case through `ClearCurrentAction` so the action's `OnCancel` → `Leave` fires; defensive direct `Leave` belt-and-suspenders.

**Player path** — `CashierInteractable.Interact` two-stage routing:
- Branch 1: seated occupant (`Cashier.Occupant == interactor`, NetVar-resolved) → `CharacterActions.RequestLeaveOccupiedFurnitureServerRpc` → server-side `ClearCurrentAction` → `OnCancel` → `Leave`.
- Otherwise: single "use this cashier" intent via `CashierNetSync.RequestUseCashierServerRpc`. Server-side role gate (`Cashier.IsCharacterAllowedToOccupy(character)`, overriding the new `OccupiableFurniture.IsCharacterAllowedToOccupy(Character)` virtual) decides:
  - Assigned `JobVendor` for this shop + seat free → queue `CharacterAction_OccupyFurniture`.
  - Otherwise → queue `CharacterAction_BuyFromShop` (existing customer flow).

Server-side role routing is required because `CharacterJob._activeJobs` is not NetVar-replicated — remote-client owners cannot read their own `CurrentJob` locally. The "no vendor on duty" toast moved server-side so it doesn't misfire for a player-vendor about to seat themselves.

**Movement lockout** — While `Character.OccupyingFurniture != null` (now replicated, see `[[character]]` §change log 2026-05-14), `PlayerController.Move` / `CharacterMovement.SetDestination` / `CharacterMovement.SetDesiredDirection` all early-return.

See [docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md](../../docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md) and the [rule #19b audit](../../docs/superpowers/audits/2026-05-14-furniture-occupancy-rule-19b.md).

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-05-14 — Document `ServerTickValidateOccupant` (vendor seat eviction half of the cashier auto-seat state machine). Fixes the "vendor in combat, customer can still open buy panel" leak. — claude
- 2026-05-14 (later) — Reverted `ServerTickValidateOccupant`. Validator's radius check fired during shopping (vendor's transform oscillated within the auto-seat radius), aborting active transactions every second. Forced eviction now goes through central `Character.AutoLeaveOccupiedFurniture` only. — claude
- 2026-05-14 (refactor) — `Cashier.ServerTickAutoOccupy` deleted. Vendor seating now drives the shared `CharacterAction_OccupyFurniture` (server-only continuous action). Players: `CashierInteractable.Interact` → `CashierNetSync.RequestUseCashierServerRpc` (server role-routes). NPCs: `JobVendor.Execute` step 3 queues the same action on arrival. Authorization gate: new `OccupiableFurniture.IsCharacterAllowedToOccupy` virtual, `Cashier` overrides to require the assigned `JobVendor` for the shop. Movement lockout via the now-replicated `Character.OccupyingFurniture`. — claude

## Sources
- [[shops]] §3.
- [Assets/Scripts/World/Furniture/Cashier.cs](../../Assets/Scripts/World/Furniture/Cashier.cs) — `Use` / `Release` / `IsCharacterAllowedToOccupy`.
- [Assets/Scripts/World/Furniture/CashierNetSync.cs](../../Assets/Scripts/World/Furniture/CashierNetSync.cs) — `RequestUseCashierServerRpc` server-side role routing.
- [Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs](../../Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs) — NPC parity path.
- [Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs) — shared continuous action.
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md) — operational procedures.
- [docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md](../../docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md) — design rationale.
