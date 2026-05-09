---
type: system
title: "World Time Skip"
tags: [world, time-skip, macro-sim, bed-furniture, hibernation]
created: 2026-04-27
updated: 2026-04-28
sources: []
related:
  - "[[world]]"
  - "[[world-macro-simulation]]"
  - "[[world-map-hibernation]]"
  - "[[character-needs]]"
  - "[[jobs-and-logistics]]"
  - "[[save-load]]"
  - "[[kevin]]"
status: wip
confidence: high
primary_agent: world-system-specialist
secondary_agents:
  - save-persistence-specialist
  - building-furniture-specialist
owner_code_path: "Assets/Scripts/DayNightCycle/"
depends_on:
  - "[[world-macro-simulation]]"
  - "[[world-map-hibernation]]"
  - "[[character-needs]]"
  - "[[save-load]]"
depended_on_by: []
---

# World Time Skip

## Summary
Server-authoritative path that advances the in-game clock by **N hours (1–168)** without running the live simulation for that span. The active map is hibernated, [[world-macro-simulation]]'s new per-hour entry point (`SimulateOneHour`) runs once per hour, the clock advances, then the map wakes. Coexists with `GameSpeedController` — that one scales `Time.timeScale` so live NPCs tick faster; this one *freezes* live NPCs entirely and progresses state via pure macro-sim math. v1 is gated to single-player; v2 will auto-trigger when all connected players are simultaneously occupying a bed slot.

## Purpose
"Sleep until 06:00 next day" and "skip 8 hours while I'm AFK" cannot use `GameSpeedController` honestly: even at 8× the live simulation still pays the per-frame cost of every NPC's GOAP, every job, every physics step, every NetworkVariable replication. Time skip flips the contract — for the duration of the skip, the world is **macro-simulated math over serialized data**, identical to the path taken when a hibernated map wakes up after a long absence. It's the same mental model already in [[world-macro-simulation]], just initiated by a player request instead of a player-distance trigger.

The bed half of the feature ([[bed-furniture]]) provides one of the three v1 triggers and is the entity that will, in v2, automatically gate the multiplayer auto-skip.

## Responsibilities
- Owning the time-skip lifecycle on the server: validation, hibernate, per-hour loop, wake, restore, save.
- Iterating the skip in 1-hour steps so cumulative effects (needs decay, resource regen, schedule snapping) accumulate at the same granularity the live simulation would.
- Day-boundary gating for steps that integrate over `daysPassed` so they don't floor-to-zero on every hour-grained call.
- Hibernating the active map without the redundant single-pass catch-up that normal `WakeUp` does (`_pendingSkipWake` flag).
- Routing all triggers (`/timeskip` chat command, `DevTimeSkipModule` dev panel, future `UI_BedSleepPrompt`) through one entry point: `TimeSkipController.Instance.RequestSkip(hours)`.
- Driving the bed sleep lifecycle: `BedFurniture.UseSlot` → `Character.EnterSleep(anchor)` → `Character.NetworkIsSleeping = true` → `PlayerController.Update` early-out.

**Non-responsibilities**:
- Does **not** implement live-fast-forward (approach B from the design brainstorm) — only hibernate-skip-wake (approach A).
- Does **not** persist `_isSleeping` across saves — players wake on load. Documented as out-of-scope; not added to `ICharacterSaveData<T>`.
- Does **not** replay combat or "danger" events that would have happened during the skip. Auto-abort fires only on player death.
- Does **not** simulate hibernated maps that aren't the active one — they continue to wake on player approach via the normal [[world-macro-simulation]] catch-up path.

## Key classes / files

