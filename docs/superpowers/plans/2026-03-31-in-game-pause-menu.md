# In-Game Pause Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an ESC-toggled in-game menu with a "Return to Main Menu" button. Pauses simulation in solo sessions, overlay-only in multiplayer.

**Architecture:** Single `PauseMenuController` MonoBehaviour on its own Canvas prefab, integrated into `PlayerUI` via serialized reference. Return-to-menu flow uses `ScreenFadeManager` for transition, then shuts down `NetworkManager` and loads the main menu scene.

**Tech Stack:** Unity UI (Canvas + Button), Unity NGO (NetworkManager), C#

**Spec:** `docs/superpowers/specs/2026-03-31-in-game-pause-menu-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `Assets/Scripts/UI/PauseMenu/PauseMenuController.cs` | ESC input, menu toggle, solo pause/resume, return-to-menu coroutine |
| Modify | `Assets/Scripts/UI/PlayerUI.cs` | Add `_pauseMenu` field, `TogglePauseMenu()`, expose `characterComponent` as read-only property, and `IsInitialized` |
| Create | `Assets/UI/Player HUD/UI_PauseMenu.prefab` (via MCP) | Canvas + panel + button prefab |
| Create | `.agent/skills/pause-menu/SKILL.md` | System documentation per Rule 28 |
| Modify | `.agent/skills/player_ui/SKILL.md` | Update with new properties and pause menu integration |

---

### Task 1: Add public accessor for Character on PlayerUI

`PlayerUI.characterComponent` is private. `PauseMenuController` needs it to check placement manager state. Add a read-only property.

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs:45`

- [ ] **Step 1: Add public property**

In `Assets/Scripts/UI/PlayerUI.cs`, after line 45 (`private Character characterComponent;`), add:

```csharp
public Character CharacterComponent => characterComponent;
public bool IsInitialized => characterComponent != null;
```

- [ ] **Step 2: Verify compilation**

