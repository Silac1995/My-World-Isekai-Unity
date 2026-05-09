# Help Wanted + Owner-Controlled Hiring — Smoketest

**Date:** 2026-04-30
**Plan:** [docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md](../plans/2026-04-30-help-wanted-and-owner-hiring.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

This smoketest validates the Help Wanted + Owner-Controlled Hiring primitives end-to-end on an existing `HarvestingBuilding` (no FarmingBuilding dependency — that lands in Plan 3). The primitives consist of: `DisplayTextFurniture` + sibling NetSync (Task 1), `_isHiring` NetworkVariable + `_helpWantedFurniture` reference (Task 2), hiring API (`TryOpenHiring`/`TryCloseHiring`/`CanRequesterControlHiring`/`GetVacantJobs`/`GetHelpWantedDisplayText`) (Task 3), sign auto-update on hiring + vacancy churn (Task 4), `InteractionAskForJob` + `BuildingManager.FindAvailableJob` IsHiring gates (Task 5), `UI_DisplayTextReader` (Tasks 6+7), `UI_OwnerHiringPanel` + menu integration (Task 8).

## Setup

- Open or create a test scene with a `HarvestingBuilding` prefab. The Lumber Mill or any commercial building works.
- The HarvestingBuilding must have an Owner (a hired NPC OR a player). Note the Owner's reference for the Manage-Hiring panel test.
- Drop a `DisplayTextFurniture_Placard.prefab` instance inside the building's transform tree (you'll need to create this prefab if it doesn't exist yet — it should have both `DisplayTextFurniture` and `DisplayTextFurnitureNetSync` components on the same GameObject, plus a NetworkObject inherited from the Furniture_prefab base). Set `_initialText` to "Welcome to the lumber mill" or any default string.
- On the HarvestingBuilding's CommercialBuilding component, set `_helpWantedFurniture` to reference the placard you just placed. Set `_initialHiringOpen = true`.
- Save the scene.

## Smoke A — Static sign reads correctly

- [ ] Walk a player up to the placard. Press E (canonical Furniture interaction).
- [ ] **Assert**: the `UI_DisplayTextReader` panel opens.
- [ ] **Assert**: title shows the building name.
- [ ] **Assert**: body shows the placard's current `DisplayText`.
- [ ] Press ESC, click the overlay, click the X — each closes the panel.

## Smoke B — Help Wanted text auto-populates on hiring open

- [ ] In the Console, call `building.TryOpenHiring(building.Owner)`.
- [ ] **Assert**: `building.IsHiring == true` (already was `true` since `_initialHiringOpen=true`; this is idempotent).
- [ ] Force a hiring close + reopen to trigger fresh sign text:
  ```csharp
  building.TryCloseHiring(building.Owner);
  building.TryOpenHiring(building.Owner);
  ```
- [ ] **Assert**: the placard's `DisplayText` now reads "Hiring at <BuildingName>:\n• N <JobTitle> ...\nApproach the owner to apply." (formatted by `GetHelpWantedDisplayText`).
- [ ] Open the reader UI again — body should match. Apply button is visible (sign is help-wanted + IsHiring).

## Smoke C — Closing hiring reverts sign + blocks applications

- [ ] `building.TryCloseHiring(building.Owner)`.
- [ ] **Assert**: `building.IsHiring == false`.
- [ ] **Assert**: placard's `DisplayText` = `GetClosedHiringDisplayText()` (default empty string — sign goes blank).
- [ ] Open reader UI on the placard: body is empty, Apply button is hidden.
- [ ] As a player without a job, attempt to apply via the canonical hold-E hiring menu on the Owner — `Apply for {JobTitle}` entry should not be selectable (or returns false on confirm).
- [ ] As an NPC needing a job (`NeedJob`), evaluate `GetGoapActions`. **Assert**: the call to `BuildingManager.FindAvailableJob<Job>(true)` skips this closed building (returns the next eligible building, or null if none is hiring).

## Smoke D — Reopening hiring restores sign

- [ ] `building.TryOpenHiring(building.Owner)`.
- [ ] **Assert**: placard text = formatted vacancy text again (auto-updated by `HandleHiringStateChanged`).
- [ ] **Assert**: applications work again — both player path and NPC path succeed.

## Smoke E — Vacancy churn refreshes sign

- [ ] With hiring open and 2 vacant Harvester slots, hire one NPC (use existing dev tools or `building.AskForJob` directly).
- [ ] **Assert**: placard text now shows "1 Harvester" (count decremented; refresh fired by `AssignWorker → HandleVacancyChanged` from Task 4).
- [ ] Have the hired NPC quit (`worker.CharacterJob.QuitJob(workerJob)`).
- [ ] **Assert**: placard text shows "2 Harvesters" again (refresh fired by `QuitJob → NotifyVacancyChanged → HandleVacancyChanged`).

## Smoke F — Owner-only authority

- [ ] Set up a player who is NOT the Owner (e.g. the Owner is a different NPC).
- [ ] In the Console: `bool ok = building.TryOpenHiring(player.Character);`
- [ ] **Assert**: `ok == false` (returns optimistic `true` on client wrapper, but server-side `ServerTryOpenHiring` rejects via `CanRequesterControlHiring`). Verify by checking `building.IsHiring` did NOT flip if it was already closed.
- [ ] Same for `TryCloseHiring` from a non-owner.
- [ ] Close hiring, then have a non-owner attempt: state stays closed.

## Smoke G — Custom sign text from owner

- [ ] As the player who IS the Owner, walk up to ANY character (an employee NPC works) and press hold-E to open the interaction menu.
- [ ] **Assert**: a "Manage Hiring..." entry appears in the menu (Section B of `GetInteractionOptions`).
- [ ] Click it — `UI_OwnerHiringPanel` opens for the player's owned building.
- [ ] **Assert**: the panel shows: building name, current hiring status, scrollable job list with rows like "Harvester — vacant" / "Harvester — Bob".
- [ ] Type "Come find me at the back tent for an interview." in the custom text input. Click Submit.
- [ ] **Assert**: placard's `DisplayText` now shows the custom string. Walk up to the placard and press E to verify visually.
- [ ] Close hiring, then reopen via the panel's toggle button.
- [ ] **Assert**: placard text reverted to auto-formatted vacancy text. Custom text is GONE — Q15.1 invariant: custom text doesn't survive a reopen. The hint label below the input ("Custom text resets when hiring is reopened.") should have warned the player.

## Smoke H — Multi-peer replication

Multiplayer test: 1 Host + 1 Client (parrelsync or two machines).

- [ ] Both peers in the same scene with the test HarvestingBuilding.
- [ ] On the Host, call `building.TryOpenHiring(building.Owner)`.
- [ ] On both Host and Client, walk up to the placard and press E.
- [ ] **Assert**: both peers see the same `DisplayText` (formatted vacancy text). NetworkVariable replication confirmed.
- [ ] On the Host, call `building.TryCloseHiring(building.Owner)`.
- [ ] **Assert**: Client's `building.IsHiring` is now `false` within one frame (NetworkVariable sync).
- [ ] **Assert**: Client's placard text is now blank.
- [ ] **Reverse direction:** if the Owner is a Client-controlled player, have THAT client call `TryOpenHiring(localPlayer.Character)`. The optimistic local return is `true`, but the actual write happens server-side via the `[Rpc(SendTo.Server)]` ServerRpc.
- [ ] **Assert**: Host sees `_isHiring.Value == true`, sign refreshes on Host, replicates back to Client.
- [ ] **Late-joiner test:** join a new client AFTER the sign has custom text set. The new client should see the current text on first frame (NetworkVariable spawn handshake).

## Result

When all 8 smokes pass, mark the file's Status as **Pass** and add a final summary line. Then commit:

```bash
git add docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md
git commit -m "test(help-wanted): smoketest pass — primitive validated on HarvestingBuilding"
```

If any smoke fails, file a fix task and iterate. The most likely failure modes:
- **Smoke E vacancy refresh** — if `HandleVacancyChanged` doesn't fire on hire, double-check that `AssignWorker` (CommercialBuilding.cs) calls `HandleVacancyChanged()` AFTER the worker is bound (Task 4 §3).
- **Smoke G owner-only menu** — if the "Manage Hiring..." entry doesn't appear, verify `interactor.CharacterJob.OwnedBuilding != null` is true for the player. If the player isn't shown as the OwnedBuilding's owner, that's a separate bug (likely in `CharacterJob.OwnedBuilding` derivation from `Room._ownerIds`).
- **Smoke H multi-peer** — if NetworkVariable doesn't replicate, verify `DisplayTextFurnitureNetSync` is on the SAME GameObject as `DisplayTextFurniture` (it requires `[RequireComponent(typeof(DisplayTextFurniture))]`) and that the GameObject has a NetworkObject.
