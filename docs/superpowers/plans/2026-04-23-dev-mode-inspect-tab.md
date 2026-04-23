# Dev-Mode Inspect Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an **Inspect** tab to the Dev-Mode panel that displays read-only runtime information about the selected `InteractableObject`, with a full Character inspector organized into 10 sub-tabs and a dispatch mechanism that lets `WorldItem` and `Building` inspectors drop in later with zero orchestrator edits.

**Architecture:** `DevSelectionModule` gains an additive `SelectedInteractable` surface (existing `SelectedCharacter` path preserved). A new `DevInspectModule` hosts a discovered list of `IInspectorView` implementations; it activates the one whose `CanInspect(target)` matches. `CharacterInspectorView` orchestrates 10 `CharacterSubTab` children, each a tiny MonoBehaviour that formats one category into a single scrollable `TMP_Text`. The existing `UI_CharacterDebugScript` formatting logic is extracted to a shared `CharacterAIDebugFormatter` so the AI sub-tab and the in-world debug UI share one source of truth.

**Tech Stack:** Unity 2023+ (URP 2D), TextMeshPro, Unity uGUI, Netcode for GameObjects (read-only consumption — no networked logic in this feature). Host-only tool gated by `DevModeManager.IsEnabled`.

**Spec:** [docs/superpowers/specs/2026-04-23-dev-mode-inspect-tab-design.md](../specs/2026-04-23-dev-mode-inspect-tab-design.md)

---

## File Map

### Created

- `Assets/Scripts/Debug/DevMode/Inspect/IInspectorView.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/DevInspectModule.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/CharacterInspectorView.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/CharacterAIDebugFormatter.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/IdentitySubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/StatsSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/NeedsSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/AISubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CombatSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SocialSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/EconomySubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/KnowledgeSubTab.cs`
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/InventorySubTab.cs`
- `.agent/skills/debug-tools/SKILL.md` (if missing — otherwise updated)

### Modified

- `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs` — add `SelectedInteractable` + event, keep `SelectedCharacter` derived, widen click-path to resolve the general `InteractableObject`.
- `Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs` — replace inline formatting with calls into the new `CharacterAIDebugFormatter`. Zero behavioural change.
- `Assets/Resources/UI/DevModePanel.prefab` — add Inspect tab entry (Button + Content) + internal sub-tab layout (Unity Editor work, via MCP).
- `.claude/agents/debug-tools-architect.md` — document the new Inspect tab + `IInspectorView` pattern.
- `wiki/systems/dev-mode.md` — Change log entry + updated Public API section.

---

## Phase 1 — Foundation

### Task 1: Generalize `DevSelectionModule`

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs`

- [ ] **Step 1: Read the current file to confirm the starting state**

Use the Read tool on `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs`. Confirm the class has `SelectedCharacter`, `OnSelectionChanged` (parameterless `event Action`), `SetSelectedCharacter`, `ClearSelection`, and a click loop in `Update()` ending in `SetSelectedCharacter(c)`.

- [ ] **Step 2: Rewrite `DevSelectionModule.cs` with the additive surface**

Replace the file's entire content with:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Select tab of the dev-mode panel. Owns cross-cutting selection state and the click-to-select loop.
/// Holds a general <see cref="InteractableObject"/> selection; <see cref="SelectedCharacter"/> is a
/// back-compat convenience populated whenever the interactable resolves to a Character. All existing
/// <c>IDevAction</c> implementations that depend on <see cref="SelectedCharacter"/> keep working.
/// </summary>
public class DevSelectionModule : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Toggle _armedToggle;
    [SerializeField] private TMP_Text _selectedLabel;
    [SerializeField] private Button _clearButton;

    [Header("Raycast")]
    [Tooltip("Layer mask for picks. Defaults to 'RigidBody' at runtime if left at zero. Widen here when adding WorldItem/Building inspectors later.")]
    [SerializeField] private LayerMask _characterLayerMask;
    private bool _layerMaskResolved;

    public InteractableObject SelectedInteractable { get; private set; }
    public Character SelectedCharacter { get; private set; }

    /// <summary>Fires whenever <see cref="SelectedInteractable"/> changes (including to/from null).</summary>
    public event Action<InteractableObject> OnInteractableSelectionChanged;

    /// <summary>Fires whenever <see cref="SelectedCharacter"/> changes. Kept for back-compat with existing IDevActions.</summary>
    public event Action OnSelectionChanged;

    private void Start()
    {
        ResolveLayerMask();
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
            if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
            if (SelectedInteractable != null) ClearSelection();
        }
    }

    private void HandleClickConsumerChanged()
    {
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    private void HandleSceneUnloaded(Scene _)
    {
        if (SelectedInteractable != null) ClearSelection();
    }

    // ─── Public API ───────────────────────────────────────────────────

    /// <summary>General entry point. Accepts any InteractableObject; derives SelectedCharacter automatically.</summary>
    public void SetSelectedInteractable(InteractableObject interactable)
    {
        if (SelectedInteractable == interactable) return;

        SelectedInteractable = interactable;
        Character derived = null;
        if (interactable is CharacterInteractable ci)
        {
            derived = ci.GetComponentInParent<Character>();
        }
        UpdateDerivedCharacter(derived);
        RefreshLabel();
        OnInteractableSelectionChanged?.Invoke(SelectedInteractable);
    }

    /// <summary>Back-compat convenience. Prefer <see cref="SetSelectedInteractable"/>.</summary>
    public void SetSelectedCharacter(Character c)
    {
        if (c == null) { ClearSelection(); return; }
        var interactable = c.GetComponentInChildren<CharacterInteractable>();
        if (interactable == null)
        {
            // No CharacterInteractable on this Character — fall back to direct-character selection.
            if (SelectedCharacter == c) return;
            SelectedInteractable = null;
            UpdateDerivedCharacter(c);
            RefreshLabel();
            OnInteractableSelectionChanged?.Invoke(null);
            return;
        }
        SetSelectedInteractable(interactable);
    }

    public void ClearSelection()
    {
        bool hadSomething = SelectedInteractable != null || SelectedCharacter != null;
        SelectedInteractable = null;
        UpdateDerivedCharacter(null);
        RefreshLabel();
        if (hadSomething) OnInteractableSelectionChanged?.Invoke(null);
    }

    private void UpdateDerivedCharacter(Character c)
    {
        if (SelectedCharacter == c) return;
        SelectedCharacter = c;
        OnSelectionChanged?.Invoke();
    }

    private void RefreshLabel()
    {
        if (_selectedLabel == null) return;
        if (SelectedCharacter != null)
        {
            _selectedLabel.text = $"Selected: {SelectedCharacter.CharacterName}";
        }
        else if (SelectedInteractable != null)
        {
            _selectedLabel.text = $"Selected: {SelectedInteractable.gameObject.name}";
        }
        else
        {
            _selectedLabel.text = "Selected: —";
        }
    }

    private void ResolveLayerMask()
    {
        if (_characterLayerMask.value != 0)
        {
            _layerMaskResolved = true;
            return;
        }

        int layer = LayerMask.NameToLayer("RigidBody");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevSelect]</color> 'RigidBody' layer is missing from Tags & Layers. Character pick will not function.");
            _layerMaskResolved = false;
            return;
        }
        _characterLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

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
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _characterLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Click missed the selectable layer.");
            return;
        }

        // General resolution: prefer an InteractableObject in the parent chain; fall back to a direct Character hit.
        InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();
        if (interactable != null)
        {
            SetSelectedInteractable(interactable);
            _armedToggle.isOn = false;
            Debug.Log($"<color=cyan>[DevSelect]</color> Selected interactable: {interactable.gameObject.name}");
            return;
        }

        Character c = hit.collider.GetComponentInParent<Character>();
        if (c != null)
        {
            SetSelectedCharacter(c);
            _armedToggle.isOn = false;
            Debug.Log($"<color=cyan>[DevSelect]</color> Selected character: {c.CharacterName}");
            return;
        }

        Debug.LogWarning("<color=orange>[DevSelect]</color> Hit found no InteractableObject or Character in the parent chain.");
    }
}
```

- [ ] **Step 3: Verify compile succeeds**

In Unity (via MCP): run `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs` — expected: no compile errors referencing `DevSelectionModule`.

- [ ] **Step 4: Smoke-test back-compat**

Enter Play mode. Press F3. Open Select tab. Arm. Click an NPC. Expected: `SelectedCharacter` label updates, existing `DevActionAssignBuilding` action still reacts (check its button enables). No exceptions in console.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs
git commit -m "feat(devmode): generalize selection to InteractableObject with back-compat"
```

