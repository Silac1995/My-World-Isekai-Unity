# Management Panel — Polymorphic Tab Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `UI_OwnerHiringPanel` into a generic tabbed shell `UI_OwnerManagementPanel` driven by a `virtual GetManagementTabs()` on `CommercialBuilding`. Hiring tab is the built-in; subtypes (Phase 2b shop, future crafting/farming/etc.) append more by overriding the virtual.

**Architecture:** Pure interface composition. `IManagementTab` is a stateless spec class with `Name` + `CreateView()`. `IManagementTabView` is implemented by a MonoBehaviour on a per-tab prefab. The generic singleton panel iterates `building.GetManagementTabs()`, builds header pills + body views, owns the tab-switch lifecycle. No type-discriminated branching anywhere — Open/Closed Principle (rule #10).

**Tech Stack:** Unity 6.x · C# · Unity Netcode for GameObjects · TextMeshPro · `Resources` prefab loading · `MWI.UI.Management` namespace (new, in default `Assembly-CSharp` — no new asmdef needed).

**Spec:** [docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md](../specs/2026-05-07-management-panel-tab-architecture-design.md).

**Branch:** Work on `multiplayyer` directly in the main repo (not a worktree). Each task is one atomic commit.

**Project rules:** CLAUDE.md at repo root — 35 mandatory rules. This plan applies #10 (open/closed), #14 (DI), #16 (unsubscribe events), #18/#19 (network), #28/#29b (SKILL + wiki updates), #31 (defensive try/catch), #34 (no per-frame allocations).

---

## Pre-flight (one-time before Task 1)

- [ ] Confirm working tree is clean enough — only the parallel session's untracked `.meta` files + `wiki/.obsidian/*` + `.claude/worktrees/` should be present (those are not ours):

  ```bash
  cd "c:/Users/Kevin/Unity/Unity Projects/Git/MWI - Version Control/My-World-Isekai-Unity"
  git status
  ```

  Expected: branch `multiplayyer`, ahead of origin by N commits, no modified tracked files outside of `wiki/.obsidian/*`. If anything tracked is dirty that isn't ours, STOP and surface to user.

- [ ] Confirm Unity Editor is running. The plan uses Unity MCP tools (`assets-refresh`, `console-get-logs`, `assets-prefab-*`, `gameobject-*`) extensively.

---

## Task 1: Add `IManagementTab` interface

**Files:**
- Create: `Assets/Scripts/UI/Management/IManagementTab.cs`

- [ ] **Step 1: Create the new folder + file**

Create file `Assets/Scripts/UI/Management/IManagementTab.cs`:

```csharp
namespace MWI.UI.Management
{
    /// <summary>
    /// Spec for one tab in the owner management panel. Plain C# class — no Unity lifecycle.
    /// Constructed once per panel-open by <see cref="CommercialBuilding.GetManagementTabs"/>;
    /// instantiates its view on demand via <see cref="CreateView"/>.
    ///
    /// Subtypes append tabs by overriding <c>GetManagementTabs()</c> on their building class —
    /// the panel never knows the concrete tab type.
    /// </summary>
    public interface IManagementTab
    {
        /// <summary>Header pill label, e.g. "Hiring".</summary>
        string Name { get; }

        /// <summary>
        /// Factory — instantiates and binds the view MonoBehaviour. Returns null on failure
        /// (e.g. missing Resources prefab); callers must null-check.
        /// </summary>
        IManagementTabView CreateView();
    }
}
```

- [ ] **Step 2: Refresh Unity asset database**

Run Unity MCP: `assets-refresh`.

- [ ] **Step 3: Verify no compile errors**

Run Unity MCP: `console-get-logs` (filter for errors).

Expected: no compile errors. The interface compiles standalone (no consumers yet).

- [ ] **Step 4: Commit**

```bash
cd "c:/Users/Kevin/Unity/Unity Projects/Git/MWI - Version Control/My-World-Isekai-Unity"
git add Assets/Scripts/UI/Management/IManagementTab.cs Assets/Scripts/UI/Management/IManagementTab.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add IManagementTab spec interface

Plain C# spec interface for one tab in the owner management panel.
Used by CommercialBuilding subtypes to declare their owner-only admin
tabs without modifying the panel.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add `IManagementTabView` interface

**Files:**
- Create: `Assets/Scripts/UI/Management/IManagementTabView.cs`

- [ ] **Step 1: Create the file**

Create file `Assets/Scripts/UI/Management/IManagementTabView.cs`:

```csharp
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Implemented by a MonoBehaviour on each tab's prefab. The panel re-parents
    /// <see cref="Root"/> under its body container after <see cref="IManagementTab.CreateView"/>
    /// returns and invokes the lifecycle hooks below.
    ///
    /// Lifecycle order:
    ///   CreateView → OnTabActivated (initial) → (OnTabDeactivated / OnTabActivated cycles per pill click) → Dispose
    ///
    /// <see cref="Dispose"/> MUST unsubscribe from any events the view subscribed to in its
    /// Bind/Awake (rule #16) and Destroy(<see cref="Root"/>) so the GameObject doesn't leak.
    /// </summary>
    public interface IManagementTabView
    {
        /// <summary>The instantiated GameObject the panel re-parents under its body.</summary>
        GameObject Root { get; }

        /// <summary>User clicked the header pill, OR the panel just opened on this tab.</summary>
        void OnTabActivated();

        /// <summary>User switched away — pause subscriptions if expensive (most views: no-op).</summary>
        void OnTabDeactivated();

        /// <summary>Panel closing or rebinding — unsubscribe events, free refs, Destroy(Root).</summary>
        void Dispose();
    }
}
```

- [ ] **Step 2: Refresh Unity asset database**

Run Unity MCP: `assets-refresh`.

- [ ] **Step 3: Verify no compile errors**

Run Unity MCP: `console-get-logs`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/Management/IManagementTabView.cs Assets/Scripts/UI/Management/IManagementTabView.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add IManagementTabView interface

View-side contract for a tab MonoBehaviour. Defines Root + Activate/
Deactivate/Dispose lifecycle that the generic panel drives.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Add `HiringTabView` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/UI/Management/HiringTabView.cs`

- [ ] **Step 1: Create the file**

