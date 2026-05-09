# Dev Mode — Select & Assign Building Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second tab (**Select**) to the dev-mode panel with click-to-select Characters and a pluggable `IDevAction` pattern. First concrete action: assign a building as owner to the selected character.

**Architecture:** `DevModeManager` gains a lightweight click-consumer slot so only one module consumes a given click. `DevSelectionModule` owns the Select tab — selection state + character click-pick. `IDevAction` is the plug-in point; `DevActionAssignBuilding` is the first concrete action, guiding the user through a building pick on click and calling `Building.SetOwner(character)` polymorphically.

**Tech Stack:** Unity 2022+, C#, uGUI + TMP, NGO (host authority).

**Spec:** [docs/superpowers/specs/2026-04-20-dev-mode-select-assign-design.md](../specs/2026-04-20-dev-mode-select-assign-design.md)

**Testing approach:** No automated test suite in the project. Every task ends with a manual Play-mode verification step plus `Debug.Log` checkpoints (project rule 27).

**World scale reminder (rule 32):** 11 Unity units = 1.67 m. Nothing in this slice uses spatial distances, but noted for consistency.

---

## File Structure

### Files created
- `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs` — Select tab controller (state, click loop, events)
- `Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs` — 3-member interface
- `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs` — first action

### Files modified
- `Assets/Scripts/Debug/DevMode/DevModeManager.cs` — add `ActiveClickConsumer` slot + event + helpers (~30 lines)
- `Assets/Scripts/Debug/DevMode/DevModePanel.cs` — add minimal tab-switch infrastructure
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs` — refactor armed click loop to use click-consumer contract
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — add `if (!IsServer) return;` at the top of `SetOwner` for parity with `ResidentialBuilding`
- `Assets/Resources/UI/DevModePanel.prefab` — restructure: tab bar + `SpawnTab` (existing UI re-parented) + `SelectTab` (new)
- `.agent/skills/dev-mode/SKILL.md` — document Select tab, `IDevAction` pattern, click-consumer mechanism, known limitations
- `.claude/agents/debug-tools-architect.md` — extend with Select tab + action pattern

---

## Phase 1 — Click-Consumer Extension on `DevModeManager`

This comes first because `DevSpawnModule` and `DevSelectionModule` both depend on it. We extend `DevModeManager`, then refactor `DevSpawnModule` to use it (behavior unchanged). No new tab yet.

### Task 1: Add click-consumer slot to `DevModeManager`

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/DevModeManager.cs`

- [ ] **Step 1.1: Add field, property, event, and helper methods**

Locate the `OnDevModeChanged` event declaration (line ~33 in `DevModeManager.cs`, right after `public event Action<bool> OnDevModeChanged;`). Insert immediately after it:

```csharp
/// <summary>
/// The MonoBehaviour that currently owns the click stream (e.g. the active dev module's
/// armed state). Only one module consumes clicks at a time; when a new module claims the
/// slot, the previous owner is evicted and auto-disarms via OnClickConsumerChanged.
/// </summary>
public MonoBehaviour ActiveClickConsumer { get; private set; }

/// <summary>
/// Fires whenever ActiveClickConsumer changes. Click-reading modules subscribe to disarm
/// themselves when they are no longer the owner.
/// </summary>
public event Action OnClickConsumerChanged;
```

Then add these methods somewhere in the class (a natural spot is right after `TryToggle()`):

```csharp
/// <summary>
/// Claims the click slot for the given consumer. If a different consumer held the slot,
/// OnClickConsumerChanged fires so the previous owner can auto-disarm. Passing null
/// releases the slot (same as ClearClickConsumer).
/// </summary>
public void SetClickConsumer(MonoBehaviour consumer)
{
    if (ActiveClickConsumer == consumer) return;
    ActiveClickConsumer = consumer;
    OnClickConsumerChanged?.Invoke();
}

/// <summary>
/// Releases the click slot, but only if the given consumer is the current owner. Other
/// callers are ignored — prevents a stale subscriber from clearing someone else's claim.
/// </summary>
public void ClearClickConsumer(MonoBehaviour consumer)
{
    if (ActiveClickConsumer != consumer) return;
    ActiveClickConsumer = null;
    OnClickConsumerChanged?.Invoke();
}
```

- [ ] **Step 1.2: Clear the slot on Disable**

In `DevModeManager.Disable()` (line ~112), add a single line at the top of the method body (before `if (!IsEnabled) return;`):

```csharp
ActiveClickConsumer = null;
```

This is defensive — turning dev mode off should drop any in-flight click claim. Modules also listen to `OnDevModeChanged(false)` and disarm themselves.

Full edited method:

```csharp
public void Disable()
{
    ActiveClickConsumer = null;
    if (!IsEnabled) return;
    IsEnabled = false;
    if (_panelInstance != null) _panelInstance.SetActive(false);
    OnDevModeChanged?.Invoke(false);
    Debug.Log("<color=magenta>[DevMode]</color> Disabled.");
}
```

- [ ] **Step 1.3: Compile check**

Run `mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`. Expect zero compile errors.

- [ ] **Step 1.4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/DevModeManager.cs
git commit -m "feat(devmode): add ActiveClickConsumer slot to DevModeManager

Single-slot click arbitration so only one armed dev module consumes a
given click. SetClickConsumer evicts the previous owner and fires
OnClickConsumerChanged so subscribers can auto-disarm. Disable clears
the slot defensively."
```

---

### Task 2: Refactor `DevSpawnModule` to use the click-consumer contract

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs`

Behavior is unchanged to the user — the Armed toggle still arms and the click still spawns. We just route through the new slot so future modules can co-exist.

- [ ] **Step 2.1: Hook the armed toggle to the click consumer**

Locate `HandleArmedChanged(bool armed)` (around line 243):

