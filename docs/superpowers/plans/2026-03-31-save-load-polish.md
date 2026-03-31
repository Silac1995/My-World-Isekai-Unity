# Save/Load Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the save/load pipeline with freeze during save, status text overlay, event-driven ISaveable readiness, state guards, and robust error handling.

**Architecture:** ScreenFadeManager becomes a modular overlay system with status/warning text. SaveManager gets a coroutine-based orchestrated save flow with state enum guard. GameLauncher uses settling-based ISaveable readiness instead of hardcoded delay. All callers route through SaveManager.RequestSave().

**Tech Stack:** Unity 6, Canvas UI + TMP, Coroutines, WaitForSecondsRealtime

**Spec:** `docs/superpowers/specs/2026-03-31-save-load-polish-design.md`

---

## File Structure

### Modify Only (no new files)

| File | What Changes |
|------|-------------|
| `Assets/Scripts/UI/ScreenFadeManager.cs` | Add TMP status/warning text, ShowOverlay/HideOverlay/UpdateStatus/ShowWarning/ClearWarnings API |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | Add SaveLoadState enum, IsReady settling, RequestSave() coroutine orchestration, state guard |
| `Assets/Scripts/Core/GameLauncher.cs` | Replace 1s delay with WaitUntil(IsReady), add UpdateStatus calls throughout load |
| `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` | Route DebugSaveProfileAndWorld through SaveManager.RequestSave() |
| `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs` | Route save through SaveManager.RequestSave() |
| `Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs` | Route save through SaveManager.RequestSave() |
| `Assets/Scripts/DayNightCycle/TimeManager.cs` | Route auto-save through SaveManager.RequestSave() |

---

## Task 1: ScreenFadeManager — Modular Overlay System

**Files:**
- Modify: `Assets/Scripts/UI/ScreenFadeManager.cs`

**Ref:** Spec Section 4

- [ ] **Step 1: Read ScreenFadeManager.cs fully**

- [ ] **Step 2: Add TMP using and fields**

Add `using TMPro;` at the top. Add fields after `_fadeCoroutine`:

```csharp
private TextMeshProUGUI _statusText;
private TextMeshProUGUI _warningText;
private int _warningCount;
private const int MAX_VISIBLE_WARNINGS = 5;
```

- [ ] **Step 3: Create TMP text elements in InitializeFadeImage()**

After the existing Image setup, create two TMP_Text children:

```csharp
// Status text — centered, white, medium
GameObject statusGO = new GameObject("StatusText");
statusGO.transform.SetParent(_fadeImage.transform, false);
_statusText = statusGO.AddComponent<TextMeshProUGUI>();
_statusText.alignment = TextAlignmentOptions.Center;
_statusText.fontSize = 28;
_statusText.color = Color.white;
_statusText.raycastTarget = false;
RectTransform statusRT = _statusText.rectTransform;
statusRT.anchorMin = new Vector2(0.1f, 0.45f);
statusRT.anchorMax = new Vector2(0.9f, 0.55f);
statusRT.offsetMin = Vector2.zero;
statusRT.offsetMax = Vector2.zero;
_statusText.gameObject.SetActive(false);

// Warning text — below status, orange, smaller
GameObject warningGO = new GameObject("WarningText");
warningGO.transform.SetParent(_fadeImage.transform, false);
_warningText = warningGO.AddComponent<TextMeshProUGUI>();
_warningText.alignment = TextAlignmentOptions.Center;
_warningText.fontSize = 18;
_warningText.color = new Color(1f, 0.6f, 0f); // orange
_warningText.raycastTarget = false;
RectTransform warningRT = _warningText.rectTransform;
warningRT.anchorMin = new Vector2(0.1f, 0.3f);
warningRT.anchorMax = new Vector2(0.9f, 0.44f);
warningRT.offsetMin = Vector2.zero;
warningRT.offsetMax = Vector2.zero;
_warningText.gameObject.SetActive(false);
```

- [ ] **Step 4: Add ShowOverlay method**