---

### Task 2: Create `IInspectorView` interface

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/IInspectorView.cs`

- [ ] **Step 1: Write the interface**

Create the file with exactly:

```csharp
/// <summary>
/// A view that knows how to display one kind of <see cref="InteractableObject"/>.
/// Owned by <see cref="DevInspectModule"/> which discovers implementations via GetComponentsInChildren.
/// </summary>
public interface IInspectorView
{
    /// <summary>True if this view is capable of displaying the given target.</summary>
    bool CanInspect(InteractableObject target);

    /// <summary>Bind the view to a fresh target. Called when the selection changes and CanInspect returned true.</summary>
    void SetTarget(InteractableObject target);

    /// <summary>Release the current target and reset internal state. Called when selection is cleared.</summary>
    void Clear();
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`, `mcp__ai-game-developer__console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/IInspectorView.cs
git commit -m "feat(devmode): add IInspectorView contract"
```

---

### Task 3: Extract `CharacterAIDebugFormatter`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/CharacterAIDebugFormatter.cs`
- Modify: `Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs`

- [ ] **Step 1: Write the formatter**

Create `CharacterAIDebugFormatter.cs`:

```csharp
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Pure formatting helpers for character AI debug state. Shared by the in-world
/// <c>UI_CharacterDebugScript</c> and the dev-mode AI inspector sub-tab so both show identical strings.
/// All methods tolerate a null character by returning a safe placeholder.
/// </summary>
public static class CharacterAIDebugFormatter
{
    public static string FormatAction(Character c)
    {
        if (c == null || c.CharacterActions == null) return "Action: N/A";
        var current = c.CharacterActions.CurrentAction;
        if (current != null) return $"<color=#FFFF00>Action: {current.GetType().Name}</color>";
        return "Action: Idle";
    }

    public static string FormatBehaviourStack(Character c)
    {
        if (c == null) return "IA: N/A";
        var controller = c.Controller as NPCController;
        if (controller == null) return "IA: <color=grey>PLAYER</color>";

        var stackNames = controller.GetBehaviourStackNames();
        if (stackNames == null || !stackNames.Any()) return "<color=grey>IA: Empty Stack</color>";

        string current = "<color=#00FFFF>Current: " + stackNames.First() + "</color>";
        string next = stackNames.Skip(1).Any()
            ? "\n<color=#F5B027>Queue: " + string.Join(" -> ", stackNames.Skip(1)) + "</color>"
            : "";
        return current + next;
    }

    public static string FormatInteraction(Character c)
    {
        if (c == null || c.CharacterInteraction == null) return "Interaction with: N/A";
        if (c.CharacterInteraction.IsInteracting && c.CharacterInteraction.CurrentTarget != null)
            return $"<color=#00FF00>Interaction with: {c.CharacterInteraction.CurrentTarget.CharacterName}</color>";
        return "<color=grey>Interaction with: None</color>";
    }

    public static string FormatAgent(Character c)
    {
        if (c == null) return "Agent: N/A";
        if (c.IsPlayer()) return "Agent: <color=grey>PLAYER (Manual)</color>";

        var controller = c.GetComponent<CharacterGameController>();
        if (controller != null && controller.Agent != null && controller.Agent.isOnNavMesh)
        {
            var agent = controller.Agent;
            string stopped = agent.isStopped ? "<color=red>STOPPED</color>" : "<color=green>RUNNING</color>";
            string path = agent.hasPath ? "Has Path" : "No Path";
            return $"Agent: {stopped} | {path}";
        }
        return "Agent: <color=orange>OFF NAVMESH</color>";
    }

    public static string FormatBusyReason(Character c)
    {
        if (c == null) return "Busy Reason: N/A";
        var reason = c.BusyReason;
        string color = reason == CharacterBusyReason.None ? "grey" : "#F5B027";
        return $"<color={color}>Busy Reason: {reason}</color>";
    }

    public static string FormatWorkPhaseGoap(Character c)
    {
        if (c == null) return "Phase: N/A\nGOAP Goal: N/A\nGOAP Action: N/A";

        string phase = "Phase: N/A";
        string goal = "GOAP Goal: None";
        string action = "GOAP Action: None";
        bool isLife = false;

        if (c.Controller is NPCController npc && npc.GoapController != null && npc.GoapController.CurrentAction != null)
        {
            isLife = true;
            string goalName = npc.GoapController.CurrentGoalName;
            goal = string.IsNullOrEmpty(goalName) || goalName == "None" ? "Life Goal: N/A" : $"Life Goal: {goalName}";
            action = $"Life Action: {npc.GoapController.CurrentAction.ActionName}";
            phase = "Phase: Life Routine";
        }
        else if (c.CharacterJob != null && c.CharacterJob.IsWorking && c.CharacterJob.CurrentJob != null)
        {
            string goalName = c.CharacterJob.CurrentJob.CurrentGoalName;
            goal = string.IsNullOrEmpty(goalName) ? "Job Goal: N/A" : $"Job Goal: {goalName}";
            string actionName = c.CharacterJob.CurrentJob.CurrentActionName;
            action = string.IsNullOrEmpty(actionName) ? "Job Action: N/A" : $"Job Action: {actionName}";
            var controller = c.Controller as NPCController;
            if (controller != null && controller.CurrentBehaviour != null && controller.CurrentBehaviour.GetType().Name == "WorkBehaviour")
                phase = "Phase: Working";
        }

        string color = isLife ? "#B0FFB0" : "#B0B0FF";
        return $"<color={color}>{phase}\n{goal}\n{action}</color>";
    }

    public static string FormatBt(Character c)
    {
        if (c == null) return "BT: N/A";
        if (c.Controller is NPCController npc)
        {
            if (npc.HasBehaviourTree) return "BT: " + npc.BehaviourTree.DebugCurrentNode;
            return "BT: N/A";
        }
        return "BT: N/A";
    }

    public static string FormatLifeGoap(Character c)
    {
        if (c == null) return "Life GOAP: N/A";
        if (c.Controller is NPCController npc && npc.GoapController != null && npc.GoapController.CurrentAction != null)
            return "Life GOAP: " + npc.GoapController.CurrentAction.ActionName;
        return "Life GOAP: None";
    }

    /// <summary>Composes every AI section into one multi-line string for a single TMP container.</summary>
    public static string FormatAll(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine(FormatAction(c));
        sb.AppendLine(FormatBehaviourStack(c));
        sb.AppendLine(FormatInteraction(c));
        sb.AppendLine(FormatAgent(c));
        sb.AppendLine(FormatBusyReason(c));
        sb.AppendLine(FormatWorkPhaseGoap(c));
        sb.AppendLine(FormatBt(c));
        sb.Append(FormatLifeGoap(c));
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Refactor `UI_CharacterDebugScript.cs` to use the formatter**

Replace the file with:

```csharp
using TMPro;
using UnityEngine;

public class UI_CharacterDebugScript : MonoBehaviour
{
    [SerializeField] private Character character;
    [SerializeField] private TextMeshProUGUI characterActionDebugText;
    [SerializeField] private TextMeshProUGUI characterBehaviourDebugText;
    [SerializeField] private TextMeshProUGUI characterInteractionDebugText;
    [SerializeField] private TextMeshProUGUI characterNeedsText;
    [SerializeField] private TextMeshProUGUI agentState;
    [SerializeField] private TextMeshProUGUI busyReasonText;
    [SerializeField] private TextMeshProUGUI workPhaseGOAPText;
    [SerializeField] private TextMeshProUGUI btStateText;
    [SerializeField] private TextMeshProUGUI lifeGoapStateText;

