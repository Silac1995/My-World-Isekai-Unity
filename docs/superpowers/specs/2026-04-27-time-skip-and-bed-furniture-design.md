# Time Skip & Bed Furniture — Design

- **Date:** 2026-04-27
- **Status:** Approved in chat (pending written-spec review)
- **Author:** Silac (via Claude Opus 4.7)

## Problem

Today the only way to advance in-game time faster than real-time is
`GameSpeedController`, which scales `UnityEngine.Time.timeScale`. That keeps
the live simulation running — every NPC ticks GOAP, every job runs, every
physics step fires — just faster. It is the right tool for "play at 4x while
I watch my city work," but the wrong tool for "go to bed and skip 8 hours."

There is also no real bed furniture. `SleepBehaviour` walks an NPC to "any
furniture tagged as bed," calls `Furniture.Use(...)`, and the NPC stands
there until the schedule advances. There is no anchor point, no sleep pose,
no per-bed slot count, and no attribute lifecycle (movement disable, animator
flag, input lock).

## Goal

Add a **time-skip** path that **coexists** with `GameSpeedController`. The
skip:

1. Moves the in-game clock from time A to time B in 1-hour iterations.
2. Hibernates the active map for the duration so live NPCs do not run during
   the skip — only the existing `MacroSimulator` math runs, per hour.