Run: Assets Refresh via MCP or check Unity console for errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(ui): expose CharacterComponent and IsInitialized on PlayerUI"
```

---

### Task 2: Create PauseMenuController script

**Files:**
- Create: `Assets/Scripts/UI/PauseMenu/PauseMenuController.cs`

- [ ] **Step 1: Create the script**

```csharp
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MWI.UI
{
    /// <summary>
    /// In-game pause menu toggled by ESC.
    /// Pauses simulation in solo sessions (no other player clients).
    /// Overlay-only in multiplayer.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject _menuPanel;
        [SerializeField] private Button _returnToMainMenuButton;

        [Header("Settings")]
        [SerializeField] private float _fadeDuration = 0.5f;
        [SerializeField] private string _mainMenuSceneName = "MainMenuScene";

        private Coroutine _returnCoroutine;
        private bool _didPauseSimulation;

        public bool IsOpen => _menuPanel != null && _menuPanel.activeSelf;

        private void Awake()
        {
            if (_returnToMainMenuButton != null)
            {
                _returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuClicked);
            }

            // Ensure menu starts closed
            if (_menuPanel != null)
            {
                _menuPanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // Don't process while return-to-menu transition is running
            if (_returnCoroutine != null) return;

            // Don't process if PlayerUI isn't initialized (no local player)
            if (PlayerUI.Instance == null || !PlayerUI.Instance.IsInitialized) return;

            // Don't open menu if a placement mode is active — ESC should cancel placement instead
            if (IsPlacementActive()) return;

            Toggle();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (_menuPanel == null) return;

            _menuPanel.SetActive(true);

            // Pause simulation in solo sessions
            if (IsSoloSession())
            {
                var speedController = MWI.Time.GameSpeedController.Instance;
                if (speedController != null)
                {
                    speedController.RequestSpeedChange(0f);
                    _didPauseSimulation = true;
                }
            }
        }

        public void Close()
        {
            if (_menuPanel == null) return;

            _menuPanel.SetActive(false);

            ResumeIfPaused();
        }

        private void OnReturnToMainMenuClicked()
        {
            if (_returnCoroutine != null) return; // Already in progress
            _returnCoroutine = StartCoroutine(ReturnToMainMenuCoroutine());
        }

        private IEnumerator ReturnToMainMenuCoroutine()
        {
            // Step 1: Resume simulation if we paused it
            ResumeIfPaused();

            // Step 2: Fade to black
            if (ScreenFadeManager.Instance != null)
            {
                ScreenFadeManager.Instance.FadeOut(_fadeDuration);
                yield return new WaitForSecondsRealtime(_fadeDuration + 0.05f);
            }

            // Step 3: Shutdown network
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            // Step 4: Clear launch parameters
            if (GameLauncher.Instance != null)
            {
                GameLauncher.Instance.ClearLaunchParameters();
            }

            // Step 5: Load main menu scene
            SceneManager.LoadScene(_mainMenuSceneName);
        }

        private void ResumeIfPaused()
        {
            if (!_didPauseSimulation) return;

            var speedController = MWI.Time.GameSpeedController.Instance;
            if (speedController != null)
            {
                speedController.RequestSpeedChange(1f);
            }

            _didPauseSimulation = false;
        }

        /// <summary>
        /// Returns true if the local player is in building or furniture placement mode.
        /// ESC should cancel placement, not open the pause menu.
        /// </summary>
        private bool IsPlacementActive()
        {
            var character = PlayerUI.Instance?.CharacterComponent;
            if (character == null) return false;

            var buildingPlacement = character.PlacementManager;
            if (buildingPlacement != null && buildingPlacement.IsPlacementActive)
                return true;

            var furniturePlacement = character.FurniturePlacementManager;
            if (furniturePlacement != null && furniturePlacement.IsPlacementActive)
                return true;

            return false;
        }

        /// <summary>
        /// Solo session = host/server with no other player clients connected.
        /// ConnectedClientsList is server-only state, but in solo the local player IS the host.
        /// </summary>
        private bool IsSoloSession()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return false;

            return nm.ConnectedClientsList.Count <= 1;
        }

        private void OnDestroy()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }

            if (_returnToMainMenuButton != null)
            {
                _returnToMainMenuButton.onClick.RemoveListener(OnReturnToMainMenuClicked);
            }

            // Safety: resume simulation if we're destroyed while paused
            ResumeIfPaused();
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: Assets Refresh via MCP. Check console for errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/PauseMenu/PauseMenuController.cs
git commit -m "feat(ui): add PauseMenuController with ESC toggle and return-to-menu"
```

---

### Task 3: Create the UI_PauseMenu prefab via MCP

Build the prefab hierarchy in Unity Editor using MCP tools. The prefab needs its own Screen Space Overlay Canvas at sort order 100.

**Files:**
- Create: `Assets/UI/Player HUD/UI_PauseMenu.prefab`

- [ ] **Step 1: Create the root GameObject with Canvas**

Create a new GameObject `UI_PauseMenu` in the active scene. Add these components:
- `Canvas` — renderMode: ScreenSpaceOverlay, sortingOrder: 100
- `CanvasScaler` — uiScaleMode: ScaleWithScreenSize, referenceResolution: 1920x1080, matchWidthOrHeight: 0.5
- `GraphicRaycaster`

- [ ] **Step 2: Create the background overlay**

Create child `Background` under `UI_PauseMenu`:
- Add `Image` component
- Color: black with ~50% alpha (0, 0, 0, 0.5)
- Anchors: stretch-stretch (0,0 to 1,1), offsets all 0

- [ ] **Step 3: Create the menu panel**

Create child `MenuPanel` under `Background`:
- Add `Image` component for panel background (dark gray, e.g., RGB 40,40,40, alpha 0.95)
- Add `VerticalLayoutGroup` — childAlignment: MiddleCenter, spacing: 20, padding: 40 all sides
- Add `ContentSizeFitter` — verticalFit: PreferredSize, horizontalFit: PreferredSize
- Anchors: center-center (0.5, 0.5), pivot: (0.5, 0.5)
- Min width: ~400px via LayoutElement

- [ ] **Step 4: Create the "Return to Main Menu" button**

Create child `Btn_ReturnToMainMenu` under `MenuPanel`:
- Add `Button` component
- Add `Image` component for button background
- Add `LayoutElement` — preferredWidth: 350, preferredHeight: 60
- Create child `Text` with `TextMeshProUGUI`:
  - text: "Return to Main Menu"
  - fontSize: 24
  - alignment: center
  - color: white

- [ ] **Step 5: Add PauseMenuController component**

Add `MWI.UI.PauseMenuController` to the root `UI_PauseMenu` GameObject:
- Wire `_menuPanel` → `Background` (the full overlay including panel)
- Wire `_returnToMainMenuButton` → `Btn_ReturnToMainMenu`

- [ ] **Step 6: Save as prefab**

Save the hierarchy as prefab at `Assets/UI/Player HUD/UI_PauseMenu.prefab`. The `Background` (and its children) should start **disabled** (`SetActive(false)`) — `PauseMenuController.Awake()` handles this, but set it in the prefab for safety. The root Canvas stays **enabled** so the controller's `Update()` runs.

- [ ] **Step 7: Clean up scene**

Delete the temporary `UI_PauseMenu` GameObject from the scene (it's now a prefab).

- [ ] **Step 8: Commit**

```bash
git add "Assets/UI/Player HUD/UI_PauseMenu.prefab" "Assets/UI/Player HUD/UI_PauseMenu.prefab.meta"
git commit -m "feat(ui): add UI_PauseMenu prefab with canvas and return button"
```

---

### Task 4: Integrate PauseMenuController into PlayerUI

Wire the pause menu prefab into the HUD so it's part of the player UI lifecycle.

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs`