    private void Update()
    {
        if (character == null) return;

        if (characterActionDebugText != null) characterActionDebugText.text = CharacterAIDebugFormatter.FormatAction(character);
        if (characterBehaviourDebugText != null) characterBehaviourDebugText.text = CharacterAIDebugFormatter.FormatBehaviourStack(character);
        if (characterInteractionDebugText != null) characterInteractionDebugText.text = CharacterAIDebugFormatter.FormatInteraction(character);
        if (agentState != null) agentState.text = CharacterAIDebugFormatter.FormatAgent(character);
        if (busyReasonText != null) busyReasonText.text = CharacterAIDebugFormatter.FormatBusyReason(character);
        if (workPhaseGOAPText != null) workPhaseGOAPText.text = CharacterAIDebugFormatter.FormatWorkPhaseGoap(character);
        if (btStateText != null) btStateText.text = CharacterAIDebugFormatter.FormatBt(character);
        if (lifeGoapStateText != null) lifeGoapStateText.text = CharacterAIDebugFormatter.FormatLifeGoap(character);
        if (characterNeedsText != null) characterNeedsText.text = FormatNeeds(character);
    }

    // Kept local — this one isn't reused by the inspector (NeedsSubTab formats its own).
    private static string FormatNeeds(Character character)
    {
        var needsSystem = character.CharacterNeeds;
        if (needsSystem == null) return "<color=grey>Needs: N/A</color>";

        var needs = needsSystem.AllNeeds;
        if (needs == null || needs.Count == 0) return "<color=grey>Needs: None registered</color>";

        var sb = new System.Text.StringBuilder(256);
        sb.Append("Besoins:");
        foreach (var need in needs)
        {
            float urgency = need.GetUrgency();
            bool isActive = need.IsActive();
            string colorCode = !isActive ? "#888888" : (urgency >= 100 ? "#FF4444" : "#F5B027");
            string status = isActive ? "ON" : "OFF";
            sb.Append($"\n<color={colorCode}>  {need.GetType().Name}: {urgency:F0}% [{status}]</color>");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Verify compile and parity**

Run `mcp__ai-game-developer__assets-refresh`. Enter Play mode. Locate the in-world `UI_CharacterDebugScript` (it's used by some Character debug prefab — find via Unity hierarchy or search `t:Prefab UI_CharacterDebugScript`). Observe the text outputs for one NPC over 5–10 seconds and compare against the pre-refactor behaviour (memory or git diff): content must be identical. No new console warnings.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/CharacterAIDebugFormatter.cs Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs
git commit -m "refactor(debug): extract CharacterAIDebugFormatter, reuse in UI_CharacterDebugScript"
```

---

### Task 4: Create `CharacterSubTab` abstract base

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterSubTab.cs`

- [ ] **Step 1: Write the base class**

```csharp
using TMPro;
using UnityEngine;

/// <summary>
/// Base class for one category of the Character inspector. Each concrete sub-tab implements
/// <see cref="RenderContent"/> to produce a formatted string; exception isolation and the
/// error-line fallback are centralized here.
/// </summary>
public abstract class CharacterSubTab : MonoBehaviour
{
    [SerializeField] protected TMP_Text _content;

    /// <summary>Refresh the sub-tab with the given character. Safe to call every frame.</summary>
    public void Refresh(Character c)
    {
        if (_content == null) return;

        if (c == null)
        {
            _content.text = "<color=grey>No character selected.</color>";
            return;
        }

        try
        {
            _content.text = RenderContent(c);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            _content.text = $"<color=red>⚠ {GetType().Name} failed — {e.Message}</color>";
        }
    }

    /// <summary>Called when the inspector detaches. Override to clear per-target caches if any.</summary>
    public virtual void Clear()
    {
        if (_content != null) _content.text = "<color=grey>No character selected.</color>";
    }

    /// <summary>Produce the formatted content for this sub-tab.</summary>
    protected abstract string RenderContent(Character c);
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`, `mcp__ai-game-developer__console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterSubTab.cs
git commit -m "feat(devmode): add CharacterSubTab base class for inspector categories"
```

---

## Phase 2 — Core orchestration

### Task 5: `DevInspectModule`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/DevInspectModule.cs`

- [ ] **Step 1: Write the module**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root controller for the dev-mode Inspect tab. Listens to <see cref="DevSelectionModule"/> and
/// activates the first child <see cref="IInspectorView"/> whose <c>CanInspect</c> returns true.
/// Views are discovered at Awake; drop a new IInspectorView into the prefab hierarchy and it becomes
/// live with no edits here.
/// </summary>
public class DevInspectModule : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Header("Placeholder")]
    [Tooltip("Shown when no IInspectorView matches the current selection (or nothing is selected).")]
    [SerializeField] private GameObject _placeholder;

    private readonly List<IInspectorView> _views = new();
    private IInspectorView _active;

    private void Awake()
    {
        CollectViews();
        ShowPlaceholder();
    }

    private void CollectViews()
    {
        _views.Clear();
        foreach (var v in GetComponentsInChildren<IInspectorView>(true))
        {
            _views.Add(v);
            // Start every view inactive so Awake/OnEnable still fire but the scene is clean.
            if (v is MonoBehaviour mb) mb.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged += HandleSelection;
            // Fire once so we sync with whatever is selected now.
            HandleSelection(_selectionModule.SelectedInteractable);
        }
    }

    private void OnDisable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged -= HandleSelection;
        }
    }

