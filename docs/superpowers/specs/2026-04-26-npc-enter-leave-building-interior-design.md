# NPC Enter / Leave Building Interior Design

**Date:** 2026-04-26
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

The `Character` system has no general-purpose "go inside building X" or "leave the current interior" primitive. The transition pieces all exist:

- [BuildingInteriorDoor.cs:50-115](../../../Assets/Scripts/World/Buildings/BuildingInteriorDoor.cs#L50-L115) — `Interact(Character)` is character-agnostic. Lazy-spawns the interior, handles lock/key, queues a `CharacterMapTransitionAction`.
- [CharacterMapTransitionAction.cs:33-91](../../../Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs#L33-L91) — already has an explicit NPC branch (server-side `ForceWarp` + `CharacterMapTracker.SetCurrentMap`). Once an NPC is at the door and `door.Interact(npc)` is called, the actual map swap works.
- The exit door inside an interior prefab is a regular `MapTransitionDoor` baked by [BuildingInteriorSpawner.cs:46-94](../../../Assets/Scripts/World/Buildings/BuildingInteriorSpawner.cs#L46-L94).

**What's missing** is the *navigation + interaction trigger* layer for NPCs. Today the only system that walks an NPC to a door and fires `Interact` is the party's private coroutine at [CharacterParty.cs:1075-1133](../../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs#L1075-L1133), and it only triggers when the leader changes maps. There is no reusable way to:

1. Order an NPC to enter a specific building interior (worker depositing into interior storage, NPC going home to sleep, NPC entering a shop to buy).
2. Order an NPC to leave the interior they are currently in (so a future "order" system, social order, BT decision, or quest can dismiss someone from a building).

This spec adds three `CharacterAction` wrappers that fill that gap and refactors the party coroutine to delegate to them (single source of truth — rule #22). Player-issued orders that *invoke* these actions (e.g., "ask a guest to leave") are out of scope here — they will be wired by the in-progress order system in a separate spec.

### Requirements

1. **Player ↔ NPC parity (rule #22).** The new behaviour ships as `CharacterAction`s that any caller (BT, GOAP, party, quests, future order system, future player UI) can enqueue uniformly. No gameplay logic lives in player-only managers.
2. **Reuse, don't replace.** `CharacterMapTransitionAction` and `BuildingInteriorDoor.Interact()` are not modified. The new actions delegate to the existing chain via `door.Interact(actor)`.
3. **Server-authoritative for NPCs (rule #18).** Movement and `door.Interact` calls run on the server when the actor is an NPC. For player actors, authority follows the actor's owner client (matches existing `CharacterAction` convention).
4. **Host↔Client, Client↔Client, Host/Client↔NPC parity (rule #19).** Verified by spot-tests in the test plan. (Player-issued *triggers* aren't in this spec — but the underlying action behaves identically across all peer combinations.)
5. **Locked-door + key flow already works for players** (`BuildingInteriorDoor.Interact` unlocks then returns; player re-clicks). The NPC action mirrors this: detect that the door unlocked and re-Interact once. If still locked, cancel cleanly.
6. **Failure modes are observable.** Cancel + `Debug.LogWarning` on no-door-found, locked-no-key, timeout, or destination unreachable. Caller (BT / GOAP / quest / order system) decides what to do next.
7. **`CharacterParty.DoorFollowRoutine` must be deleted**, not kept in parallel. The party becomes the first internal consumer of the new actions.
8. **No persistence.** Like other in-flight `CharacterAction`s, these are runtime-only. If a save/hibernate happens mid-walk, the NPC restores at its serialized position and the BT/schedule re-issues if appropriate.

### Non-Goals

- **Knock-and-wait / doorbell.** If the door is locked and the NPC has no key, the action cancels. A future "polite waiting" mechanic is out of scope.
- **Player-issued orders / "Ask to leave" UI.** A separate in-progress *order system* will own player-issued orders to NPCs (and NPC-issued orders to players, including the obey/refuse layer). This spec ships only the action primitives the order system will queue. No UI hooks, no `Building` RPCs, no permission predicates here.
- **Auto-walk for the player to a building they clicked.** The player still walks themselves and clicks the door manually. The new actions are queueable for the player too, but no UI surfaces it in v1.
- **Multi-map travel.** The action assumes the NPC is already on the same exterior map as the building (or already inside that building). Cross-map travel ("walk to another city's shop") is a separate planning problem owned by GOAP / quests.

## Architecture

### Action layer (new files)

Two **public** new actions + one **internal abstract base** in [Assets/Scripts/Character/CharacterActions/](../../../Assets/Scripts/Character/CharacterActions/):

| Class | Visibility | Constructor | Responsibility |
|---|---|---|---|
| `CharacterDoorTraversalAction` | abstract (internal) | `(Character actor)` | Holds the shared walk-loop (freeze, repath, locked-retry, timeout, unfreeze). Abstract `ResolveDoor()` and `IsActionRedundant()`. |
| `CharacterEnterBuildingAction` | public concrete | `(Character actor, Building target)` | `ResolveDoor` = closest `BuildingInteriorDoor` child of `target`. `IsActionRedundant` = actor is already on the building's interior map. |
| `CharacterLeaveInteriorAction` | public concrete | `(Character actor)` | `ResolveDoor` = closest `MapTransitionDoor` child of the actor's current interior `MapController` whose `TargetMapId == record.ExteriorMapId`. `IsActionRedundant` = actor is already on the exterior map. |

Public gameplay surface is exactly two actions — Enter and Leave — matching the two meaningful gameplay verbs. The base class is an implementation detail that exists purely so the walk-loop isn't duplicated.

**Why a base class instead of a static helper:** The walk loop *is* the action's tick coroutine; cancellation must stop it; freeze/unfreeze must be owned by the action lifecycle. A base class makes the loop a natural part of `OnStart` / `OnCancel`. A static helper would force each action to forward its lifecycle into the helper — same logic, more ceremony.

### `CharacterDoorTraversalAction` flow (the shared walk loop)

```
OnStart:
  door = ResolveDoor()
  if door == null:
    Debug.LogWarning("...no door found"); cancel; return
  if IsActionRedundant():
    cancel-as-success; return
  if actor is NPC: actor.Controller.Freeze()        // prevent BT override
  actor.CharacterMovement.Resume()
  interactRange = via InteractableObject.IsCharacterInInteractionZone(actor)  // canonical-API rule
  start tick coroutine

Tick (coroutine, runs on actor's owner — server for NPCs, client for player):
  while elapsed < 15s:
    if actor dead OR door destroyed: cancel; return
    if door.IsCharacterInInteractionZone(actor):
      actor.CharacterMovement.Stop()
      door.Interact(actor)
      // door.Interact does ALL of:
      //   - locked + has key → fires RequestUnlockServerRpc, returns
      //   - locked + no key  → fires RequestJiggleServerRpc + toast, returns
      //   - broken / unlocked → queues CharacterMapTransitionAction
      yield WaitForSeconds(0.3)
      if actor.CharacterActions.CurrentAction is CharacterMapTransitionAction:
        cancel-as-success; return                   // door is taking us through, our job is done
      if doorLock != null AND !doorLock.IsLocked.Value AND wasLockedBefore:
        door.Interact(actor)                        // we just unlocked it — re-interact to enter
        yield WaitForSeconds(0.3)
        if actor.CharacterActions.CurrentAction is CharacterMapTransitionAction:
          cancel-as-success; return
      // door rejected us (locked + no key, or some other gate) — let it rattle, give up
      Debug.LogWarning("...door refused entry"); cancel; return
    if elapsed % 2 < deltaTime:
      actor.CharacterMovement.SetDestination(door.transform.position)
    yield null
  Debug.LogWarning("...timeout"); cancel

OnCancel:
  if actor is NPC: actor.Controller.Unfreeze()
  actor.CharacterMovement.Stop()
```

**The action never reimplements transition or lock logic.** It calls `door.Interact(actor)` and observes the door's reaction:
- If the door queued `CharacterMapTransitionAction`, the existing transition pipeline (fade + warp + tracker update) takes over — same path the player uses today.
- If the door rattled (locked, no key), the door already played its rattle SFX and shown the toast (for players) — the action just observes that nothing happened and gives up.
- If the door unlocked (key available), the action gets one free re-interact attempt, mirroring what a player does when they click the locked door, see it unlock, and click again.

This keeps the door as the single owner of "what happens when someone tries to use me" — the new actions are purely "navigate to a door and tap it".

### Portal-door case (party leader walked through a non-building map door)

The party currently handles two door types in its follow coroutine: `BuildingInteriorDoor` (case A) and any other `MapTransitionDoor` (case B — outdoor↔outdoor portals, gates between zones). Case A becomes `CharacterEnterBuildingAction`. Case B doesn't fit Enter/Leave semantically, so the party keeps a **small** dedicated coroutine for portals only — same walk-loop pattern, ~25 lines (down from 75). It calls `door.Interact(member)` directly, just like the new actions. If portals later become a frequent player-issued use case, we can graduate this into a third public action; for now it's a single internal user.

### Caller refactor — `CharacterParty`

[CharacterParty.cs:1060-1133](../../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs#L1060-L1133) — current `StartDoorFollow` / `StopDoorFollow` / `DoorFollowRoutine` is replaced by:

```csharp
// At the call site (line 994):
if (door is BuildingInteriorDoor bd)
{
    var building = bd.GetComponentInParent<Building>();
    if (building != null)
        member.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(member, building));
}
else
{
    // Portal door (outdoor↔outdoor / gate / non-building map transition)
    StartPortalFollow(member, door);     // small dedicated coroutine, ~25 lines
}
```

`StartPortalFollow` is a thin coroutine that mirrors `CharacterDoorTraversalAction`'s walk-loop but is scoped to the portal-door case. Net delete from `CharacterParty.cs`: ~50 lines (75 → 25). The complex BuildingInteriorDoor path is fully gone; only the portal special-case remains.

### File touch summary

| File | Change | Approx LOC |
|---|---|---|
| `CharacterDoorTraversalAction.cs` | **new** (abstract base, internal) | ~120 |
| `CharacterEnterBuildingAction.cs` | **new** (public concrete) | ~40 |
| `CharacterLeaveInteriorAction.cs` | **new** (public concrete) | ~40 |
| `CharacterParty.cs` | delete `BuildingInteriorDoor` branch of door-follow, keep small portal-only coroutine, swap building call site to action enqueue | -50 / +30 |
| `wiki/systems/character-party.md` | bump `updated:`, change-log, refresh `depends_on` | edit |
| `wiki/systems/character.md` | append "Enter / Leave Building" section under CharacterActions catalogue (no separate `character-actions.md` page — `wiki/systems/ai-actions.md` is GOAP-only and `wiki/systems/character.md` is the canonical home for `CharacterAction` subclasses) | edit |
| `wiki/systems/building-interior.md` | add note that NPCs can now enter/leave via `CharacterEnterBuildingAction` / `CharacterLeaveInteriorAction`, refresh `depended_on_by` | edit |
| `.agent/skills/party-system/SKILL.md` | replace door-follow coroutine procedure with action-enqueue procedure | edit |
| `.agent/skills/building_system/SKILL.md` | new section on programmatic NPC interior entry/exit | edit |
| `.claude/agents/building-furniture-specialist.md` | mention the new actions under building-interior section | edit |
| `.claude/agents/character-system-specialist.md` | mention the new actions under CharacterActions catalogue | edit |

**Zero touches** to `Building.cs`, `BuildingInteriorRegistry.cs`, `BuildingInteriorSpawner.cs`, `MapController.cs`, `CharacterInteractable.cs`. The action primitives don't need replicated owner lookups or RPCs — those will be added by the order system if/when it surfaces a player-issued kick.

### `CharacterLeaveInteriorAction` exit-door resolution

Without the registry helper, the action just walks the actor's current `MapController`'s child hierarchy and picks the closest `MapTransitionDoor` whose `TargetMapId == record.ExteriorMapId`. The interior MapController is reachable via `actor.GetComponent<CharacterMapTracker>().CurrentMapID.Value` → `MapController.GetByMapId`. The exit door is the same `MapTransitionDoor` baked in by `BuildingInteriorSpawner`.

## Data flow

### Worker entering a shop interior to deposit (future GOAP consumer — illustrative)

```
GoapAction_DepositInInteriorStorage.Execute()
  → npc.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(npc, shop))
       (CharacterEnterBuildingAction inherits CharacterDoorTraversalAction)
       OnStart: ResolveDoor() returns shop's closest BuildingInteriorDoor
                IsActionRedundant() returns false (npc is outside)
                walks npc to door (server-side, NavMesh repath every 2s)
                door.Interact(npc)
                     → BuildingInteriorDoor.Interact
                          (lock/key check, rattle if locked-no-key, unlock if key)
                          queues new CharacterMapTransitionAction
                               → ForceWarp + tracker.SetCurrentMap (existing)
  // npc is now on interior map; GOAP plan continues with FindStorageFurniture
```

### NPC leaving an interior (BT / schedule / future order system — illustrative)

```
SomeCaller (BT node, schedule, future order system)
  → npc.CharacterActions.ExecuteAction(new CharacterLeaveInteriorAction(npc))
       OnStart: ResolveDoor() returns exit door on npc's current interior MapController
                IsActionRedundant() returns false (npc is on interior)
                walks npc to exit door
                door.Interact(npc)
                     → MapTransitionDoor.Interact queues CharacterMapTransitionAction
                          → ForceWarp + tracker.SetCurrentMap to exterior
  // npc is now outside; their normal BT / schedule resumes
```

## Failure modes & cancellation

| Scenario | Behaviour |
|---|---|
| Building has no interior door child | `CharacterEnterBuildingAction` cancels in `OnStart` with `Debug.LogWarning`. |
| NPC is already inside the target | `CharacterEnterBuildingAction` cancels as success in `OnStart` (no-op). |
| NPC is already on exterior | `CharacterLeaveInteriorAction` cancels as success in `OnStart` (no-op). |
| Door locked + NPC has key | `door.Interact` unlocks (existing flow). Action waits 0.3 s, re-Interacts once. |
| Door locked + NPC has no key | After Interact, action detects no map transition + door still locked, cancels with warning. |
| Path to door blocked / unreachable | Movement timeout (15 s) → cancel + warning. |
| NPC dies / is interrupted by combat | `OnCancel` runs → `Unfreeze` controller, `Stop()` movement, no leftover state. |
| Action cancelled by a higher-priority `CharacterAction` (e.g., flee) | Same as above — `CharacterActions` queue handles preemption. |
| Building destroyed mid-walk | Coroutine null-checks `door`, cancels. |

## State & persistence

- **Runtime-only.** Neither new action persists. `CharacterActions` does not serialize in-flight actions today.
- **No NetworkVariables.** All state lives in the action coroutine on the server.
- **No new save schema.** The existing `BuildingInteriorRegistry` save data already covers the rooms NPCs walk into.

## Networking notes (rule #18, #19)

- **Authority follows the actor.** `CharacterDoorTraversalAction`'s tick coroutine runs on whichever instance owns the actor — server for NPCs, owning client for players (matches the convention used by other `CharacterAction`s like `CharacterHarvestAction`). NPC movement is then server-driven via `CharacterMovement`; player movement is client-prediction-driven and `CharacterMapTransitionAction` already handles both branches.
- **Player path is queueable but not surfaced in v1.** The action class is not locked to NPC-only — a future feature can queue `CharacterEnterBuildingAction` on the local player (e.g., click-to-walk on a building, or via the upcoming order system) and it will work via the same chain. v1 simply doesn't add player UI for it.
- **No new RPCs.** The actions internally call existing flows (`CharacterMovement`, `door.Interact`) which already handle their own networking. The order system that surfaces these as player-issued commands will own its own RPC layer.
- **Late-joiners.** Doors and `BuildingInteriorRegistry` already replicate via existing channels. A late-joining client that sees an NPC mid-action will see the NPC's `CharacterMovement` `NetworkVariable` destination — same as any other server-driven movement.
- **Verified scenarios in test plan:** NPC autonomously enters a shop interior on the host; same on a client-only NPC scenario; host's NPC follower party-follows leader who entered a building (refactored party path); leave action drops NPC on the exterior cleanly.

## Gotchas

1. **`Controller.Freeze()` must always be paired with `Unfreeze()`.** All cancel paths in `CharacterDoorTraversalAction` (success, timeout, error, preemption) call `Unfreeze` from `OnCancel`. The party's old coroutine handled this manually and was bug-prone; centralising in the base class removes the duplication. The party's remaining portal-only coroutine must mirror this discipline (a one-line comment in the spec / source flags it).
2. **The two-step lock+enter is asymmetric.** `door.Interact` either (a) starts a transition, (b) unlocks the door, or (c) does nothing visible. The action distinguishes (a) by checking `actor.CurrentAction is CharacterMapTransitionAction` after a 0.3 s yield; (b) by re-checking `doorLock.IsLocked.Value`; otherwise treats as (c) and cancels.
3. **NavMesh on interior maps.** Interior `MapController`s carry their own `NavMeshSurface`. Walking to an exit door that's on a different `NavMeshSurface` than the spawn is fine because both bake into the same NavMesh data once the interior is loaded.
4. **Interior is lazy-spawned on first entry.** When the NPC is the very first character to enter a building, `BuildingInteriorRegistry.RegisterInterior` runs from `door.Interact`. The action does not need to special-case this — `CharacterMapTransitionAction.OnApplyEffect` already handles `_targetPosition == Vector3.zero` for first-visit.
5. **No client-side building lookup needed in this spec.** `CharacterLeaveInteriorAction` runs server-side for NPCs and resolves the exit door directly from the actor's current `MapController` — no need to know which building owns the interior. If the order system later requires client-side "who owns this interior?" lookups, it will add the necessary replication (e.g., a `MapController.OwningBuildingId` NetworkVariable) at that time.

## Open questions

- *None blocking.* The "obey/refuse" system that gates whether the target NPC actually executes `CharacterLeaveInteriorAction` is acknowledged as a separate future system; nothing in this spec depends on or precludes it.

## Test plan

### EditMode unit-style

- `CharacterEnterBuildingAction` no-ops when actor is already inside the target.
- `CharacterLeaveInteriorAction` no-ops when actor is already on the exterior map.
- `CharacterEnterBuildingAction` cancels with warning when target has no `BuildingInteriorDoor`.
- `CharacterLeaveInteriorAction` cancels with warning when actor's current map has no exit `MapTransitionDoor`.

### PlayMode (single-player / host)

1. Queue `CharacterEnterBuildingAction` on a free NPC via `/devmode` chat command targeting a shop. NPC walks to the shop's door, interacts, transitions inside. *(Verifies: enter action, lazy interior spawn.)*
2. Same as (1) but the door is locked and NPC has the matching key in their equipment. *(Verifies: two-step unlock-then-enter.)*
3. Same as (1) but the door is locked and NPC has no key. NPC walks to door, attempts, cancels, console shows warning. *(Verifies: failure mode.)*
4. While the NPC from (1) is inside, queue `CharacterLeaveInteriorAction` on them via `/devmode`. NPC walks to exit door, transitions to exterior. *(Verifies: leave action, exit-door resolution.)*
5. Form a party (player leader + NPC follower). Leader enters a building. Follower uses `CharacterEnterBuildingAction` (refactored path) to follow. *(Verifies: party refactor.)*

### Multiplayer

6. Host kicks off `CharacterEnterBuildingAction` on a server-owned NPC; remote client sees the NPC walk to the door and disappear (transition to interior — confirms tracker / interest replication). *(Host↔Client NPC parity.)*
7. Same as (6) but client connects mid-walk. Late-joining client sees the NPC at its current movement destination, then sees the transition. *(Late-joiner.)*
8. Two NPCs (one host-spawned, one spawned via client request) each queue `CharacterEnterBuildingAction` on the same shop. Both end up inside, no race / collision. *(Concurrency sanity.)*

## Sources

- [.agent/skills/party-system/SKILL.md](../../../.agent/skills/party-system/SKILL.md) — party follow + door routine (current procedure being superseded)
- [.agent/skills/building_system/SKILL.md](../../../.agent/skills/building_system/SKILL.md) — building interior spawning and door wiring
- [wiki/systems/character-party.md](../../../wiki/systems/character-party.md) — party architecture
- [wiki/systems/building-interior.md](../../../wiki/systems/building-interior.md) — interior MapController + door wiring
- [wiki/systems/world-map-transitions.md](../../../wiki/systems/world-map-transitions.md) — map transition mechanics
- [wiki/systems/character.md](../../../wiki/systems/character.md) — Character facade and CharacterAction catalogue
- [Assets/Scripts/Character/CharacterParty/CharacterParty.cs](../../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs) — current door-follow coroutine
- [Assets/Scripts/World/Buildings/BuildingInteriorDoor.cs](../../../Assets/Scripts/World/Buildings/BuildingInteriorDoor.cs)
- [Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs)
- 2026-04-26 conversation with Kevin — scope decision (option B narrowed: actions + party refactor only; player-issued "Ask to leave" deferred to in-progress order system)

## Change log

- 2026-04-26 — initial design — claude
- 2026-04-26 — narrowed scope: removed all "Ask to leave" UI/RPC/predicate content (deferred to in-progress order system); spec is now actions + party refactor only — claude
- 2026-04-26 — replaced public `CharacterUseDoorAction` with internal abstract `CharacterDoorTraversalAction` base class; public surface is now exactly two actions (Enter, Leave); portal-door follow stays as a small dedicated coroutine in `CharacterParty` — claude