```csharp
/// <summary>
/// Shows the overlay at the given alpha with optional status text.
/// Cancels any running fade. Sets raycastTarget to block input.
/// </summary>
public void ShowOverlay(float alpha, string status = null)
{
    if (_fadeCoroutine != null)
    {
        StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = null;
    }

    SetAlpha(alpha);
    _fadeImage.raycastTarget = true;

    if (_statusText != null)
    {
        _statusText.text = status ?? "";
        _statusText.gameObject.SetActive(true);
    }
    if (_warningText != null)
    {
        _warningText.gameObject.SetActive(true);
    }
}
```

- [ ] **Step 5: Add HideOverlay method**

```csharp
/// <summary>
/// Fades overlay out and disables text + input blocking.
/// </summary>
public void HideOverlay(float fadeDuration = 0.5f)
{
    if (_fadeCoroutine != null)
    {
        StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = null;
    }

    if (_statusText != null) _statusText.gameObject.SetActive(false);
    if (_warningText != null) _warningText.gameObject.SetActive(false);

    if (fadeDuration <= 0f)
    {
        SetAlpha(0f);
        _fadeImage.raycastTarget = false;
    }
    else
    {
        _fadeCoroutine = StartCoroutine(HideOverlayRoutine(fadeDuration));
    }
}

private System.Collections.IEnumerator HideOverlayRoutine(float duration)
{
    float startAlpha = _fadeImage.color.a;
    float elapsed = 0f;
    while (elapsed < duration)
    {
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        SetAlpha(Mathf.Lerp(startAlpha, 0f, t));
        yield return null;
    }
    SetAlpha(0f);
    _fadeImage.raycastTarget = false;
    _fadeCoroutine = null;
}
```

- [ ] **Step 6: Add UpdateStatus, ShowWarning, ClearWarnings**

```csharp
public void UpdateStatus(string status)
{
    if (_statusText != null) _statusText.text = status;
}

public void ShowWarning(string warning)
{
    if (_warningText == null) return;
    _warningCount++;
    if (_warningCount <= MAX_VISIBLE_WARNINGS)
    {
        _warningText.text += (string.IsNullOrEmpty(_warningText.text) ? "" : "\n") +
                             $"<color=orange>{warning}</color>";
    }
    else if (_warningCount == MAX_VISIBLE_WARNINGS + 1)
    {
        _warningText.text += $"\n<color=orange>+more warnings...</color>";
    }
}

public void ClearWarnings()
{
    _warningCount = 0;
    if (_warningText != null) _warningText.text = "";
}
```

- [ ] **Step 7: Update FadeIn/FadeOut to clear overlay text**

In `StartFade()`, clear text when doing a simple fade:
```csharp
private void StartFade(float from, float to, float duration)
{
    if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);

    // Clear overlay text — FadeIn/FadeOut are "simple fades" without status
    if (_statusText != null) _statusText.gameObject.SetActive(false);
    if (_warningText != null) _warningText.gameObject.SetActive(false);

    _fadeCoroutine = StartCoroutine(FadeRoutine(from, to, duration));
}
```

- [ ] **Step 8: Compile and verify**

- [ ] **Step 9: Commit**

```bash
git commit -m "feat(ui): extend ScreenFadeManager with modular overlay system (status + warnings)"
```

---

## Task 2: SaveManager — State Guard + Settling-Based Readiness

**Files:**
- Modify: `Assets/Scripts/Core/SaveLoad/SaveManager.cs`

**Ref:** Spec Sections 2 (State Guard), 5 (Readiness)

- [ ] **Step 1: Read SaveManager.cs fully**

- [ ] **Step 2: Add SaveLoadState enum and readiness fields**

At the top of the class, add:
```csharp
public enum SaveLoadState { Idle, Saving, Loading }
public SaveLoadState CurrentState { get; private set; } = SaveLoadState.Idle;

// Settling-based readiness
public bool IsReady { get; private set; }
public event Action OnReady;
private Coroutine _settlingCoroutine;
```

- [ ] **Step 3: Add settling timer to RegisterWorldSaveable**