```csharp
private void HandleArmedChanged(bool armed)
{
    Debug.Log($"<color=cyan>[DevSpawn]</color> Armed: {armed}");
}
```

Replace with:

```csharp
private void HandleArmedChanged(bool armed)
{
    Debug.Log($"<color=cyan>[DevSpawn]</color> Armed: {armed}");
    if (DevModeManager.Instance == null) return;
    if (armed) DevModeManager.Instance.SetClickConsumer(this);
    else DevModeManager.Instance.ClearClickConsumer(this);
}
```

- [ ] **Step 2.2: Subscribe to `OnClickConsumerChanged` for auto-disarm**

Locate `WireListeners()` (around line 152). Add one more subscription at the end (before the method's closing brace):

```csharp
if (DevModeManager.Instance != null)
{
    DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
}
```

Note: the existing block already has a null-check + `OnDevModeChanged` subscription. Put the new subscription inside the same null-guard for efficiency. Final shape:

```csharp
if (DevModeManager.Instance != null)
{
    DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
    DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
}
```

Matching unsubscribe in `UnwireListeners()` (around line 166), inside the existing null-guard:

```csharp
if (DevModeManager.Instance != null)
{
    DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
    DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
}
```

Then add the handler itself. Put it right after `HandleDevModeChanged`:

```csharp
private void HandleClickConsumerChanged()
{
    if (DevModeManager.Instance == null) return;
    if (DevModeManager.Instance.ActiveClickConsumer == this) return;
    // Another module claimed the click stream — disarm our toggle.
    if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
}
```

- [ ] **Step 2.3: Gate the click read on being the consumer**

Locate the `Update()` method (around line 248). Change the early-return from:

```csharp
if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
if (_armedToggle == null || !_armedToggle.isOn) return;
```

to:

```csharp
if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
if (_armedToggle == null || !_armedToggle.isOn) return;
if (DevModeManager.Instance.ActiveClickConsumer != this) return;
```

The third line is the new gate. The first two stay — they guard against dead state cheaply before the consumer check.

- [ ] **Step 2.4: Compile and manual verification**

Run `assets-refresh` + `console-get-logs`. Zero compile errors.

Manual verification (Play mode, host only):
1. Press F3 → panel opens.
2. Arm Spawn's **Armed** toggle → console: `[DevSpawn] Armed: True`.
3. Click on ground → NPC spawns as before. Behavior unchanged.
4. Arm Spawn, disarm Spawn → no click consumed, armed logs shown.

If Play mode is impractical from the subagent context, compile-clean + code-inspection is acceptable. Manual behavior verified in Task 5 when the Select module lands.

- [ ] **Step 2.5: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs
git commit -m "refactor(devmode): route spawn module click loop through ActiveClickConsumer

Armed toggle now claims/releases the DevModeManager click slot and
subscribes to OnClickConsumerChanged for auto-disarm. Behavior
unchanged from the user's perspective — prepares the module to coexist
with the upcoming Select tab."
```

---

## Phase 2 — Select Tab Core

### Task 3: Create `IDevAction` interface

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs`

- [ ] **Step 3.1: Ensure folder exists**

Use `mcp__ai-game-developer__assets-create-folder` on `Assets/Scripts/Debug/DevMode/Modules/Actions/`. The `Modules/` folder already exists from the first slice.

- [ ] **Step 3.2: Write the interface**

Create `Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs` with EXACTLY this content:

```csharp
/// <summary>
/// Dev-mode action contract. Implementers live as MonoBehaviours under the Select tab's
/// ActionsContainer. Each action owns its own button; it subscribes to
/// DevSelectionModule.OnSelectionChanged to refresh IsAvailable, and runs Execute when the
/// button is clicked.
///
/// Execute can run synchronously, open a prompt, enter a click-armed state, etc. — the
/// interface leaves that open. Actions that consume clicks must coordinate via
/// DevModeManager.SetClickConsumer.
/// </summary>
public interface IDevAction
{
    /// <summary>Display label for the action button.</summary>
    string Label { get; }

    /// <summary>True when this action can be invoked given the current selection state.</summary>
    bool IsAvailable(DevSelectionModule sel);

    /// <summary>Run the action. Caller has already confirmed IsAvailable.</summary>
    void Execute(DevSelectionModule sel);
}
```

- [ ] **Step 3.3: Compile check**

`assets-refresh` + `console-get-logs`. Zero errors. The interface is an empty contract — no runtime behavior.

- [ ] **Step 3.4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs.meta
git commit -m "feat(devmode): add IDevAction interface

Plug-in contract for Select-tab actions. Each action is a MonoBehaviour
that owns its button, evaluates availability against the current
selection, and runs on click."
```

---

### Task 4: Create `DevSelectionModule`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs`

- [ ] **Step 4.1: Write the script**

Create `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs` with EXACTLY this content:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Select tab of the dev-mode panel. Owns cross-cutting selection state (currently just
/// SelectedCharacter) and handles the click-to-select loop for characters. Actions attach
/// as children of the actions container and consume this module via the IDevAction contract.
/// </summary>
public class DevSelectionModule : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Toggle _armedToggle;
    [SerializeField] private TMP_Text _selectedLabel;
    [SerializeField] private Button _clearButton;

    public Character SelectedCharacter { get; private set; }

    /// <summary>Fires whenever SelectedCharacter changes (including to/from null).</summary>
    public event Action OnSelectionChanged;

    private void Start()
    {
        WireListeners();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        UnwireListeners();
    }

    private void OnEnable()
    {
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
    }

    // ─── Wiring ───────────────────────────────────────────────────────

    private void WireListeners()
    {
        if (_armedToggle != null) _armedToggle.onValueChanged.AddListener(HandleArmedChanged);
        if (_clearButton != null) _clearButton.onClick.AddListener(ClearSelection);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
        }
    }

    private void UnwireListeners()
    {
        if (_armedToggle != null) _armedToggle.onValueChanged.RemoveListener(HandleArmedChanged);
        if (_clearButton != null) _clearButton.onClick.RemoveListener(ClearSelection);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
        }
    }

    private void HandleArmedChanged(bool armed)
    {
        Debug.Log($"<color=cyan>[DevSelect]</color> Armed: {armed}");
        if (DevModeManager.Instance == null) return;
        if (armed) DevModeManager.Instance.SetClickConsumer(this);
        else DevModeManager.Instance.ClearClickConsumer(this);
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled)
        {
            // Dev mode turned off — disarm and clear selection so we don't carry stale state.
            if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
            if (SelectedCharacter != null) ClearSelection();
        }
    }

    private void HandleClickConsumerChanged()
    {
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        // Another module claimed the click stream — disarm our toggle.
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    private void HandleSceneUnloaded(Scene _)
    {
        if (SelectedCharacter != null) ClearSelection();
    }

    // ─── Public API ───────────────────────────────────────────────────

    public void SetSelectedCharacter(Character c)
    {
        if (SelectedCharacter == c) return;
        SelectedCharacter = c;
        RefreshLabel();
        OnSelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (SelectedCharacter == null)
        {
            RefreshLabel();
            return;
        }
        SelectedCharacter = null;
        RefreshLabel();
        OnSelectionChanged?.Invoke();
    }

    private void RefreshLabel()
    {
        if (_selectedLabel == null) return;
        _selectedLabel.text = SelectedCharacter != null
            ? $"Selected: {SelectedCharacter.CharacterName}"
            : "Selected: —";
    }

    // ─── Click loop ───────────────────────────────────────────────────

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _armedToggle.isOn = false;
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Camera.main is null — cannot select.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, ~0))
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Raycast missed — click into the world.");
            return;
        }

        Character c = hit.collider.GetComponentInParent<Character>();
        if (c == null)
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Click missed a Character.");
            return;
        }

        SetSelectedCharacter(c);
        _armedToggle.isOn = false;
        Debug.Log($"<color=cyan>[DevSelect]</color> Selected: {c.CharacterName}");
    }
}
```

- [ ] **Step 4.2: Compile check**

`assets-refresh` + `console-get-logs`. Zero compile errors. The script references no unresolved types.

- [ ] **Step 4.3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs.meta
git commit -m "feat(devmode): add DevSelectionModule

Select tab controller. Owns SelectedCharacter + OnSelectionChanged.
Click loop picks any Character via all-layers raycast with
GetComponentInParent filter. Integrates with ActiveClickConsumer and
clears selection on scene unload or dev-mode disable."
```

