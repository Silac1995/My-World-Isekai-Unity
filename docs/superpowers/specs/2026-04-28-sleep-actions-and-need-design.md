# Sleep Actions & NeedSleep — Design

**Date:** 2026-04-28
**Status:** Design approved, ready for implementation plan
**Author:** Claude (with [[kevin]])
**Related:** [[world-time-skip]], [[character-needs]], [[bed-furniture]] (in [[world-time-skip]])

---

## 1. Problem statement

Sleep already exists in the codebase, but only as a side-effect of:

- The NPC `SleepBehaviour` (an `IAIBehaviour`), which calls `BedFurniture.UseSlot(...)` directly.
- The shipped `TimeSkipController` flow, which mutates `Character.IsSleeping` directly via `EnterSleep` / `ExitSleep`.
- A bed prompt UI for setting `PendingSkipHours`.

Three gaps remain, all of which the user explicitly asked for:

1. **No `CharacterAction` for sleep.** Both NPC and player code mutate sleep state directly, violating rule #22 (all gameplay effects go through `CharacterAction`). Player input has no canonical entry point and there is no parity contract with NPCs.
2. **No `NeedSleep`.** The save/load schema, HUD, and offline decay/restore math have no place to live. `MacroSimulator.SimulateOneHour` decays needs but cannot decay or restore a need that doesn't exist.
3. **No `BedFurnitureInteractable`.** The bed sleep UI flow exists as a script but has no interactable to fire it. The wiki flags this as "out of scope for v1" — this design closes that gap.

Plus two cross-cutting concerns:

4. Live restoration math (stamina + NeedSleep) is undefined for both the live tick path and the per-hour macro-sim path.
5. Wake-on-attack is not wired — a character being attacked while asleep needs to leave the sleep state.

## 2. Constraints (from brainstorm)

- **NeedSleep is passive (Option A).** It decays and restores; it does **not** drive GOAP. Sleep remains schedule-driven for NPCs and player-driven for players.
- **Restoration is stamina + NeedSleep only.** HP is not touched.
- **Save trigger fires ONLY when a time skip ends.** Combat-interrupted wakes, manual cancels, or "lay down by accident → immediately stand up" cases do **not** save. Rationale: a player who toggles sleep without committing to a real skip should not pay save churn for it, and a wake-on-attack will be re-saved at the next legitimate save event anyway. Host-only gate (server already-implicit because TimeSkipController is server-only); per-player iteration over connected players (only player characters, not NPCs).
- **Default sleep duration = 7 hours.** The bed prompt's slider defaults to 7h so that "lie down → confirm" is a one-click action with a sensible Minecraft-style default.
- **Pre-skip fade-to-black transition.** After the bed prompt confirms (or after the auto-trigger fires in MP), a brief unscaled real-time fade plays before the per-hour skip loop begins. Same fade in single-player and multiplayer — it's the visual cue that "I am falling asleep now," not a coordination wait.
- **All-players-sleeping fast-forward uses the existing `TimeSkipController`.** No new time-warp controller.
- **Both ground sleep and bed sleep are first-class.** Player can sleep where standing (key) or in a bed (interactable).
- **Player↔NPC parity.** Both ends of the system enqueue the same `CharacterAction` subclasses.

## 3. Architecture (Approach 3 — short repeating action with per-tick restoration)

Each sleep `CharacterAction` instance has a short Duration (5 s real-time). On `OnApplyEffect`, the server applies one *chunk* of stamina + NeedSleep restoration. The driving layer (SleepBehaviour for NPC, PlayerController for player) re-enqueues the action while sleep is still wanted (schedule active / player not interrupted) and the bed slot (if any) is still held.

This works alongside the time-skip path: when `TimeSkipController` fires, the live action cancels (it does not need to drive restoration during the skip — `MacroSimulator.SimulateOneHour` runs an offline restoration pass per hour on every `IsSleeping` character). Live ticks only matter for the brief window before the auto-trigger or for short cat-naps that never trigger a skip.

## 4. Component inventory

