---
type: system
title: "Loading Overlay"
tags: [ui, multiplayer, network, loading, tier-3]
created: 2026-04-25
updated: 2026-04-25
sources: []
related:
  - "[[network]]"
  - "[[player-ui]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: network-specialist
secondary_agents: []
owner_code_path: "Assets/Scripts/UI/Loading/"
depends_on:
  - "[[network]]"
depended_on_by: []
---

# Loading Overlay

## Summary

Generic full-screen loading screen used to hide the latency of long-running operations from the player. It is a **pure UI controller** (`LoadingOverlay`, lazy-singleton, `DontDestroyOnLoad`, lazy-loads its prefab from `Resources/UI/UI_LoadingOverlay`) with a small push API: `Show / SetStage / SetDetail / SetCancelHandler / ShowFailure / Hide`. Producers — currently only `NetworkConnectionLoadingDriver` for multiplayer client-join — translate their domain events into stage updates and push them in. The overlay knows nothing about NGO, save/load, or scene transitions.

## Purpose

A remote client joining an MWI host walks NGO's full scene-sync handshake: transport handshake → connection approval → scene load → spawn-payload streaming for every networked entity → finalize. On a populated world this can take several seconds with no visual indication that anything is happening — the screen freezes from the player's perspective. Without a loading overlay, users assume the game crashed and quit. The overlay surfaces (a) which stage of the join is currently running, (b) approximately how much progress has been made, and (c) an escape hatch ("Cancel") if the join stalls.

The overlay is **deliberately generic** so future loading scenarios — save-load restore, large scene transitions, solo session boot, large procedural-generation passes — can drive it without changes. The "what's loading" lives in driver components; the overlay is just a renderer.

## Responsibilities

