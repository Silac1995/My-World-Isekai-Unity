# Time Clock Furniture — Design

- **Date:** 2026-04-24
- **Status:** Approved (user confirmed in chat)
- **Author:** Silac (via Claude Opus 4.7)

## Problem

Punch-in/out for shift work today happens "out of thin air" — `BTAction_Work`
executes `Action_PunchIn` as soon as an NPC stands anywhere inside the
`BuildingZone`. Players have no route at all: there is no interactable that
fires a punch, so the mechanic is NPC-only. This violates project rule #22
(anything a player can do, an NPC can do — and vice versa) and breaks the
immersion model of the commercial-building job pipeline.

## Goal

Introduce a physical Time Clock furniture that every character — player or
NPC — must physically interact with in order to punch in or out. Existing
`Action_PunchIn` and `Action_PunchOut` `CharacterAction` classes are reused
unchanged; only the trigger surface moves from "inside BuildingZone" to
"touch the Time Clock".

## Scope

### In

- `FurnitureTag.TimeClock` — new enum value.
- `TimeClockFurniture : Furniture` — new component, type-marker only.
- `TimeClockFurnitureInteractable : FurnitureInteractable` — new interactable.
- `CommercialBuilding`:
  - lazy-resolved `TimeClock` property (`GetComponentInChildren<TimeClockFurniture>`);
  - server-authoritative entry points `TryPunchIn(Character)` / `TryPunchOut(Character)`
    modeled on the existing `TrySetAssignmentWage` pattern (`!IsServer` guard,
    authorization check, delegates to existing `WorkerStartingShift` / `WorkerEndingShift`).
- `BTAction_Work` — walk to the clock's `GetInteractionPosition()` instead of
  anywhere in `BuildingZone`; NPC calls `Interact(self)` on the clock, which
  queues `Action_PunchIn` via the shared code path.
- `BTAction_PunchOut` — same, reversed: after inventory cleanup, walk to the
  clock and interact.
- Networking: `Character.RequestPunchAtTimeClockServerRpc(NetworkObjectReference buildingRef)`
  so a player's client-side `Interact()` mutates server state through a
  ServerRpc, not directly on the client.
- Soft fallback when a building has no Time Clock authored yet: one-time
  warning log + legacy zone-punch so existing scenes keep working.