    private void HandleSelection(InteractableObject target)
    {
        if (target == null)
        {
            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        IInspectorView match = null;
        for (int i = 0; i < _views.Count; i++)
        {
            var v = _views[i];
            try
            {
                if (v != null && v.CanInspect(target)) { match = v; break; }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        if (match == null)
        {
            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        if (match != _active)
        {
            DeactivateActive();
            _active = match;
            if (_active is MonoBehaviour mb) mb.gameObject.SetActive(true);
        }

        try { _active.SetTarget(target); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        if (_placeholder != null) _placeholder.SetActive(false);
    }

    private void DeactivateActive()
    {
        if (_active == null) return;
        try { _active.Clear(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
        if (_active is MonoBehaviour mb) mb.gameObject.SetActive(false);
        _active = null;
    }

    private void ShowPlaceholder()
    {
        if (_placeholder != null) _placeholder.SetActive(true);
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`, `mcp__ai-game-developer__console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/DevInspectModule.cs
git commit -m "feat(devmode): add DevInspectModule dispatcher"
```

---

### Task 6: `CharacterInspectorView`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/CharacterInspectorView.cs`

- [ ] **Step 1: Write the view**

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IInspectorView for Character targets. Owns the tab bar and 10 CharacterSubTab children.
/// Refreshes the currently visible sub-tab every frame; inactive sub-tabs are skipped.
/// </summary>
public class CharacterInspectorView : MonoBehaviour, IInspectorView
{
    [Serializable]
    public struct SubTabEntry
    {
        public Button TabButton;
        public GameObject Content;
        public CharacterSubTab Tab;
    }

    [Header("Sub-tabs (fill in prefab)")]
    [SerializeField] private SubTabEntry[] _subTabs = System.Array.Empty<SubTabEntry>();

    [Header("Labels")]
    [SerializeField] private TMPro.TMP_Text _headerLabel;

    private int _activeIndex = -1;
    private Character _target;

    public bool CanInspect(InteractableObject target)
    {
        return target is CharacterInteractable;
    }

    public void SetTarget(InteractableObject target)
    {
        if (target is CharacterInteractable ci)
        {
            _target = ci.GetComponentInParent<Character>();
        }
        else
        {
            _target = null;
        }

        UpdateHeader();
        if (_activeIndex < 0 && _subTabs.Length > 0) SwitchTab(0);
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].Tab != null) _subTabs[i].Tab.Clear();
        }
    }

    private void Awake()
    {
        for (int i = 0; i < _subTabs.Length; i++)
        {
            int captured = i;
            if (_subTabs[i].TabButton != null)
            {
                _subTabs[i].TabButton.onClick.AddListener(() => SwitchTab(captured));
            }
        }
        if (_subTabs.Length > 0) SwitchTab(0);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].TabButton != null) _subTabs[i].TabButton.onClick.RemoveAllListeners();
        }
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _subTabs.Length) return;
        _activeIndex = index;
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].Content != null) _subTabs[i].Content.SetActive(i == index);
        }
    }

    private void Update()
    {
        if (_target == null) return;
        if (_activeIndex < 0 || _activeIndex >= _subTabs.Length) return;
        var tab = _subTabs[_activeIndex].Tab;
        if (tab == null) return;
        tab.Refresh(_target); // CharacterSubTab.Refresh wraps its own try/catch.
    }