| File | Role |
|------|------|
| [TimeSkipController.cs](../../Assets/Scripts/DayNightCycle/TimeSkipController.cs) | Server-authoritative singleton `NetworkBehaviour`. Owns `RequestSkip(hours)`, `RequestAbort()`, the per-hour coroutine, and the three lifecycle events. |
| [TimeManager.cs](../../Assets/Scripts/DayNightCycle/TimeManager.cs) | Provides `AdvanceOneHour()` — clock advance with the same event semantics as live `ProgressTime`. |
| [MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) | New `SimulateOneHour(MapSaveData, currentDay, currentTime01, JobYieldRegistry, previousHour)` entry point. `ApplyNeedsDecayHours` and `SnapPositionFromSchedule` extracted as helpers shared with `SimulateCatchUp`. |
| [MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) | `HibernateForSkip()` + `_pendingSkipWake` flag + `WakeUpFromSkip()` public wrapper. The wake path skips the redundant single-pass catch-up when `_pendingSkipWake` is set. |
| [BedFurniture.cs](../../Assets/Scripts/World/Furniture/BedFurniture.cs) | `Furniture` subclass with serialized `List<BedSlot>` and slot-aware lifecycle (`UseSlot`, `ReleaseSlot`, `FindFreeSlotIndex`, `GetSlotIndexFor`, `ReserveSlot`). |
| [Character.cs](../../Assets/Scripts/Character/Character.cs) | `NetworkIsSleeping` `NetworkVariable<bool>` + `EnterSleep(Transform)` / `ExitSleep()` server methods. Reuses the existing `ConfigureNavMesh(bool)` helper. |
| [PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs) | `Update()` early-outs on `Character.IsSleeping`, locking out keyboard/mouse for the owning player while asleep. |
| [SleepBehaviour.cs](../../Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs) | NPC routing — uses `BedFurniture.UseSlot`/`ReleaseSlot` when the bed is a `BedFurniture`; legacy plain-`Furniture` fallback preserved. |
| [DevChatCommands.cs](../../Assets/Scripts/Debug/DevMode/DevChatCommands.cs) | `/timeskip <hours>` command surface. |
| [DevTimeSkipModule.cs](../../Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs) | Dev-mode tab module (script only — prefab tab is manual setup). |
| [UI_TimeSkipOverlay.cs](../../Assets/Scripts/UI/UI_TimeSkipOverlay.cs) | Fade overlay with cancel button + live hour readout (script only — prefab is manual setup). |
| [UI_BedSleepPrompt.cs](../../Assets/Scripts/UI/UI_BedSleepPrompt.cs) | Bed-triggered modal with a 1–168 h slider (script only — prefab is manual setup). |

## Public API / entry points

- `TimeSkipController.Instance.RequestSkip(int hours)` — server-only. Returns `bool` (`true` = started, `false` = rejected). Validates (`hours ∈ [1, 168]`, single-player gate, not already skipping).
- `TimeSkipController.Instance.RequestAbort()` — server-only. Lets the in-flight per-hour coroutine finish the current hour, then exits cleanly.
- `TimeSkipController.OnSkipStarted(int hours)` — server-side event, fires at start.
- `TimeSkipController.OnSkipHourTick(int elapsedHours, int totalHours)` — server-side event, fires once per hour after the macro-sim pass.
- `TimeSkipController.OnSkipEnded()` — server-side event, fires when the skip ends (completed or aborted).
- `TimeManager.AdvanceOneHour()` — clock advance step. Same event semantics as live `ProgressTime`.
- `MacroSimulator.SimulateOneHour(MapSaveData data, int currentDay, float currentTime01, JobYieldRegistry yields, int previousHour)` — pure-math per-hour entry. `previousHour` is used to detect day-boundary crossings.
- `MapController.HibernateForSkip()` — flushes the active map into `HibernatedNPCData`/`HibernatedItemData` exactly like the normal hibernation path, but sets `_pendingSkipWake = true` so the next `WakeUp` does not run the single-pass catch-up over the same delta we just simulated hour-by-hour.
- `MapController.WakeUpFromSkip()` — public wrapper that calls `WakeUp()` after the skip's last hour. Symmetrical entry point so call sites read clearly.
- `BedFurniture.UseSlot(int slotIndex, Character character)` / `ReleaseSlot(int slotIndex)` / `FindFreeSlotIndex()` / `ReserveSlot(int, Character)` / `GetSlotIndexFor(Character)` — slot-aware lifecycle.
- `Character.EnterSleep(Transform anchor)` / `Character.ExitSleep()` — server-only. Snap position+rotation, toggle NavMeshAgent via `ConfigureNavMesh(bool)`, raise `OnSleepStateChanged`.
- `Character.NetworkIsSleeping` / `Character.IsSleeping` — replicated state. `PlayerController.Update` reads `IsSleeping`.

## Data flow

