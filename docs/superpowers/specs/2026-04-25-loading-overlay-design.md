# Loading Overlay — design spec

**Date:** 2026-04-25
**Author:** Claude (with Kevin)
**Status:** approved
**Supersedes:** none

---

## 1. Goal

Show a polished loading screen while a remote client joins a multiplayer host. Display:

- A title (e.g. "Joining game…").
- A textual description of the current stage (e.g. "Synchronizing world…").
- A horizontal progress bar that advances by stage and refines within the spawn phase.
- A cancel button that appears after a delay so users can escape stalled joins.

The implementation must be generic — designed as a reusable "loading overlay" so future loading scenarios (save-load, scene transitions, solo session boot) can drive it without rewrites.

## 2. Non-goals

- Not refactoring `PauseMenuController` — separate component with separate responsibility.
- Not implementing additional consumers (save-load driver, scene-transition driver) in this pass; only `NetworkConnectionLoadingDriver` ships now. The overlay API is generic so adding consumers later is a separate ~50-LOC addition.
- Not changing NGO config (`SpawnTimeout`, `MaxPacketQueueSize` already tuned in `GameSessionManager.ApplyTransportTuning`).
- Not numeric `spawned/total` progress — NGO does not expose the expected total to the client cleanly. We use a stage-based bar with an in-stage spawn counter for text only.

## 3. Architecture

### Components

| Component | Type | Lifetime | Knows about |
|---|---|---|---|
| `LoadingOverlay` | MonoBehaviour, lazy singleton, `DontDestroyOnLoad` | Process-wide | UI only |
| `NetworkConnectionLoadingDriver` | MonoBehaviour, short-lived | One join attempt | NGO + `LoadingOverlay` |
| `UI_LoadingOverlay.prefab` | Prefab | — | — |

The overlay is a **pure UI controller**. It exposes a push API and knows nothing about NGO. The driver is the **producer** — it observes NGO events and pushes stage updates into the overlay. Future scenarios add new drivers; the overlay is unchanged.

### Why two components instead of one

Combining them would couple the overlay to NGO and force a rewrite when the second consumer (e.g. save-load) arrives. SOLID rule #9 (single purpose per class) applies here. Approach 3 (event bus) was rejected as overkill — direct push API is cleaner at this scale.

### Visual basis

`UI_LoadingOverlay.prefab` is created by **duplicating `UI_PauseMenu.prefab`** to inherit the project's overlay look (background dim, panel proportions, fonts, colours). The contents are then swapped: menu buttons removed, replaced with title text + stage text + progress bar (Slider) + cancel button.

## 4. `LoadingOverlay` API

```csharp
namespace MWI.UI.Loading
{
    public class LoadingOverlay : MonoBehaviour
    {
        public static LoadingOverlay Instance { get; }

        // Show the overlay with a title (e.g. "Joining game…").
        // Lazy-instantiates the prefab on first call.
        public void Show(string title);

        // Update the visible stage text + bar fill (0..1).
        // Bar tweens smoothly to the new value (unscaled time).
        public void SetStage(string stageText, float progress01);

        // Append/replace the small detail subline (e.g. "42 entities so far").
        // Optional. Pass null/empty to clear.
        public void SetDetail(string detail);

        // Wire up the cancel button. Pass null to disable cancel for this session.
        // The button stays hidden until `cancelDelaySeconds` (default 10) of unscaled time has elapsed since Show().
        public void SetCancelHandler(System.Action onCancel, float cancelDelaySeconds = 10f);

        // Switch the overlay into a failure state with a "Back to main menu" button.
        public void ShowFailure(string reason);

        // Hide and reset.
        public void Hide();

        public bool IsVisible { get; }
    }
}
```

### Lifecycle

