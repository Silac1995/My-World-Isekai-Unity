# Furniture Occupancy via CharacterAction — Rule #19b Audit

**Date:** 2026-05-14
**Branch:** `claude/quirky-swirles-fd0ed4` off `multiplayyer` @ `60d6c269`
**Reference spec:** [docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md](../specs/2026-05-14-furniture-occupancy-via-characteraction-design.md)
**Plan:** [docs/superpowers/plans/2026-05-14-furniture-occupancy-via-characteraction.md](../plans/2026-05-14-furniture-occupancy-via-characteraction.md)

## Scope

This audit covers every code path touched by the refactor that mutates or reads furniture-occupancy state. Conducted per project rule #19b's six-question checklist, in advance of the mandatory late-joiner play-mode repro.

## Six-Question Audit

### 1. Who writes / who reads the state?

| State | Writer | Readers |
|-------|--------|---------|
| `Character._occupyingFurniture` (in-memory) | Server-side `Character.SetOccupyingFurniture` (called from `OccupiableFurniture.Use` / `Release`) | Server-side `OccupyingFurniture` getter |
| `Character.NetworkOccupyingFurnitureNetId` (new NetVar) | Server-side `Character.SetOccupyingFurniture` | Every peer: clients via the `OccupyingFurniture` getter's SpawnManager resolution; `OnValueChanged` fires `OnOccupyingFurnitureChanged` on remote peers |
| `Cashier._occupant` (in-memory, server-only) | Server-side `Cashier.Use` / `Release` | Server-side `Cashier.Occupant` getter |
| `CashierNetSync.OccupantNetworkObjectId` (existing NetVar) | Server-side `Cashier.Use` / `Release` (via `SetOccupantServer`) | Every peer; client-side `Cashier.Occupant` getter override resolves the NetVar |
| `CharacterActions._currentAction` | Local `ExecuteAction` (server-side for continuous; visual proxy is set on every peer via `BroadcastActionVisualsClientRpc`) | Local readers (movement gate, interaction gates, HUD progress) |

### 2. What replication channel is used for every client-readable field?

| Field | Channel | Notes |
|-------|---------|-------|
| `Character.NetworkOccupyingFurnitureNetId` | **NEW** `NetworkVariable<ulong>`, `EveryoneRead` / `ServerWrite` | Added 2026-05-14 to fix the pre-existing gap; mirrors `Character.IsSleeping` pattern |
| `Cashier.Occupant` (read on client) | Existing `CashierNetSync.OccupantNetworkObjectId` | Unchanged this refactor |
| `Cashier.CurrentCustomer` (read on client) | Existing `CashierNetSync.CurrentCustomerNetworkObjectId` | Unchanged |
| Visual proxy of `CharacterAction_OccupyFurniture` | `CharacterActions.BroadcastActionVisualsClientRpc` (server → NotServer, 600s sentinel duration) | Owner client's `_currentAction` is set to a `CharacterVisualProxyAction` so movement gates via `_currentAction != null` also fire client-side. Note: the spec's literal gate is `OccupyingFurniture != null`, which is the channel relied upon in the new code; the visual proxy is the fallback for anything else that reads `_currentAction` |

### 3. What does the late-joiner see on connect?

**Required manual repro** (must run in Unity Editor host + Standalone client build):