---

### Task 5: Add tab infrastructure to `DevModePanel`

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/DevModePanel.cs`

Current `DevModePanel.cs` is a single-content-root script with ~30 lines. We extend it with a minimal multi-tab switcher.

- [ ] **Step 5.1: Rewrite the script**

Replace the full contents of `Assets/Scripts/Debug/DevMode/DevModePanel.cs` with:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root of the dev-mode UI. Lives on the DevModePanel prefab. Listens to
/// DevModeManager.OnDevModeChanged to show/hide its content root, and wires a simple
/// tab-switch bar (one tab visible at a time). Tab content GameObjects' Awake/OnEnable
/// still fire once when the prefab is instantiated, so MonoBehaviours inside inactive
/// tabs retain their serialized state.
/// </summary>
public class DevModePanel : MonoBehaviour
{
    [Serializable]
    public struct TabEntry
    {
        public Button TabButton;
        public GameObject Content;
    }

    [SerializeField] private GameObject _contentRoot;
    [SerializeField] private List<TabEntry> _tabs = new List<TabEntry>();

    private int _activeTabIndex = -1;

    private void Start()
    {
        // Wire each tab button to switch to its own index.
        for (int i = 0; i < _tabs.Count; i++)
        {
            int captured = i;
            if (_tabs[i].TabButton != null)
            {
                _tabs[i].TabButton.onClick.AddListener(() => SwitchTab(captured));
            }
        }

        if (_tabs.Count > 0) SwitchTab(0);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].TabButton != null)
            {
                _tabs[i].TabButton.onClick.RemoveAllListeners();
            }
        }
    }

    private void OnEnable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            HandleDevModeChanged(DevModeManager.Instance.IsEnabled);
        }
    }

    private void OnDisable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(isEnabled);
        }
    }

    public void SwitchTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Content != null)
            {
                _tabs[i].Content.SetActive(i == index);
            }
        }
        _activeTabIndex = index;
    }
}
```

- [ ] **Step 5.2: Compile check**

`assets-refresh` + `console-get-logs`. Zero errors. The script still exports the `_contentRoot` SerializeField used by the existing prefab instance, plus two new fields (`_tabs`, `_activeTabIndex`) — existing serialized references survive.

- [ ] **Step 5.3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/DevModePanel.cs
git commit -m "feat(devmode): add minimal tab-switch to DevModePanel

TabEntry struct + _tabs list. Start wires each tab button to SwitchTab,
activates tab 0. SwitchTab SetActives the matching content and hides
the others. Existing _contentRoot + OnDevModeChanged wiring preserved."
```

---

### Task 6: Create `DevActionAssignBuilding`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs`

- [ ] **Step 6.1: Write the script**