- First `Show()` call instantiates `UI_LoadingOverlay.prefab` from a `Resources/UI/UI_LoadingOverlay` path (so the singleton works without scene authoring), parents under a `DontDestroyOnLoad` GameObject, and caches it.
- Subsequent `Show()` reuses the cached instance.
- `Hide()` deactivates the panel; the GameObject persists (cheap reuse).
- The panel uses `Time.unscaledDeltaTime` for the progress-bar tween and the cancel-button fade-in (rule #26 — UI must remain responsive when `GameSpeedController` is paused or warping).

## 5. `NetworkConnectionLoadingDriver` behaviour

### Lifetime

- Created in `GameSessionManager.JoinMultiplayer()` **before** `NetworkManager.StartClient()` is called (`new GameObject(...).AddComponent<NetworkConnectionLoadingDriver>()`).
- Self-destructs on `OnClientConnectedCallback` (success), `OnClientDisconnectCallback` (failure), or after the cancel button is clicked.
- Subscribes/unsubscribes from NGO events in `OnEnable`/`OnDisable`.

### Stage map

| # | Trigger | `SetStage(text, progress)` |
|---|---|---|
| 1 | `NetworkManager.OnClientStarted` | `"Connecting to host…", 0.10f` |
| 2 | StartClient returned true (set immediately after step 1) | `"Awaiting host approval…", 0.25f` |
| 3 | `NetworkSceneManager.OnSceneEvent` (`SceneEventType.Load`) | `"Loading scene: {sceneName}…", 0.40f` |
| 4 | `NetworkSceneManager.OnSceneEvent` (`SceneEventType.Synchronize`) | `"Synchronizing world…", 0.60f` (then incremented per spawn — see below) |
| 5 | `NetworkSceneManager.OnSceneEvent` (`SceneEventType.SynchronizeComplete`) | `"Finalizing…", 0.95f` |
| 6 | `NetworkManager.OnClientConnectedCallback` | `LoadingOverlay.Hide()` + driver self-destructs |
| 7 | `NetworkManager.OnClientDisconnectCallback` (when not yet connected) | `LoadingOverlay.ShowFailure("Disconnected from host")` + driver self-destructs |

### Spawn counter (within stage 4)

- Driver polls `NetworkManager.SpawnManager.SpawnedObjectsList.Count` every 0.1 s of unscaled time during stage 4 (the polling tick is cheap — `HashSet.Count` is O(1)). Cached baseline is recorded on entry to stage 4; `n = currentCount - baseline` is the per-stage spawn count.
- Pushes `SetDetail($"{n} entities loaded")` to the overlay on each tick.
- Bar fill formula: `0.60 + 0.30 * n / (n + 50)`. This is a clean asymptotic curve: bar = 0.75 at n=50, 0.80 at n=100, 0.85 at n=200, approaches 0.90 as n→∞. The bar never reaches 0.90 from spawns alone — only `SynchronizeComplete` (stage 5) jumps to 0.95.
- Polling is preferred over per-spawn event hooks because NGO 2.x doesn't expose a single static "an NO just spawned" event; the alternatives (per-NetworkBehaviour `OnNetworkSpawn` override or instrumenting `NetworkSpawnManager` internals) are invasive. A 10 Hz poll is sufficient for human-perceptible progress.

### Cancel handler

```csharp
LoadingOverlay.Instance.SetCancelHandler(() =>
{
    NetworkManager.Singleton?.Shutdown();
    SceneManager.LoadScene("MainMenuScene");
});
```

The 10 s delay matches the pause-menu prefab's existing `_fadeDuration`-style polish: short joins don't see the button; stalled joins get an escape hatch well before NGO's `SpawnTimeout` (30 s) fires.

## 6. Failure paths

| Path | Driver behaviour |
|---|---|
| `StartClient()` returns false | `ShowFailure("Failed to start client")`, driver self-destructs after 3 s. |
| Disconnect before `OnClientConnectedCallback` | `ShowFailure("Disconnected from host: {reason}")`. |
| `SpawnTimeout` fires (NGO logs an error and shuts down) | NGO triggers `OnClientDisconnectCallback`; same path as above. |
| User clicks cancel | `Shutdown()` + load `MainMenuScene`. |

The failure state replaces the cancel button with a "Back to main menu" button. Clicking it loads `MainMenuScene` (no `Shutdown()` call needed if NGO already disconnected).

## 7. Integration touchpoints

| File | Change |
|---|---|
| `GameSessionManager.JoinMultiplayer()` | Add 3 lines: instantiate driver, call `LoadingOverlay.Show("Joining game…")`, then proceed to `StartClient()`. Driver subscribes to NGO events in its own `OnEnable`. |
| `GameSessionManager.StartSolo()` | Out of scope (this pass). Could call `LoadingOverlay.Show("Loading session…")` in a follow-up. |

## 8. File layout

```
Assets/
  Scripts/
    UI/
      Loading/
        LoadingOverlay.cs
        NetworkConnectionLoadingDriver.cs
  Resources/
    UI/
      UI_LoadingOverlay.prefab     # lazy-loaded by LoadingOverlay singleton
docs/
  superpowers/
    specs/
      2026-04-25-loading-overlay-design.md     # this file
```

The prefab lives under `Resources/UI/` (not `Assets/UI/Menu/`) so the singleton can `Resources.Load` it without a serialized scene reference. This keeps `LoadingOverlay` truly autonomous — works from any scene, no setup required.

## 9. Edge cases & invariants

- **Multiple `Show()` calls before `Hide()`**: idempotent — second call updates the title and resets the cancel-delay timer.
- **`Show()` called during scene transition**: `DontDestroyOnLoad` keeps the overlay across scenes; the new scene sees it immediately.
- **Driver outlives a successful connect by accident**: `OnDestroy` unsubscribes from NGO events to prevent double-fire on reconnect.
- **Singleton already exists when scene loads**: lazy ctor returns the existing instance; no duplicate canvases.
- **Cancel clicked during failure state**: handler is replaced by `ShowFailure`'s back-button handler; double-click safe.
- **Bar tween still running when `Hide()` is called**: tween is cancelled; bar resets to 0 on next `Show()`.

## 10. Testing notes

- **Solo host**: place 1 building, then start client from another instance. Should see all 5 stages briefly, no cancel button (join completes < 10 s).
- **Heavy world**: load a saved world with many NPCs/buildings, then connect. Stage 4 should show the spawn counter ticking; bar fills smoothly.
- **Stuck join**: kill the host process during stage 4. After 10 s the cancel button appears; after ~30 s NGO's `SpawnTimeout` fires and the overlay flips to failure state.
- **Cancel mid-join**: click cancel during stage 4. Network shuts down cleanly, main menu loads.
- **Repeated joins**: cancel and re-join multiple times. No leaked drivers, no duplicate canvases, no event-handler accumulation (verified by Unity's `Profiler` Memory tab if needed).

## 11. Documentation impact

After implementation, update:

- `wiki/systems/network.md` — new section "Connection loading UI" referencing the overlay + driver pattern.
- `.agent/skills/multiplayer/SKILL.md` — add a short note on the driver pattern as the canonical way to surface NGO progress to UI.
- New `wiki/systems/loading-overlay.md` — full system page (Purpose, Public API, drivers, etc.) once the overlay has > 1 driver.

## 12. Sources

- [Assets/Scripts/Core/Network/GameSessionManager.cs](../../../Assets/Scripts/Core/Network/GameSessionManager.cs) — `JoinMultiplayer`, `ApprovalCheck`, NGO config tuning.
- [Assets/Scripts/UI/PauseMenu/PauseMenuController.cs](../../../Assets/Scripts/UI/PauseMenu/PauseMenuController.cs) — visual pattern reference + shutdown/return-to-menu coroutine pattern.
- [Assets/UI/UI_PauseMenu.prefab](../../../Assets/UI/UI_PauseMenu.prefab) — visual basis for the overlay prefab.
- [docs/superpowers/specs/2026-03-31-in-game-pause-menu-design.md](2026-03-31-in-game-pause-menu-design.md) — prior pause-menu design (style reference).
- 2026-04-25 conversation with Kevin — clarifying choices: A (coarse stages), B (cancel after 10 s), B (generic overlay).