- Doc updates (rules #28 + #29b): `.agent/skills/job_system/SKILL.md`,
  `.agent/skills/building_system/SKILL.md`, `wiki/systems/commercial-building.md`.

### Out

- No new `CharacterAction` classes (explicit user ask: reuse `Action_PunchIn` / `Action_PunchOut`).
- No changes to save / load surface — punch state is already transient.
- No scene-authoring sweep — placing clocks inside each existing
  `CommercialBuilding` prefab / scene is a separate follow-up. Until authored,
  the soft fallback takes over.

## Architecture

### File map

| Path | Change |
| ---- | ------ |
| `Assets/Scripts/World/Furniture/FurnitureTag.cs` | edit — add `TimeClock` |
| `Assets/Scripts/World/Furniture/TimeClockFurniture.cs` | new |
| `Assets/Scripts/Interactable/TimeClockFurnitureInteractable.cs` | new |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | edit — `TimeClock` property + `TryPunchIn/Out` + IsServer guards |
| `Assets/Scripts/Character/Character.cs` | edit — `RequestPunchAtTimeClockServerRpc` |
| `Assets/Scripts/AI/Actions/BTAction_Work.cs` | edit — target clock |
| `Assets/Scripts/AI/Actions/BTAction_PunchOut.cs` | edit — target clock |
| `.agent/skills/job_system/SKILL.md` | edit |
| `.agent/skills/building_system/SKILL.md` | edit |
| `wiki/systems/commercial-building.md` | edit — bump `updated:` + changelog |

### Player flow

1. Player walks up to the Time Clock; `PlayerInteractionDetector` shows the
   prompt (delegated to `InteractableObject.interactionPrompt`, but the
   interactable rewrites it per frame to `"Punch In"` or `"Punch Out"` based
   on `building.IsWorkerOnShift(player)`).
2. Press E → `TimeClockFurnitureInteractable.Interact(player)` runs on the
   player's local client.
3. Local eligibility short-circuit: if the player is not an employee of the
   parent `CommercialBuilding`, raise a toast via the existing
   `ToastNotificationChannel` on `FurnitureInteractable` and bail.
4. Otherwise: call `player.RequestPunchAtTimeClockServerRpc(building.NetworkObject)`.
5. Server receives the RPC, re-validates authority + employment + clock
   proximity, then `player.CharacterActions.ExecuteAction(new Action_PunchIn(player, building))`
   on the server.
6. `Action_PunchIn.OnApplyEffect` → `workplace.WorkerStartingShift(player)`.
   `_activeWorkerIds` (replicated `NetworkList<FixedString64Bytes>`),
   `_punchInTimeByWorker`, and the quest auto-claim subscription all update
   on the server; clients automatically observe the on-shift change through
   the replicated list (no separate replication path needed for the
   roster).

### NPC flow

NPCs already live on the server, so there is no ServerRpc hop.

1. `BTAction_Work` enter: phase = `MovingToBuilding`.
2. New sub-phase `MovingToTimeClock`: once the NPC is inside `BuildingZone`
   and `building.TimeClock != null`, set destination to
   `building.TimeClock.GetInteractionPosition()`.
3. Arrived at the clock → `clock.Interactable.Interact(self)`. Because we're
   on the server and the interactable is shared between player and NPC, the
   Interact branch just calls the action directly (no ServerRpc self-hop).
4. Phase advances to `Working` when `building.IsWorkerOnShift(self)` flips true.
5. `BTAction_PunchOut` mirrors it — `MovingToTimeClock` between
   `CleaningUpInventory` and `PunchingOut`.
6. Fallback: if `building.TimeClock == null`, emit the one-time warning and
   keep the legacy "punch anywhere in BuildingZone" code path alive. Rule #4
   — never silently skip complexity.

### Eligibility + authorization

- **Employee check:** `Character.CharacterJob.ActiveJobs.Any(a => a.Workplace == building)`.
  Non-employees see `"{name} doesn't work here"` toast and are ignored.
- **Proximity check (server):** on the server path, require the character
  to be within `clock.InteractionZone.bounds`. This is the same shape of
  check `BTAction_Work` already does for the BuildingZone, just scoped to
  the clock.
- **On/off shift check:** `building.IsWorkerOnShift(interactor)` — reads the
  replicated `_activeWorkerIds` NetworkList, so the answer is identical on
  server and clients. Picks Punch In vs Punch Out — no user-facing error
  for "already on/off shift".

## Networking (rules #18 / #19)

- `WorkerStartingShift` / `WorkerEndingShift` are server-mutating. Add
  defensive `IsServer` guards to `TryPunchIn/Out` (mirrors
  `TrySetAssignmentWage`). Offline / solo keeps working because the guard
  allows the call when `NetworkManager.Singleton == null || !IsListening`.
- **Host ↔ Client:** client's `Interact()` routes through the ServerRpc; host
  runs the action; `_jobWorkerIds` NetworkList + existing animation
  replication propagate the visible state change to all peers.
- **Client ↔ Client:** both clients hop to the host via ServerRpc; host
  arbitrates.
- **Host / Client ↔ NPC:** NPC path runs on server directly.
- `Action_PunchIn` / `Action_PunchOut` themselves are not networked; they
  ride on the existing `CharacterActions` replication (animations already
  visible today for NPCs).

## Edge cases

- **Clock is occupied** by another character in the middle of their punch
  animation: `FurnitureInteractable.Interact` already returns early on
  `IsOccupied`. Second character waits naturally.
- **Punch-in while already on shift** (or reverse): the interactable picks
  the matching action from the shift state — no duplicate punches possible.
- **NPC can't reach clock** (NavMesh broken / clock despawned mid-walk):
  BT falls back to `MovingToBuilding`-only + zone-punch after a short
  timeout, same way the existing code falls through `HasPath` checks.
- **Clock destroyed mid-shift** (furniture pickup): `Furniture.Release()` in
  `PickUp` already clears occupant; next NPC punch cycle sees `TimeClock == null`
  and takes the fallback. Not blocking.

## Doc updates (rules #28, #29b)

- `.agent/skills/job_system/SKILL.md`: note that punch-in/out now requires
  touching a Time Clock furniture + link to `FurnitureInteractable` and
  `BTAction_Work`.
- `.agent/skills/building_system/SKILL.md`: add `TimeClockFurniture` to the
  furniture-type catalog.
- `wiki/systems/commercial-building.md`: bump `updated:`, add a change-log
  line, update the `Key classes / files` section.

## Open questions

None — all defaults (soft fallback, NPC-goes-through-Interact, employee-only
eligibility) were approved by the user in chat.