| File | Status | Role |
|---|---|---|
| `Assets/Scripts/Character/CharacterNeeds/NeedSleep.cs` | **new** | Passive `CharacterNeed`. NetworkVariable bridge to `CharacterNeeds._networkedSleep`. Per-phase decay on the server. `OnValueChanged` + `OnExhaustedChanged` events for HUD. `IsActive() => false`. `GetGoapActions() => empty`. |
| `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs` | **modify** | Add `_networkedSleep` `NetworkVariable<float>`, `NetworkedSleepValue`, `ServerSetSleep(float)`, `RequestAdjustSleepRpc(float)`, `Subscribe/UnsubscribeNetworkedSleepChanged`. Register `NeedSleep` in `Awake`. Save/load via the existing `_allNeeds` loop. |
| `Assets/Scripts/Character/CharacterNeeds/NeedSleepMath.cs` | **new** | Pure-math constants/helpers: `DEFAULT_MAX = 100f`, `DEFAULT_START = 80f`, `DEFAULT_LOW_THRESHOLD`, `DEFAULT_DECAY_PER_PHASE`. Mirrors `NeedHungerMath`. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_Sleep.cs` | **new** | Ground sleep. `OnStart`: `character.EnterSleep(character.transform)`. Duration: 5 s. `OnApplyEffect` (server): apply ground-rate restoration chunk (stamina + NeedSleep). `OnCancel`: `character.ExitSleep()`. **No save trigger** — only legitimate time-skip wakes save (see §2 + TimeSkipController row). |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_SleepOnFurniture.cs` | **new** | Bed sleep. Constructor takes `BedFurniture` + `int slotIndex`. `CanExecute`: slot is free OR already occupied by self. `OnStart`: `bed.UseSlot(slotIndex, character)` (chains to `EnterSleep`). Duration: 5 s. `OnApplyEffect` (server): apply bed-rate restoration chunk. `OnCancel`: `bed.ReleaseSlot(slotIndex)` (chains to `ExitSleep`). |
| `Assets/Scripts/Character/Character.cs` (`ExitSleep` method) | **modify** | Make `ExitSleep` idempotent — early-out at the top if `!IsSleeping` so double-calls (e.g., wake-on-attack ExitSleep then action OnCancel ExitSleep) are no-ops. **No save trigger** — the save is owned by `TimeSkipController.RunSkip` post-WakeUp loop (see below). |
| `Assets/Scripts/DayNightCycle/TimeSkipController.cs` (`RunSkip` coroutine) | **modify** | (1) After the existing `OnSkipStarted` invoke and `Time.timeScale = 0`, add a `yield return new WaitForSecondsRealtime(_preSkipFadeSeconds)` (default 1.5 s, serialized field) before continuing into the hibernate-skip-wake cycle. This gives `UI_TimeSkipOverlay` time to fade to black via its `OnSkipStarted` subscription. (2) After `WakeUpFromSkip` and the `ExitSleep` loop, add a **post-skip save loop**: iterate `players`, and for each `player.IsPlayer()` call `SaveManager.Instance?.RequestSave(player)`. This is the **single source of truth for the post-sleep save** — only legitimate time-skipped sleep produces a save. |
| `Assets/Scripts/UI/UI_BedSleepPrompt.cs` (or its prefab) | **modify** | Default the slider value to 7 (hours). Confirm button enqueues the existing `SetPendingSkipHours` ServerRpc with the slider value, then the new `RequestSleepOnFurnitureServerRpc`. |
| `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` | **modify** | Add `RequestSleepOnFurnitureServerRpc(NetworkObjectReference bedRef, int slotIndex)` — mirrors the existing `Request*ServerRpc` family. Resolves the bed, validates the slot, then enqueues `CharacterAction_SleepOnFurniture` server-side. Client players use this RPC; offline / host / NPC paths take the direct `ExecuteAction` route. |
| `Assets/Scripts/Interactable/BedFurnitureInteractable.cs` | **new** (`[RequireComponent(typeof(BedFurniture))]`) | Mirrors `TimeClockFurnitureInteractable` pattern. Overrides `Interact(Character)`: <br>• If interactor is local player → open `UI_BedSleepPrompt` (sets `PendingSkipHours`), then on confirm enqueue `CharacterAction_SleepOnFurniture` for a free slot via `RequestSleepOnFurnitureServerRpc`. <br>• Else (server, NPC) → resolve a free slot and enqueue the action directly. <br>Overrides `GetHoldInteractionOptions` to add a "Sleep" entry (label "Wake up" if already occupant). |
| `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs` | **modify** | Replace direct `bed.UseSlot(...)` with `character.CharacterActions.ExecuteAction(new CharacterAction_SleepOnFurniture(bed, slotIdx))`. Replace the "no bed available — sleep at home anyway" fallback with `CharacterAction_Sleep`. Re-enqueue while still in sleep schedule and not in combat. **Remove the `SaveManager.RequestSave` call** — action's `OnCancel` owns it now. |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | **modify** | New helper `ApplySleepRestoreHours(HibernatedNPCData npc, int hours)` mirroring `ApplyNeedsDecayHours`. Called from inside `SimulateOneHour`: for each `HibernatedNPCData` whose `IsSleeping` is true, restore stamina + NeedSleep by per-hour chunks. Treated as **hour-grained** (runs every hour, not at day boundaries). |
| `Assets/Scripts/Character/CharacterStatusManager.cs` (or `CharacterCombat.cs`) | **modify** | When damage is received and `character.IsSleeping == true`, call `character.ExitSleep()`. The sleep action's `OnCancel` then runs (releases bed slot, fires save). Read first to confirm which class owns the damage event. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | **modify** | Inside `Update()` (gated by `IsOwner`): <br>• A key (default `Z`) that **toggles** ground sleep — enqueues `CharacterAction_Sleep` if not asleep, calls `ClearCurrentAction` if currently asleep. <br>• Movement input (WASD, click-to-move via `PlayerMoveCommand`) while `IsSleeping == true` also calls `ClearCurrentAction` so any movement attempt wakes the character. <br>Bed interaction already flows through the existing `PlayerInteractCommand` → `BedFurnitureInteractable.Interact` path. |
| `CharacterAction_Sleep*` (both new files) | **integration** | Both sleep actions subscribe to `TimeSkipController.OnSkipStarted` in `OnStart` and cancel themselves when it fires (the offline restoration via `MacroSimulator.ApplySleepRestoreHours` takes over for the duration of the skip). Unsubscribe in `OnCancel` / `OnApplyEffect`. Defensive null-check on `TimeSkipController.Instance` (single-player / pre-spawn). |
| `Assets/Scripts/Character/Character.cs` (or wherever `EnterSleep` lives) | **read-only verify** | Confirm `EnterSleep` is idempotent (re-call on `IsSleeping = true` is a no-op or returns silently). If not, add a guard. Same for `ExitSleep`. |

