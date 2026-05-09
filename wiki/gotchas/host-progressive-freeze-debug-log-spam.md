---
type: gotcha
title: "Host progressive freeze from per-tick Debug.Log spam (Windows)"
tags: [performance, logging, server-authoritative, host, unity-editor, windows]
created: 2026-04-24
updated: 2026-04-25
sources:
  - "Assets/Scripts/AI/NPCDebug.cs"
  - "Assets/Scripts/Character/CharacterGoapController.cs"
  - "Assets/Scripts/AI/GOAP/GoapPlanner.cs"
  - "2026-04-24 conversation with Kevin — progressive host-freeze diagnosis"
related:
  - "[[ai-goap]]"
  - "[[ai-behaviour-tree]]"
  - "[[jobs-and-logistics]]"
status: mitigated
confidence: high
---

# Host progressive freeze from per-tick Debug.Log spam (Windows)

## Summary
On Windows, Unity Editor console rendering cost scales super-linearly with the number of entries retained in the buffer. A `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` call inside any server-authoritative per-tick path (BT tick, `Job.Execute`, `GoapAction.Execute`, `Update`/`FixedUpdate`) becomes a silent accumulator: the host starts fine, then progressively freezes until it renders one frame every several seconds. Clients are unaffected because they don't tick those paths. The same symptom matches "pathological CPU loop" and "memory leak" at first glance, which makes it easy to misdiagnose.

## Symptom
- Host: starts at 60 fps, gradually drops to 30, then 10, then 1, then <1. Clients stay smooth the whole time.
- Console window has thousands of entries (often from a single source) with the same stack trace repeating.
- Symptom scales with the number of NPCs / workers / interactables in the "stuck in X state" condition that triggers the log.
- Turning off the console (or building a non-development player) makes the freeze disappear.
- Profiler shows time concentrated in editor render + `EditorGUI.DoTextField` or similar, not in game code.

## Root cause
Unity's Editor console retains every log entry for the session (up to its own cap) and redraws them on every repaint. On Windows, the `UnityEditor.ConsoleWindow` redraw cost grows approximately **O(entries × avg message length)** with nontrivial constant factors. Once the entry count crosses a few thousand, each editor repaint dominates frame time. Because the editor also drives the host's game loop in play mode, the game loop itself stalls.

Compounding factors specific to this project:
- NPC BTs tick at 10Hz per NPC on the server (`NPCBehaviourTree._tickIntervalSeconds = 0.1f`).
- Jobs execute per worker per tick; Job.Execute branches into "action invalid", "no plan", "waiting for ..." code paths that can persist for seconds or minutes.
- GOAP Replan can be called twice per BT tick (OnEnter + OnExecute when `CurrentAction == null`).
- A jobless / task-less NPC can hold a "stuck" state indefinitely — any log in that branch fires at the tick rate until the state changes.

## How to avoid
- **Never put a bare `Debug.Log` inside any method reachable from `NPCBehaviourTree.Update`, `Job.Execute`, `GoapAction.Execute`, `Update`, `FixedUpdate`, `LateUpdate`, or `OnTriggerStay`/`OnCollisionStay` without a guard.** The failure modes are:
  - `if (target == null) Debug.Log(...)` — fires per tick while target is null
  - `if (!IsValid) Debug.Log(...)` — fires per tick while invalid
  - "waiting for X" logs — fire per tick while waiting
  - "can't find Y" logs — fire per tick while missing
- **Accepted guard patterns:**
  1. **Global flag.** Gate behind `NPCDebug.VerbosePlanning` / `VerboseJobs` / `VerboseActions` / `VerboseMovement` (see [NPCDebug.cs](../../Assets/Scripts/AI/NPCDebug.cs)). Default off; flip at runtime while diagnosing.
  2. **Per-instance one-shot.** `if (!_warnedX) { Debug.Log(...); _warnedX = true; }`, reset in `OnEnter` if a BT node, or in a state-change callback. Pattern example: `_warnedNoTimeClock` in [BTAction_Work.cs](../../Assets/Scripts/AI/Actions/BTAction_Work.cs).
  3. **Time rate-limit.** `if (Time.time > _lastLogTime + 5f) { Debug.Log(...); _lastLogTime = Time.time; }`. Pattern example: `_lastLogTime_NoOrder` in [JobBlacksmith.cs](../../Assets/Scripts/World/Jobs/CraftingJobs/JobBlacksmith.cs).