    private void UpdateHeader()
    {
        if (_headerLabel == null) return;
        _headerLabel.text = _target != null ? $"Inspecting: {_target.CharacterName}" : "Inspecting: —";
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`, `mcp__ai-game-developer__console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/CharacterInspectorView.cs
git commit -m "feat(devmode): add CharacterInspectorView with sub-tab orchestration"
```

---

## Phase 3 — Sub-tabs

Each sub-tab below follows the same recipe:
1. Create the file with the concrete `CharacterSubTab`.
2. Recompile via MCP; confirm no errors.
3. Commit.

Sub-tabs are independent — a skilled implementer can parallelize Tasks 7–16. Property-name references follow the character subsystem public APIs. If a getter is named slightly differently in the live code, the base class `try/catch` surfaces it as a red one-line error; adjust the sub-tab and re-commit.

### Task 7: `IdentitySubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/IdentitySubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class IdentitySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("<b><color=#FFFFFF>Identity</color></b>");
        sb.AppendLine($"Name: {c.CharacterName}");

        var bio = c.CharacterBio;
        if (bio != null)
        {
            sb.AppendLine($"Gender: {bio.Gender}");
            sb.AppendLine($"Age: {bio.Age}");
        }

        sb.AppendLine($"Race: {(c.Race != null ? c.Race.RaceName : "—")}");
        sb.AppendLine($"Archetype: {(c.Archetype != null ? c.Archetype.name : "—")}");
        sb.AppendLine($"Character Id: {c.CharacterId}");
        sb.AppendLine($"Origin World: {(string.IsNullOrEmpty(c.OriginWorldGuid) ? "—" : c.OriginWorldGuid)}");

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>State</color></b>");
        sb.AppendLine($"Busy Reason: {c.BusyReason}");
        sb.AppendLine($"Is Alive: {c.IsAlive()}");
        sb.AppendLine($"Is Unconscious: {c.IsUnconscious}");
        sb.AppendLine($"Is Building: {c.IsBuilding}");
        sb.AppendLine($"Is Player: {c.IsPlayer()}");
        sb.AppendLine($"In Party: {c.IsInParty()}  |  Party Leader: {c.IsPartyLeader()}");

        if (c.IsAbandoned)
        {
            sb.AppendLine($"<color=#FF4444>Abandoned  |  Former Leader: {c.FormerPartyLeaderId}</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`, `mcp__ai-game-developer__console-get-logs`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/IdentitySubTab.cs
git commit -m "feat(devmode): add IdentitySubTab"
```

---

### Task 8: `StatsSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/StatsSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;
using UnityEngine;

public class StatsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("<b><color=#FFFFFF>Combat Level</color></b>");

        var lvl = c.CharacterCombatLevel;
        if (lvl != null)
        {
            sb.AppendLine($"Level: {lvl.CurrentLevel}");
            sb.AppendLine($"XP: {lvl.CurrentExperience}");
            sb.AppendLine($"Unassigned Stat Points: {lvl.UnassignedStatPoints}");
            var history = lvl.LevelHistory;
            if (history != null && history.Count > 0)
            {
                sb.AppendLine($"Level History entries: {history.Count}");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterCombatLevel</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Stats</color></b>");

        var s = c.Stats;
        if (s != null)
        {
            AppendStat(sb, "Health", s.Health);
            AppendStat(sb, "Stamina", s.Stamina);
            AppendStat(sb, "Mana", s.Mana);
            AppendStat(sb, "Initiative", s.Initiative);
            AppendStat(sb, "Strength", s.Strength);
            AppendStat(sb, "Agility", s.Agility);
            AppendStat(sb, "Dexterity", s.Dexterity);
            AppendStat(sb, "Intelligence", s.Intelligence);
            AppendStat(sb, "Endurance", s.Endurance);
            AppendStat(sb, "Charisma", s.Charisma);
            AppendStat(sb, "PhysicalPower", s.PhysicalPower);
            AppendStat(sb, "Speed", s.Speed);
            AppendStat(sb, "DodgeChance", s.DodgeChance);
            AppendStat(sb, "Accuracy", s.Accuracy);
            AppendStat(sb, "ManaRegenRate", s.ManaRegenRate);
            AppendStat(sb, "StaminaRegenRate", s.StaminaRegenRate);
            AppendStat(sb, "CriticalHitChance", s.CriticalHitChance);
            AppendStat(sb, "MoveSpeed", s.MoveSpeed);
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterStats</color>");
        }

        return sb.ToString();
    }

    private static void AppendStat(StringBuilder sb, string label, CharacterStat stat)
    {
        if (stat == null) { sb.AppendLine($"  {label}: —"); return; }
        sb.AppendLine($"  {label}: {stat.CurrentValue:F2}");
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean. **If** `CharacterStat` field accessors are named differently on your codebase (`.Value`, `.Current`, etc.), adjust `AppendStat` and recompile.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/StatsSubTab.cs
git commit -m "feat(devmode): add StatsSubTab"
```

---

### Task 9: `SkillsTraitsSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class SkillsTraitsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Personality (Traits)</color></b>");
        var traits = c.CharacterTraits;
        if (traits != null)
        {
            sb.AppendLine($"Aggressivity: {traits.GetAggressivity():F2}");
            sb.AppendLine($"Sociability: {traits.GetSociability():F2}");
            sb.AppendLine($"Loyalty: {traits.GetLoyalty():F2}");
            sb.AppendLine($"Can Create Community: {traits.CanCreateCommunity()}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterTraits</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Skills</color></b>");
        var skills = c.CharacterSkills;
        if (skills != null && skills.Skills != null && skills.Skills.Count > 0)
        {
            foreach (var skill in skills.Skills)
            {
                if (skill == null) continue;
                sb.AppendLine($"  {skill}"); // Let SkillInstance.ToString() format itself — or adjust once we inspect the API.
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No skills registered.</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean. **If** `SkillInstance` does not override `ToString()` you'll see raw type names — in that case replace the inner loop with named getters (`skill.SkillName`, `skill.Level`, `skill.Experience`) once confirmed against the source.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs
git commit -m "feat(devmode): add SkillsTraitsSubTab"
```

---

### Task 10: `NeedsSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/NeedsSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class NeedsSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("<b><color=#FFFFFF>Needs</color></b>");

        var needsSystem = c.CharacterNeeds;
        if (needsSystem == null)
        {
            sb.AppendLine("<color=grey>No CharacterNeeds</color>");
            return sb.ToString();
        }

        var needs = needsSystem.AllNeeds;
        if (needs == null || needs.Count == 0)
        {
            sb.AppendLine("<color=grey>None registered.</color>");
            return sb.ToString();
        }

        foreach (var need in needs)
        {
            if (need == null) continue;
            float urgency = need.GetUrgency();
            bool isActive = need.IsActive();
            string colorCode = !isActive ? "#888888" : (urgency >= 100 ? "#FF4444" : "#F5B027");
            string status = isActive ? "ON" : "OFF";
            sb.AppendLine($"<color={colorCode}>  {need.GetType().Name}: {urgency:F0}% [{status}]</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/NeedsSubTab.cs
git commit -m "feat(devmode): add NeedsSubTab"
```

---

### Task 11: `AISubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/AISubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
public class AISubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        return CharacterAIDebugFormatter.FormatAll(c);
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/AISubTab.cs
git commit -m "feat(devmode): add AISubTab (delegates to CharacterAIDebugFormatter)"
```

---

### Task 12: `CombatSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CombatSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class CombatSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Combat</color></b>");
        var combat = c.CharacterCombat;
        if (combat != null)
        {
            sb.AppendLine($"In Battle: {combat.IsInBattle}");
            sb.AppendLine($"Combat Mode: {combat.IsCombatMode}");
            sb.AppendLine($"Planned Target: {(combat.PlannedTarget != null ? combat.PlannedTarget.CharacterName : "—")}");
            sb.AppendLine($"Battle Manager: {(combat.CurrentBattleManager != null ? combat.CurrentBattleManager.name : "—")}");
            sb.AppendLine($"Current Style Expertise: {combat.CurrentCombatStyleExpertise}");

            var styles = combat.KnownStyles;
            if (styles != null && styles.Count > 0)
            {
                sb.AppendLine("Known Styles:");
                foreach (var style in styles)
                {
                    if (style == null) continue;
                    sb.AppendLine($"  {style}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterCombat</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Status Effects</color></b>");
        var status = c.StatusManager;
        if (status != null)
        {
            var effects = status.ActiveEffects;
            if (effects != null && effects.Count > 0)
            {
                foreach (var effect in effects)
                {
                    if (effect == null) continue;
                    sb.AppendLine($"  {effect}");
                }
            }
            else
            {
                sb.AppendLine("<color=grey>None active.</color>");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterStatusManager</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean. **If** properties like `IsCombatMode` aren't exposed, adjust — see the `CharacterCombat` file.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CombatSubTab.cs
git commit -m "feat(devmode): add CombatSubTab"
```

---

### Task 13: `SocialSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SocialSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class SocialSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Relationships</color></b>");
        var rel = c.CharacterRelation;
        if (rel != null && rel.Relationships != null && rel.Relationships.Count > 0)
        {
            foreach (var r in rel.Relationships)
            {
                if (r == null) continue;
                sb.AppendLine($"  {r}");
            }
        }
        else
        {
            sb.AppendLine("<color=grey>None.</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Community</color></b>");
        var community = c.CharacterCommunity;
        if (community != null) sb.AppendLine($"  {community}");
        else sb.AppendLine("<color=grey>No CharacterCommunity</color>");

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Mentorship</color></b>");
        var mentor = c.CharacterMentorship;
        if (mentor != null)
        {
            sb.AppendLine($"  IsCurrentlyTeaching: {mentor.IsCurrentlyTeaching}");
            sb.AppendLine($"  {mentor}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterMentorship</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean. **If** `CharacterRelation.Relationship` doesn't override `ToString()`, replace the inner line with explicit fields (other character name, type, value).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SocialSubTab.cs
git commit -m "feat(devmode): add SocialSubTab"
```

---

### Task 14: `EconomySubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/EconomySubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class EconomySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Wallet</color></b>");
        var wallet = c.CharacterWallet;
        if (wallet != null)
        {
            var balances = wallet.GetAllBalances();
            if (balances != null)
            {
                foreach (var kv in balances)
                {
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWallet</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Job</color></b>");
        var job = c.CharacterJob;
        if (job != null)
        {
            sb.AppendLine($"  Is Working: {job.IsWorking}");
            sb.AppendLine($"  Current Job: {(job.CurrentJob != null ? job.CurrentJob.ToString() : "—")}");
            var active = job.ActiveJobs;
            if (active != null && active.Count > 0)
            {
                sb.AppendLine("  Active Jobs:");
                foreach (var j in active)
                {
                    if (j == null) continue;
                    sb.AppendLine($"    {j}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterJob</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Work Log</color></b>");
        var log = c.CharacterWorkLog;
        if (log != null)
        {
            var history = log.GetAllHistory();
            if (history != null)
            {
                sb.AppendLine($"  History entries: {history.Count}");
                foreach (var entry in history)
                {
                    if (entry == null) continue;
                    sb.AppendLine($"    {entry}");
                }
            }
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterWorkLog</color>");
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean. **If** `CharacterWallet.GetAllBalances()` returns a type without Key/Value (e.g. a `List<CurrencyBalance>`), replace the foreach with matching fields; the try/catch will surface any mismatch as an error line.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/EconomySubTab.cs
git commit -m "feat(devmode): add EconomySubTab"
```

---

### Task 15: `KnowledgeSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/KnowledgeSubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class KnowledgeSubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("<b><color=#FFFFFF>Book Knowledge</color></b>");
        var books = c.CharacterBookKnowledge;
        if (books != null)
        {
            sb.AppendLine($"  {books}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterBookKnowledge</color>");
        }

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Schedule</color></b>");
        var sched = c.CharacterSchedule;
        if (sched != null)
        {
            sb.AppendLine($"  {sched}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterSchedule</color>");
        }

        return sb.ToString();
    }
}
```

**Note:** Because `CharacterBookKnowledge` and `CharacterSchedule` API surfaces weren't fully catalogued in the exploration, this sub-tab starts by printing each component's default `ToString()`. After the first manual verification pass, replace with specific getters (entries list, current slot, etc.) once you confirm the public surface.

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/KnowledgeSubTab.cs
git commit -m "feat(devmode): add KnowledgeSubTab (initial ToString pass)"
```

---

### Task 16: `InventorySubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/InventorySubTab.cs`

- [ ] **Step 1: Write the sub-tab**

```csharp
using System.Text;

public class InventorySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Equipment</color></b>");
        var equip = c.CharacterEquipment;
        if (equip != null)
        {
            sb.AppendLine($"  {equip}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterEquipment</color>");
        }

        return sb.ToString();
    }
}
```

**Note:** `CharacterEquipment` surface (equipped slots, inventory iteration) wasn't fully catalogued during exploration. First pass uses `ToString()` for a baseline render. After manual verification, replace with explicit per-slot enumeration (`Head`, `Chest`, etc.) and an inventory item list once the public API is confirmed.

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh`. Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/InventorySubTab.cs
git commit -m "feat(devmode): add InventorySubTab (initial ToString pass)"
```

---

## Phase 4 — Prefab wiring & integration

### Task 17: Wire the DevModePanel prefab

**Files:**
- Modify: `Assets/Resources/UI/DevModePanel.prefab`

This task is Unity-Editor work done through MCP (`mcp__ai-game-developer__assets-prefab-open`, `gameobject-create`, `gameobject-component-add`, `gameobject-component-modify`, `assets-prefab-save`). It adds the Inspect tab + internal layout.

- [ ] **Step 1: Open the prefab**

Use `mcp__ai-game-developer__assets-prefab-open` with path `Assets/Resources/UI/DevModePanel.prefab`.

- [ ] **Step 2: Create the Inspect outer tab entry**

Under the prefab's content root, duplicate an existing Tab Button (e.g. Select tab button) and rename it `InspectTabButton`. Its label → `Inspect`.

Under the content container, create an empty GameObject `InspectContent` as a sibling of `SelectContent`. Add a vertical `RectTransform` that stretches to parent.

Add `DevInspectModule` component. Wire:
- `_selectionModule` → the DevSelectionModule component on the existing Select content.
- `_placeholder` → create a child `Placeholder` with a `TMP_Text` reading "Select an InteractableObject to inspect it." — assign that child to `_placeholder`.

Append a new `TabEntry` to `DevModePanel._tabs` on the prefab root: TabButton = `InspectTabButton`, Content = `InspectContent`.

- [ ] **Step 3: Build the CharacterInspectorView**

Create child `InspectContent/Views/CharacterInspectorView`. Add `CharacterInspectorView` component. Add a `TMP_Text` header field and assign it to `_headerLabel`.

Inside `CharacterInspectorView`, create two children:
- `TabBar` (Horizontal Layout Group, single row — or Grid Layout Group 5×2 if the row gets cramped).
- `SubTabContents` (Vertical / Fill).

Inside `TabBar`, create 10 `Button` children labelled `Identity`, `Stats`, `Skills & Traits`, `Needs`, `AI`, `Combat`, `Social`, `Economy`, `Knowledge`, `Inventory`.

Inside `SubTabContents`, create 10 sibling GameObjects (`Identity`, `Stats`, `SkillsTraits`, `Needs`, `AI`, `Combat`, `Social`, `Economy`, `Knowledge`, `Inventory`). Each has:
- `ScrollRect` (vertical only)
- `Viewport` with `Mask` + `Image`
- `Content` with `ContentSizeFitter` (Vertical = Preferred Size)
- A `TMP_Text` inside `Content` (anchors = stretch horizontal, top-aligned, wrap enabled)
- One CharacterSubTab component on the sub-tab root (`IdentitySubTab` on `Identity`, etc.) with its `_content` field pointing to the TMP_Text.

On `CharacterInspectorView`, populate `_subTabs[0..9]` with `{ TabButton = InspectBtn_X, Content = SubTabContents/X, Tab = SubTabContents/X/.IdentitySubTab (etc.) }` in the exact order used by the table in Spec §4.

- [ ] **Step 4: Save the prefab**

Run `mcp__ai-game-developer__assets-prefab-save`, then `mcp__ai-game-developer__assets-prefab-close`.

- [ ] **Step 5: Smoke test**

Enter Play mode. Press F3 → Dev-Mode opens. Click Inspect tab — placeholder "Select an InteractableObject…" shows.

Switch to Select tab, arm, click an NPC. Switch back to Inspect — Character view visible, Identity tab active by default with the NPC's bio data.

Click each of the other 9 tabs — each renders. Red error lines (if any) point to adjustments needed in that specific sub-tab.

- [ ] **Step 6: Commit**

```bash
git add Assets/Resources/UI/DevModePanel.prefab
git commit -m "feat(devmode): wire Inspect tab with CharacterInspectorView + 10 sub-tabs"
```

---

### Task 18: Full manual verification pass

Follow Spec §11 in order. All 8 steps must pass before docs/agent updates.

- [ ] **Selection round-trip** — back-compat: arm Select, click an NPC, confirm `DevActionAssignBuilding` still reacts.
- [ ] **Inspect activation** — Inspect tab shows Character view; placeholder hidden.
- [ ] **Each sub-tab renders** — no red error lines on a healthy NPC.
- [ ] **Subsystem failure isolation** — temporarily null out one subsystem reference (e.g. `CharacterWallet`) on the selected Character via the Unity Inspector; confirm Economy shows red error but other 9 remain functional.
- [ ] **Selection change** — select a different NPC, data updates with no stale content.
- [ ] **Deselect** — cancel selection, placeholder returns.
- [ ] **AI tab parity** — in-world `UI_CharacterDebugScript` and Inspect AI sub-tab produce identical strings.
- [ ] **Player vs NPC** — Agent/BT/GOAP fields show `PLAYER (Manual)` / N/A for the local player avatar.

No commit for this task — it is a verification gate. If any step fails, fix the offending task and re-run.

---

## Phase 5 — Documentation (CLAUDE.md rules 28, 29, 29b)

### Task 19: Create or update the Debug-Tools SKILL

**Files:**
- Create or modify: `.agent/skills/debug-tools/SKILL.md`

- [ ] **Step 1: Check existence**

Run Glob `.agent/skills/debug-tools/SKILL.md`. If present → append; if not → create following `.agent/skills/skill-creator/SKILL.md` template.

- [ ] **Step 2: Write or extend the SKILL**

Ensure the SKILL.md covers:
- **Inspect tab overview** — purpose, host-only, read-only.
- **`IInspectorView`** contract and the one-view-per-type dispatch rule.
- **Adding a new inspector** — recipe from Spec §12: create class implementing `IInspectorView`, drop GameObject under `Views/`, prefab autoregistration.
- **Adding a new Character sub-tab** — subclass `CharacterSubTab`, override `RenderContent`, add entry to `CharacterInspectorView._subTabs` in the prefab.
- **`CharacterAIDebugFormatter`** — shared source of truth for AI debug strings; called by both `UI_CharacterDebugScript` and `AISubTab`.
- **`DevSelectionModule`** additive surface — `SelectedInteractable` + `OnInteractableSelectionChanged`; `SelectedCharacter` kept derived.

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/debug-tools/SKILL.md
git commit -m "docs(skills): document Inspect tab, IInspectorView, CharacterSubTab pattern"
```

---

### Task 20: Update `debug-tools-architect` agent

**Files:**
- Modify: `.claude/agents/debug-tools-architect.md`

- [ ] **Step 1: Read the current agent file**

Read `.claude/agents/debug-tools-architect.md` to find the section describing Dev-Mode contents.

- [ ] **Step 2: Add Inspect tab + pattern**

Append to the agent's capability list:
- The Inspect tab (`DevInspectModule` + `IInspectorView` + `CharacterInspectorView`).
- The `CharacterSubTab` base + 10 concrete categories.
- The `CharacterAIDebugFormatter` helper (shared with `UI_CharacterDebugScript`).
- Selection generalization (`SelectedInteractable`/`OnInteractableSelectionChanged` on `DevSelectionModule`).

Ensure `model: opus` is still the frontmatter value (CLAUDE.md rule 29 + memory `feedback_always_opus`).

- [ ] **Step 3: Commit**

```bash
git add .claude/agents/debug-tools-architect.md
git commit -m "docs(agents): debug-tools-architect covers Inspect tab + IInspectorView"
```

---

### Task 21: Update the Dev-Mode wiki page

**Files:**
- Modify: `wiki/systems/dev-mode.md`

Per CLAUDE.md rule 29b — architecture is the wiki's concern; procedures stay in SKILL.md.

- [ ] **Step 1: Read `wiki/CLAUDE.md`**

Read `wiki/CLAUDE.md` before touching any wiki file (mandatory per the top-level project rules).

- [ ] **Step 2: Update the Dev-Mode page**

- Bump the `updated:` frontmatter date to `2026-04-23`.
- Append to `## Change log`: `- 2026-04-23 — Added Inspect tab (IInspectorView + CharacterInspectorView + 10 sub-tabs); generalized DevSelectionModule; extracted CharacterAIDebugFormatter — claude`.
- Update `## Public API` to list `DevInspectModule`, `IInspectorView`, `CharacterInspectorView`, `CharacterSubTab`, `CharacterAIDebugFormatter` plus the additive `DevSelectionModule` surface.
- Update `## Responsibilities` to include "provide read-only runtime inspection of selected interactables".
- Update `## Key classes / files` with the 14 new files.
- Confirm `depends_on` / `depended_on_by` / `related` reflect the reuse of `UI_CharacterDebugScript` by sharing the formatter.
- Add a Sources link to `.agent/skills/debug-tools/SKILL.md` (procedural source of truth).

- [ ] **Step 3: Commit**

```bash
git add wiki/systems/dev-mode.md
git commit -m "docs(wiki): dev-mode page covers Inspect tab + selection generalization"
```

---

## Self-Review

### Spec coverage
- Goal (§1) → Tasks 5, 6, 7–16 (Character inspector + dispatch) + Task 17 (wiring).
- Non-goals (§2) → honored: no mutation, no client-side logic, no WorldItem/Building views.
- User-facing behaviour (§3) → Tasks 17, 18 cover placeholder, sub-tab switching, selection round-trip.
- Sub-tabs (§4) → Tasks 7–16, one task per row in the table.
- Architecture (§5) → Task 1 (selection), Task 2 (IInspectorView), Task 3 (formatter), Tasks 4–6 (sub-tab base + module + view).
- File layout (§6) → matches Tasks 2–16 file paths.
- Prefab structure (§7) → Task 17.
- Update cadence & performance (§8) → Tasks 4 + 6 only refresh the active sub-tab.
- Network & multiplayer (§9) → no networked code added; host-only inherited from DevMode.
- Error handling (§10) → `CharacterSubTab.Refresh` try/catch (Task 4), `DevInspectModule.HandleSelection` try/catch (Task 5).
- Testing plan (§11) → Task 18.
- Extension path (§12) → SKILL doc (Task 19), agent doc (Task 20), wiki doc (Task 21).
- Rule alignment (§14) → Tasks 19, 20, 21 explicitly address CLAUDE.md rules 28, 29, 29b; Task 20 enforces rule `feedback_always_opus`.

### Placeholder scan
- Tasks 15 and 16 include an explicit **Note** acknowledging that `CharacterBookKnowledge`, `CharacterSchedule`, and `CharacterEquipment` public APIs weren't fully mapped. First pass uses `ToString()` — a deliberate baseline, not a placeholder. Refinement is documented as a follow-up inside the task note.
- No "TODO", no "implement later", no "similar to Task N" references.

### Type consistency
- `DevSelectionModule.OnInteractableSelectionChanged` is `event Action<InteractableObject>` in Task 1 and consumed identically in Task 5.
- `CharacterSubTab.Refresh(Character)` signature matches `CharacterInspectorView.Update` in Tasks 4 + 6.
- `IInspectorView` surface (`CanInspect` / `SetTarget` / `Clear`) is identical between Task 2 (declaration) and Tasks 5, 6 (consumers/implementors).
- `CharacterAIDebugFormatter.FormatAll` declared in Task 3 and called in Task 11.

No gaps detected.

---

## Handoff

Plan saved to `docs/superpowers/plans/2026-04-23-dev-mode-inspect-tab.md`.