- Display the current loading **title**, **stage description**, optional **detail subline**, and a **progress bar** (Slider 0..1 with smooth tween).
- Display a **cancel button** that stays hidden for a configurable delay (default 10 s of unscaled time) then fades in. Click invokes the registered handler.
- Display a **failure state** with an immediately-surfaced "Back to main menu" button when a producer reports the operation cannot complete.
- Survive scene loads (`DontDestroyOnLoad`).
- Use **unscaled time** for all animations (rule #26 — survives `GameSpeedController` pause / warp).

**Non-responsibilities** (common misconceptions):
- Not responsible for observing NGO events — that lives in [[network]] drivers (e.g. `NetworkConnectionLoadingDriver`).
- Not responsible for triggering loading — callers (`GameSessionManager.JoinMultiplayer`, future save-load entry points, etc.) call `Show()`.
- Not a `NetworkBehaviour` and never accesses `NetworkManager` — strict client-side UI only, no replication concerns.
- Not responsible for visual styling beyond the prefab — the prefab inherits the look of `UI_PauseMenu.prefab` for consistency, but the controller has no opinion on colours/fonts.

## Key classes / files

| File | Role |
|------|------|
| [LoadingOverlay.cs](../../Assets/Scripts/UI/Loading/LoadingOverlay.cs) | Lazy singleton MonoBehaviour. Public push API + Awake/Update/coroutine internals (bar tween, cancel-button fade-in). |
| [NetworkConnectionLoadingDriver.cs](../../Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs) | Short-lived NGO observer. Subscribes to `NetworkManager` connection lifecycle events in `OnEnable`; pushes stage updates into `LoadingOverlay`; self-destructs on connect/disconnect/cancel. |
| [Resources/UI/UI_LoadingOverlay.prefab](../../Assets/Resources/UI/UI_LoadingOverlay.prefab) | Visual prefab. Canvas `sortingOrder=1000` (above gameplay HUD), full-screen dim background, centered 800×320 panel with title / stage / detail / progress bar / cancel button. Lives in `Resources/` so `LoadingOverlay.Instance` can `Resources.Load` it without scene authoring. |
| [GameSessionManager.cs](../../Assets/Scripts/Core/Network/GameSessionManager.cs) (`JoinMultiplayer`) | Sole call site that currently invokes the overlay + driver pair. |

## Public API / entry points

`LoadingOverlay` (singleton):

- `static LoadingOverlay Instance { get; }` — lazy-instantiates the prefab on first access. Returns null if `Resources.Load` fails (logs an error). `DontDestroyOnLoad` is guarded by `Application.isPlaying` so the singleton is also usable from EditMode tooling.
- `void Show(string title)` — activates the panel, resets state (bar to 0, cancel hidden, stage/detail empty), records the moment for the cancel-delay timer.
- `void SetStage(string stageText, float progress01)` — updates stage text and tweens the bar to the clamped target value.
- `void SetDetail(string detail)` — updates the small detail subline (e.g. live entity count). Pass null/empty to clear.
- `void SetCancelHandler(Action onCancel, float cancelDelaySeconds = 10f)` — stores the callback; `Update` watches the unscaled clock and fades the button in once the delay elapses.
- `void ShowFailure(string reason)` — flips into terminal failure state: stage text becomes `"Connection failed: <reason>"`, the cancel button surfaces immediately at full alpha + interactive, label becomes "Back to main menu".
- `void Hide()` — deactivates the panel, cancels in-flight tweens.
- `bool IsVisible { get; }` — reflects the panel root's active state.

`NetworkConnectionLoadingDriver`:

- `void RegisterCancelHandler()` — called by `GameSessionManager.JoinMultiplayer` immediately after instantiating the driver. Wires the overlay's cancel button to a handler that calls `NetworkManager.Singleton.Shutdown()` then loads `MainMenuScene`.

## Data flow

```
[Caller: GameSessionManager.JoinMultiplayer]
        │
        ├─► LoadingOverlay.Instance.Show("Joining game…")
        │
        └─► new NetworkConnectionLoadingDriver
                │
                ├─ subscribes to NetworkManager events:
                │   • OnClientStarted
                │   • OnClientConnectedCallback
                │   • OnClientDisconnectCallback
                │   • SceneManager.OnSceneEvent (Load / Synchronize / SynchronizeComplete)
                │
                └─ for each event: pushes a stage update
                        ▼
                LoadingOverlay.SetStage(text, 0..1)   ──►  Slider tween + text refresh
                LoadingOverlay.SetDetail(text)        ──►  detail subline refresh
                LoadingOverlay.ShowFailure(reason)    ──►  terminal failure state
                LoadingOverlay.Hide()                 ──►  panel hidden, driver self-destructs
```

### Stage map (client-join via `NetworkConnectionLoadingDriver`)

| Trigger | Stage text | Bar fill |
|---|---|---|
| `OnClientStarted` | "Connecting to host…" | 0.10 |
| (one frame after OnClientStarted) | "Awaiting host approval…" | 0.25 |
| `OnSceneEvent` `Load` | "Loading scene: {sceneName}…" | 0.40 |
| `OnSceneEvent` `Synchronize` | "Synchronizing world…" | 0.60 → 0.90 (asymptotic) |
| (during Synchronize, polled at 10 Hz) | (detail) "{n} entities loaded" | bar fills via `0.60 + 0.30 · n / (n + 50)` |
| `OnSceneEvent` `SynchronizeComplete` | "Finalizing…" | 0.95 |
| `OnClientConnectedCallback` (matching `LocalClientId`) | (overlay hidden, driver destroyed) | — |
| `OnClientDisconnectCallback` (before connect) | "Connection failed: <reason>" | 1.00 + Back button |

The synchronize-stage spawn count is computed as `SpawnManager.SpawnedObjectsList.Count - baseline` (baseline captured on stage entry). Polling beats per-spawn event hooks because NGO 2.x doesn't expose a single static "an NO just spawned" event; the alternatives (per-NetworkBehaviour `OnNetworkSpawn` overrides or instrumenting `NetworkSpawnManager` internals) are invasive. A 10 Hz cadence is sufficient for human-perceptible progress and the cost is `O(1)` (`HashSet.Count`).

### Cancel flow

- During an in-flight join: `_onCancel` is set to a closure that calls `NetworkManager.Shutdown()` + loads `MainMenuScene`. After the 10 s delay the button fades in via `Time.unscaledDeltaTime`. Click → shutdown → main menu.
- On failure (pre-connect disconnect): the driver replaces the cancel handler with a "back to main menu" handler (delay 0 → button surfaces immediately) and calls `LoadingOverlay.ShowFailure(reason)`. The overlay's `ShowFailure` repurposes the same button (label "Back to main menu", full alpha, interactive). Click → main menu (NGO has already disconnected).

## Dependencies

### Upstream (this system needs)

- [[network]] — `NetworkConnectionLoadingDriver` consumes NGO events. Without `NetworkManager.Singleton` initialised, the driver self-destructs in `OnEnable` and logs an error.
- Unity UI (uGUI) Slider, Image, Button, CanvasGroup. TextMeshProUGUI for text.

### Downstream (systems that need this)

- [[network]] (`GameSessionManager.JoinMultiplayer`) — only current consumer. The overlay+driver are instantiated immediately before `StartClient()` so the driver's `OnEnable` hooks `OnClientStarted` before NGO fires it.

## State & persistence

- **Runtime state** (singleton, in-memory): cached prefab instance, current bar target / displayed values, cancel-handler callback, "shown at" unscaled timestamp, "cancel button shown" latch, two coroutines (bar tween + cancel fade-in).
- **Persisted state**: none. The overlay is purely transient client-side UI; nothing is saved to disk or replicated over the network.
- **NetworkVariable / RPC traffic**: zero. `LoadingOverlay` is a `MonoBehaviour`, not a `NetworkBehaviour`. The driver only reads NGO state and subscribes to events.

## Known gotchas / edge cases

- **`DontDestroyOnLoad` only works in playmode.** The singleton's lazy `Instance` getter guards the call with `if (Application.isPlaying)` so EditMode tools / smoke tests can instantiate it without throwing. See `.agent/skills/multiplayer/SKILL.md` §10 ("Application.isPlaying guard").
- **Driver must exist before `StartClient`.** The driver's `OnEnable` subscribes to `NetworkManager.OnClientStarted`, which fires synchronously inside `StartClient`. If the driver is instantiated *after* `StartClient`, the connecting stage is missed and the bar starts at the awaiting-approval step instead of connecting.
- **`SceneManager` may be null in the driver's `OnEnable`.** NGO creates `NetworkManager.SceneManager` only when `StartClient` succeeds. The driver handles this with `WatchForSceneManager` — a one-shot per-frame poll that subscribes to `OnSceneEvent` as soon as `SceneManager` is non-null. Without this, scene events are missed and the bar stalls at "Awaiting approval…".
- **Bar tween / cancel-button delay use `UnityEngine.Time.unscaledDeltaTime`, not `Time.deltaTime`.** Required by rule #26 (UI must remain responsive when `GameSpeedController` pauses or warps simulation time). The fully-qualified `UnityEngine.Time` is also required because the project has its own `MWI.Time` namespace that shadows `UnityEngine.Time` for code inside `MWI.*` namespaces. See `.agent/skills/multiplayer/SKILL.md` §10.
- **Failure state expects the cancel handler to have been registered first.** `ShowFailure` does not set `_onCancel` itself; the driver calls `SetCancelHandler(BackToMainMenu, 0f)` before `ShowFailure(reason)`. If a future caller calls `ShowFailure` without first wiring a handler, the button is visible but click does nothing — intentional fail-safe rather than a crash.
- **Overlay singleton survives scene loads, including return to main menu.** That's by design — when a save-load driver eventually exists and the user has just returned to main menu, the overlay is ready to show without re-instantiation.
- See [[host-progressive-freeze-debug-log-spam]] for the broader "don't put per-tick logs in NGO event handlers" rule that applies to driver implementations.

## Open questions / TODO

- [ ] **Solo-as-host loading**: `GameSessionManager.StartSolo()` currently shows nothing during initial scene+world boot. A trivial follow-up driver (`SoloSessionLoadingDriver` or just an ad-hoc `LoadingOverlay.Show(...)/Hide()` pair around the boot sequence) would extend the same overlay there. Not implemented in this pass.
- [ ] **Save-load progress**: when save-restore eventually streams large saves into a scene, an analogous `SaveLoadDriver` should consume `SaveManager` progress events and drive the overlay. Not implemented; SaveManager doesn't currently emit per-step progress events.
- [ ] **Localisation**: stage strings ("Connecting to host…" etc.) are hard-coded English. When the project adopts a localisation system, these become `LocalizedString` lookups. Out of scope for the current pass.

## Change log

- 2026-04-25 — Initial documentation pass after the system shipped on the `multiplayyer` branch (commits `14a8ca3` → `e58f7c0`). Spec at `docs/superpowers/specs/2026-04-25-loading-overlay-design.md`, plan at `docs/superpowers/plans/2026-04-25-loading-overlay.md`. — claude

## Sources

- [Assets/Scripts/UI/Loading/LoadingOverlay.cs](../../Assets/Scripts/UI/Loading/LoadingOverlay.cs)
- [Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs](../../Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs)
- [Assets/Resources/UI/UI_LoadingOverlay.prefab](../../Assets/Resources/UI/UI_LoadingOverlay.prefab)
- [Assets/Scripts/Core/Network/GameSessionManager.cs](../../Assets/Scripts/Core/Network/GameSessionManager.cs) — `JoinMultiplayer` instantiates the driver + overlay.
- [.agent/skills/multiplayer/SKILL.md](../../.agent/skills/multiplayer/SKILL.md) §10 — driver-pattern rule, `InvalidParentException` rule, `DontDestroyOnLoad` EditMode guard rule, `MWI.Time` shadowing note. Procedural source of truth.
- [docs/superpowers/specs/2026-04-25-loading-overlay-design.md](../../docs/superpowers/specs/2026-04-25-loading-overlay-design.md) — design spec (architectural decisions, stage map, edge cases).
- [docs/superpowers/plans/2026-04-25-loading-overlay.md](../../docs/superpowers/plans/2026-04-25-loading-overlay.md) — implementation plan (task breakdown, build scripts, smoke test).
- 2026-04-25 conversation with Kevin — design choices: A (coarse stage labels), B (cancel after 10 s), B (generic overlay reusable for future loading scenarios).