## 5. Data flow

### 5.1 Player bed flow

```
Player clicks bed → PlayerController issues PlayerInteractCommand
        │
        ▼
Auto-walk to bed → enters InteractionZone → detector.TriggerInteract(bed)
        │
        ▼
BedFurnitureInteractable.Interact(localPlayer)
        │
        ▼
UI_BedSleepPrompt.Show(slotIdx, onConfirm)
        │  (player picks hours via slider, clicks Confirm)
        ▼
Character.SetPendingSkipHours(hours)        — server-auth via existing path
CharacterActions.RequestSleepOnFurnitureServerRpc(bedRef, slotIdx)
        │
        ▼ (on server)
new CharacterAction_SleepOnFurniture(bed, slotIdx)
character.CharacterActions.ExecuteAction(action)
        │
        ▼
action.OnStart → bed.UseSlot(slotIdx, character) → Character.EnterSleep(slot.Anchor)
                                                   └─ Character.NetworkIsSleeping = true
        │
        ▼
TimeSkipController.Update() detects all players IsSleeping=true + ≥1 PendingSkipHours>0
        │
        ▼
Auto-fire RequestSkip(MIN(PendingSkipHours), force: false)
        │
        ▼
TimeSkipController.OnSkipStarted fires → live sleep action subscribes/cancels itself
        │
        ▼ (per-hour loop, Time.timeScale = 0)
MacroSimulator.SimulateOneHour:
    ApplyNeedsDecayHours (existing)
    ApplySleepRestoreHours (NEW — restores stamina + NeedSleep for IsSleeping characters)
        │
        ▼
TimeSkipController calls ExitSleep on every player → IsSleeping=false (no save)
        │
        ▼
TimeSkipController post-skip save loop → RequestSave(player) for each connected player character
        │  (single source of truth for post-sleep save)
        ▼
(No live action to OnCancel — already cancelled at skip start.)
        │
        ▼
action.OnCancel → bed.ReleaseSlot(slotIdx) → Character.ExitSleep
                                              (idempotent; no save here — post-skip loop owns it)
```

### 5.2 NPC bed flow

```
CharacterSchedule transitions to SleepActivity
        │
        ▼
NPCBehaviourTree priority 6 (SCHEDULE) selects SleepBehaviour (existing)
        │
        ▼
SleepBehaviour walks NPC home → finds bed → resolves free slot
        │
        ▼
character.CharacterActions.ExecuteAction(new CharacterAction_SleepOnFurniture(bed, slotIdx))
        │
        ▼ (action lifecycle identical to player path above, sans UI)
        │
        ▼ Each tick (5 s) ends → action finishes → SleepBehaviour re-enqueues if still sleep schedule
        │
        ▼ Schedule transitions out of SleepActivity → SleepBehaviour stops re-enqueueing
        │
        ▼
Action finishes, no new one starts → BT priority drops back to next available
```