1. Start Unity Editor as host.
2. Load a save with a populated shop. Confirm a vendor NPC seats at the cashier via the new action — console should log `[Furniture] <vendor> utilise <cashier>` then `[Cashier] Use server: …` (no `[OccupyFurniture]` warning).
3. Launch a Standalone build, connect as a client.
4. On the joining client, verify:
   - `Cashier.Occupant` resolves to the vendor (read via DevMode Inspect → Cashier inspector → "Occupant" field, or via `_cashier.Occupant?.CharacterName` probe).
   - The vendor's `Character.OccupyingFurniture` is non-null (DevMode Inspect → Character inspector — the new getter resolves via the NetVar).
   - Tap E on a different cashier from the client — should route through `RequestUseCashierServerRpc` → Server picks buy path (client isn't the assigned vendor) → buy panel opens correctly.
   - Tap E on the seated vendor's cashier (from the client, as a customer) — buy panel opens (vendor is on duty).

**Expected behavior on every connect path:**
- `Character` NetworkObject (the vendor) spawns on the joining client. NGO's initial-state sync delivers the current `NetworkOccupyingFurnitureNetId` value. `OnValueChanged` fires; `OnOccupyingFurnitureChanged` fires; `Character.OccupyingFurniture` returns the resolved Cashier on the first read.
- `CashierNetSync` NetworkObject (the cashier) spawns on the joining client. Initial-state sync delivers `OccupantNetworkObjectId`. `Cashier.Occupant` returns the vendor on the first read.
- The `CashierNetSync.OnNetworkSpawn` backfill (lines 67-77) is a safety net: if the cashier's NetSync somehow spawns AFTER its `Cashier` already has an occupant (save/load or pre-spawn race), it re-pushes the occupant id into the NetVar so the next late-joiner sees the truth.

### 4. What does the client-side pre-gate read, and does it match the server's authoritative value?

| Gate | Reads | Matches server? |
|------|-------|-----------------|
| `CashierInteractable.Interact` Branch 1 (leave) | `_cashier.Occupant` | ✓ (CashierNetSync.OccupantNetworkObjectId) |
| `CashierInteractable.Interact` busy-toast pre-gate | `_cashier.CurrentCustomer` | ✓ (CashierNetSync.CurrentCustomerNetworkObjectId) |
| `CashierInteractable.Interact` unified "use" call | None (server decides via `RequestUseCashierServerRpc`) | ✓ (server has authoritative `CharacterJob`) |
| `OccupiableFurniture.OnInteract` | `IsOccupied` + `Occupant` | ✓ (NetVar-resolved on subclasses with a sync channel; for Bed/Chair the gate is non-null only — same identity-approximate fallback documented in the new Character resolver) |
| `PlayerController.Move` | `_character.OccupyingFurniture` | ✓ (new Character NetVar) |
| `CharacterMovement.SetDestination` / `SetDesiredDirection` | `_character.OccupyingFurniture` | ✓ (new Character NetVar) |
| `JobVendor.Execute` step 3 (NPC) | `_heldCashier.Occupant`, `_worker.OccupyingFurniture`, `IsCharacterInInteractionZone` | Server-side only — JobVendor only runs on server |

**Known scope limitation, documented in code + spec:** `CharacterJob._activeJobs` is not NetVar-replicated. The old client-side Branch 2 check (`interactor.CharacterJob.CurrentJob is JobVendor`) would have silently failed on remote-client player-vendors. The fix collapses Branches 2 + 3 into the single `RequestUseCashierServerRpc` so the server (where `_activeJobs` IS populated) makes the role decision authoritatively. The role gate itself lives on `OccupiableFurniture.IsCharacterAllowedToOccupy` (virtual; `Cashier` overrides) — every server-side entry point (CanExecute, the RPC) calls it.

### 5. GetComponentInParent spawn-race — any new call sites?

`Character.SetOccupyingFurniture` calls `furniture.GetComponentInParent<NetworkObject>()` to derive the NetVar value. **This runs server-side only**, at the moment the seating happens. Server already has the building/cashier spawned (seating requires the furniture to exist as a target), so there's no race.

On clients, the property getter resolves via `NetworkManager.Singleton.SpawnManager.SpawnedObjects[id]` — the NetworkObject is guaranteed spawned (we just resolved it from SpawnManager). `GetComponent<Furniture>()` and `GetComponentInChildren<Furniture>()` then run on that spawned GameObject — no race.

No new `GetComponentInParent` calls introduced on the client-side / Awake path.

### 6. Proximity gate — every player path through `IsCharacterInInteractionZone`?

| Path | Proximity gate |
|------|----------------|
| `CashierInteractable.Interact` (client entry) | `IsCharacterInInteractionZone(interactor)` at the top, line 37 |
| `CashierNetSync.RequestUseCashierServerRpc` (server validation) | `_cashier.GetComponent<InteractableObject>().IsCharacterInInteractionZone(customer)` at the top |
| `CharacterActions.RequestOccupyFurnitureServerRpc` (server validation) | `target.GetComponent<InteractableObject>().IsCharacterInInteractionZone(_character)` |
| `CharacterAction_OccupyFurniture.CanExecute` | `target.GetComponent<InteractableObject>().IsCharacterInInteractionZone(character)` |
| `OccupiableFurniture.OnInteract` (default tap-E) | Not directly — relays through the ServerRpc which re-validates server-side |
| `JobVendor.Execute` step 3 (NPC) | `interactable.IsCharacterInInteractionZone(_worker)` — server-side only |

All player paths route through `IsCharacterInInteractionZone` (2D X-Z canonical helper) somewhere between client press and server effect. No inline distance math added.

## Known limitations (deferred — not in scope for this refactor)

- **CharacterJob NetVar replication.** `_activeJobs` stays server-only. The new `RequestUseCashierServerRpc` works around this by routing server-side, but if other UI / pre-gates start to need client-side `CurrentJob` reads, a NetworkVariable replication is the right next step. Tracked in [wiki/projects/optimisation-backlog.md](../../../wiki/projects/optimisation-backlog.md) (to add).
- **Bed/Chair identity on clients.** `Character.OccupyingFurniture` resolves via the parent NetworkObject's NetworkObjectId; multi-furniture buildings (multiple chairs under one building) fall back to "first OccupiableFurniture in children" because Bed/Chair have no per-furniture NO (no-nested-NO rule). Boolean non-null gates work; identity-exact reads break for multi-furniture-per-building. Per the design spec's out-of-scope note, this is acceptable for the current refactor — a future per-furniture NetVar (or per-furniture NetSync) would fix it.

## Verdict

All six checkpoints pass on code review. Late-joiner repro is the runtime confirmation gate — to be completed before the refactor branch is declared done. Run the manual repro per section 3 above and either:
- Mark the audit `PASSED` here and proceed to merge; or
- Capture the failure mode and append a remediation task.