Create file `Assets/Scripts/UI/Management/HiringTabView.cs`:

```csharp
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Built-in Hiring tab — open/closed toggle for accepting job applications.
    /// Body is intentionally minimal per spec narrowing: just one toggle button + label.
    /// Sign-text editing + job-list display from the legacy <c>UI_OwnerHiringPanel</c> are
    /// dropped (sign editing migrates to a future sign-furniture rework).
    ///
    /// Bit-for-bit network behavior preserved: same <c>TryOpenHiring</c>/<c>TryCloseHiring</c>
    /// calls into <c>CommercialBuilding</c>, same <c>OnHiringStateChanged</c> subscription,
    /// no new ServerRpcs introduced.
    ///
    /// Rule #16: <see cref="Dispose"/> unsubscribes both the event + the button click.
    /// </summary>
    public sealed class HiringTabView : MonoBehaviour, IManagementTabView
    {
        [Header("Toggle")]
        [SerializeField] private Button _toggleHiringButton;
        [SerializeField] private TextMeshProUGUI _toggleHiringLabel;

        private CommercialBuilding _building;

        public GameObject Root => gameObject;

        /// <summary>Called by <see cref="HiringTab.CreateView"/> right after Instantiate.</summary>
        public void Bind(CommercialBuilding building)
        {
            _building = building;
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.AddListener(OnToggle);
            if (_building != null) _building.OnHiringStateChanged += HandleHiringChanged;
            Refresh();
        }

        public void OnTabActivated()   { /* no-op — view is live the whole time it's bound */ }
        public void OnTabDeactivated() { /* no-op */ }

        public void Dispose()
        {
            if (_toggleHiringButton != null) _toggleHiringButton.onClick.RemoveListener(OnToggle);
            if (_building != null) _building.OnHiringStateChanged -= HandleHiringChanged;
            _building = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void HandleHiringChanged(bool _) => Refresh();

        private void Refresh()
        {
            if (_building == null || _toggleHiringLabel == null) return;
            _toggleHiringLabel.text = _building.IsHiring ? "Close Hiring" : "Open Hiring";
        }

        private void OnToggle()
        {
            if (_building == null)
            {
                Debug.LogWarning("[HiringTabView] Toggle rejected — building reference is null.");
                return;
            }
            var localPlayer = ResolveLocalPlayerCharacter();
            if (localPlayer == null)
            {
                Debug.LogWarning("[HiringTabView] Toggle rejected — could not resolve local player Character.");
                return;
            }
            if (_building.IsHiring) _building.TryCloseHiring(localPlayer);
            else                    _building.TryOpenHiring(localPlayer);
            // OnHiringStateChanged fires after replication and triggers Refresh().
        }

        /// <summary>
        /// Same resolver pattern used elsewhere in the UI layer (rule #31 — wraps
        /// network-API access in try/catch). Returns null if NetworkManager isn't up,
        /// no LocalClient, or the player NetworkObject hasn't spawned.
        /// </summary>
        private static Character ResolveLocalPlayerCharacter()
        {
            try
            {
                if (NetworkManager.Singleton == null) return null;
                var localClient = NetworkManager.Singleton.LocalClient;
                if (localClient == null || localClient.PlayerObject == null) return null;
                return localClient.PlayerObject.GetComponent<Character>();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Refresh Unity asset database**

Run Unity MCP: `assets-refresh`.

- [ ] **Step 3: Verify no compile errors**

Run Unity MCP: `console-get-logs`. Expected: no errors.

The class references `CommercialBuilding` from the global namespace — that resolves automatically since it's also in `Assembly-CSharp`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/Management/HiringTabView.cs Assets/Scripts/UI/Management/HiringTabView.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add HiringTabView (toggle-only body)

Built-in Hiring tab MonoBehaviour. Single open/closed toggle button +
label, matching the spec's narrowed scope. Reuses existing TryOpenHiring/
TryCloseHiring + OnHiringStateChanged — no new network surface.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Add `HiringTab` spec class

**Files:**
- Create: `Assets/Scripts/UI/Management/HiringTab.cs`

- [ ] **Step 1: Create the file**

Create file `Assets/Scripts/UI/Management/HiringTab.cs`:

```csharp
using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Built-in Hiring tab — every <see cref="CommercialBuilding"/> gets one.
    /// Stateless POCO; instantiates a <see cref="HiringTabView"/> on demand from
    /// <c>Resources/UI/Management/HiringTab</c>.
    /// </summary>
    public sealed class HiringTab : IManagementTab
    {
        public const string PrefabResourcePath = "UI/Management/HiringTab";

        private readonly CommercialBuilding _building;

        public HiringTab(CommercialBuilding building) { _building = building; }

        public string Name => "Hiring";

        public IManagementTabView CreateView()
        {
            try
            {
                var prefab = Resources.Load<HiringTabView>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[HiringTab] Prefab missing at Resources/{PrefabResourcePath} — tab will not render.");
                    return null;
                }
                var view = Object.Instantiate(prefab);
                view.Bind(_building);
                return view;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Management/HiringTab.cs Assets/Scripts/UI/Management/HiringTab.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add HiringTab spec class

Plain C# IManagementTab. CreateView() Resources.Loads the HiringTab
prefab, instantiates the HiringTabView, binds the building reference,
returns the view. Defensive try/catch + null-prefab handling per rule #31.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Add `CommercialBuilding.GetManagementTabs()` virtual

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

- [ ] **Step 1: Find the right insertion point**

Open `Assets/Scripts/World/Buildings/CommercialBuilding.cs`. Look for the existing virtual surface (e.g. near `protected abstract void InitializeJobs();` at line ~569, OR near the public hiring API around line 2489). Insert near the hiring API since it's thematically related.

- [ ] **Step 2: Add the virtual method**

In `CommercialBuilding.cs`, just AFTER the existing `TryCloseHiring` block (around line 2548 — locate by searching `private void TryCloseHiringServerRpc`), insert:

```csharp
    // ============================================================================
    // MANAGEMENT PANEL — polymorphic tab surface
    // ============================================================================

    /// <summary>
    /// Returns the list of owner-only admin tabs surfaced in <c>UI_OwnerManagementPanel</c>.
    /// Base returns just the <c>HiringTab</c>. Subtypes (e.g. ShopBuilding) append their own
    /// tabs by calling <c>base.GetManagementTabs()</c> and adding to the result:
    ///
    /// <code>
    /// public override IReadOnlyList&lt;IManagementTab&gt; GetManagementTabs()
    /// {
    ///     var tabs = new List&lt;IManagementTab&gt;(base.GetManagementTabs());
    ///     tabs.Add(new ShopCatalogTab(this));
    ///     return tabs;
    /// }
    /// </code>
    ///
    /// Open/Closed (rule #10): the panel never knows the concrete tab type. Allocations
    /// are per-panel-open (not per-frame, rule #34) — single 1-element array on the base
    /// path is acceptable.
    /// </summary>
    public virtual System.Collections.Generic.IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()
    {
        return new MWI.UI.Management.IManagementTab[] { new MWI.UI.Management.HiringTab(this) };
    }
```

- [ ] **Step 3: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors. The virtual references `MWI.UI.Management.HiringTab` which compiled in Task 4.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "$(cat <<'EOF'
feat(commercial-building): add GetManagementTabs() virtual

Returns the list of owner-only admin tabs for the management panel. Base
returns [HiringTab]; subtypes append by overriding (Open/Closed, rule #10).
Allocates per-panel-open only — not per-frame.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Add `UI_OwnerManagementPanel` MonoBehaviour

**Files:**
- Create: `Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs`:

```csharp
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management
{
    /// <summary>
    /// Owner-only generic management panel for a <see cref="CommercialBuilding"/>.
    /// Replaces the legacy single-purpose <c>UI_OwnerHiringPanel</c>. Tabs are supplied
    /// by the building via <see cref="CommercialBuilding.GetManagementTabs"/>, so the
    /// panel itself never knows the concrete subtype.
    ///
    /// Lazy singleton-on-demand pattern: first <see cref="Show"/> call instantiates the
    /// prefab from <c>Resources/UI/UI_OwnerManagementPanel</c>; subsequent calls re-bind
    /// to the new building (warm-path = same building, cold-path = different building).
    ///
    /// Owner-gate is at the call sites (<c>ManagementFurniture.Use</c> + <c>CharacterJob.
    /// GetInteractionOptions</c>); the panel adds a defense-in-depth fail-silent re-check
    /// in <see cref="ShowInternal"/> for any future caller that forgets.
    ///
    /// Rule #16: cleanup happens in <see cref="OnDestroy"/> + <see cref="Close"/> —
    /// every spawned tab view is Disposed (which destroys its GameObject + unsubscribes).
    /// </summary>
    public class UI_OwnerManagementPanel : MonoBehaviour
    {
        public const string PrefabResourcePath = "UI/UI_OwnerManagementPanel";
        private static UI_OwnerManagementPanel _instance;

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI _titleLabel;

        [Header("Tabs")]
        [Tooltip("Parent transform for the header pill buttons (one per tab).")]
        [SerializeField] private RectTransform _tabHeaderRoot;
        [Tooltip("Pill prefab — must contain a Button at the root and a TextMeshProUGUI in itself or a child for the label.")]
        [SerializeField] private GameObject _tabHeaderPillPrefab;
        [Tooltip("Parent transform for the active tab's view Root.")]
        [SerializeField] private RectTransform _tabBodyRoot;

        [Header("Close")]
        [SerializeField] private Button _closeButton;
        [Tooltip("Full-screen invisible button behind the content panel — outside-click closes the panel.")]
        [SerializeField] private Button _dismissOverlay;

        private CommercialBuilding _building;

        private struct Entry
        {
            public IManagementTabView View;
            public GameObject Pill;
            public Button PillButton;
        }
        private readonly List<Entry> _spawned = new List<Entry>(4);
        private int _activeIndex = -1;

        // ============================================================================
        // PUBLIC ENTRY
        // ============================================================================

        /// <summary>
        /// Open (or re-open) the panel for <paramref name="building"/>. Lazy-instantiates
        /// the singleton instance on first call. Safe to call repeatedly — same building
        /// hits the warm path; different building tears down + rebuilds.
        /// </summary>
        public static void Show(CommercialBuilding building)
        {
            if (building == null)
            {
                Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — building is null.");
                return;
            }
            if (_instance == null)
            {
                try
                {
                    var prefab = Resources.Load<UI_OwnerManagementPanel>(PrefabResourcePath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[UI_OwnerManagementPanel] Prefab missing at Resources/{PrefabResourcePath}.");
                        return;
                    }
                    var canvas = Object.FindFirstObjectByType<Canvas>();
                    _instance = Instantiate(prefab, canvas != null ? canvas.transform : null);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    return;
                }
            }
            _instance.ShowInternal(building);
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        private void Awake()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dismissOverlay != null) _dismissOverlay.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
            if (_dismissOverlay != null) _dismissOverlay.onClick.RemoveListener(Close);
            DisposeAllTabs();
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // Local UI dismissal only — exempt from rule #33 (PlayerController-owned input)
            // because it doesn't drive the player character.
            if (gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ============================================================================
        // SHOW / SWITCH / CLOSE
        // ============================================================================

        private void ShowInternal(CommercialBuilding building)
        {
            // Defense-in-depth owner gate — does not authoritatively reject (call sites do that),
            // just guards against future callers forgetting the gate. Fail-silent.
            var localPlayer = ResolveLocalPlayerCharacter();
            if (localPlayer == null || building.Owner != localPlayer)
            {
                Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — local character is not the owner.");
                return;
            }

            // Warm path: same building → just re-show.
            if (_building == building && _spawned.Count > 0)
            {
                gameObject.SetActive(true);
                if (_activeIndex >= 0) _spawned[_activeIndex].View.OnTabActivated();
                return;
            }

            // Cold path: different building (or first show) → tear down + rebuild.
            DisposeAllTabs();
            _building = building;

            if (_titleLabel != null) _titleLabel.text = building.BuildingName;

            var tabs = building.GetManagementTabs();
            if (tabs == null || tabs.Count == 0)
            {
                Debug.LogWarning($"[UI_OwnerManagementPanel] {building.BuildingName} returned no tabs.");
                return;
            }

            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                if (tab == null) continue;

                var view = tab.CreateView();
                if (view == null || view.Root == null)
                {
                    Debug.LogWarning($"[UI_OwnerManagementPanel] Tab '{tab.Name}' produced null view — skipping.");
                    continue;
                }
                view.Root.transform.SetParent(_tabBodyRoot, false);
                view.Root.SetActive(false);

                GameObject pill = null;
                Button pillButton = null;
                if (_tabHeaderPillPrefab != null && _tabHeaderRoot != null)
                {
                    pill = Instantiate(_tabHeaderPillPrefab, _tabHeaderRoot);
                    pillButton = pill.GetComponent<Button>();
                    var pillLabel = pill.GetComponentInChildren<TextMeshProUGUI>();
                    if (pillLabel != null) pillLabel.text = tab.Name;
                    int capturedIndex = _spawned.Count;
                    if (pillButton != null) pillButton.onClick.AddListener(() => SwitchTo(capturedIndex));
                }

                _spawned.Add(new Entry { View = view, Pill = pill, PillButton = pillButton });
            }

            if (_spawned.Count > 0) SwitchTo(0);
            gameObject.SetActive(true);
        }

        private void SwitchTo(int index)
        {
            if (index < 0 || index >= _spawned.Count) return;
            if (_activeIndex == index) return;

            if (_activeIndex >= 0 && _activeIndex < _spawned.Count)
            {
                var prev = _spawned[_activeIndex];
                prev.View.OnTabDeactivated();
                if (prev.View.Root != null) prev.View.Root.SetActive(false);
                SetPillSelected(prev.Pill, false);
            }

            _activeIndex = index;
            var next = _spawned[index];
            if (next.View.Root != null) next.View.Root.SetActive(true);
            next.View.OnTabActivated();
            SetPillSelected(next.Pill, true);
        }

        private void Close()
        {
            DisposeAllTabs();
            _building = null;
            gameObject.SetActive(false);
        }

        private void DisposeAllTabs()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                var e = _spawned[i];
                try { e.View?.Dispose(); }
                catch (System.Exception ex) { Debug.LogException(ex); }
                if (e.PillButton != null) e.PillButton.onClick.RemoveAllListeners();
                if (e.Pill != null) Destroy(e.Pill);
            }
            _spawned.Clear();
            _activeIndex = -1;
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        /// <summary>
        /// Best-effort visual cue: tints the pill's Image (or leaves it alone if no Image).
        /// Designer is free to override the prefab to do something fancier; this is the
        /// fallback when no custom selection logic is wired.
        /// </summary>
        private static void SetPillSelected(GameObject pill, bool selected)
        {
            if (pill == null) return;
            var image = pill.GetComponent<Image>();
            if (image != null)
            {
                var c = image.color;
                c.a = selected ? 1f : 0.6f;
                image.color = c;
            }
        }

        private static Character ResolveLocalPlayerCharacter()
        {
            try
            {
                if (NetworkManager.Singleton == null) return null;
                var localClient = NetworkManager.Singleton.LocalClient;
                if (localClient == null || localClient.PlayerObject == null) return null;
                return localClient.PlayerObject.GetComponent<Character>();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors. The class references `CommercialBuilding.GetManagementTabs` (added Task 5) and `IManagementTab`/`IManagementTabView` (Tasks 1-2).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add UI_OwnerManagementPanel

Generic tabbed shell — header label + tab pill bar + body container +
close + dismiss-overlay. Lazy singleton, warm-path on same-building re-Show.
Defense-in-depth owner gate. Cascading Dispose on Close / OnDestroy. No
new ServerRpcs; reads building.GetManagementTabs() to enumerate tabs.

Compiles but won't display anything yet — prefab lands in next task.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Create the `HiringTab` prefab via Unity MCP

**Files:**
- Create: `Assets/Resources/UI/Management/HiringTab.prefab`

The HiringTab prefab is the body content for one tab — a plain UI panel with a Button + TextMeshProUGUI label. Designer styling can be tweaked later; this task focuses on a structurally-correct prefab the panel can instantiate.

- [ ] **Step 1: Create the Resources subfolder**

Run Unity MCP: `assets-create-folder` with `path: "Assets/Resources/UI/Management"`.

- [ ] **Step 2: Read the legacy prefab to copy its toggle-button styling**

Run Unity MCP: `assets-find` with filter `"UI_OwnerHiringPanel t:Prefab"` to locate the legacy prefab. Then `assets-prefab-open` it. Use `gameobject-find` to inspect the structure of the toggle button (`_toggleHiringButton` referenced at `UI_OwnerHiringPanel.cs:44`) so Task 7 produces a button visually consistent with the rest of the UI. Capture: button RectTransform size, Image color, label font + size + color. Close the prefab without saving (`assets-prefab-close`).

- [ ] **Step 3: Build the new HiringTab prefab in an empty scene**

Open or create a temporary scratch scene (`scene-list-opened` to find one already open, otherwise `scene-create` a temp). In the scene:

1. `gameobject-create` a new GameObject named `HiringTab` with `RectTransform` (auto-added when parent is a Canvas, but for a stand-alone prefab we add manually next).
2. `gameobject-component-add` `RectTransform` (replace the default Transform).
3. `gameobject-component-add` `HiringTabView` (the script from Task 3).
4. As a child, `gameobject-create` `ToggleButton` with `RectTransform`.
5. `gameobject-component-add` on `ToggleButton`: `Image` (matches captured styling), `Button`.
6. As a child of `ToggleButton`, `gameobject-create` `Label` with `RectTransform` + `TextMeshProUGUI`. Initial text: `"Open Hiring"`.
7. Use `object-modify` (or `gameobject-component-modify`) on the `HiringTabView` component to wire `_toggleHiringButton` to the `Button` and `_toggleHiringLabel` to the `TextMeshProUGUI`.

- [ ] **Step 4: Save as a prefab**

Run Unity MCP: `assets-prefab-create` with the `HiringTab` GameObject as source and target path `Assets/Resources/UI/Management/HiringTab.prefab`.

Then `gameobject-destroy` the scene-side instance and revert the scene if you opened one.

- [ ] **Step 5: Verify the prefab**

Run Unity MCP: `assets-find` with `"HiringTab t:Prefab"`. Expected: one match at the new path. Then `assets-prefab-open` it and `gameobject-find` to confirm the structure (Button + Label children, `HiringTabView` component bound).

Run `console-get-logs` — expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Resources/UI/Management/HiringTab.prefab Assets/Resources/UI/Management/HiringTab.prefab.meta Assets/Resources/UI/Management.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add HiringTab prefab

Resources/UI/Management/HiringTab — body content for the built-in Hiring
tab. Single Button + TextMeshProUGUI label, both wired to the
HiringTabView component on the prefab root.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Create the `UI_OwnerManagementPanel` prefab via Unity MCP

**Files:**
- Create: `Assets/Resources/UI/UI_OwnerManagementPanel.prefab`
- Create: `Assets/Resources/UI/Management/TabHeaderPill.prefab` (small helper prefab — the pill button used in the tab header)

The fastest path: copy the legacy `UI_OwnerHiringPanel.prefab` as a base (preserves Canvas reference / styling / close button / dismiss overlay), then strip the legacy controls and add the new tab-bar + body containers.

- [ ] **Step 1: Build the TabHeaderPill helper prefab first**

Run Unity MCP `gameobject-create` `TabHeaderPill` with `RectTransform`. Add `Image` (semi-transparent fill matching project palette). Add `Button` referencing the same Image. Create a child `Label` with `TextMeshProUGUI` (placeholder text "Tab"). Save as `Assets/Resources/UI/Management/TabHeaderPill.prefab` via `assets-prefab-create`. Destroy the scene instance.

- [ ] **Step 2: Copy the legacy panel prefab**

Run Unity MCP: `assets-copy` with `from: Assets/Resources/UI/UI_OwnerHiringPanel.prefab`, `to: Assets/Resources/UI/UI_OwnerManagementPanel.prefab`.

(Do NOT delete the legacy prefab yet — Task 11 handles that.)

- [ ] **Step 3: Open the copy and modify it**

Run `assets-prefab-open` on `UI_OwnerManagementPanel.prefab`.

Inside the prefab:

1. **Remove legacy elements:** `gameobject-find` and `gameobject-destroy` the following children (whichever exist in the legacy structure — names mirror `UI_OwnerHiringPanel.cs:33-50`):
   - the status label (`_statusLabel`)
   - the job-list scroll view / job-list root + row prefab references (`_jobListRoot`)
   - the toggle hiring button + its label (was `_toggleHiringButton` / `_toggleHiringLabel`)
   - the custom-text input field (`_customTextInput`)
   - the submit-text button (`_submitTextButton`)
   - the custom-text hint label (`_customTextHint`)

   KEEP: title label, close button, dismiss overlay, the panel root + Canvas wiring.

2. **Remove the old script component:** `gameobject-component-destroy` `UI_OwnerHiringPanel` from the prefab root.

3. **Add the new script component:** `gameobject-component-add` `MWI.UI.Management.UI_OwnerManagementPanel` to the prefab root.

4. **Add tab-bar + body containers:** `gameobject-create` two new children:
   - `TabHeader` (RectTransform, Horizontal Layout Group, sized as a header strip across the top of the panel content area).
   - `TabBody` (RectTransform, sized to fill the remaining content area below the header).

5. **Wire the new component's serialized fields** via `gameobject-component-modify`:
   - `_titleLabel` → existing title TextMeshProUGUI (kept from the legacy prefab).
   - `_tabHeaderRoot` → the new `TabHeader` RectTransform.
   - `_tabHeaderPillPrefab` → reference to `Assets/Resources/UI/Management/TabHeaderPill.prefab` (use `assets-find` to get its asset reference).
   - `_tabBodyRoot` → the new `TabBody` RectTransform.
   - `_closeButton` → existing close button.
   - `_dismissOverlay` → existing dismiss overlay.

6. `assets-prefab-save`.

- [ ] **Step 4: Verify**

Run `assets-prefab-close`, then `assets-find` `UI_OwnerManagementPanel t:Prefab` to confirm. Run `console-get-logs` — expected: no errors / no missing-reference warnings.

- [ ] **Step 5: Commit**

```bash
git add Assets/Resources/UI/UI_OwnerManagementPanel.prefab Assets/Resources/UI/UI_OwnerManagementPanel.prefab.meta Assets/Resources/UI/Management/TabHeaderPill.prefab Assets/Resources/UI/Management/TabHeaderPill.prefab.meta
git commit -m "$(cat <<'EOF'
feat(ui-management): add UI_OwnerManagementPanel + TabHeaderPill prefabs

Resources/UI/UI_OwnerManagementPanel — generic tabbed shell. Built by
copying the legacy hiring prefab + stripping its body controls + adding
TabHeader/TabBody containers + rebinding to the new component.
Resources/UI/Management/TabHeaderPill — small button-+-label pill used
for the header bar (one per tab).

Legacy UI_OwnerHiringPanel prefab still on disk — deleted in Task 11
once both call sites are rewired.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Rewire `ManagementFurniture.Use` call site

**Files:**
- Modify: `Assets/Scripts/World/Furniture/ManagementFurniture.cs:54` (and xmldoc references at lines 6 + 14 if present).

- [ ] **Step 1: Inspect the file**

Read `Assets/Scripts/World/Furniture/ManagementFurniture.cs`. Confirm:
- Line ~54: `UI_OwnerHiringPanel.Show(building);`
- Line ~6: xmldoc reference `<see cref="UI_OwnerHiringPanel"/>`.

- [ ] **Step 2: Replace the call site**

Replace `UI_OwnerHiringPanel.Show(building);` with `MWI.UI.Management.UI_OwnerManagementPanel.Show(building);`.

(Using the fully-qualified name to avoid touching the `using` block or worrying about ambiguity.)

- [ ] **Step 3: Update the xmldoc reference**

Replace the `<see cref="UI_OwnerHiringPanel"/>` occurrence in the class summary with `<see cref="MWI.UI.Management.UI_OwnerManagementPanel"/>`.

- [ ] **Step 4: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Furniture/ManagementFurniture.cs
git commit -m "$(cat <<'EOF'
refactor(management-furniture): route Use() to UI_OwnerManagementPanel

The owner's management desk now opens the new generic panel. Owner gate +
toast behavior unchanged. Legacy UI_OwnerHiringPanel.Show call replaced.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Rewire `CharacterJob.GetInteractionOptions:678` call site

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` (around line 678 — locate by searching for the string `"Manage Hiring..."`).

- [ ] **Step 1: Inspect the file**

Read the relevant block in `CharacterJob.cs` (use Grep for `"Manage Hiring..."` to find it). Expected current state:

```csharp
        var interactorOwned = interactor.CharacterJob.OwnedBuilding;
        if (interactorOwned != null && !interactorOwned.HasManagementFurniture)
        {
            var capturedOwned = interactorOwned;
            options.Add(new InteractionOption
            {
                Name = "Manage Hiring...",
                IsDisabled = false,
                Action = () => UI_OwnerHiringPanel.Show(capturedOwned)
            });
        }
```

- [ ] **Step 2: Update the menu label and the call**

Change two lines:

- `Name = "Manage Hiring...",`  →  `Name = "Manage...",`
- `Action = () => UI_OwnerHiringPanel.Show(capturedOwned)`  →  `Action = () => MWI.UI.Management.UI_OwnerManagementPanel.Show(capturedOwned)`

- [ ] **Step 3: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "$(cat <<'EOF'
refactor(character-job): route fallback Manage menu to new panel

Owners without a ManagementFurniture desk can still access the panel
via the boss-NPC interaction menu. Renamed "Manage Hiring..." to
"Manage..." since the panel is now polymorphic (Hiring is one of N tabs).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Delete legacy `UI_OwnerHiringPanel.cs` + decommission its prefab

**Files:**
- Delete: `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` + `.cs.meta`
- Delete: `Assets/Resources/UI/UI_OwnerHiringPanel.prefab` + `.prefab.meta`

Both call sites are now rewired (Tasks 9 + 10). `UI_OwnerHiringPanel` has zero callers.

- [ ] **Step 1: Confirm no remaining references**

Run Grep for `UI_OwnerHiringPanel` across `Assets/Scripts/` (excluding the file we're about to delete). Expected: zero matches.

If there are matches, STOP and surface them — there's a missed call site.

- [ ] **Step 2: Delete the script + meta**

```bash
cd "c:/Users/Kevin/Unity/Unity Projects/Git/MWI - Version Control/My-World-Isekai-Unity"
rm Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs.meta
```

- [ ] **Step 3: Delete the prefab + meta**

Use Unity MCP: `assets-delete` with `paths: ["Assets/Resources/UI/UI_OwnerHiringPanel.prefab"]`.

(Letting Unity handle prefab deletion is safer than `rm` — it cleans up the meta + AssetDatabase entry in one shot.)

- [ ] **Step 4: Refresh + verify**

Run Unity MCP: `assets-refresh`, then `console-get-logs`. Expected: no errors. (Compile is fine — no callers; no GUID-missing warnings since both files are gone in lockstep.)

- [ ] **Step 5: Commit**

```bash
git add -u Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs.meta Assets/Resources/UI/UI_OwnerHiringPanel.prefab Assets/Resources/UI/UI_OwnerHiringPanel.prefab.meta
git commit -m "$(cat <<'EOF'
refactor(ui): delete legacy UI_OwnerHiringPanel.cs + prefab

Both call sites (ManagementFurniture.Use + CharacterJob.GetInteractionOptions
fallback menu) have been rewired to UI_OwnerManagementPanel. The legacy
class + Resources prefab are no longer referenced anywhere.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: Manual multiplayer matrix verification

This task does not modify code; it verifies the implementation against the spec's Section 5.2 multiplayer matrix.

- [ ] **Step 1: Set up a Host + Client play session**

In Unity:
1. Open the main scene with a Region containing at least one `CommercialBuilding` that has a `ManagementFurniture` desk + an authored `Owner`.
2. Build & Run a standalone client (or use ParrelSync / two editor instances).
3. Start as Host on instance A, Connect as Client on instance B.

- [ ] **Step 2: Run scenarios**

For each scenario, document the observed behavior (PASS/FAIL). Spec source: `docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md` §5.2.

| # | Scenario | Expected | Result |
|---|----------|----------|--------|
| 1 | Host owner walks to desk, presses E | Panel opens with Hiring tab active. Toggle label reads "Open Hiring" or "Close Hiring" matching `IsHiring`. | |
| 2 | Host owner clicks toggle | Label flips. Client sees `IsHiring` change (open Manage panel on client to confirm if applicable) within 1 frame. | |
| 3 | Client owner walks to desk, presses E | Panel opens locally only. UI is client-side. | |
| 4 | Client owner clicks toggle | ServerRpc fires; host validates; replication updates label after RTT. | |
| 5 | Non-owner peer presses E on owner's desk | Toast "Only the owner can use this management desk." Panel does not open. | |
| 6 | Owner toggles repeatedly | Each toggle replicates correctly. No duplicate event subscriptions. | |
| 7 | Open panel, press ESC | Panel closes; subsequent open re-uses the singleton (warm path on same building). | |
| 8 | Open panel, click outside content area (dismiss-overlay) | Panel closes. | |
| 9 | Open panel, click X close button | Panel closes. | |
| 10 | Open panel building A, walk to building B (different owner-by-same-character), press E | Panel re-binds to building B; old tabs Disposed; new tabs built. Title label updates. | |
| 11 | Late-joiner connects with hiring already toggled | Late client opens panel; toggle label reflects current `IsHiring`. | |
| 12 | Building destroyed mid-panel-open (despawn / hibernate) | Toggle click bails on null; panel can still be closed via ESC/X/dismiss. | |

- [ ] **Step 3: If any scenario fails**

STOP. Surface the failure to the user. Do NOT commit a verification record until all 12 scenarios pass.

- [ ] **Step 4: Optional — record verification**

If all pass, write a short smoketest record:

`docs/superpowers/smoketests/2026-05-07-management-panel-smoketest.md`

```markdown
# Management Panel — Smoketest

**Date**: 2026-05-07
**Spec**: docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md
**Plan**: docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md

## Scenarios

(Copy the table above with PASS markers + observation notes.)

## Sign-off

All 12 scenarios PASS as of commit `<sha>`.
```

```bash
git add docs/superpowers/smoketests/2026-05-07-management-panel-smoketest.md
git commit -m "$(cat <<'EOF'
docs(smoketest): management-panel multiplayer matrix verified

12/12 scenarios PASS on host+client. Spec §5.2 fully validated.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: Update wiki + SKILL files

**Files:**
- Modify: `wiki/systems/help-wanted-and-hiring.md`
- Modify: `wiki/systems/commercial-building.md`
- Create: `wiki/systems/management-panel-architecture.md`
- Modify: `.agent/skills/help-wanted-and-hiring/SKILL.md` (only if it exists; otherwise skip)
- Create: `.agent/skills/management-panel/SKILL.md`

Per CLAUDE.md rule #28 (SKILL.md must reflect changes) + #29b (wiki/systems/ must be updated). Reading [wiki/CLAUDE.md](../../../wiki/CLAUDE.md) is required before touching any file under `wiki/`.

- [ ] **Step 1: Read wiki authoring rules**

Read `wiki/CLAUDE.md` to refresh on frontmatter / wikilinks / sources / change-log conventions.

- [ ] **Step 2: Update `wiki/systems/help-wanted-and-hiring.md`**

Edit:

a. **Frontmatter `updated:`** field → `2026-05-07`.

b. **`Key classes / files` table** — replace the row that points at `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` with the row trio:

```markdown
| [Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs](../../Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs) | Generic owner management panel — replaces UI_OwnerHiringPanel |
| [Assets/Scripts/UI/Management/HiringTab.cs](../../Assets/Scripts/UI/Management/HiringTab.cs) | Built-in Hiring tab — IManagementTab spec class |
| [Assets/Scripts/UI/Management/HiringTabView.cs](../../Assets/Scripts/UI/Management/HiringTabView.cs) | HiringTab view MonoBehaviour — toggle-only body |
```

Also replace the `Assets/Resources/UI/UI_OwnerHiringPanel.prefab` row with:

```markdown
| [Assets/Resources/UI/UI_OwnerManagementPanel.prefab](../../Assets/Resources/UI/UI_OwnerManagementPanel.prefab) | Singleton-on-demand generic panel prefab |
| [Assets/Resources/UI/Management/HiringTab.prefab](../../Assets/Resources/UI/Management/HiringTab.prefab) | Hiring tab body prefab — toggle button + label |
```

c. **`Public API / entry points` section** — replace `UI_OwnerHiringPanel.Show(building)` with `MWI.UI.Management.UI_OwnerManagementPanel.Show(building)`.

d. **`Data flow` block** — update the "Player owner manages hiring" arrow at the bottom to reference `UI_OwnerManagementPanel.Show(OwnedBuilding) → HiringTab → toggle hiring`.

e. **`Open questions / TODO`** — add (or clarify):

```markdown
- **Phase 2: per-job hiring (replace building-wide `_isHiring` with per-Job flag).** See [[management-panel-architecture]] §Open questions for the deferred design surface.
- **Phase 2: Help Wanted sign rework (its own readable+editable furniture type).** Sign editing was removed from the management panel during the 2026-05-07 polymorphic refactor; owners can no longer customize text from the panel until the sign-furniture rework lands.
```

f. **`Change log`** — append:

```markdown
- 2026-05-07 — **Polymorphic management panel refactor.** `UI_OwnerHiringPanel` replaced by the generic `UI_OwnerManagementPanel` driven by a virtual `CommercialBuilding.GetManagementTabs()` (returns `[HiringTab]` on the base; subtypes append). `HiringTab` body simplified to toggle-only — sign editing dropped (deferred to a future sign-furniture rework), job-list display dropped. `_isHiring` building-wide bool unchanged — per-job hiring deferred to a dedicated phase. See [[management-panel-architecture]]. — claude
```

- [ ] **Step 3: Update `wiki/systems/commercial-building.md`**

Edit:

a. **Frontmatter `updated:`** field → `2026-05-07`.

b. **`Public API / entry points` section** — add a bullet under the existing API:

```markdown
- `building.GetManagementTabs()` → `IReadOnlyList<MWI.UI.Management.IManagementTab>` (virtual). Returns `[HiringTab]` on the base. Subtypes override to append more owner-only admin tabs (Open/Closed Principle, rule #10). Allocates per-panel-open only.
```

c. **`Change log`** — append:

```markdown
- 2026-05-07 — Added `GetManagementTabs()` virtual — polymorphic surface for the new owner management panel. Subtypes append tabs without modifying the panel. See [[management-panel-architecture]]. — claude
```

- [ ] **Step 4: Create `wiki/systems/management-panel-architecture.md`**

Use the project's wiki template structure (see `wiki/_templates/` if present, else mirror the format of `wiki/systems/help-wanted-and-hiring.md`). Required ten sections (per `wiki/CLAUDE.md`): Purpose · Responsibilities · Key classes / files · Public API · Data flow · Dependencies · State & persistence · Gotchas · Open questions · Change log.

Frontmatter:

```yaml
---
type: system
title: "Owner Management Panel — Polymorphic Tabs"
tags: [building, ui, owner, management, tabs, tier-2]
created: 2026-05-07
updated: 2026-05-07
sources: []
related:
  - "[[commercial-building]]"
  - "[[help-wanted-and-hiring]]"
  - "[[management-furniture]]"
  - "[[character-job]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/UI/Management/"
depends_on:
  - "[[commercial-building]]"
depended_on_by: []
---
```

Body content sources: this plan + the spec at `docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md`. Pull the architecture description, public API table, and lifecycle from §3 of the spec; data-flow diagram from §4 of the spec; multiplayer matrix from §5.2 of the spec; deferred work from §12 of the spec.

`Sources` section MUST include:
```markdown
- [docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md](../../docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md)
- [docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md](../../docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md)
- [.agent/skills/management-panel/SKILL.md](../../.agent/skills/management-panel/SKILL.md)
```

`Change log`:
```markdown
- 2026-05-07 — Initial documentation of the polymorphic tab system. Foundation only; per-job hiring + universal Storage tab deferred to dedicated phases. — claude
```

- [ ] **Step 5: Update or create `.agent/skills/help-wanted-and-hiring/SKILL.md`**

Check `ls .agent/skills/help-wanted-and-hiring/SKILL.md`. If it exists, edit to:
- Replace any reference to `UI_OwnerHiringPanel` with `UI_OwnerManagementPanel.Show(building) → HiringTab`.
- Add a "Sign editing removed from panel — see help-wanted-and-hiring wiki Open questions for the future sign-furniture rework" note.
- Bump the version / last-updated date if the file uses one.

If it doesn't exist, skip.

- [ ] **Step 6: Create `.agent/skills/management-panel/SKILL.md`**

Procedural how-to for adding a new tab to a CommercialBuilding subtype. Mirror the format of an existing skill file (e.g. `.agent/skills/help-wanted-and-hiring/SKILL.md` if present, otherwise `.agent/skills/skill-creator/SKILL.md`).

Required content:

```markdown
---
name: Management Panel — Adding a New Tab
description: How to add an owner-only admin tab to a CommercialBuilding subtype.
when_to_use: When implementing a new feature that needs an owner-facing management UI on a building (e.g., a recipe editor for CraftingBuilding, a crop-plan editor for FarmingBuilding, etc.).
---

# Adding a New Management Tab

## Quick steps

1. Create a `MyFeatureTab : IManagementTab` (plain C# class) in your feature's UI folder.
   - Constructor takes the `CommercialBuilding` reference.
   - `Name` returns the header pill label.
   - `CreateView()` does `Resources.Load<MyFeatureTabView>` + `Instantiate` + `view.Bind(_building)`.

2. Create a `MyFeatureTabView : MonoBehaviour, IManagementTabView` MonoBehaviour.
   - `Root => gameObject`.
   - `Bind(...)` subscribes to events and stores the building reference.
   - `Dispose()` MUST unsubscribe (rule #16) and `Destroy(gameObject)`.
   - All ServerRpcs go through the building's existing API. NEVER add a panel-side RPC.

3. Build a `Resources/UI/Management/MyFeatureTab.prefab` with the controls + a `MyFeatureTabView` component on the root.

4. In your subtype building, override `GetManagementTabs()`:

   ```csharp
   public override IReadOnlyList<IManagementTab> GetManagementTabs()
   {
       var tabs = new List<IManagementTab>(base.GetManagementTabs());
       tabs.Add(new MyFeatureTab(this));
       return tabs;
   }
   ```

   ALWAYS call `base.GetManagementTabs()` first so the Hiring tab is preserved.

## Things to NOT do

- Do NOT modify `UI_OwnerManagementPanel` for your tab. The whole point of the polymorphic design is that the panel never knows your concrete tab type.
- Do NOT add owner-gate logic in the tab — the panel's defense-in-depth gate + the call sites' authoritative gate already cover it.
- Do NOT cache `Character` references across tab activations. Re-resolve via `NetworkManager.LocalClient.PlayerObject` on each user-driven action.

## Sources

- [wiki/systems/management-panel-architecture.md](../../wiki/systems/management-panel-architecture.md)
- [docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md](../../docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md)
```

- [ ] **Step 7: Verify (no Unity refresh needed — wiki + skill files are not Unity assets)**

Run `git status`. Expected: 4-5 wiki/skill file changes staged.

- [ ] **Step 8: Commit**

```bash
git add wiki/systems/help-wanted-and-hiring.md wiki/systems/commercial-building.md wiki/systems/management-panel-architecture.md .agent/skills/management-panel/SKILL.md
# Add the help-wanted-and-hiring skill file ONLY if it was modified in step 5:
# git add .agent/skills/help-wanted-and-hiring/SKILL.md
git commit -m "$(cat <<'EOF'
docs(wiki+skill): management-panel polymorphic tabs

Updates per CLAUDE.md rules #28 (skill) + #29b (wiki).
- New page wiki/systems/management-panel-architecture.md.
- Updated help-wanted-and-hiring + commercial-building wiki pages.
- New .agent/skills/management-panel/SKILL.md procedural how-to.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review (run by the executor before declaring done)

- [ ] **All 13 tasks committed?** Run `git log --oneline -20` — expected: tasks 1-11 + 13 are commits (12 may be a no-commit verification step). Each commit message starts with the conventional-commits prefix from this plan.

- [ ] **`UI_OwnerHiringPanel` is fully gone?** Run Grep for `UI_OwnerHiringPanel` across the entire repo (excluding `docs/`, `wiki/`, `.agent/skills/`, and the plan/spec files which legitimately reference it historically). Expected: zero hits in `Assets/`.

- [ ] **No compile errors?** Run Unity MCP `console-get-logs` after the final commit. Expected: no errors.

- [ ] **All scenarios in Task 12 PASS?** Re-confirm.

- [ ] **Wiki + SKILL changes consistent with code?** Quick eyeball — does each file reference the actual file paths that exist?

- [ ] **Spec coverage:** quick-scan `2026-05-07-management-panel-tab-architecture-design.md`:
  - §2 Architecture — covered by Tasks 1-6.
  - §3 Components — covered by Tasks 1-6.
  - §4 Data flow + Lifecycle — implemented by Task 6 (panel) + Task 3 (HiringTabView).
  - §5 Network rules + multiplayer matrix — verified by Task 12.
  - §6 Error handling — implemented in Tasks 3, 4, 6.
  - §7 Performance — caches none required for now (per spec).
  - §8 Testing — manual matrix done in Task 12; EditMode tests deferred per spec.
  - §9 Backward compat — Tasks 9-11 atomic rewire+deletion.
  - §10 Wiki + SKILL update plan — covered by Task 13.

- [ ] **Phase 2b parallel session unaffected?** Quick-scan their committed files (ShopBuilding.cs, ShopBuildingNetSync.cs, Cashier.cs, etc.) — confirm we did not modify any of them. Confirm `ShopBuilding.GetManagementTabs()` is NOT overridden by us (only the base virtual is added). Phase 2b will land their override on top.

If any check fails, surface to the user before declaring done.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task with two-stage review between tasks. Best for keeping the main context lean across 13 tasks.

**2. Inline Execution** — Execute tasks in this session using executing-plans. Batch execution with checkpoints for user review.

Which approach?