Create `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs` with EXACTLY this content:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// First concrete IDevAction. Assigns the selected Character as the owner of a
/// player-clicked Building. Supports both CommercialBuilding and ResidentialBuilding
/// polymorphically via their SetOwner entry points.
///
/// Flow:
///   1. IsAvailable requires sel.SelectedCharacter != null → button enabled only then.
///   2. Execute claims the click slot and enters an armed state. Button label flips to
///      "Pick a building… (ESC to cancel)" and the button is disabled.
///   3. Update polls for Mouse0 (or ESC). On a valid Building-layer hit, SetOwner runs
///      and the action releases the click slot. On ESC, the action cancels.
/// </summary>
public class DevActionAssignBuilding : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    [Header("Raycast")]
    [Tooltip("Layer mask for building picks. Defaults to 'Building' at runtime if left at zero.")]
    [SerializeField] private LayerMask _buildingLayerMask;
    private bool _layerMaskResolved;

    private const string DEFAULT_LABEL = "Assign Building as Owner";
    private const string ARMED_LABEL = "Pick a building… (ESC to cancel)";

    private bool _waitingForBuildingPick;
    private Character _pendingCharacter;

    public string Label => DEFAULT_LABEL;

    public bool IsAvailable(DevSelectionModule sel)
    {
        return sel != null && sel.SelectedCharacter != null;
    }

    public void Execute(DevSelectionModule sel)
    {
        if (!IsAvailable(sel))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Assign Building: no character selected.");
            return;
        }

        _pendingCharacter = sel.SelectedCharacter;
        _waitingForBuildingPick = true;

        if (DevModeManager.Instance != null) DevModeManager.Instance.SetClickConsumer(this);
        SetButtonState(armed: true);

        Debug.Log($"<color=cyan>[DevAction]</color> Assign Building: pick a building for {_pendingCharacter.CharacterName} (ESC to cancel).");
    }

    // ─── Unity lifecycle ──────────────────────────────────────────────

    private void Start()
    {
        ResolveLayerMask();
        SetButtonState(armed: false);

        if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged += RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
        }

        RefreshAvailability();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged -= RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void ResolveLayerMask()
    {
        if (_buildingLayerMask.value != 0)
        {
            _layerMaskResolved = true;
            return;
        }

        int layer = LayerMask.NameToLayer("Building");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevAction]</color> 'Building' layer is missing from Tags & Layers. Assign Building will not function.");
            _layerMaskResolved = false;
            return;
        }
        _buildingLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    // ─── Button + availability ────────────────────────────────────────

    private void OnButtonClicked()
    {
        if (_selection == null) return;
        Execute(_selection);
    }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = !_waitingForBuildingPick && IsAvailable(_selection);
    }

    private void SetButtonState(bool armed)
    {
        _waitingForBuildingPick = armed;
        if (_buttonLabel != null)
        {
            _buttonLabel.text = armed ? ARMED_LABEL : DEFAULT_LABEL;
        }
        RefreshAvailability();
    }

    private void Cancel(string reason)
    {
        if (!_waitingForBuildingPick) return;
        _waitingForBuildingPick = false;
        _pendingCharacter = null;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
        Debug.Log($"<color=cyan>[DevAction]</color> Assign Building: {reason}.");
    }

    private void HandleClickConsumerChanged()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        // Someone else claimed the click slot — cancel our pending pick.
        Cancel("superseded by another module");
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _waitingForBuildingPick)
        {
            Cancel("dev mode disabled");
        }
    }

    // ─── Click loop ───────────────────────────────────────────────────

    private void Update()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel("cancelled by user");
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Camera.main is null — cannot pick building.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _buildingLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Click missed the Building layer.");
            return;
        }

        Building building = hit.collider.GetComponentInParent<Building>();
        if (building == null)
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Building layer hit but no Building component found in parent chain.");
            return;
        }

        if (_pendingCharacter == null)
        {
            Debug.LogError("<color=red>[DevAction]</color> Pending character was lost mid-action — cancelling.");
            Cancel("pending character null");
            return;
        }

        // CommercialBuilding is abstract; the pattern-match covers every concrete
        // subclass (Tavern, Shop, etc.) polymorphically. Same for ResidentialBuilding
        // subclasses. Order matters only if a subclass inherits from both — none does.
        if (building is CommercialBuilding commercial)
        {
            commercial.SetOwner(_pendingCharacter, null);
        }
        else if (building is ResidentialBuilding residential)
        {
            residential.SetOwner(_pendingCharacter);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[DevAction]</color> {building.GetType().Name} does not support SetOwner. Stay armed, pick a different building.");
            return;
        }

        Debug.Log($"<color=green>[DevAction]</color> {_pendingCharacter.CharacterName} set as owner of {building.name}.");

        // Release the click slot; keep selection intact so user can chain further actions.
        Character doneChar = _pendingCharacter;
        _pendingCharacter = null;
        _waitingForBuildingPick = false;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
    }
}
```

- [ ] **Step 6.2: Compile check**

`assets-refresh` + `console-get-logs`. Zero errors. References `Character`, `Building`, `CommercialBuilding`, `ResidentialBuilding` — all live in `Assembly-CSharp` already.

- [ ] **Step 6.3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs.meta
git commit -m "feat(devmode): add DevActionAssignBuilding

First IDevAction. Enabled when a Character is selected. Execute claims
the click slot and waits for a Building-layer click; on hit, routes to
CommercialBuilding.SetOwner or ResidentialBuilding.SetOwner. ESC
cancels, another module claiming the slot cancels. Selection preserved
after success for chained actions."
```

---

## Phase 3 — Defensive Building Server Guard

### Task 7: Add `IsServer` guard to `CommercialBuilding.SetOwner`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Per spec §7.3, `ResidentialBuilding.SetOwner` has a defensive `IsServer` guard but `CommercialBuilding.SetOwner` does not. Add the guard for parity. Dev mode is host-only, so this doesn't change dev-tool behavior, but it protects the method from any future non-server caller.

- [ ] **Step 7.1: Locate the method**

`Assets/Scripts/World/Buildings/CommercialBuilding.cs:124` — `public void SetOwner(Character newOwner, Job ownerJob = null)`.

- [ ] **Step 7.2: Add the guard at the top of the method body**