Replace the existing `RegisterWorldSaveable`:
```csharp
public void RegisterWorldSaveable(ISaveable s)
{
    if (!worldSaveables.Contains(s)) worldSaveables.Add(s);
    ResetSettlingTimer();
    Debug.Log($"<color=green>[SaveManager]</color> Registered ISaveable '{s.SaveKey}'. Count: {worldSaveables.Count}");
}

private void ResetSettlingTimer()
{
    IsReady = false;
    if (_settlingCoroutine != null) StopCoroutine(_settlingCoroutine);
    _settlingCoroutine = StartCoroutine(SettlingRoutine());
}

private System.Collections.IEnumerator SettlingRoutine()
{
    yield return new WaitForSecondsRealtime(0.5f);
    IsReady = true;
    OnReady?.Invoke();
    _settlingCoroutine = null;
    Debug.Log($"<color=green>[SaveManager]</color> All ISaveables settled. IsReady=true. Count: {worldSaveables.Count}");
}
```

- [ ] **Step 4: Add RequestSave() entry point — coroutine-based orchestrated flow**

This is the single entry point for all save triggers. Replaces direct calls to `SaveWorldAsync()`.

```csharp
/// <summary>
/// Single entry point for all save triggers. Checks state guard, freezes game,
/// shows overlay with status, saves character+world, then unfreezes.
/// </summary>
public void RequestSave(Character playerCharacter)
{
    if (CurrentState != SaveLoadState.Idle)
    {
        Debug.LogWarning("<color=yellow>[SaveManager]</color> Save request dropped — already busy.");
        return;
    }
    StartCoroutine(OrchestratedSaveRoutine(playerCharacter));
}

private System.Collections.IEnumerator OrchestratedSaveRoutine(Character playerCharacter)
{
    CurrentState = SaveLoadState.Saving;
    Time.timeScale = 0f;

    var fade = ScreenFadeManager.Instance;
    fade?.ShowOverlay(0.7f, "Saving...");
    fade?.ClearWarnings();

    // Step 1: Save character profile FIRST (most important)
    fade?.UpdateStatus("Saving character profile...");
    yield return null; // Let UI update
    try
    {
        var coordinator = playerCharacter.GetComponent<CharacterDataCoordinator>();
        if (coordinator != null)
        {
            var task = coordinator.SaveLocalProfileAsync();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
                throw task.Exception.InnerException ?? task.Exception;
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"<color=red>[SaveManager]</color> Character profile save failed: {ex.Message}");
        fade?.ShowWarning("Character profile save failed!");
    }

    // Step 2: Snapshot active buildings
    fade?.UpdateStatus("Syncing buildings...");
    yield return null;
    foreach (var mc in MapController.ActiveControllers.ToArray())
    {
        if (mc != null && !string.IsNullOrEmpty(mc.MapId))
        {
            try { mc.SnapshotActiveBuildings(); }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> Building snapshot failed for '{mc.MapId}': {ex.Message}");
                fade?.ShowWarning($"Building sync failed: {mc.MapId}");
            }
        }
    }

    // Step 3: Serialize ISaveables
    var jsonSettings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
    var data = new GameSaveData();
    data.metadata.worldGuid = CurrentWorldGuid;
    data.metadata.worldName = !string.IsNullOrEmpty(CurrentWorldName) ? CurrentWorldName : "My World";
    data.metadata.timestamp = DateTime.Now.ToString("o");
    data.metadata.isEmpty = false;

    foreach (var s in worldSaveables)
    {
        fade?.UpdateStatus($"Saving {s.SaveKey}...");
        yield return null;
        try
        {
            data.worldStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState(), jsonSettings);
        }
        catch (Exception ex)
        {
            Debug.LogError($"<color=red>[SaveManager]</color> Failed to capture '{s.SaveKey}': {ex.Message}");
            fade?.ShowWarning($"Failed: {s.SaveKey}");
        }
    }

    // Step 4: Snapshot active NPCs
    fade?.UpdateStatus("Saving NPCs...");
    yield return null;
    foreach (var mc in MapController.ActiveControllers.ToArray())
    {
        if (mc == null || string.IsNullOrEmpty(mc.MapId)) continue;
        try
        {
            var snapshot = mc.SnapshotActiveNPCs();
            if (snapshot.HibernatedNPCs.Count > 0)
                data.worldStates[$"MapSnapshot_{mc.MapId}"] = JsonConvert.SerializeObject(snapshot, jsonSettings);
        }
        catch (Exception ex)
        {
            Debug.LogError($"<color=red>[SaveManager]</color> NPC snapshot failed for '{mc.MapId}': {ex.Message}");
            fade?.ShowWarning($"NPC snapshot failed: {mc.MapId}");
        }
    }

    // Step 5: Write world file
    fade?.UpdateStatus("Writing world to disk...");
    yield return null;
    try
    {
        if (!string.IsNullOrEmpty(CurrentWorldGuid))
        {
            var writeTask = SaveFileHandler.WriteWorldAsync(CurrentWorldGuid, data);
            while (!writeTask.IsCompleted) yield return null;
            if (writeTask.IsFaulted)
                throw writeTask.Exception.InnerException ?? writeTask.Exception;
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"<color=red>[SaveManager]</color> World file write failed: {ex.Message}");
        fade?.ShowWarning("World file write failed!");
    }

    // Step 6: Complete
    fade?.UpdateStatus("Save complete!");
    yield return new WaitForSecondsRealtime(0.5f);

    Time.timeScale = 1f;
    fade?.HideOverlay(0.3f);
    CurrentState = SaveLoadState.Idle;
    OnSaveCompleted?.Invoke(CurrentWorldGuid);

    Debug.Log($"<color=green>[SaveManager]</color> Orchestrated save complete for '{CurrentWorldName}'.");
}
```

