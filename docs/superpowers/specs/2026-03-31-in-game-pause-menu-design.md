# In-Game Pause Menu — Design Spec

**Date:** 2026-03-31  
**Status:** Approved

## Overview

A toggleable in-game menu activated by ESC. Currently contains a single "Return to Main Menu" button. Designed to be extended later with settings, audio, keybindings, etc.

## Behavior

- **ESC** toggles the menu open/closed.
- **Solo session** (host with no other player clients): opening the menu **pauses simulation** via `GameSpeedController.Instance.RequestSpeedChange(0f)`. Closing it resumes with `RequestSpeedChange(1f)`. Since solo sessions are hosted servers, `RequestSpeedChange` goes through the server path (`IsServer == true`), which works correctly.
- **Multiplayer session** (other player clients connected): menu is **overlay-only**, no simulation pause.
- **Solo detection:** `NetworkManager.Singleton.ConnectedClientsList.Count <= 1`. This check is valid because `PauseMenuController` only runs on the local player's HUD, and in solo the local player IS the host/server. In a pure client scenario, the menu still works — it just won't attempt to pause (which is correct multiplayer behavior).
- The game world continues rendering in all cases.
- `Input.GetKeyDown(KeyCode.Escape)` works at `timeScale = 0` since `Update()` is frame-driven, not time-driven. ESC input remains responsive while paused.

### ESC Conflict Handling

`BuildingPlacementManager` and `FurniturePlacementManager` already consume ESC to cancel placement. `PauseMenuController` checks `characterComponent.PlacementManager.IsPlacementActive` and `FurniturePlacementManager.IsPlacementActive` (accessed via `PlayerUI.Instance`) before toggling. If either is active, ESC is ignored by the pause menu.

### "Return to Main Menu" Flow

Executed as a coroutine using `WaitForSecondsRealtime` to survive `timeScale = 0`:

1. If simulation was paused (solo), resume it via `GameSpeedController.Instance.RequestSpeedChange(1f)`.
2. `ScreenFadeManager.Instance.FadeOut(0.5f)`.
3. `yield return new WaitForSecondsRealtime(0.5f)` — wait for fade to complete.
4. `NetworkManager.Singleton?.Shutdown()` — null-guarded.
5. `GameLauncher.Instance.ClearLaunchParameters()`.
6. `SceneManager.LoadScene("MainMenuScene")`.

### Edge Cases

- **`NetworkManager.Singleton` is null:** All accesses are null-guarded with `?.` operator. If null, skip shutdown and solo-check (treat as non-networked).
- **Scene transition while menu is open:** `OnDestroy()` stops any running coroutine and resumes `timeScale` if it was paused (CLAUDE.md Rule 16).
- **Player despawn while menu is open:** If the Character is destroyed, `Close()` is called via `OnDestroy` cleanup, which also resumes simulation if paused.

## Architecture

### New Script: `PauseMenuController.cs`

**Location:** `Assets/Scripts/UI/PauseMenu/PauseMenuController.cs`  
**Responsibility:** ESC input handling, menu toggle, solo pause/resume, and return-to-menu trigger.

Key members:
- `[SerializeField] GameObject _menuPanel` — the panel to toggle
- `[SerializeField] Button _returnToMainMenuButton` — wired in Awake
- `bool IsOpen` — read-only property
- `void Toggle()` — opens or closes, handles solo pause/resume
- `void Open()` / `void Close()` — explicit control
- `Coroutine ReturnToMainMenu()` — the full exit sequence
- `bool IsSoloSession()` — `NetworkManager.Singleton?.ConnectedClientsList.Count <= 1`
- ESC conflict check via `PlayerUI.Instance` → `characterComponent` → placement managers' `IsPlacementActive`

The return-to-menu coroutine is stopped in `OnDestroy()`. If `_wasPausedBySelf` is true on destroy, `timeScale` is restored.

### New Prefab: `Assets/UI/Player HUD/UI_PauseMenu.prefab`

- Own **Screen Space Overlay Canvas**, sort order **100** (above HUD at 0, below fade at 999).
- `CanvasScaler`: Scale With Screen Size, reference 1920x1080, match width/height 0.5.
- Semi-transparent dark background covering full screen.
- Centered panel with "Return to Main Menu" button.
- Menu panel starts **disabled**.

### Integration with PlayerUI

- Add `[SerializeField] PauseMenuController _pauseMenu` to `PlayerUI`.
- Add `TogglePauseMenu()` method.
- Auto-assign in `Awake()` via `GetComponentInChildren<PauseMenuController>()` as fallback.

### Skill Documentation

- Create `.agent/skills/pause-menu/SKILL.md` documenting the system (CLAUDE.md Rule 28).

## What Is NOT Included

- No settings, audio, or keybind panels (future work).
- No "Are you sure?" confirmation dialog for returning to main menu.
- No multiplayer "disconnect" vs "quit" distinction.
- No cursor lock/unlock handling.

## Dependencies

| System | Usage |
|--------|-------|
| `GameSpeedController` | Pause/resume simulation in solo |
| `ScreenFadeManager` | Fade transition on return to menu (`FadeOut(float duration)`) |
| `NetworkManager` | Shutdown on return, client count for solo check (null-guarded) |
| `GameLauncher` | `ClearLaunchParameters()` on return |
| `PlayerUI` | Integration point, access to `characterComponent` for placement checks |
| `BuildingPlacementManager` / `FurniturePlacementManager` | `IsPlacementActive` for ESC conflict |