- [ ] **Step 1: Add serialized field and toggle method**

In `Assets/Scripts/UI/PlayerUI.cs`, add `using MWI.UI;` is not needed since we use the fully-qualified name. Add to the `[Header("UI Windows")]` section:

```csharp
[SerializeField] private MWI.UI.PauseMenuController _pauseMenu;
```

Add the toggle method alongside the other toggle methods:

```csharp
public void TogglePauseMenu()
{
    if (_pauseMenu == null) return;
    _pauseMenu.Toggle();
}
```

- [ ] **Step 2: Add fallback auto-assign in Awake**

In `PlayerUI.Awake()`, after the singleton check, add:

```csharp
if (_pauseMenu == null)
    _pauseMenu = GetComponentInChildren<MWI.UI.PauseMenuController>(true);
```

Note: `true` parameter includes inactive GameObjects in the search.

- [ ] **Step 3: Verify compilation**

Run: Assets Refresh via MCP. Check console for errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(ui): integrate PauseMenuController into PlayerUI"
```

---

### Task 5: Instantiate or place the pause menu prefab in the game

The pause menu prefab needs to exist at runtime. Since `PlayerUI` is itself a prefab that gets instantiated, the `UI_PauseMenu` prefab should be added as a child of the `UI_PlayerHUD` prefab, OR instantiated separately. Since `PauseMenuController` has its own Canvas, it can be a sibling.

**Files:**
- Modify: `Assets/UI/Player HUD/UI_PlayerHUD.prefab` (add reference to pause menu)

- [ ] **Step 1: Determine where PlayerUI lives**

Use MCP `gameobject-find` in the GameScene to locate the `PlayerUI` instance and understand how it's instantiated. If it's part of `UI_PlayerHUD` prefab, open that prefab and add the pause menu as a child or wire the serialized reference.

- [ ] **Step 2: Wire the reference**

Either:
- **Option A:** Instantiate `UI_PauseMenu` prefab as a child of the PlayerUI root, and wire `_pauseMenu` in the `UI_PlayerHUD` prefab.
- **Option B:** Place `UI_PauseMenu` in the GameScene directly (not as a child of PlayerUI) and rely on the `GetComponentInChildren` fallback. Since it has its own Canvas, this is clean.

Choose whichever option the existing scene structure supports best. The auto-assign fallback in `Awake()` will work either way.

- [ ] **Step 3: Test in editor**

Enter Play Mode. Press ESC — menu should appear. Press ESC again — menu should close. Click "Return to Main Menu" — should fade to black and load `MainMenuScene`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/UI/Player HUD/UI_PauseMenu.prefab" "Assets/UI/Player HUD/UI_PlayerHUD.prefab" "Assets/Scenes/GameScene.unity"
git commit -m "feat(ui): wire pause menu prefab into game scene"
```

