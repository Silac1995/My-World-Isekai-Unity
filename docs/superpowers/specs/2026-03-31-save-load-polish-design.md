# Save/Load Polish — Design Spec

**Date:** 2026-03-31
**Status:** Approved
**Scope:** Robust save/load flow with freeze, status overlay, event-driven ISaveable readiness, save guards, error handling, and modular ScreenFadeManager

---

## 1. Overview

Harden the save/load pipeline so it works reliably at scale (dozens of buildings, hundreds of NPCs). The player sees clear feedback during saves and loads, the game freezes during saves to prevent state corruption, and ISaveable registration uses event-driven readiness instead of hardcoded delays.

### Problems Solved

- Save can fire while another save is in progress (data corruption)
- No player feedback during save or load (looks frozen/broken)
- Hardcoded 1s delay for ISaveable registration (fragile, arbitrary)
- Player can move/interact during save (state changes mid-serialization)
- Errors during save/load are silently swallowed

---

## 2. Save Flow

When a save triggers (bed/sleep, map transition, host shutdown, debug context menu):

1. **Guard check**: `if (_isSaving) return;` — set `_isSaving = true`
2. **Freeze**: `Time.timeScale = 0`
3. **Show semi-transparent overlay** (alpha ~0.7): `ScreenFadeManager.Instance.ShowOverlay(0.7f, "Saving...")`
4. **Save character profile** (most important — saved first):
   - Status: "Saving character profile..."
   - `CharacterDataCoordinator.SaveLocalProfileAsync()`
   - On error: show orange warning, continue
5. **Snapshot active buildings**:
   - Status: "Syncing buildings..."
   - `SnapshotActiveBuildings()` per active MapController
6. **Serialize ISaveables**:
   - For each: status = "Saving {SaveKey}..."
   - Catch exceptions per system — show warning, continue with next
7. **Snapshot active NPCs**:
   - Status: "Saving NPCs..."
   - `SnapshotActiveNPCs()` per active MapController
8. **Write world file**:
   - Status: "Writing world to disk..."
   - `SaveFileHandler.WriteWorldAsync()`
9. **Complete**:
   - Status: "Save complete!" (hold 0.5s so player sees it)
10. **Unfreeze**: `Time.timeScale = 1`, `HideOverlay()`
11. Set `_isSaving = false`

### State Guard (Mutual Exclusion)

`SaveManager` uses a state enum instead of boolean flags:
```csharp
public enum SaveLoadState { Idle, Saving, Loading }
public SaveLoadState CurrentState { get; private set; } = SaveLoadState.Idle;
```

- Saves are blocked when `CurrentState != Idle`
- Loads are blocked when `CurrentState != Idle`
- No save-during-load or load-during-save or save-during-save
- Any trigger that fires during a non-Idle state is silently dropped

**Shutdown exception:** `OnApplicationQuit` always saves regardless of state — it's the last chance. It skips the guard and writes immediately.

### Character Profile Saves First

If the world save fails partway through, the character profile is already safely on disk. The player never loses their character data.

### Implementation Pattern

The save flow is implemented as a **coroutine** on `SaveManager` (not an async Task). This allows `yield return` for timing (e.g., hold "Save complete!" text) while `Time.timeScale = 0` using `WaitForSecondsRealtime`. Async sub-operations (file I/O) are bridged with `Task.ContinueWith` or `yield return null` polling.

All existing callers of `SaveWorldAsync()` — including `TimeManager` auto-save, `SleepBehaviour`, `CharacterMapTransitionAction`, and the debug context menu — are routed through a single `RequestSave()` entry point that checks the state guard.

### Warning Lifecycle

`ClearWarnings()` is called at the start of each save/load flow. Warnings from previous operations are not carried over. Maximum 5 visible warnings — additional warnings show "+N more" suffix.

---

## 3. Load Flow

When GameLauncher loads a world + character:

1. **Black overlay**: `ShowOverlay(1.0f, "Loading...")`
2. **Load game scene**: status = "Loading scene..."
3. **Start network**: status = "Starting network..."
4. **Wait for player spawn**: status = "Spawning player..."
5. **Wait for ISaveable readiness** (event-driven): status = "Waiting for world systems..."
   - `yield return WaitUntil(() => SaveManager.Instance.IsReady)` with 10s timeout
   - On timeout: proceed with warning
6. **Load world save**: status = "Restoring world data..."
   - For each ISaveable: status = "Restoring {SaveKey}..."
   - Errors shown as orange warnings, continue with next
7. **Spawn buildings**: status = "Spawning buildings..."
   - `SpawnSavedBuildings()` on each predefined MapController
8. **Spawn NPCs from snapshots**: status = "Spawning NPCs..."
   - From `PendingSnapshots`
9. **Import character profile**: status = "Loading character profile..."
   - Each subsystem: "Restoring {SaveKey}..."
10. **Position character**: status = "Positioning character..."
11. **Spawn party NPCs**: status = "Spawning party members..."
12. **Fade in**: `HideOverlay(0.5f)`

---

## 4. ScreenFadeManager Extension (Modular Overlay System)

The existing `ScreenFadeManager` becomes a **general-purpose screen overlay** usable by any system — save/load, map transitions, server connections, etc.

### New UI Elements (children of existing full-screen Image)

- `TMP_Text _statusText` — centered, white, medium font. Shows current operation.
- `TMP_Text _warningText` — below status, orange, smaller font. Accumulates warning messages.

### API

```csharp
// Core overlay control
void ShowOverlay(float alpha, string status = null);  // Shows overlay at specified alpha
void HideOverlay(float fadeDuration = 0.5f);          // Fades out and hides overlay
void UpdateStatus(string status);                       // Updates status text
void ShowWarning(string warning);                       // Appends orange warning line
void ClearWarnings();                                   // Clears warning text

// Existing (unchanged)
void FadeOut(float duration);
void FadeIn(float duration);
bool IsFading { get; }
```

### Usage Examples

| System | Call | Alpha | Purpose |
|--------|------|-------|---------|
| Save | `ShowOverlay(0.7f, "Saving...")` | 0.7 | Semi-transparent — player sees frozen world |
| Load | `ShowOverlay(1.0f, "Loading scene...")` | 1.0 | Full black — nothing to see |
| Map transition | `ShowOverlay(1.0f, "Entering building...")` | 1.0 | Full black during warp |
| Server connect | `ShowOverlay(1.0f, "Connecting to server...")` | 1.0 | Full black while connecting |
| Any system | `UpdateStatus("Step 3 of 5...")` | - | Update text without changing overlay |

### Implementation Notes

- Text elements created programmatically in `Awake()` (same self-bootstrap pattern as existing Image)
- `ShowOverlay` cancels any running `FadeIn`/`FadeOut` coroutine first, then sets Image alpha immediately, enables text, sets `raycastTarget = true` to block player input
- `HideOverlay` cancels any running fade, then fades alpha to 0 using `Time.unscaledDeltaTime`, disables text, sets `raycastTarget = false`
- `FadeIn`/`FadeOut` also clear overlay text — they are "simple fades" without status. If overlay text is showing, calling `FadeIn` clears it (transition from overlay mode to simple fade mode)
- `ShowWarning` appends to `_warningText.text` with newline. Orange color via TMP rich text `<color=orange>`
- All fade/overlay animations use `Time.unscaledDeltaTime` (works during `timeScale = 0`)
- All timing waits during save flow use `WaitForSecondsRealtime` (not `WaitForSeconds`) since `timeScale = 0`

---

## 5. SaveManager Readiness (Event-Driven)

### Problem

ISaveable systems (CommunityTracker, TimeManager, etc.) register with `SaveManager` via deferred `Invoke(0.5f)`. The current solution is a hardcoded 1s wait, which is fragile.

### Solution: Settling-Based Readiness

- `SaveManager` tracks a readiness state based on registration activity
- After the **last registration**, a 0.2s settling timer starts
- If no new registrations arrive within 0.5s, `IsReady` becomes true and `OnReady` fires
- Any new registration resets the 0.2s timer