The current method opening is:

```csharp
public void SetOwner(Character newOwner, Job ownerJob = null)
{
    // Remove from old community
    if (_ownerCommunity != null && _ownerCommunity.ownedBuildings.Contains(this))
    ...
```

Change to:

```csharp
public void SetOwner(Character newOwner, Job ownerJob = null)
{
    if (!IsServer) return;

    // Remove from old community
    if (_ownerCommunity != null && _ownerCommunity.ownedBuildings.Contains(this))
    ...
```

Add the single `if (!IsServer) return;` line as the first statement inside the method. Nothing else changes.

- [ ] **Step 7.3: Compile check**

`assets-refresh` + `console-get-logs`. Zero errors. `CommercialBuilding` extends `Building : ComplexRoom`, which extends `NetworkBehaviour` (confirm by reading `Building.cs:15` — `class Building : ComplexRoom` — and tracing up). `IsServer` is an NGO property on `NetworkBehaviour`; no extra using needed.

If `IsServer` is not in scope, STOP and report — the inheritance chain may not reach `NetworkBehaviour`.

- [ ] **Step 7.4: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "fix(building): add IsServer guard to CommercialBuilding.SetOwner

Matches ResidentialBuilding.SetOwner's defensive guard. Dev mode is
already host-only so behavior is unchanged, but the method is now
symmetric with its residential counterpart and safe against any
non-server caller."
```

---

## Phase 4 — Prefab Restructure

### Task 8: Restructure `DevModePanel.prefab` for tabs + Select tab

**Files:**
- Modify (Unity authoring): `Assets/Resources/UI/DevModePanel.prefab`

This task is authored in the Unity Editor via MCP tools. The goal: the existing Spawn UI is re-parented under a new `SpawnTab` child, a new `SelectTab` sibling is created with its controls, and the tab bar + `_tabs` list on `DevModePanel` are wired.

Given the size of this prefab edit, prefer the `script-execute` fallback approach used in Task 13 of the first slice's plan (commit `2528701`). A `PrefabUtility.SaveAsPrefabAsset` round-trip is more reliable than chaining dozens of individual MCP calls.

- [ ] **Step 8.0: Inspect the current prefab**

Before editing, use `mcp__ai-game-developer__assets-prefab-open` on `Assets/Resources/UI/DevModePanel.prefab` and walk the hierarchy. Record specifically:

1. The exact path to the `DevSpawnModule` component (which GameObject it lives on — ContentRoot itself, or a child).
2. The list of direct children of `ContentRoot` (these are the pieces that will be re-parented into `SpawnTab`).
3. Whether `DevSpawnModule._rowPrefab` and the other SerializeField references point to GameObjects that are direct children of ContentRoot or nested deeper.

This evidence drives Step 8.2's "wrap children into SpawnTab but leave DevSpawnModule where it is" strategy. If the state differs from expectations (e.g. `DevSpawnModule` is on a deep nested child that itself would be re-parented), adjust the script in Step 8.3 so component references survive — Unity object references are GUID-based, so surviving a parent swap is fine, but record it as explicit intent.

Close the prefab with `mcp__ai-game-developer__assets-prefab-close` when done inspecting.

- [ ] **Step 8.1: Strategy decision**

Pick one:

**Option A (recommended): programmatic edit via `script-execute`.** Open the existing prefab with `PrefabUtility.LoadPrefabContents`, perform all re-parenting + new-child creation + SerializeField wiring in C#, then `PrefabUtility.SaveAsPrefabAsset` + `PrefabUtility.UnloadPrefabContents`. One script-execute call produces the full edit deterministically.

**Option B: MCP `assets-prefab-open` → chain of `gameobject-*` calls → `assets-prefab-save`.** More visible step-by-step but much noisier; realistic for the Select tab's ~5 widgets but error-prone for the re-parent step.

Choose Option A unless you have a specific reason.

- [ ] **Step 8.2: Required post-state of the prefab**

The prefab hierarchy after the edit:

```
DevModePanel (root)
├── Canvas / CanvasScaler / GraphicRaycaster / DevModePanel script
└── ContentRoot
    ├── TabBar (new) — HorizontalLayoutGroup
    │   ├── SpawnTabButton — Button with TMP label "Spawn"
    │   └── SelectTabButton — Button with TMP label "Select"
    ├── SpawnTab (new) — RectTransform + VerticalLayoutGroup
    │   └── (existing Spawn UI re-parented here: Race/Prefab/Personality/Trait labels + dropdowns, Combat + Skills containers + Add buttons, Count, Armed toggle)
    │   └── DevSpawnModule component stays on this GameObject (it was on the old ContentRoot → move it to SpawnTab, OR leave it on ContentRoot if existing references depend on position)
    └── SelectTab (new) — RectTransform + VerticalLayoutGroup
        ├── Header_Selection — TMP_Text "Selection"
        ├── ArmedToggle — Toggle with label "Select Character (click to pick)"
        ├── SelectedLabel — TMP_Text, initial text "Selected: —"
        ├── ClearButton — Button with TMP label "Clear Selection"
        ├── Separator — small vertical gap or thin image
        ├── Header_Actions — TMP_Text "Actions"
        └── ActionsContainer — VerticalLayoutGroup (empty initially)
            └── AssignBuildingAction — Button with TMP label "Assign Building as Owner"
                (DevActionAssignBuilding component attached)