```
Trigger (DevTimeSkipModule | /timeskip cmd | UI_BedSleepPrompt)
        │  (client → ServerRpc, or already on server)
        ▼
TimeSkipController.RequestSkip(hours)        — server only
        │  validation: hours ∈ [1, 168], not already skipping,
        │  ConnectedClients.Count == 1 (v1 gate)
        ▼
OnSkipStarted(hours)                         — UI subscribes, fades to black
        │
        ▼
MapController.HibernateForSkip()             — _pendingSkipWake = true
        │  serialize NPCs/items, despawn live prefabs
        ▼
for (h = 1; h ≤ hours; ++h)                  — per-hour coroutine
    TimeManager.AdvanceOneHour()             — clock + OnNewHour event
    MacroSimulator.SimulateOneHour(           — pure math over MapSaveData
        data, currentDay, currentTime01,
        jobYields, previousHour)
    OnSkipHourTick(h, hours)                 — UI updates readout
    if (_aborted) break
        │
        ▼
MapController.WakeUpFromSkip()               — wake; skips redundant catch-up
        │  respawn NPCs/items at updated state
        ▼
restore players (position, sleeping=false)
        │
        ▼
SaveManager.SaveAll()                        — checkpoint
        │
        ▼
OnSkipEnded                                  — UI fades back in
```

**Authority:** every step above runs on the server. Clients see the result via `NetworkVariable` replication (clock, `Character.NetworkIsSleeping`) and ClientRpc fan-out from `TimeSkipController` lifecycle events.

### Day-boundary gating inside `SimulateOneHour`