### API

```csharp
public bool IsReady { get; }           // True when all systems have registered
public event Action OnReady;           // Fires once when IsReady becomes true
```

### Implementation

```csharp
public void RegisterWorldSaveable(ISaveable s)
{
    if (!worldSaveables.Contains(s)) worldSaveables.Add(s);
    ResetSettlingTimer();
}

private void ResetSettlingTimer()
{
    if (_settlingCoroutine != null) StopCoroutine(_settlingCoroutine);
    _settlingCoroutine = StartCoroutine(SettlingRoutine());
}

private IEnumerator SettlingRoutine()
{
    yield return new WaitForSecondsRealtime(0.5f);
    IsReady = true;
    OnReady?.Invoke();
    _settlingCoroutine = null;
}
```

### GameLauncher Usage

```csharp
// Replace hardcoded 1s delay with:
float timeout = 10f;
float elapsed = 0f;
while (!SaveManager.Instance.IsReady && elapsed < timeout)
{
    yield return null;
    elapsed += Time.unscaledDeltaTime;
}
if (!SaveManager.Instance.IsReady)
{
    Debug.LogWarning("[GameLauncher] Timeout waiting for ISaveable registration — proceeding anyway.");
    ScreenFadeManager.Instance.ShowWarning("Some world systems may not have loaded.");
}
```

### Why Settling, Not Count

We don't know the exact number of ISaveables upfront. The settling approach adapts automatically — add a new ISaveable system and it just works. No hardcoded count to maintain.

---

## 6. Error Handling

### Philosophy: Continue and Warn

Every fallible operation (serialization, file I/O, deserialization) is wrapped in try-catch. On failure:

1. Log the error with `Debug.LogError`
2. Show orange warning on the overlay via `ScreenFadeManager.Instance.ShowWarning(message)`
3. Continue with the next step

Nothing aborts the save or load. Partial saves are better than no saves. Partial loads are better than no loads.

### Save Errors

| Step | On Failure | Effect |
|------|-----------|--------|
| Character profile | Warning shown | World save still proceeds |
| Building snapshot | Warning shown | ISaveable serialize still proceeds |
| ISaveable serialize | Warning per system | Other systems still serialize |
| NPC snapshot | Warning shown | World file still writes |
| World file write | Warning shown | Character profile already saved |

### Load Errors

| Step | On Failure | Effect |
|------|-----------|--------|
| World file read | Warning, skip world restore | Character still loads, fresh world |
| ISaveable restore | Warning per system | Other systems still restore |
| Building spawn | Warning per building | Other buildings still spawn |
| NPC spawn | Warning per NPC | Other NPCs still spawn |
| Character profile | Warning | Player spawns with defaults |

---

## 7. Files to Create / Modify

### Modify
- `Assets/Scripts/UI/ScreenFadeManager.cs` — Add status text, warning text, ShowOverlay/HideOverlay/UpdateStatus/ShowWarning API
- `Assets/Scripts/Core/SaveLoad/SaveManager.cs` — Add `_isSaving` guard, `IsReady`/`OnReady` settling-based readiness, move save orchestration here with status updates
- `Assets/Scripts/Core/GameLauncher.cs` — Replace hardcoded 1s delay with `WaitUntil(IsReady)`, add status updates throughout load sequence
- `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` — Update `DebugSaveProfileAndWorld` to use new SaveManager orchestrated flow
- `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs` — Use new orchestrated save flow
- `Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs` — Use new orchestrated save flow
- `Assets/Scripts/DayNightCycle/TimeManager.cs` — Route auto-save through `SaveManager.RequestSave()` instead of direct `SaveWorldAsync()`

### No New Files

All changes are extensions to existing classes. No new scripts needed.

---

## 8. Out of Scope

- Progress bar (percentage/fill bar UI) — text status is sufficient for now
- Save file backup/rotation (keeping multiple versions)
- Save file integrity verification (checksums)
- Async scene loading progress bar