```

**Crucial wiring:**
- `DevModePanel._contentRoot` → still points at `ContentRoot`.
- `DevModePanel._tabs[0].TabButton` = `SpawnTabButton`, `.Content` = `SpawnTab`.
- `DevModePanel._tabs[1].TabButton` = `SelectTabButton`, `.Content` = `SelectTab`.
- `DevSelectionModule` component on `SelectTab`. SerializeFields:
  - `_armedToggle` = `SelectTab/ArmedToggle`
  - `_selectedLabel` = `SelectTab/SelectedLabel`
  - `_clearButton` = `SelectTab/ClearButton`
- `DevActionAssignBuilding` component on `AssignBuildingAction`. SerializeFields:
  - `_selection` = the `DevSelectionModule` on `SelectTab`
  - `_button` = the `Button` on `AssignBuildingAction`
  - `_buttonLabel` = the TMP child of `AssignBuildingAction`
  - `_buildingLayerMask` = leave at 0 (script resolves "Building" at Start); OR set explicitly to `1 << LayerMask.NameToLayer("Building")` during the script-execute edit.

**Existing `DevSpawnModule` references** must stay valid. The script-execute edit should:
1. Find the existing `ContentRoot` GameObject inside the prefab.
2. Create a new `SpawnTab` child under `ContentRoot`.
3. Re-parent every existing direct child of `ContentRoot` (the Spawn UI pieces) under `SpawnTab`, preserving their order.
4. `DevSpawnModule` likely sits on a child GameObject already; if so, don't touch it. If it's on `ContentRoot` itself, move it to `SpawnTab` and re-wire every SerializeField (which should still be valid because the child references aren't path-based).

If the prefab has `DevSpawnModule` on `ContentRoot` directly and moving is too risky, alternative: leave `DevSpawnModule` on `ContentRoot` and just wrap all its UI children into a new `SpawnTab` GameObject. Then `SpawnTab` is the content GameObject passed to `DevModePanel._tabs[0].Content`, even though `DevSpawnModule` lives one level up. That works because tab switching only SetActives the tab content, and `DevSpawnModule.Update`'s gating (`_armedToggle.isOn` — the toggle is under `SpawnTab` and goes inactive with it) naturally inhibits click reads.

Actually that's cleaner. Pick that.

- [ ] **Step 8.3: Reference C# for the script-execute**

An outline for the edit script (populate with exact GameObject names as you find them via the MCP hierarchy inspection):

```csharp
public static class DevModePanelEditor
{
    public static void AddSelectTab()
    {
        const string prefabPath = "Assets/Resources/UI/DevModePanel.prefab";
        var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var contentRoot = root.transform.Find("ContentRoot");

            // 1. Add TabBar as the first child of ContentRoot (for layout order).
            var tabBar = new UnityEngine.GameObject("TabBar", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup));
            tabBar.transform.SetParent(contentRoot, false);
            tabBar.transform.SetSiblingIndex(0);

            UnityEngine.UI.Button spawnBtn = CreateButton(tabBar.transform, "SpawnTabButton", "Spawn");
            UnityEngine.UI.Button selectBtn = CreateButton(tabBar.transform, "SelectTabButton", "Select");