### 5.3 Player ground sleep

```
Player presses Z → PlayerController.Update reads input (IsOwner gated)
        │
        ▼
character.CharacterActions.ExecuteAction(new CharacterAction_Sleep())
        │
        ▼
action.OnStart → Character.EnterSleep(character.transform)
        │
        ▼
TimeSkipController auto-trigger logic same as bed flow (IsSleeping + PendingSkipHours)
        │
        ▼
... ground rate (smaller chunks per tick) ...
        │
        ▼
Player presses any movement key → PlayerController detects, calls ClearCurrentAction
        │
        ▼
action.OnCancel → Character.ExitSleep (no save — manual cancel doesn't trigger save)
```

### 5.4 Wake on attack

```
Enemy attack hits character (asleep)
        │
        ▼
CharacterStatusManager (or CharacterCombat) damage handler
        │
        ▼
if (character.IsSleeping) character.ExitSleep()
        │
        ▼
Character.IsSleeping = false → CharacterActions.HandleCombatStateChanged(true)
                                  (already wired — fires when combat state flips)
        │
        ▼
ClearCurrentActionLocally → action.OnCancel
        │
        ▼
Bed slot released (no save — wake-on-attack doesn't trigger save, see §2 constraint), character available for combat
```

## 6. State & networking

### 6.1 Authority

| State | Owner | Replicated? |
|---|---|---|
| `NeedSleep.CurrentValue` | Server | Yes — `CharacterNeeds._networkedSleep` `NetworkVariable<float>` |
| `Character.IsSleeping` | Server | Already wired (`NetworkIsSleeping`) |
| `Character.PendingSkipHours` | Server | Already wired |
| `BedSlot.Occupant` | Server | Runtime-only; not replicated, not persisted |
| Sleep action lifecycle | Server | Visual proxy via existing `BroadcastActionVisualsClientRpc` |

### 6.2 Save / load

- **`NeedSleep.CurrentValue`** is persisted via the existing `CharacterNeeds._allNeeds` loop in `Serialize` / `Deserialize`. No new code; `NeedSleep` just needs to be in the list and have a meaningful `CurrentValue` getter/setter.
- **`Character.IsSleeping`** is intentionally **not** persisted (per the shipped time-skip design — reload always wakes the player).
- **Save trigger** lives only inside `TimeSkipController.RunSkip` after the post-skip `ExitSleep` loop. Fires once per connected player character (server-only by virtue of TimeSkipController being server-authoritative). Other wake paths (wake-on-attack, manual cancel before any skip fired, immediate stand-up after lying down) do **not** trigger a save — those produce no significant state delta worth a full save round-trip. The pre-skip checkpoint save in `RunSkip` step 2 is independent and continues to fire.

### 6.3 Multiplayer scenarios

- **Host↔Client:** Client opens bed prompt locally, sets `PendingSkipHours` via existing ServerRpc, enqueues sleep via new `RequestSleepOnFurnitureServerRpc`. Server runs the action authoritatively. Visual proxy replicates animation. `NetworkIsSleeping` and the NeedSleep NetworkVariable replicate to all peers.
- **Client↔Client:** Each client sees the other's `IsSleeping = true` via NetworkVariable replication. The sleeping client's character is in the sleep pose, NavMesh agent off (already wired in `EnterSleep`).
- **Host/Client↔NPC:** NPC sleep runs server-side directly (no RPC). Visual proxy replicates animation to all clients. NeedSleep NetworkVariable replicates per-NPC.
- **Late joiner:** New client receives current `NetworkIsSleeping`, `_networkedSleep` value, current `CurrentAction` via existing replication paths. No special handling.
- **Disconnect mid-sleep:** Existing TimeSkipController's `ResolveAllPlayers` recomputes the connected list each frame; a disconnected player no longer contributes to the all-sleeping gate.

## 7. Restoration math

All values are serialized constants on the action / NeedSleepMath classes. Placeholder defaults:

| Quantity | Value | Notes |
|---|---|---|
| `NeedSleep` max | 100 | Mirror NeedHunger. |
| `NeedSleep` starting | 80 | Mirror NeedHunger. |
| `NeedSleep` decay per phase | 25 | Same cadence as hunger — 4 phases/day → empties in 1 day if never slept. |
| `CharacterAction_Sleep` Duration | 5 s | Real-time. |
| `CharacterAction_Sleep` stamina restore per tick | +10% of max | Ground rate. |
| `CharacterAction_Sleep` NeedSleep restore per tick | +10% of max | Ground rate. |
| `CharacterAction_SleepOnFurniture` stamina restore per tick | +25% of max | Bed rate. |
| `CharacterAction_SleepOnFurniture` NeedSleep restore per tick | +25% of max | Bed rate. |
| `MacroSimulator.ApplySleepRestoreHours` per-hour stamina restore (bed) | +50% of max per hour | Tunable; covers a one-hour macro-sim slice. |
| `MacroSimulator.ApplySleepRestoreHours` per-hour NeedSleep restore (bed) | +50% of max per hour | Same. |
| Ground vs bed differentiation in macro-sim | Ground = 0.4× bed rate | Differentiator preserved offline. |

All values clamp at `MaxValue` (no overflow).

## 8. Error handling

- **Bed slot reservation race** (two characters target the same slot): `bed.UseSlot` returns false on collision; the action's `OnStart` checks the return and calls `Finish()` immediately if it failed. Caller (SleepBehaviour or BedFurnitureInteractable) sees `OnActionFinished` with no restoration applied and can retry with a different slot.
- **`EnterSleep` / `ExitSleep` on incapacitated character:** the sleep action's `CanExecute` checks `character.IsAlive()` and `!character.IsIncapacitated`. Defensive — `EnterSleep` is server-side and presumably safe, but the action layer is the right gate.
- **Action queued during a time skip** (`TimeSkipController.IsSkipping == true`): action's `CanExecute` rejects. Players cannot enqueue new sleep mid-skip; NPCs are frozen during the skip anyway.
- **Damage to a character whose `CurrentAction` is null** (already woke another way): `ExitSleep` is idempotent (verify in implementation). No-op.
- **Bed prefab without `BedFurniture` component** (legacy `Furniture` with `FurnitureTag.Bed`): existing fallback in `SleepBehaviour` preserves the legacy `Furniture.Use` path. New `BedFurnitureInteractable` `[RequireComponent]` will surface the missing component at edit time for new beds.
- **All `try/catch` follows rule #31:** I/O (save), network callbacks (RPC handlers), and the macro-sim restoration loop wrap fallible work and `Debug.LogException` on failure without swallowing.

## 9. Testing plan

### 9.1 Single-player smoke (host alone)

1. Start a fresh game. Verify HUD shows NeedSleep at 80 (starting value).
2. Wait for a phase tick — verify NeedSleep decays by 25.
3. Walk player to a bed, click → bed prompt opens with **slider defaulted to 7h**. Confirm.
4. Verify: player snaps to slot anchor; `IsSleeping = true`; auto-trigger fires; **fade-to-black plays for ~1.5s before the per-hour loop starts**; clock advances 7 hours; on wake, NeedSleep is restored, stamina is restored, **save fires once (host log)**.
5. Press `Z` somewhere not near a bed → `CharacterAction_Sleep` enqueues; player snaps to ground sleep pose; ticks restore at ground rate. Press `Z` again — player wakes. **Verify NO save fires** (this is a manual-cancel wake, not a time-skip wake).
6. Walk to a bed, click → prompt opens, immediately click Cancel. **Verify NO save fires** (no skip occurred).

### 9.2 Multiplayer smoke (Host + 1 Client)

1. Both players go to separate beds. Both pick different hours (4h, 6h).
2. Verify auto-trigger fires with `MIN(4, 6) = 4`. Fade-to-black plays. Both wake. Each sees the other's `IsSleeping` flip true → false correctly.
3. Verify `NeedSleep` restored on both sides (both peers see the NetworkVariable change).
4. Verify save fires once per player character on the host (host log shows two `RequestSave` calls — one per player). Client log shows no `SaveManager.RequestSave` invocation (server-only API).

### 9.3 Wake-on-attack

1. NPC enters scheduled sleep at home bed.
2. Hostile creature spawned in the bedroom; attacks NPC.
3. Verify NPC's `IsSleeping` flips false, sleep action cancels, bed slot released, NPC engages combat. **No save fires** (NPC + non-skip wake).
4. Repeat with player asleep on bed before time-skip auto-trigger fires + hostile NPC attack. **Verify NO save fires** even for the player character.