- **State-transition logs are OK unconditional.** "Assigned as X", "Shift ended", "Order placed", "OnNetworkSpawn". They fire on an edge, not while a condition persists — the accumulation risk is low.
- Same discipline applies to server-only code even if the project eventually ships a non-development build — it shows up as a host-only editor regression today and costs real debugging hours before anyone suspects logs.

## How to fix (if already hit)
1. **Clear the Unity console** (Clear button, disable Collapse) and reproduce the freeze from a clean session. Measure time-to-freeze.
2. **Identify the dominant log source**: let it freeze partially, then scroll the console and look at which single log message repeats the most. Its stack trace tells you which file:line to gate.
3. **Apply one of the three guard patterns above** (global flag preferred for broad hot paths, one-shot for prefab misconfiguration warnings, time rate-limit for "waiting for X" type messages).
4. Re-test with the console cleared. Frame time should stay flat.

For the historical 2026-04-24 incident, see the change log in [[ai-goap]] — the fix gated ~15 logs across `JobLogisticsManager`, `JobTransporter`, `JobHarvester`, `GoapAction_HarvestResources`, `GoapAction_LocateItem`, `BTAction_Work`, `BTAction_PunchOut`, and `CharacterGoapController`.

### 2026-04-25 follow-up — second wave caught after worker-shift trigger

A re-occurrence of the same symptom was reproduced after assigning a boss + workers to a Forge: host frame time stayed flat for several minutes, then started drifting once a transporter began running its delivery cycle. Four additional sites were gated:

- `GoapAction_LocateItem.cs:68` — `"…ignore l'item blacklisté: {wi.name}"` inside the visible-interactables loop. **Smoking gun**: scales with `PathingMemory` blacklist size, which only grows over a session, so the per-tick cost of this single line itself grows over time. Gated behind `NPCDebug.VerboseActions`.
- `GoapAction_LocateItem.cs:113` — `"…trouvé l'item hors de portée visuelle via scan de zone"` inside the storage-zone fallback. Per-tick reachable. Gated behind `NPCDebug.VerboseActions`.
- `GoapAction_LocateItem.cs:190` — `"…a assigné TargetWorldItem"` fires once per LocateItem completion. Lower cadence but scales linearly with delivery volume. Gated behind `NPCDebug.VerboseActions`.
- `CommercialBuilding.AddToInventory` — `"{itemName} ajouté à l'inventaire de {buildingName}"` fires per delivered item / crafting completion / harvest deposit. Gated behind `NPCDebug.VerboseJobs`.

Lesson reinforced: **any log inside a method reachable from `Job.Execute` / `GoapAction.Execute` / inventory-mutation flows needs a guard, even if it "only fires per delivery" — at steady-state worker activity that's still hundreds per minute.** Use the same three guard patterns documented above.

## Affected systems
- [[ai-goap]]
- [[ai-behaviour-tree]]
- [[jobs-and-logistics]]
- [[character]]

## Links
- [NPCDebug.cs](../../Assets/Scripts/AI/NPCDebug.cs) — central on/off flags
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md) §8.5 — performance rules for GOAP planning
- [[ai-goap]] — GOAP architecture, throttle semantics, scratch buffers

## Sources
- 2026-04-24 conversation with Kevin — diagnosed the progressive freeze after a false first fix; root-caused to per-tick `Debug.Log` accumulation in the Windows Unity console buffer.
- [Assets/Scripts/AI/NPCDebug.cs](../../Assets/Scripts/AI/NPCDebug.cs)
- [Assets/Scripts/Character/CharacterGoapController.cs](../../Assets/Scripts/Character/CharacterGoapController.cs)
- [Assets/Scripts/AI/GOAP/GoapPlanner.cs](../../Assets/Scripts/AI/GOAP/GoapPlanner.cs)