            // 2. Wrap existing Spawn UI into a SpawnTab child.
            var spawnTab = new UnityEngine.GameObject("SpawnTab", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup));
            spawnTab.transform.SetParent(contentRoot, false);

            // Move all non-TabBar, non-SpawnTab children (i.e., the previously existing Spawn UI nodes) into spawnTab.
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                var child = contentRoot.GetChild(i);
                if (child == tabBar.transform) continue;
                if (child == spawnTab.transform) continue;
                child.SetParent(spawnTab.transform, false);
            }

            // 3. Build SelectTab subtree.
            var selectTab = new UnityEngine.GameObject("SelectTab", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup));
            selectTab.transform.SetParent(contentRoot, false);

            CreateLabel(selectTab.transform, "Header_Selection", "Selection");
            var armedToggle = CreateToggle(selectTab.transform, "ArmedToggle", "Select Character (click to pick)");
            var selectedLabel = CreateLabel(selectTab.transform, "SelectedLabel", "Selected: —");
            var clearBtn = CreateButton(selectTab.transform, "ClearButton", "Clear Selection");
            CreateSeparator(selectTab.transform, "Separator");
            CreateLabel(selectTab.transform, "Header_Actions", "Actions");
            var actionsContainer = new UnityEngine.GameObject("ActionsContainer", typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup));
            actionsContainer.transform.SetParent(selectTab.transform, false);

            // AssignBuildingAction button
            var assignGO = new UnityEngine.GameObject("AssignBuildingAction", typeof(UnityEngine.RectTransform));
            assignGO.transform.SetParent(actionsContainer.transform, false);
            var assignBtn = CreateButton(assignGO.transform, "AssignBuildingButton", "Assign Building as Owner");
            var assignBtnLabel = assignBtn.GetComponentInChildren<TMPro.TMP_Text>();

            // Attach selection module + action components.
            var selectionModule = selectTab.AddComponent<DevSelectionModule>();
            var assignAction = assignGO.AddComponent<DevActionAssignBuilding>();

            // 4. Wire SerializeFields via SerializedObject.
            var soSel = new UnityEditor.SerializedObject(selectionModule);
            soSel.FindProperty("_armedToggle").objectReferenceValue = armedToggle;
            soSel.FindProperty("_selectedLabel").objectReferenceValue = selectedLabel;
            soSel.FindProperty("_clearButton").objectReferenceValue = clearBtn;
            soSel.ApplyModifiedPropertiesWithoutUndo();

            var soAct = new UnityEditor.SerializedObject(assignAction);
            soAct.FindProperty("_selection").objectReferenceValue = selectionModule;
            soAct.FindProperty("_button").objectReferenceValue = assignBtn;
            soAct.FindProperty("_buttonLabel").objectReferenceValue = assignBtnLabel;
            soAct.ApplyModifiedPropertiesWithoutUndo();

            // 5. Populate DevModePanel._tabs.
            var panelScript = root.GetComponent<DevModePanel>();
            var soPanel = new UnityEditor.SerializedObject(panelScript);
            var tabsProp = soPanel.FindProperty("_tabs");
            tabsProp.arraySize = 2;

            var t0 = tabsProp.GetArrayElementAtIndex(0);
            t0.FindPropertyRelative("TabButton").objectReferenceValue = spawnBtn;
            t0.FindPropertyRelative("Content").objectReferenceValue = spawnTab;

            var t1 = tabsProp.GetArrayElementAtIndex(1);
            t1.FindPropertyRelative("TabButton").objectReferenceValue = selectBtn;
            t1.FindPropertyRelative("Content").objectReferenceValue = selectTab;

            soPanel.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Helpers (CreateButton, CreateLabel, CreateToggle, CreateSeparator) — implement with
    // minimal TMP scaffolding similar to Task 13's first-slice prefab authoring.
}
```

Implement the helper methods (`CreateButton`, `CreateLabel`, `CreateToggle`, `CreateSeparator`) using the same minimal-TMP pattern you used in the first-slice prefab authoring (commit `2528701`). Consistency with the existing panel visuals matters less than correctness — plain TMP visuals are acceptable.

- [ ] **Step 8.4: Verify the prefab post-state**

After running the script-execute:

1. Use `mcp__ai-game-developer__assets-prefab-open` on `Assets/Resources/UI/DevModePanel.prefab` (or inspect via `mcp__ai-game-developer__gameobject-find` + hierarchy walks).
2. Confirm the hierarchy matches the target in Step 8.2.
3. Confirm `DevModePanel._tabs` has exactly 2 entries with correct refs.
4. Confirm `DevSelectionModule` serialized refs are non-null.
5. Confirm `DevActionAssignBuilding` serialized refs are non-null.
6. Confirm the old Spawn UI is still reachable under `SpawnTab` and `DevSpawnModule` still has its refs (or is on the same GameObject as before with the children re-parented).
7. Close the prefab.

- [ ] **Step 8.5: Manual Play-mode verification**

1. Press F3 → panel opens.
2. Click **Spawn** tab button → Spawn tab content is visible, Select tab hidden.
3. Click **Select** tab button → Select tab visible, Spawn hidden.
4. Arm `Select Character` toggle → `[DevSelect] Armed: True`.
5. Click an NPC in world → `[DevSelect] Selected: {name}`, toggle auto-disarms, label updates.
6. "Assign Building as Owner" button becomes enabled.
7. Click the button → label flips to `"Pick a building… (ESC to cancel)"`, button disabled.
8. Click a `CommercialBuilding` → `[DevAction] {name} set as owner of {building.name}.` in green.
9. Verify on the `CommercialBuilding` component that `_owner` is now the selected character.
10. Press Select-Character armed, click a `ResidentialBuilding` via the Assign flow → owner set + character becomes resident per `ResidentialBuilding.SetOwner` behavior.
11. Arm Select, then switch to Spawn tab → Spawn's Armed toggle stays off (no cross-toggle). Arm Spawn → Select's Armed auto-disarms.
12. While Assign Building armed, press ESC → cancel log, button label restored.

- [ ] **Step 8.6: Commit**

```bash
git add Assets/Resources/UI/DevModePanel.prefab
git commit -m "feat(devmode): restructure DevModePanel prefab for tab bar + Select tab

Tab bar with Spawn/Select buttons inside ContentRoot. Existing Spawn UI
re-parented under SpawnTab. New SelectTab holds the Select-Character
toggle, Selected label, Clear button, and the AssignBuildingAction
button wired to DevActionAssignBuilding. DevModePanel._tabs populated."
```

---

## Phase 5 — Documentation

### Task 9: Update `.agent/skills/dev-mode/SKILL.md`

**Files:**
- Modify: `.agent/skills/dev-mode/SKILL.md`

Add a new major section for the Select tab. Touch existing sections where they reference the single-tab world.

- [ ] **Step 9.1: Update the description frontmatter**

The current frontmatter (from commit `483ad5b`) mentions the spawn module only. Extend the description so someone searching for "select" / "assign building" finds the skill:

Change:
```
description: Host-only god-mode developer tool. F3 toggle (editor/dev), /devmode on|off in chat (release). First module: click-to-spawn NPCs with full configuration.
```

To:
```
description: Host-only god-mode developer tool. F3 toggle (editor/dev), /devmode on|off in chat (release). Modules: Spawn (click-to-spawn NPCs with full configuration), Select (click-to-select Characters + IDevAction plug-in; first action assigns building ownership).
```

- [ ] **Step 9.2: Add click-arbitration note**

Locate the section describing module interactions (likely under "Module Registry Pattern" or similar). Add this subsection:

```markdown
### Click arbitration

`DevModeManager` exposes a single-slot click consumer: `ActiveClickConsumer` (MonoBehaviour), `OnClickConsumerChanged` (event), `SetClickConsumer(x)`, `ClearClickConsumer(x)`. Armed dev modules MUST claim the slot when arming and release when disarming, and MUST gate their click loop on `ActiveClickConsumer == this`. Subscribing to `OnClickConsumerChanged` lets a module auto-disarm when another claims the slot — so arming Select flips Spawn off, and vice versa.
```

- [ ] **Step 9.3: Add Select tab section**

Add a new top-level section (peer to the existing Spawn module description):

```markdown
## Select Tab

Click-to-select for Characters + pluggable actions via the `IDevAction` interface.

### DevSelectionModule

Attached to the SelectTab GameObject in `DevModePanel.prefab`. Public API:

- `Character SelectedCharacter { get; }` — the currently selected Character, or null.
- `event Action OnSelectionChanged` — fires on any change (including to/from null).
- `void SetSelectedCharacter(Character c)` — replaces the selection.
- `void ClearSelection()` — sets selection to null.

Selection is cleared automatically on `SceneManager.sceneUnloaded` and on `DevModeManager.OnDevModeChanged(false)` — prevents stale references.