- [ ] **Step 5: Keep SaveWorldAsync() as an internal/direct method for shutdown**

Rename it to `SaveWorldDirectAsync()` — used only by `SaveHostPlayerProfileOnShutdown` (no overlay, no freeze, no guard):

```csharp
/// <summary>
/// Direct async save — no overlay, no freeze, no guard.
/// Used ONLY by shutdown path (OnApplicationQuit).
/// </summary>
private async Task SaveWorldDirectAsync()
{
    // ... existing SaveWorldAsync() body (unchanged)
}
```

Update `SaveHostPlayerProfileOnShutdown` to call `SaveWorldDirectAsync()`.

- [ ] **Step 6: Update LoadWorldAsync to use state guard**

At the top of `LoadWorldAsync`, add:
```csharp
CurrentState = SaveLoadState.Loading;
```
At the end:
```csharp
CurrentState = SaveLoadState.Idle;
```

- [ ] **Step 7: Compile and verify**

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(save): add orchestrated save flow with state guard and settling-based readiness"
```

---

## Task 3: GameLauncher — Status Updates + Readiness Wait

**Files:**
- Modify: `Assets/Scripts/Core/GameLauncher.cs`

**Ref:** Spec Section 3

- [ ] **Step 1: Read GameLauncher.cs fully**

- [ ] **Step 2: Replace hardcoded 1s delay with settling-based wait**

Find the `WaitForSecondsRealtime(1.0f)` and replace with:

```csharp
// Wait for ISaveable readiness (settling-based)
ScreenFadeManager.Instance?.UpdateStatus("Waiting for world systems...");
float timeout = 10f;
float elapsed = 0f;
while (SaveManager.Instance != null && !SaveManager.Instance.IsReady && elapsed < timeout)
{
    yield return null;
    elapsed += Time.unscaledDeltaTime;
}
if (SaveManager.Instance != null && !SaveManager.Instance.IsReady)
{
    Debug.LogWarning($"{LOG_TAG} Timeout waiting for ISaveable registration — proceeding anyway.");
    ScreenFadeManager.Instance?.ShowWarning("Some world systems may not have loaded.");
}
```

- [ ] **Step 3: Add UpdateStatus calls throughout LaunchSequence**

Add `ScreenFadeManager.Instance?.UpdateStatus(...)` before each major step:
- Before scene load: "Loading scene..."
- Before WaitForPlayerSpawn: "Spawning player..."
- Before ISaveable wait: "Waiting for world systems..."
- Before LoadWorldData: "Restoring world data..."
- Before SpawnSavedBuildings: "Spawning buildings..."
- Before LoadAndImportProfile: "Loading character profile..."
- Before PositionCharacter: "Positioning character..."
- Before SpawnPartyMembers: "Spawning party members..."

- [ ] **Step 4: Set SaveManager state to Loading at start, Idle at end**

At the start of LaunchSequence (after IsLaunching = true):
```csharp
if (SaveManager.Instance != null) SaveManager.Instance.CurrentState = SaveLoadState.Loading;
```
Before FadeInSafely at the end:
```csharp
if (SaveManager.Instance != null) SaveManager.Instance.CurrentState = SaveLoadState.Idle;
```

Note: `CurrentState` setter needs to be made internal or public for this. Or add a `SetState()` method on SaveManager.

- [ ] **Step 5: Compile and verify**

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(load): add status updates and settling-based readiness wait to GameLauncher"
```

