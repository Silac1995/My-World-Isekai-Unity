# Pause Menu System

## Purpose
Provides an in-game menu toggled by ESC with a "Return to Main Menu" button. Pauses simulation in solo sessions, overlay-only in multiplayer.

## Key Files
- `Assets/Scripts/UI/PauseMenu/PauseMenuController.cs` — Main controller
- `Assets/UI/Player HUD/UI_PauseMenu.prefab` — UI prefab (nested inside UI_PlayerHUD)
- `Assets/Scripts/UI/PlayerUI.cs` — Integration point (`_pauseMenu` field)

## Public API

### PauseMenuController (namespace MWI.UI)
- `bool IsOpen` — Whether the menu is currently visible
- `void Toggle()` — Opens or closes the menu
- `void Open()` — Opens the menu (pauses in solo)
- `void Close()` — Closes the menu (resumes if paused)

## Behavior
- **ESC** toggles menu open/close
- ESC is ignored while return-to-menu coroutine is running
- ESC is ignored if `BuildingPlacementManager.IsPlacementActive` or `FurniturePlacementManager.IsPlacementActive` is true
- Solo sessions (host with `ConnectedClientsList.Count <= 1`): simulation pauses via `GameSpeedController.RequestSpeedChange(0f)`
- Multiplayer: overlay-only, no pause
- "Return to Main Menu": resumes simulation → fades out (0.5s) → `NetworkManager.Shutdown()` → `GameLauncher.ClearLaunchParameters()` → `SceneManager.LoadScene("MainMenuScene")`

## Dependencies
- `PlayerUI` — singleton, provides `CharacterComponent` for placement checks
- `GameSpeedController` — simulation pause/resume (namespace `MWI.Time`)
- `ScreenFadeManager` — fade transition (`FadeOut(float duration)`)
- `NetworkManager` — shutdown + solo detection (null-guarded)
- `GameLauncher` — `ClearLaunchParameters()` on return

## Events
None currently.

## Integration Points
- `PlayerUI._pauseMenu` serialized reference (auto-assigned via `GetComponentInChildren<PauseMenuController>(true)`)
- `PlayerUI.TogglePauseMenu()` for external callers
- Prefab nested inside `Assets/UI/Player HUD/UI_PlayerHUD.prefab`
- Own Canvas at sort order 100 (above HUD at 0, below fade at 999)

## Cleanup
- `OnDestroy()` stops running coroutines, unsubscribes button listener, resumes `timeScale` if paused