Click flow: armed toggle claims the click slot; next click raycasts `~0` layers with a `GetComponentInParent<Character>()` filter. Accepts any `Character` — player or NPC, local or remote-replicated.

### IDevAction

Plug-in interface for Select-tab actions. Each action is a MonoBehaviour parented under the Select tab's ActionsContainer.

```csharp
public interface IDevAction
{
    string Label { get; }
    bool IsAvailable(DevSelectionModule sel);
    void Execute(DevSelectionModule sel);
}
```

Action recipe:
1. Create `MyDevAction : MonoBehaviour, IDevAction` under `Assets/Scripts/Debug/DevMode/Modules/Actions/`.
2. Hold `[SerializeField] DevSelectionModule _selection; [SerializeField] Button _button; [SerializeField] TMP_Text _buttonLabel;`.
3. In `Start`, wire the button click to `OnButtonClicked` and subscribe to `_selection.OnSelectionChanged` to refresh the button's interactable state via `IsAvailable`.
4. If the action needs to consume additional clicks (e.g., pick a second target), use `DevModeManager.SetClickConsumer(this)` while armed and the standard click-loop pattern.
5. Add the action GameObject as a child of the SelectTab's `ActionsContainer` in the prefab.

### DevActionAssignBuilding (first action)

Enabled when a Character is selected. On `Execute`, claims the click slot and waits for the next `LayerMask.GetMask("Building")` hit. Dispatches polymorphically:

- `CommercialBuilding` → `SetOwner(character, null)` (makes character the boss).
- `ResidentialBuilding` → `SetOwner(character)` (sets primary owner; character also becomes resident).

ESC cancels. Another module claiming the click slot also cancels. Selection is preserved after success so further actions can chain.
```

- [ ] **Step 9.4: Update Known Limitations**

Extend the existing Known Limitations list with:

```markdown
- **No visual selection indicator** — the selected character is shown by label only in the panel. Follow-up slice can add a world-space outline (shader-based per rule 25) or a UI marker.
- **No undo on ownership assignment** — the new owner replaces the previous via `SetOwner`'s existing semantics. No confirmation dialog.
- **Character-first flow only** — "click building first, then assign a character to it" is deferred.
- **No multi-character selection** — one at a time.
- **No exclude-self filter** — clicking on the host's own character selects it. Add a toggle if this becomes annoying.
- **Worker/resident/job actions** — deferred. Assign Building sets ownership only.
- **Item selection + actions** — not in this slice.
```

- [ ] **Step 9.5: Commit**

```bash
git add .agent/skills/dev-mode/SKILL.md
git commit -m "docs(skill): document Select tab + IDevAction pattern

Click arbitration via ActiveClickConsumer, DevSelectionModule public
API, IDevAction plug-in recipe, DevActionAssignBuilding dispatch
semantics. Extended Known Limitations."
```

---

### Task 10: Update `.claude/agents/debug-tools-architect.md`

**Files:**
- Modify: `.claude/agents/debug-tools-architect.md`

Add a short section referencing the Select tab + click arbitration and point to the SKILL.md.

- [ ] **Step 10.1: Read and extend**

Open the agent file. The first-slice update already added a **Dev-Mode System** section with Spawn tab details. Extend it:

Add a new subsection under "Dev-Mode System" titled "Select Tab":

```markdown
### Select Tab (2nd module)

Click-to-select Characters + pluggable actions via `IDevAction`. First concrete action: `DevActionAssignBuilding` routes through `CommercialBuilding.SetOwner` / `ResidentialBuilding.SetOwner`.

Click arbitration across modules is mediated by `DevModeManager.ActiveClickConsumer` — only one armed module consumes a given click; arming a new one auto-disarms the others. New click-driven dev modules MUST use this contract.

See `.agent/skills/dev-mode/SKILL.md` for the full IDevAction recipe.
```

Frontmatter stays: `model: opus`, `memory: project`, tool list preserved. Description can optionally be extended to include "Select tab" alongside "Spawn".

- [ ] **Step 10.2: Commit**

```bash
git add .claude/agents/debug-tools-architect.md
git commit -m "docs(agent): extend debug-tools-architect with Select tab"
```

---

## Final Verification (Spec §10 Matrix)

After all tasks ship, run through the multiplayer validation matrix from the spec manually:

| # | Scenario | Expected |
|---|---|---|
| 1 | Host opens Select tab, arms toggle, clicks NPC | NPC selected, label updates, `[DevSelect] Selected:` log |
| 2 | Host selects character, Assign Building → commercial | `SetOwner(character, null)` runs; building owner changes |
| 3 | Host selects character, Assign Building → residential | `SetOwner(character)` runs; character also becomes resident |
| 4 | Host selects own player character | Accepted (no self-filter) |
| 5 | Host selects a remote client's player character | Accepted; SetOwner replicates server-side |
| 6 | Arm Spawn, arm Select | Spawn auto-disarms; only Select consumes next click |
| 7 | Arm Select, arm Spawn | Select auto-disarms |
| 8 | Arm Assign Building, press ESC | Cancel log, no mutation |
| 9 | Re-select a different character while Assign Building pending | Pending pick cancelled (cancel-on-reselect) |
| 10 | Client opens dev panel | Blocked by host-only gate, never reaches Select tab |
| 11 | Host clicks empty space while Select armed | Warning logged, stays armed |
| 12 | Host switches scene while Select holds a selection | Selection cleared on sceneUnloaded |
| 13 | Host types `/devmode off` with selection held | Selection cleared on OnDevModeChanged(false) |

Document results in the PR body or a follow-up commit message.

---

## Execution

Plan complete. Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task, spec + quality review between tasks.
2. **Inline Execution** — execute tasks in this session with batch checkpoints.

Which approach?