---

## Task 4: Route All Save Callers Through RequestSave()

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs`
- Modify: `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs`
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterMapTransitionAction.cs`
- Modify: `Assets/Scripts/DayNightCycle/TimeManager.cs`

**Ref:** Spec Section 2 (Implementation Pattern)

- [ ] **Step 1: Update CharacterDataCoordinator.DebugSaveProfileAndWorld**

Replace the direct calls with:
```csharp
[ContextMenu("Debug: Save Profile + World")]
private void DebugSaveProfileAndWorld()
{
    if (SaveManager.Instance != null)
        SaveManager.Instance.RequestSave(_character);
    else
        Debug.LogWarning($"{LOG_TAG} SaveManager.Instance is null — cannot save.");
}
```

- [ ] **Step 2: Update SleepBehaviour**

Find the save trigger in `Exit()` and replace the direct `SaveLocalProfileAsync()` + `SaveWorldAsync()` calls with:
```csharp
if (character.IsServer && character.IsPlayer())
{
    if (SaveManager.Instance != null)
        SaveManager.Instance.RequestSave(character);
}
```

- [ ] **Step 3: Update CharacterMapTransitionAction**

Find the save trigger in `OnApplyEffect()` and replace with the same `RequestSave()` call.

- [ ] **Step 4: Update TimeManager auto-save**

Find where `SaveWorldAsync()` is called on hour change and replace with:
```csharp
// Find the local player character for RequestSave
if (SaveManager.Instance != null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
{
    var localClient = Unity.Netcode.NetworkManager.Singleton.LocalClient;
    if (localClient?.PlayerObject != null)
    {
        var playerChar = localClient.PlayerObject.GetComponent<Character>();
        if (playerChar != null)
            SaveManager.Instance.RequestSave(playerChar);
    }
}
```

- [ ] **Step 5: Compile and verify no direct SaveWorldAsync() calls remain**

Search: `grep -r "SaveWorldAsync\|SaveLocalProfileAsync" Assets/Scripts/ --include="*.cs" -l`

The only remaining call should be in `SaveManager.SaveWorldDirectAsync()` (internal) and `SaveManager.SaveHostPlayerProfileOnShutdown()` (shutdown path).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(save): route all save callers through SaveManager.RequestSave()"
```

---

## Task 5: Integration Verification

**Files:**
- No changes — verification only

- [ ] **Step 1: Test save flow**

1. Play → create world → create character → load in
2. Place a building
3. Right-click CharacterDataCoordinator → "Debug: Save Profile + World"
4. Verify: game freezes, semi-transparent overlay appears, status text progresses through steps, "Save complete!" shows briefly, game unfreezes
5. Check console for green save logs with no red errors

- [ ] **Step 2: Test load flow**

1. Stop play mode → re-enter play mode
2. Select same world + character
3. Verify: black screen with "Loading..." status progresses through steps, building appears after load, character at saved position

- [ ] **Step 3: Test save guard**

1. While in-game, trigger a save (context menu)
2. Immediately try to trigger another save — verify console shows "Save request dropped"

- [ ] **Step 4: Test error display**

1. Intentionally corrupt a save file (edit JSON to be invalid)
2. Load the world — verify orange warnings appear on overlay
3. Game still loads (partial restore)

- [ ] **Step 5: Commit any fixes**

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | ScreenFadeManager modular overlay | 1 file |
| 2 | SaveManager orchestrated flow + state guard + readiness | 1 file |
| 3 | GameLauncher status updates + readiness wait | 1 file |
| 4 | Route all callers through RequestSave() | 4 files |
| 5 | Integration verification | 0 files |

**Total: 5 tasks, 7 files**

**Execution order:** Tasks 1-2 are independent (can parallel). Task 3 depends on Task 2. Task 4 depends on Task 2. Task 5 depends on all.