Cumulative steps that integrate over `daysPassed` (resource pool regeneration, biome-driven inventory yields, zone motion, city growth) cannot run unconditionally per hour — `daysPassed` for a one-hour delta floors to 0 and the step becomes a no-op every call. Instead, `SimulateOneHour` compares the **previous hour** to the **current hour**: when the boundary from hour 23 → 0 is crossed, the cumulative steps run with `daysPassed = 1`. Hour-grained steps (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule`) run every hour because they're already scaled per-hour internally.

This is the central correctness invariant for the per-hour entry point. Any new step added to the macro-sim must be classified as **hour-grained** or **day-grained** and placed accordingly.

## Dependencies

### Upstream (this system needs)
- [[world-macro-simulation]] — `SimulateOneHour` is a sibling entry point to `SimulateCatchUp`. Without macro-sim, there's nothing to call per-hour.
- [[world-map-hibernation]] — `HibernateForSkip` reuses the existing serialization/despawn path. Without hibernation, the live simulation would still tick during the skip.
- [[character-needs]] — `ApplyNeedsDecayHours` is the per-hour version of the offline-decay formula owned by [[character-needs]].
- [[save-load]] — checkpoint at the end of the skip uses the existing `SaveManager.SaveAll()` path.
- [[bed-furniture]] — provides one of the three v1 triggers and the v2 multiplayer gate (sleeping = consent).

### Downstream (systems that need this)
None today. Future quest hooks ("sleep until dawn before the boss fight") will consume `OnSkipStarted` / `OnSkipEnded`.

## State & persistence

- **Runtime state (server only):** `TimeSkipController.IsSkipping`, `_aborted`, the running coroutine, the `_pendingSkipWake` flag on the active `MapController`.
- **Replicated state:** `Character.NetworkIsSleeping` (per character). The clock itself is already replicated via `TimeManager`.
- **Persisted state:** none specific to the skip — at the end of the skip, `SaveManager.SaveAll()` flushes the new clock value, the post-skip `MapSaveData`, and every character's persistent state (`ICharacterSaveData<T>` chain). `_isSleeping` is intentionally **not** persisted; reload always wakes the player. The `BedSlot.Occupant` reference is runtime-only and not saved.
- **NPC sleep pose across map hibernation:** out of scope. NPCs that were sleeping when the map hibernates wake into idle on respawn.

## Manual setup checklist

The implementation lands as scripts only — the Editor wiring is manual. Without these steps the feature exists in code but cannot be exercised in a play session.

1. **Add `TimeSkipController` component to the same GameObject as `GameSpeedController`** in the active gameplay scene. (`GameSpeedController` is currently not in any tracked scene file under `Assets/Scenes/` — locate it in your working scene.) Save the scene.
2. **Build the `Tab_TimeSkip` content GameObject in `DevModePanel.prefab`:**
   - Open the prefab.
   - Duplicate an existing tab content (e.g. `Tab_Spawn`) → rename `Tab_TimeSkip` → strip its inner widgets.
   - Add child `TMP_InputField` named `HoursInput` (placeholder "Hours (1-168)").
   - Add child `Button` named `SkipButton` (label "Skip").
   - Add child `TMP_Text` named `StatusLabel` (empty initial text).
   - Add `DevTimeSkipModule` component to `Tab_TimeSkip` and wire its three serialized fields.
   - Duplicate an existing tab button → rename `TabButton_TimeSkip` (label "Time Skip").
   - On the prefab root's `DevModePanel` component, expand `_tabs` → `+` a new `TabEntry` → wire `TabButton_TimeSkip` and `Tab_TimeSkip`.
   - Save the prefab.
3. **Build `UI_TimeSkipOverlay.prefab`** under `Assets/UI/HUD/`: full-screen `Image` (black, alpha controlled by CanvasGroup), centered `TMP_Text` for the hour readout, bottom-right Cancel `Button`. Add `UI_TimeSkipOverlay` to root and wire the three serialized fields. Drop into the persistent UI canvas.
4. **Build `UI_BedSleepPrompt.prefab`** under `Assets/UI/HUD/`: small modal panel with `Slider`, `TMP_Text` for the "Skip N h" label, and Confirm + Cancel buttons. Add `UI_BedSleepPrompt` to root and wire the four serialized fields. Drop into the persistent UI canvas. Hidden by default.
5. **Future (out of scope for v1):** wire a `BedFurnitureInteractable` to call `UI_BedSleepPrompt.Show()` when the local player uses a `BedFurniture`.

Procedural how-to for invoking the skip, abort semantics, and integration tests lives in [.agent/skills/world-system/SKILL.md](../../.agent/skills/world-system/SKILL.md) §Time Skip.

## Multiplayer model — consent + auto-trigger

The skip is multiplayer-aware. Two paths:

### Bed / UI flow (default — `force: false`)

1. Each player walks to a bed and chooses a target hour count via `UI_BedSleepPrompt`.
2. The bed's `UseSlot` path raises `Character.IsSleeping = true` and the UI sets `Character.PendingSkipHours` to the chosen value (server-authoritative `NetworkVariable`s — both replicate to all peers).
3. Server's `TimeSkipController.Update` polls every connected player's Character. When **all** have `IsSleeping == true` AND at least one has `PendingSkipHours > 0`, the watcher auto-fires `RequestSkip(hours, force: false)` with `hours = MIN(every player's PendingSkipHours)` — the **closest target wins**, so nobody is forced to skip beyond what they asked for.
4. `RequestSkip` re-validates the all-sleeping gate; the per-hour loop runs.
5. After WakeUp, every player's `PendingSkipHours` is reset to 0 inside `Character.ExitSleep` so a stale value can't auto-trigger the next session.

### Host force-override (`force: true`)

The dev paths (`/timeskip <hours>` chat command + `DevTimeSkipModule` panel button) call `RequestSkip(hours, force: true)`. This:
- Bypasses the all-sleeping gate.
- Auto-EnterSleeps every connected player at the start of the coroutine (admin override — players see themselves snap into the sleep pose without going to a bed).
- Otherwise behaves identically to the consent path.

This lets the host time-skip without all players cooperating — useful for dev iteration, story beats, and diagnostics.

### Why "closest target wins"

A 4-player party where targets are `2h / 6h / 8h / 12h` skips **2h**. The 12h player still gets their nap, just not the full duration; they can re-trigger the bed flow afterward. The 2h player isn't dragged 10 hours into a future they didn't consent to. Sleep duration is a "minimum-of-cohort" social negotiation, not a vote.

### Pre-skip checkpoint save

Step 2 of `RunSkip` writes a save **before** the hibernate-skip-wake cycle (waiting for `SaveManager.CurrentState == Idle` before continuing). If anything goes wrong mid-skip — exception, abort, crash, scene unload — the player reverts to the pre-skip world on next load. The post-skip save fires naturally via the existing `SleepBehaviour.Exit` save hook on the bed flow; the dev paths rely solely on the pre-skip checkpoint.

## Known gotchas / edge cases

- **Day-boundary gating is not optional.** If a future contributor adds a step to `SimulateOneHour` that integrates over `daysPassed` and forgets to wrap it in the boundary-crossing branch, the step silently no-ops every hour — a 168 h skip will produce 0 of that effect.
- **`_pendingSkipWake` must be cleared inside `WakeUp()`.** If the flag leaks, the *next* normal player-approach wake-up will skip the per-day catch-up it actually needs and the map will resume with stale state.
- **`force: true` bypasses ALL consent.** The dev panel + `/timeskip` chat command are admin tools — they auto-EnterSleep every connected player at the start of the coroutine without asking. Don't expose `force: true` to non-host gameplay paths or any player can override every other player's autonomy.
- **`PendingSkipHours = 0` means "no target set"** — a player who's sleeping but never picked an hour count via the bed UI doesn't block other players, but they don't *contribute* a target either. The auto-trigger fires when all are sleeping AND ≥1 has set hours. If everyone goes to bed with 0 hours, the watcher never fires (intentional — somebody needs to actually request the skip).
- **`BedSlot.Occupant` is runtime-only.** A save mid-sleep will deserialize with the slot empty; the bed is functionally vacant on load even if a character ended their session in it. NPCs land back in idle on map respawn.
- **`Character.NetworkIsSleeping` does not persist.** Saving while asleep, then loading, wakes the player. Documented; not a bug.
- **`MapController.WakeUp()` has ungated `Debug.Log` calls** that are now reachable via the `WakeUpFromSkip` flow. Tracked in [[optimisation-backlog]].
- **`MacroSimulator.SimulateOneHour` resolves the active map via `Object.FindObjectsByType<MapController>` per hour.** One array allocation per hour during a skip (max 168 / skip). Tracked in [[optimisation-backlog]].

## Open questions / TODO

- [ ] EditMode test for the `24× SimulateOneHour ≡ 1× SimulateCatchUp(daysPassed=1.0)` invariant. Couldn't ship in v1 because `MacroSimulator` lives in the default `Assembly-CSharp` and Unity asmdefs don't reference it directly. Resolution path: extract `MacroSimulator`'s pure-math helpers (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule`) into a `MWI.MacroSim.Pure` asmdef, then add an EditMode test asmdef referencing it. Tracked in [[optimisation-backlog]].
- [ ] Combat / "danger detector" auto-abort. v1 only auto-aborts on player death.
- [ ] NPC sleep pose persistence across map hibernation. Out of scope for v1.
- [ ] Restoring `_isSleeping` from save (would extend `ICharacterSaveData<T>`). Not added to v1.
- [ ] Scene migration of existing plain-`Furniture` beds to `BedFurniture`. Existing beds keep working through the legacy `SleepBehaviour` fallback.

## Change log
- 2026-04-27 — Initial v1 implementation (TimeSkipController, BedFurniture, EnterSleep/ExitSleep, MacroSimulator.SimulateOneHour). — Claude / [[kevin]]
- 2026-04-28 — Multiplayer model shipped. RequestSkip gained `force: bool` (admin override). Removed single-player gate. Added auto-trigger watcher (TimeSkipController.Update) firing when all players are sleeping with `Character.PendingSkipHours` set; uses MIN of targets. Added `Character.NetworkPendingSkipHours` NetworkVariable + `SetPendingSkipHours` server method. Reset to 0 inside `ExitSleep`. Save moved to BEFORE the hibernate-skip-wake cycle (waits for SaveManager.Idle). Dev paths pass `force: true`. — Claude / [[kevin]]
- 2026-04-28 — `MapController.Hibernate` now skips "virtual" buildings whose `NetworkObject` is shared with the MapController (root cause of the WakeUp MissingReferenceException found via the Virtual Node placeholder). — Claude / [[kevin]]

## Sources
- [Assets/Scripts/DayNightCycle/TimeSkipController.cs](../../Assets/Scripts/DayNightCycle/TimeSkipController.cs) — server-authoritative entry point.
- [Assets/Scripts/DayNightCycle/TimeManager.cs](../../Assets/Scripts/DayNightCycle/TimeManager.cs) — `AdvanceOneHour`.
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — `SimulateOneHour` + shared helpers.
- [Assets/Scripts/World/MapSystem/MapController.cs](../../Assets/Scripts/World/MapSystem/MapController.cs) — `HibernateForSkip`, `_pendingSkipWake`, `WakeUpFromSkip`.
- [Assets/Scripts/World/Furniture/BedFurniture.cs](../../Assets/Scripts/World/Furniture/BedFurniture.cs) — slot-aware bed lifecycle.
- [Assets/Scripts/Character/Character.cs](../../Assets/Scripts/Character/Character.cs) — `NetworkIsSleeping`, `EnterSleep`, `ExitSleep`.
- [Assets/Scripts/Character/CharacterControllers/PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs) — `Update` early-out on sleep.
- [Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs](../../Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs) — slot-aware NPC sleep routing.
- [.agent/skills/world-system/SKILL.md](../../.agent/skills/world-system/SKILL.md) — procedural how-to (the "Time Skip" section).
- [docs/superpowers/specs/2026-04-27-time-skip-and-bed-furniture-design.md](../../docs/superpowers/specs/2026-04-27-time-skip-and-bed-furniture-design.md) — original design spec.
- [docs/superpowers/plans/2026-04-27-time-skip-and-bed-furniture.md](../../docs/superpowers/plans/2026-04-27-time-skip-and-bed-furniture.md) — implementation plan.
- 2026-04-27 implementation pass (commits `7f82a8da` through `38aa4b7f` on `multiplayyer`).
