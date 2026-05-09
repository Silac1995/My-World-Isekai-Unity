# Management Furniture + Application Flow — Smoketest

**Date:** 2026-04-30
**Plan:** [docs/superpowers/plans/2026-04-30-management-furniture-and-application-flow.md](../plans/2026-04-30-management-furniture-and-application-flow.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

This smoketest validates the three Plan 2.5 refinements: `ManagementFurniture` (owner's hiring desk), Apply-button removal from `UI_DisplayTextReader` (sign is informative-only), and `NeedJob` `OnNewDay` event-driven throttle. Run on the same `HarvestingBuilding` test scene used for Plans 1+2.

## Setup

- HarvestingBuilding with Owner set; `_helpWantedFurniture` and `_toolStorageFurniture` already wired (Plan 1+2 setup).
- **NEW:** Drop a `ManagementFurniture` prefab inside the building's transform tree. Use the `Furniture_prefab` base + add the `ManagementFurniture` component.
- On the building's `CommercialBuilding` component, set `_managementFurniture` to reference the new desk.
- Save the scene.

## Smoke A — Management furniture replaces menu entry

- [ ] As the Owner-player, walk up to a non-owner NPC. Press hold-E to open the interaction menu.
- [ ] **Assert**: "Manage Hiring..." entry is **NOT** in the menu (Plan 2.5 Task 3 gates Section B on `!HasManagementFurniture`).
- [ ] Walk up to the Management Furniture (the new desk). Press E.
- [ ] **Assert**: `UI_OwnerHiringPanel` opens for the building.

## Smoke B — Non-owner gets denial toast

- [ ] As a non-owner player, walk up to the Management Furniture. Press E.
- [ ] **Assert**: A toast appears at the top of the screen: "Only the owner can use this management desk."
- [ ] **Assert**: `UI_OwnerHiringPanel` does NOT open.

## Smoke C — Sign is informative-only (no Apply button)

- [ ] Walk a player up to the Help Wanted placard. Press E.
- [ ] **Assert**: `UI_DisplayTextReader` opens with title + body text.
- [ ] **Assert**: NO "Apply for a job" button anywhere in the panel.
- [ ] **Assert**: The body text ends with "For application, see the owner in person." (or whichever finalised wording the implementer chose).

## Smoke D — Player application requires walking to boss

- [ ] As a player without a job, read the Help Wanted sign (it tells you to see the owner).
- [ ] Walk to the Owner NPC. Press hold-E to open the menu.
- [ ] **Assert**: "Apply for {JobTitle}" entry appears (existing 2026-04-24 path).
- [ ] Click it. The `InteractionAskForJob` social interaction runs.
- [ ] **Assert**: Player is hired (assuming `IsHiring == true` and the position is open).

## Smoke E — Fallback menu entry when no management furniture

- [ ] On a different building (or temporarily unset `_managementFurniture` to null on this one), confirm the fallback path:
- [ ] As the Owner-player, walk up to any NPC. Press hold-E.
- [ ] **Assert**: "Manage Hiring..." entry appears (because `HasManagementFurniture == false`).
- [ ] Click it → panel opens correctly.
- [ ] Restore the `_managementFurniture` reference if you unset it.

## Smoke F — NPC NeedJob OnNewDay throttle

- [ ] Setup: an unemployed NPC with `NeedJob` active. A vacant Harvester position at a building with `IsHiring == true` and an Owner.
- [ ] Pre-day: pause the game on day N. Inspect the NPC's `NeedJob.GetUrgency()` via debug or dev-mode inspector.
- [ ] **Assert**: returns 0 (cache empty — Need is dormant).
- [ ] Trigger a new day via `TimeManager.AdvanceToNextDay()` (or whatever the dev-mode time-skip control is).
- [ ] **Assert**: Console shows `[NeedJob]` log line: "OnNewDay scan → cached <BuildingName>/<JobTitle>." (gated behind `NPCDebug.VerboseJobs` — toggle that flag if needed).
- [ ] **Assert**: `NeedJob.GetUrgency()` now returns `BASE_URGENCY`.
- [ ] Verify the NPC plans an Apply via `GetGoapActions()` (returns `GoapAction_GoToBoss + GoapAction_AskForJob`) and physically walks to the boss.
- [ ] **Assert**: NPC successfully applies + is hired (the `InteractionAskForJob` flow runs as before).

## Smoke G — Mid-day staleness handled cleanly

- [ ] Setup: TWO unemployed NPCs both with `NeedJob`. ONE vacant Harvester slot at a building.
- [ ] Trigger OnNewDay. Both NPCs cache the same `(building, job)` pair (their scans are independent but find the same candidate).
- [ ] First NPC walks to boss + gets hired. The job is now `IsAssigned == true`.
- [ ] Second NPC's `GetGoapActions` runs the staleness re-validation (`_cachedJob.IsAssigned || !_cachedBuilding.IsHiring || !_cachedBuilding.HasOwner`).
- [ ] **Assert**: Returns empty list. NPC idles silently.
- [ ] **Assert**: Console shows `[NeedJob]` warning (orange): "{Name} cached job stale; idling until next day."
- [ ] Wait until next OnNewDay (or trigger via dev tool).
- [ ] **Assert**: Second NPC re-scans on the new day. If a different vacancy exists (or the first NPC's hire opened a new position elsewhere), they cache that one and apply. If no vacancies remain, cache stays empty + Need stays dormant.

## Smoke H — Performance regression check (manual / profiler)

- [ ] With 10+ unemployed NPCs in a scene with `NeedJob` (use dev-mode spawn to populate), profile a 1-minute window in Unity Profiler with Deep Profile + Allocation Tracking enabled.
- [ ] **Assert**: `BuildingManager.FindAvailableJob` is called at most 10 times per in-game day (once per NPC per OnNewDay), NOT 10 × N-frames-per-day.
- [ ] **Assert**: `NeedJob.GetUrgency` is sub-microsecond when cache is empty (single null-check + return). `GetGoapActions` early-exits cheaply when cache is empty.
- [ ] If the FindAvailableJob count is still per-tick: check that `TrySubscribe` succeeded (NPCDebug log should show the OnNewDay scan log line at least once per day). Likely cause: subscribe failed because `NetworkManager.Singleton` wasn't ready at OnNetworkSpawn time and the lazy retry from `IsActive()` isn't firing.

## Smoke I — Multi-peer behaviour

- [ ] Multiplayer setup: 1 Host + 1 Client.
- [ ] On the Host, an unemployed NPC's `NeedJob` cache populates server-side on OnNewDay.
- [ ] **Assert**: Client does NOT run its own subscription (server-only gate inside `TrySubscribe`).
- [ ] **Assert**: GOAP planning runs server-only; the NPC's behaviour replicates to the client via existing AI sync.
- [ ] **Assert**: `ManagementFurniture` interaction still requires the LOCAL owner-player to press E (the IsOwner gate inside `Use`); remote clients seeing the press-E event do not pop a UI on their own machine.

## Result

When all 9 smokes pass, mark the file's Status as **Pass** and add a final summary line. Then commit:

```bash
git add docs/superpowers/smoketests/2026-04-30-management-furniture-and-application-flow-smoketest.md
git commit -m "test(plan-2.5): smoketest pass — management furniture + apply flow validated"
```

If any smoke fails, common root causes:
- **Smoke A or E menu visibility** — `HasManagementFurniture` may not be returning what you expect; verify by setting `_managementFurniture = null` temporarily and confirming the menu entry appears.
- **Smoke F cache never populates** — likely `TrySubscribe` failed because `NetworkManager.Singleton.IsServer` was false on the calling peer; verify the NPC is server-spawned and not a client-only mirror.
- **Smoke G staleness check** — verify the re-validation also checks `IsHiring` (not just `IsAssigned`), so a closed building doesn't keep producing actions.
