# Tool Storage Primitive — Smoketest

**Date:** 2026-04-29
**Plan:** [docs/superpowers/plans/2026-04-29-tool-storage-primitive.md](../plans/2026-04-29-tool-storage-primitive.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

This smoketest validates the Tool Storage primitive end-to-end on an existing `HarvestingBuilding` (no FarmingBuilding dependency — that lands in Plan 3). The primitive consists of 7 pieces: `ItemInstance.OwnerBuildingId` (Task 1), `CommercialBuilding._toolStorageFurniture` + `WorkerCarriesUnreturnedTools` (Task 2), `GoapAction_FetchToolFromStorage` (Task 3), `GoapAction_ReturnToolToStorage` + `StorageFurniture.AddItem` origin-clear hook (Task 4), `CharacterJob.CanPunchOut` + `QuitJob` auto-return (Task 5), `CharacterSchedule.EvaluateSchedule` punch-out gate + `NotifyPunchOutBlockedClientRpc` (Task 6), `UI_ToolReturnReminderToast` (Task 7).

## Setup

- Open or create a test scene with at least one `HarvestingBuilding` prefab. Ideally the `Lumber Mill` or a simple custom test building — anything with a `Zone` for the harvesting area + a `DepositZone`.
- Inside the building's transform tree, ensure a `StorageFurniture` exists (any chest / barrel / cupboard prefab). Pre-fill it with 1 `ItemInstance` of a "tool" type — any existing `MiscSO` or weapon will do (e.g. an axe). Use the dev-mode spawn module if available, or hand-fill via the Inspector.
- On the `CommercialBuilding` (or HarvestingBuilding) component, set `_toolStorageFurniture` (the new field from Task 2) to reference the StorageFurniture from the previous step. **This is the manual wiring step that turns a regular chest into a tool storage.**
- Save the scene.
- Hire one NPC into the building so the worker has an active `CharacterJob` assignment.

## Smoke Steps

### Smoke A — GOAP fetch stamps OwnerBuildingId

- [ ] In the Unity Console (or via a temporary debug script), build a `GoapAction_FetchToolFromStorage` against the building + the tool's ItemSO and run it on the worker. Example via `mcp__ai-game-developer__script-execute` or a temporary `MonoBehaviour.Update` debug hook:
  ```csharp
  var building = GameObject.Find("HarvestingBuilding").GetComponent<CommercialBuilding>();
  var worker   = GameObject.Find("TestWorker").GetComponent<Character>();
  var toolItem = building.ToolStorage.ItemSlots[0].ItemInstance.ItemSO;

  var fetch = new GoapAction_FetchToolFromStorage(building, toolItem);
  if (!fetch.IsValid(worker)) Debug.LogError("Fetch IsValid returned false — check hands-free + storage contents.");
  // Spin Update() until IsComplete (movement + take):
  while (!fetch.IsComplete) { fetch.Execute(worker); /* simulate frame advance */ yield return null; }
  fetch.Exit(worker);
  ```
- [ ] **Assert**: `worker.CharacterVisual.BodyPartsController.HandsController.CarriedItem.OwnerBuildingId == building.BuildingId`. Inspect via the Inspector or a `Debug.Log`.
- [ ] **Assert**: the storage chest now has one fewer item (or the slot is empty if it was the only one).

### Smoke B — Punch-out gate blocks

- [ ] After Smoke A, with the worker still on shift and carrying the stamped tool, force `CharacterJob.CanPunchOut()` from the Console:
  ```csharp
  var (canPunchOut, reason) = worker.CharacterJob.CanPunchOut();
  Debug.Log($"canPunchOut={canPunchOut}, reason={reason}");
  ```
- [ ] **Assert**: `canPunchOut == false` and `reason == "Return tools to the tool storage before punching out: <ToolName> (<BuildingName>)."`
- [ ] Advance the in-game clock past the worker's shift end (use `TimeManager.SetClockTime(...)` or the time-skip dev tool).
- [ ] **Assert**: the worker stays in `ScheduleActivity.Work`. Check `worker.CharacterSchedule.CurrentActivity` in the Inspector.
- [ ] Set the test worker to be player-owned (assign client ownership via dev tools) and repeat the time advance.
- [ ] **Assert**: the toast appears at the top of the screen ("Return tools" — the tool name + building name in the body). It should auto-dismiss after ~4 seconds.

### Smoke C — Return clears + unblocks

- [ ] Run a `GoapAction_ReturnToolToStorage` against the same worker + tool:
  ```csharp
  var ret = new GoapAction_ReturnToolToStorage(building, toolItem);
  if (!ret.IsValid(worker)) Debug.LogError("Return IsValid returned false — check hand contents + OwnerBuildingId.");
  while (!ret.IsComplete) { ret.Execute(worker); yield return null; }
  ret.Exit(worker);
  ```
- [ ] **Assert**: `worker.CharacterVisual.BodyPartsController.HandsController.IsCarrying == false`.
- [ ] **Assert**: the tool is back in `building.ToolStorage` slots — count of items returned to its pre-Smoke-A state.
- [ ] **Assert**: the returned slot's `ItemInstance.OwnerBuildingId == ""` (cleared by the `StorageFurniture.AddItem` origin-match hook from Task 4).
- [ ] **Assert**: `CharacterJob.CanPunchOut()` now returns `(true, null)`.
- [ ] **Assert**: on the next schedule tick, the worker transitions out of `Work` normally.

### Smoke D — Player drop-in path clears OwnerBuildingId

- [ ] Repeat Smoke A so the worker holds a stamped tool.
- [ ] Switch control to a player character (not the NPC worker). Walk the player up to the building's tool storage chest and drop the tool in via the existing player UI (right-click → drop on chest, or whatever the canonical UI flow is).
- [ ] **Assert**: the dropped item lands in the storage with `OwnerBuildingId == ""`. This validates the `StorageFurniture.AddItem` origin-match hook works on the **non-GOAP / non-`GoapAction_ReturnToolToStorage`** path. (Important: this case is the player drop-in coverage.)

### Smoke E — Save/load round-trip

- [ ] With a worker carrying a stamped tool: trigger a save (sleep at bed / portal gate, whatever the canonical save trigger is).
- [ ] Reload the save.
- [ ] **Assert**: the carried tool's `OwnerBuildingId` is restored to the original building's BuildingId. Check via the Inspector on the worker's HandsController or inventory slot.
- [ ] **Assert**: `CanPunchOut` still blocks correctly post-load.
- [ ] (Bonus) Repeat the save/load test where the tool is in the worker's INVENTORY (not hand) — confirm the marker survives there too.

### Smoke F — Worker fired mid-shift returns tools

- [ ] With a worker carrying a stamped tool, force-quit their job via `worker.CharacterJob.QuitJob(workerJob)` from the Console.
- [ ] **Assert**: the tool was auto-returned to the building's storage. Verify by inspecting the storage's slot count + the worker's hand/inventory (should no longer hold the tool).
- [ ] **Assert**: the returned slot's `ItemInstance.OwnerBuildingId == ""`.

### Smoke G — Storage destroyed mid-shift falls back gracefully

- [ ] With a worker carrying a stamped tool, destroy the building's `_toolStorageFurniture` GameObject via `DestroyImmediate(building.ToolStorage.gameObject)` (dev console).
- [ ] Trigger a `QuitJob` on the worker.
- [ ] **Assert**: the worker still has the tool in their inventory / hand, but `OwnerBuildingId == ""` (cleared by the fallback path in `TryAutoReturnTools`). A LogWarning should appear in the console.
- [ ] **Assert**: `CanPunchOut` returns `(true, null)` immediately — the worker is not permanently gated.

## Notes

Capture observations, screenshots, or unexpected behaviour here:

- _(your notes)_

## Result

When all smokes pass, mark the file's Status as **Pass** and add a final summary line. Then commit:
```bash
git add docs/superpowers/smoketests/2026-04-29-tool-storage-primitive-smoketest.md
git commit -m "test(tool-storage): smoketest pass — primitive validated on HarvestingBuilding"
```