3. Restores the world (and the player's character) cleanly when the skip
   ends.

In the same change, introduce a real `BedFurniture` with a **modular per-prefab
slot list** (single bed = 1 slot, double bed = 2 slots, family bed = 4 slots,
…) and a sleep-state lifecycle on `Character` that the bed drives on
enter/exit. The bed becomes one of the three v1 triggers for the time-skip.

## Scope

### In

- `BedFurniture : Furniture` — new component with a serialized
  `List<BedSlot>` (each slot pairs `Transform Anchor` with runtime
  `Occupant` / `ReservedBy`). Slot-aware lifecycle methods.
- `BedSlot` — small `[System.Serializable]` data class.
- `Character.IsSleeping` — new `NetworkVariable<bool>`.
- `Character.EnterSleep(Transform anchor)` / `ExitSleep()` — server-only
  methods that snap position+rotation, toggle `NavMeshAgent`, raise
  `OnSleepStateChanged`.
- `PlayerController.Update()` — early-out when `Character.IsSleeping`.
- `SleepBehaviour` — routes through `BedFurniture.UseSlot` when the bed is a
  `BedFurniture`, falls back to legacy `Furniture.Use(...)` otherwise.
- `TimeSkipController` — new server-authoritative MonoBehaviour, single
  entry point `RequestSkip(int hours)` for all triggers.
- `TimeManager.AdvanceOneHour()` — new method on the existing class. Same
  event semantics as live `ProgressTime()`.
- `MacroSimulator.SimulateOneHour(...)` — new entry point. Day-boundary
  gating for cumulative steps. Existing `SimulateCatchUp` untouched.
- `MapController.HibernateForSkip()` + `_pendingSkipWake` flag on
  `WakeUp()` so the wake-up path skips the redundant single-pass catch-up.
- `DevTimeSkipModule` — new dev-mode tab module (sibling to
  `DevSpawnModule` / `DevSelectionModule` / `DevInspectModule`). UI:
  hours input + skip button → `TimeSkipController.Instance.RequestSkip(...)`.
- `/timeskip <hours>` chat command → same entry point.
- `UI_BedSleepPrompt` — modal that appears when the player uses a bed.
  Hours slider 1–168, default = "until 06:00 next day."
- `UI_TimeSkipOverlay` — fade-to-black overlay during the skip with a live
  hour readout and a Cancel button.
- Doc updates (rules #28 + #29b):
  - `.agent/skills/world-system/SKILL.md`
  - `wiki/systems/world-macro-simulation.md`
  - new `wiki/systems/world-time-skip.md`
- Edit-mode test:
  `MacroSimulator_SimulateOneHour_Tests` asserting that 24×
  `SimulateOneHour` ≡ 1× `SimulateCatchUp(daysPassed=1.0)` for a fixture
  `MapSaveData`.

### Out (deferred — listed for traceability)

- **Multiplayer time-skip.** v1 is gated `NetworkManager.ConnectedClients.Count == 1`.
  v2 will replace the gate with auto-trigger when **all connected players are
  simultaneously occupying a bed slot** (sleeping = consent). The single-player
  guard is one `if` and trivial to remove.
- **Combat / "danger detector" auto-abort.** v1 only auto-aborts on player
  death.
- **Live fast-forward path** (approach B from brainstorm). We use only
  hibernate-skip-wake (approach A).
- **NPC sleep pose persistence across map hibernation.** NPCs wake into
  idle. `BedSlot.Occupant` is runtime-only.
- **Restoring `_isSleeping` from save.** Player wakes on load. Documented;
  not added to `ICharacterSaveData<T>`.
- **Scene migration of existing plain-`Furniture` beds to `BedFurniture`.**
  Tracked as a follow-up task; existing beds keep working through the
  legacy `SleepBehaviour` fallback.

## Architecture

### Hibernate-skip-wake flow (server-authoritative)

```
Trigger (DevTimeSkipModule | /timeskip cmd | UI_BedSleepPrompt)
        │
        ▼
TimeSkipController.RequestSkip(hours)        ◄── server-only single entry point
        │
        ├── 1. Validate
        │       - IsServer
        │       - ConnectedClients.Count == 1   (v1 gate)
        │       - 1 ≤ hours ≤ 168
        │       - !_isSkipping (no nested skip)
        │
        ├── 2. EnterSkipMode()
        │       - foreach player Character: EnterSleep(anchor or current transform)
        │       - PlayerController gates Update on IsSleeping
        │       - UI_TimeSkipOverlay.Show() (clients receive ClientRpc)
        │       - activeMap.HibernateForSkip()
        │           • mirrors existing hibernate path
        │           • forceHibernate=true bypasses "no players nearby" check
        │           • sets _pendingSkipWake = true
        │
        ├── 3. Per-hour loop (server coroutine)
        │       for h in 1..hours:
        │           if _aborted or playerDead: break
        │           int prevHour = TimeManager.CurrentHour
        │           TimeManager.AdvanceOneHour()
        │              → fires OnHourChanged / OnNewDay / OnPhaseChanged
        │              → GameSpeedController.OnServerHourChanged pushes to clients
        │           MacroSimulator.SimulateOneHour(activeMap.HibernationData,
        │               currentDay, currentTime01, jobYields, prevHour)
        │           // OTHER hibernated maps are NOT iterated — their LastHibernationTime
        │           // stays old; their next player-visit WakeUp() will catch up via the
        │           // existing single-pass SimulateCatchUp(daysPassed). The macro-sim is
        │           // deterministic and time-only, so running per-hour now or in one pass
        │           // at visit time produces the same result.
        │           yield return null   // one frame per hour, lets cancel input + UI tick
        │
        ├── 4. ExitSkipMode()
        │       - activeMap.WakeUp()
        │           • sees _pendingSkipWake = true → skips SimulateCatchUp
        │           • restores buildings + spawns NPCs from updated state
        │       - foreach player: ExitSleep()
        │       - UI_TimeSkipOverlay.Hide()
        │       - SaveManager.RequestSave(player) (matches existing bed-sleep save)
```

### Per-hour macro-sim — day-boundary gating

`MacroSimulator.SimulateCatchUp` integrates over a `daysPassed` delta. Several
of its steps use `Mathf.FloorToInt((float)daysPassed * X)` and would silently
floor to 0 every hour if called naively at 1/24-day deltas:

| Step | Hour-grained | Day-grained |
|---|---|---|
| Resource pool regen | — | ✔ (only on rollover) |
| Inventory yields per NPC | — | ✔ (only on rollover) |
| City growth | — | ✔ (only on rollover) |
| Zone motion | — | ✔ (only on rollover) |
| Needs decay | ✔ (uses `hoursPassed`) | — |
| Schedule snap | ✔ (already hour-aware) | — |
| Terrain catch-up | ✔ (uses `hoursPassed`) | — |
| Vegetation catch-up | ✔ (uses `hoursPassed`) | — |

`SimulateOneHour` runs the hour-grained block every call and the day-grained
block only when `prevHour == 23 && currentHour == 0`. Internally it shares
helpers with `SimulateCatchUp` (extract once, call from both), so the two
paths cannot drift.

**Correctness invariant:** for any 24-hour window starting at hour 0, the
cumulative effect of 24× `SimulateOneHour` calls equals one
`SimulateCatchUp(daysPassed=1.0)` call on the same `MapSaveData`. Asserted in
`MacroSimulator_SimulateOneHour_Tests`.

### `BedFurniture` data & API

```csharp
[System.Serializable]
public class BedSlot
{
    [SerializeField] public Transform Anchor;
    public Character Occupant { get; internal set; }
    public Character ReservedBy { get; internal set; }
    public bool IsFree => Occupant == null && ReservedBy == null;
}

public class BedFurniture : Furniture
{
    [Header("Bed")]
    [SerializeField] private List<BedSlot> _slots = new();

    public IReadOnlyList<BedSlot> Slots => _slots;
    public int SlotCount => _slots.Count;
    public int FreeSlotCount { get; }
    public bool HasFreeSlot => FreeSlotCount > 0;

    // Slot-aware lifecycle (preferred API)
    public bool ReserveSlot(int slotIndex, Character c);
    public bool UseSlot(int slotIndex, Character c);   // → c.EnterSleep(slot.Anchor)
    public void ReleaseSlot(int slotIndex);            // → c.ExitSleep()
    public int FindFreeSlotIndex();                    // -1 if none
    public int GetSlotIndexFor(Character c);           // -1 if not in bed

    // Override base contract for backward-compat callers:
    public override bool IsFree() => HasFreeSlot;
    // Reserve(Character) / Use(Character) / Release() pick first free slot, log warning.
}
```

Each bed prefab (specific shapes are author-decided — single, double, bunk,
…) ships with `Anchor_Slot_0..N` child empties whose local position+rotation
define where the character is placed. `_slots` list length = slot count. No
per-prefab code, only data.

### `Character` sleep lifecycle

New on `Character.cs` (existing class — same rationale as `IsUnconscious`):

```csharp
private NetworkVariable<bool> _isSleeping = new(false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);
public bool IsSleeping => _isSleeping.Value;
public event Action<bool> OnSleepStateChanged;

public void EnterSleep(Transform anchor)
{
    if (!IsServer) return;
    transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    if (_navMeshAgent != null) _navMeshAgent.enabled = false;
    _characterMovement?.ResetPath();
    _isSleeping.Value = true;
}

public void ExitSleep()
{
    if (!IsServer) return;
    if (_navMeshAgent != null) _navMeshAgent.enabled = true;
    _isSleeping.Value = false;
}
```

`_isSleeping.OnValueChanged` raises `OnSleepStateChanged` on every peer; the
animator/visual layer subscribes and switches to a sleep pose. Per project
rule #18, position is server-driven and replicated by `NetworkTransform` —
clients never snap themselves.

`PlayerController.Update()` gains an early-out:
`if (Character.IsSleeping) return;` — keeps rule #33 (all player input lives
in `PlayerController`).

`SleepBehaviour` is updated: when `_bed is BedFurniture bed`, route through
`bed.UseSlot(bed.FindFreeSlotIndex(), character)` instead of
`_bed.Use(character)`. Plain `Furniture` beds keep the legacy code path
(no anchor snap, no `IsSleeping` flag) so existing scenes remain functional
during the migration window.

### Attribute restoration table

| Attribute | On `UseSlot` | On `ReleaseSlot` |
|---|---|---|
| `transform.position` | snap to `slot.Anchor.position` | left at anchor (character walks off) |
| `transform.rotation` | snap to `slot.Anchor.rotation` | left at anchor |
| `NavMeshAgent.enabled` | `false` | `true` |
| `CharacterMovement.ResetPath()` | called | not needed |
| Player input (PlayerController) | locked via `IsSleeping` early-out | unlocked |
| `_isSleeping` (drives anim/visual) | `true` | `false` |
| Collider | untouched | — |
| Renderer / visibility | untouched | — |

## Files

### New (8)

| File | Role |
|---|---|
| `Assets/Scripts/World/Furniture/BedFurniture.cs` | `BedFurniture : Furniture` + `BedSlot` |
| `Assets/Scripts/DayNightCycle/TimeSkipController.cs` | `RequestSkip(int)` + per-hour loop coroutine |
| `Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs` | Dev-panel tab module |
| `Assets/Scripts/UI/UI_TimeSkipOverlay.cs` | Fade-to-black overlay + cancel button |
| `Assets/Scripts/UI/UI_BedSleepPrompt.cs` | In-world modal for bed-triggered skip |
| `Assets/Tests/EditMode/MacroSimulator_SimulateOneHour_Tests.cs` | 24×OneHour ≡ 1×CatchUp invariant |
| `wiki/systems/world-time-skip.md` | New system page |
| `Assets/Prefabs/Furniture/Bed_*.prefab` (one per bed shape) | Author work, not code — slot count baked in via `_slots` list |

### Modified (~10)

| File | Change |
|---|---|
| `Assets/Scripts/DayNightCycle/TimeManager.cs` | `+ AdvanceOneHour()` |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | `+ SimulateOneHour(...)`, extract shared helpers |
| `Assets/Scripts/World/MapSystem/MapController.cs` | `+ HibernateForSkip()`, `_pendingSkipWake` flag handled in `WakeUp()` |
| `Assets/Scripts/Character/Character.cs` | `+ _isSleeping`, `EnterSleep`, `ExitSleep`, `OnSleepStateChanged` |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | early-out on `IsSleeping` |
| `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs` | route through `BedFurniture.UseSlot` when applicable |
| Chat command registry | `+ /timeskip <hours>` |
| `Assets/Prefabs/UI/DevModePanel.prefab` (Inspector) | one new `TabEntry` for Time Skip |
| `.agent/skills/world-system/SKILL.md` | new section "Time Skip (player-initiated macro-sim loop)" |
| `wiki/systems/world-macro-simulation.md` | bump `updated:`, add change-log entry, document `SimulateOneHour` entry point |

`Assets/Scripts/World/Furniture/Furniture.cs` is **not** modified — base stays
single-occupant, `BedFurniture` overrides what it needs.

## Networking

- `TimeSkipController` is a `NetworkBehaviour` singleton (mirrors
  `GameSpeedController`).
- `RequestSkip` is server-only. Clients call via a `RequestSkipServerRpc`
  (only relevant once the v1 single-player gate is lifted).
- The skip loop runs as a server coroutine. Time changes propagate to clients
  through the existing `GameSpeedController._serverDay/_serverTime01`
  `NetworkVariable`s — the per-hour `OnServerHourChanged` already pushes to
  the network on each hour rollover.
- `UI_TimeSkipOverlay.Show()/Hide()` are issued via `ClientRpc`.
- `Character._isSleeping` is `NetworkVariable<bool>` — replicates to all
  peers automatically.
- Position snap on `EnterSleep` is server-side; `NetworkTransform` carries
  it to clients. No client-side prediction.
- v1 single-player gate (`ConnectedClients.Count == 1`) deliberately
  short-circuits the host↔client / client↔client / NPC paths from rule #19
  — there are no remote peers to validate against. Removing the gate in v2
  re-opens those paths and the auto-trigger watcher (see "Out") will need a
  full network-validator pass at that time.

## Persistence

- During the skip, no write-to-disk happens. `LastHibernationTime` updates
  in-memory each hour.
- On `ExitSkipMode`, `SaveManager.RequestSave(player)` fires (matches the
  existing bed-sleep save trigger from `SleepBehaviour.Exit`, commit
  `ed64dcc2`). Player profile + world state both written.
- `Character.IsSleeping` is **not** in `ICharacterSaveData<T>`. Saving and
  quitting while in a bed restores the player out-of-bed at the bed's
  position on load. Documented as accepted behavior.
- `BedSlot.Occupant` / `ReservedBy` are runtime-only. After load, all bed
  slots start empty; NPCs re-walk to bed via `SleepBehaviour` if their
  schedule says so.

## Risks & open questions

1. **Existing `MapController.Hibernate` may guard against active-player
   presence.** `HibernateForSkip` adds a `forceHibernate: true` parameter
   that skips that guard. Must be implemented carefully so a non-skip
   accidental call does not strand a player.
2. **`OnNewDay` subscribers running per-day during a long skip.** A 168-hour
   skip fires `OnNewDay` 7 times. Anything that does heavy UI work on
   `OnNewDay` will get N callbacks. Audit subscribers during implementation.
   If a real subscriber misbehaves, add an `_isInSkipMode` flag for opt-out
   — premature otherwise.
3. **`OnPhaseChanged` debug log spam.** Existing `TimeManager.UpdatePhase`
   logs every phase change. A 168-hour skip can fire it ~28 times. The log
   is already in the hot path during normal play; we accept it for v1, but
   gate behind `NPCDebug.VerboseTime` (or new toggle) per rule #34 if it
   shows up in profiling.
4. **NPCs on *other* maps during the skip.** Already hibernated. Their
   `LastHibernationTime` predates the skip; when a player visits them later,
   their normal `WakeUp() → SimulateCatchUp(daysPassed)` runs as today. No
   change needed.
5. **`SimulateOneHour` extracting helpers from `SimulateCatchUp`.** Risk of
   regression in the wake-up path. Mitigated by the
   `MacroSimulator_SimulateOneHour_Tests` invariant (24×OneHour ≡ 1×CatchUp)
   plus a parallel test that snapshots existing `SimulateCatchUp` behavior
   on a fixture before the refactor and re-runs it after.
6. **NPC sleep poses on respawn.** When the active map wakes up after a
   skip, NPCs whose schedule says "should be in bed" spawn into idle (not
   sleep) until `SleepBehaviour` runs them through `BedFurniture.UseSlot`
   on the next AI tick. Acceptable for v1; flagged as a polish item.

## Migration

- Existing scenes have plain `Furniture` instances tagged as beds. They
  keep working: `SleepBehaviour` falls back to `_bed.Use(character)` when
  the bed is **not** `BedFurniture`. No `IsSleeping`, no anchor snap on
  those legacy beds.
- Migration is a one-pass scene edit: replace the `Furniture` component
  with `BedFurniture`, author one or more `Anchor_Slot_*` child empties,
  populate `_slots`. Tracked as a separate task post-merge.

## Test plan

- **Edit-mode**:
  `MacroSimulator_SimulateOneHour_Tests` — fixture `MapSaveData` with
  resource pools, hibernated NPCs, terrain cells. Assert 24×OneHour
  resource pool / inventory yield / needs decay totals match 1×CatchUp.
- **Play-mode (manual)**:
  - Single-player. Open `/timeskip 8`. Verify clock advances, NPCs visible
    after wake-up, hunger / social drained on player and NPCs, lighting
    reflects the new hour, save written.
  - Repeat with `/timeskip 168` (week). Verify city growth scaffold
    spawned, terrain moisture/temperature updated.
  - Bed flow: walk to a bed, prompt opens, slide to "until 06:00,"
    confirm. Fade overlay shows, cancel works mid-skip.
  - Two-slot bed: NPC and player both occupy distinct slots; verify
    correct anchor placement and independent release.
  - Multiplayer (host + client). Verify `/timeskip` is gated (no-op +
    log warning). Bed UI hidden / disabled on clients.

## Future work

- v2 multiplayer time-skip: replace single-player gate with watcher that
  auto-fires when **all connected players are simultaneously in a bed
  slot**. Per-player wake-time picker before sleeping; server uses
  `min(targets)`.
- Combat / "danger detector" auto-abort.
- Smart-cap "skip until next noteworthy event" (raid warning, NPC death,
  food running out).
- Live fast-forward path for short skips (<= 1h?) where the player wants
  to *see* NPCs work — opt-in alternative to hibernate-skip-wake.