### 9.4 Save/load round-trip

1. Sleep, wake naturally, save fires. Reload save.
2. Verify `NeedSleep` restored to the post-sleep value (not the starting value).
3. Verify character is awake (not IsSleeping=true).

### 9.5 Edge cases

- Two NPCs target the same single-slot bed — second one falls back to ground sleep.
- Player opens bed prompt and Cancels — `PendingSkipHours` is never set, auto-trigger does NOT fire. Player walks away. No save.
- Player goes to bed, time-skip auto-fires, mid-skip the player disconnects — verify the skip continues to completion (one less player in the gate). Post-skip save loop iterates only currently-connected players.
- Host-alone session: confirm bed prompt with default 7h → fade plays → 7h skip runs → host wakes → save fires once.

## 9.6 Save-trigger correctness (regression-prevention)

This is the highest-risk behavioural change in the spec — a regression that re-enables saves on every wake would re-introduce the "lay-down-by-mistake" save churn the user explicitly called out. Add an EditMode-equivalent manual check to the test plan:

- After wake-on-attack: log inspection shows ZERO `RequestSave` calls between the attack moment and the next legitimate save event.
- After manual `Z` cancel: same check.
- After bed prompt cancel: same check.
- After successful time-skip: log inspection shows EXACTLY ONE `RequestSave` per player character.

## 10. Open questions / follow-ups

- **Wake-on-damage hook location:** confirmed at implementation time after a targeted read of `CharacterStatusManager` / `CharacterCombat`. If both have a damage event, prefer the higher-level one (status manager).
- **`NeedSleep` GOAP integration:** out of scope for v1 (Option A locked). If a future need emerges to have NPCs seek sleep when critically tired outside their schedule, add `IsActive` + `GetGoapActions` later — pure additive change.
- **Stamina restoration API:** uncertain whether `CharacterStamina` exposes a direct increase method or needs to go through `CharacterStats`. Read at implementation time; defensive default is to add a `Restore(float amount)` helper if missing.
- **HUD bar for NeedSleep:** out of scope for this design. Mirror `UI_HungerBar` later.
- **Bed prompt UX for cohabiting players:** if two players approach the same single-slot bed, the second's prompt should disable the Confirm button. Defer to UI polish.
- **Sleep pose on hibernation:** noted in the time-skip wiki as out of scope; this design preserves that decision.
- **Per-tick log volume in macro-sim:** `ApplySleepRestoreHours` runs per character per hour. Gate any logs behind `NPCDebug.VerboseSleep` (new toggle) per rule #34.

## 11. Out of scope (deliberately)

- Any GOAP integration for NeedSleep.
- Any new time-warp controller (existing `TimeSkipController` covers it).
- Any UI bar for NeedSleep (separate UI task).
- Migrating legacy plain-`Furniture` beds to `BedFurniture` (already noted in time-skip wiki).
- Persisting `IsSleeping` across save/load (already decided no).
- Auto-abort time-skip on combat (already noted as TODO in time-skip wiki).

## 12. Sources

- [docs/superpowers/specs/2026-04-27-time-skip-and-bed-furniture-design.md](2026-04-27-time-skip-and-bed-furniture-design.md) — predecessor design (shipped 2026-04-28).
- [wiki/systems/world-time-skip.md](../../../wiki/systems/world-time-skip.md) — shipped time-skip + bed-furniture architecture.
- [wiki/systems/character-needs.md](../../../wiki/systems/character-needs.md) — need pattern reference.
- [Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs](../../../Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs) — template for NeedSleep.
- [Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs](../../../Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs) — NetworkVariable plumbing template.
- [Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) — action lifecycle + RPC family.
- [Assets/Scripts/Character/CharacterActions/CharacterAction.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterAction.cs) — base contract.
- [Assets/Scripts/Interactable/TimeClockFurnitureInteractable.cs](../../../Assets/Scripts/Interactable/TimeClockFurnitureInteractable.cs) — interactable→action template.
- [Assets/Scripts/Interactable/FurnitureInteractable.cs](../../../Assets/Scripts/Interactable/FurnitureInteractable.cs) — interactable base.
- [Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs](../../../Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs) — driver to refactor.
- [Assets/Scripts/DayNightCycle/TimeSkipController.cs](../../../Assets/Scripts/DayNightCycle/TimeSkipController.cs) — multiplayer auto-trigger context.
- 2026-04-28 brainstorming conversation with Kevin — design constraints (Option A passive need, host-only save, every-wake save, ground+bed first-class, use existing time-skip).