Note: Only stage the files actually modified. If Option A was used, include the `UI_PlayerHUD.prefab`. If Option B was used, include `GameScene.unity`. Adjust the `git add` accordingly.

---

### Task 6: Write SKILL.md documentation

**Files:**
- Create: `.agent/skills/pause-menu/SKILL.md`
- Modify: `.agent/skills/player_ui/SKILL.md`

- [ ] **Step 1: Create the skill file**

```markdown
# Pause Menu System

## Purpose
Provides an in-game menu toggled by ESC with a "Return to Main Menu" button. Pauses simulation in solo sessions, overlay-only in multiplayer.

## Key Files
- `Assets/Scripts/UI/PauseMenu/PauseMenuController.cs` — Main controller
- `Assets/UI/Player HUD/UI_PauseMenu.prefab` — UI prefab
- `Assets/Scripts/UI/PlayerUI.cs` — Integration point (`_pauseMenu` field)

## Public API

### PauseMenuController
- `bool IsOpen` — Whether the menu is currently visible
- `void Toggle()` — Opens or closes the menu
- `void Open()` — Opens the menu (pauses in solo)
- `void Close()` — Closes the menu (resumes if paused)

## Behavior
- **ESC** toggles menu open/close
- ESC is ignored if `BuildingPlacementManager.IsPlacementActive` or `FurniturePlacementManager.IsPlacementActive` is true
- Solo sessions (host with `ConnectedClientsList.Count <= 1`): simulation pauses via `GameSpeedController.RequestSpeedChange(0f)`
- Multiplayer: overlay-only, no pause
- "Return to Main Menu": resumes simulation → fades out → `NetworkManager.Shutdown()` → `GameLauncher.ClearLaunchParameters()` → `SceneManager.LoadScene("MainMenuScene")`

## Dependencies
- `PlayerUI` — singleton, provides `CharacterComponent` for placement checks
- `GameSpeedController` — simulation pause/resume
- `ScreenFadeManager` — fade transition
- `NetworkManager` — shutdown + solo detection
- `GameLauncher` — clear launch parameters

## Events
None currently.

## Integration Points
- `PlayerUI._pauseMenu` serialized reference
- `PlayerUI.TogglePauseMenu()` for external callers
- `PlayerUI.Awake()` auto-assigns via `GetComponentInChildren<PauseMenuController>(true)`
```

- [ ] **Step 2: Update player_ui SKILL.md**

Read `.agent/skills/player_ui/SKILL.md` and add the following new members to its Public API section:
- `Character CharacterComponent` — read-only property exposing the current character (added in Task 1)
- `bool IsInitialized` — whether a character is bound (added in Task 1)
- `MWI.UI.PauseMenuController _pauseMenu` — serialized reference to pause menu
- `void TogglePauseMenu()` — toggles the pause menu open/close

Also add `PauseMenuController` to its Dependencies section.

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/pause-menu/SKILL.md .agent/skills/player_ui/SKILL.md
git commit -m "docs: add pause-menu SKILL.md, update player_ui SKILL.md"
```

---

## Execution Notes

- **Scene name:** The main menu scene file is `MainMenuScene.unity`. Use `"MainMenuScene"` for `SceneManager.LoadScene()`. Note: the existing `MainMenu.cs` uses `"MainMenu"` which may be a latent bug — do not replicate it.
- **`characterComponent` access:** Task 1 exposes it as `PlayerUI.CharacterComponent` (read-only property). This is needed for Task 2's placement active check.
- **Canvas sort order:** The pause menu Canvas uses sort order 100. The debug Canvas uses 0. The `ScreenFadeManager` uses 999. This ensures proper layering: HUD < pause menu < fade overlay.
- **`IsSoloSession()` uses `IsServer` guard:** This ensures the `ConnectedClientsList` access is only made on the host/server, where it's valid. On a pure client, `IsSoloSession()` returns false, which means no pause — correct multiplayer behavior.
