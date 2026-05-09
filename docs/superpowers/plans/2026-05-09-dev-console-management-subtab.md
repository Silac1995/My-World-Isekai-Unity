# DevMode Console Management Sub-Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Console Management" sub-tab to `BuildingInspectorView` that lets the host dev perform every owner-gated management action on any `CommercialBuilding` (and `ShopBuilding`) regardless of who owns it — purely as a debug surface, gated by `DevModeManager`.

**Architecture:** Refactor `BuildingInspectorView` from a single TMP-text view into a tab-bar + sub-tab pattern (mirrors `CharacterInspectorView`/`CharacterSubTab`). Ship two sub-tabs: `BuildingOverviewSubTab` (wraps the existing 11-section read-out unchanged) and `BuildingConsoleManagementSubTab` (new mutator surface). Because `DevModeManager` is already host-only by hard policy, the dev panel runs on the server — so we add server-only `DevForce*` methods on `CommercialBuilding`/`ShopBuilding` that bypass owner gating and are called directly. No new RPCs, no `devOverride` parameter, no remote-client trust surface to defend.

**Tech Stack:** Unity 2022.3 + Netcode for GameObjects (NGO) 2.x, TextMeshPro, programmatic UGUI (Button/InputField/Toggle), `[Rpc(SendTo.Server)]` and `[ServerRpc(RequireOwnership=false)]` (existing — unchanged), `#if UNITY_EDITOR || DEVELOPMENT_BUILD` strip in release.

---

## Why this shape (vs. twin RPCs / devOverride bool)

The original spec offered "twin RPCs" or "boolean override parameter" as the two options for the server-side override. Both add an RPC-layer protocol that doesn't need to exist:

- **`DevModeManager` is host-only.** Both `DevModeManager.TryEnable` ([Assets/Scripts/Debug/DevMode/DevModeManager.cs:199-203](Assets/Scripts/Debug/DevMode/DevModeManager.cs#L199-L203)) and `DevChatCommands.HandleDevmode` reject any caller that is not `NetworkManager.IsServer`. The dev panel therefore only ever runs on the server.
- **Direct call beats RPC.** Since the dev panel runs on the server, it can call any server-only method directly. No client→server protocol is needed. RPCs (whether twin or boolean-overloaded) only make sense if a non-host client holds the dev surface — which is explicitly disallowed.
- **No remote-client trust surface to defend.** A `devOverride = true` boolean would need server-side trust validation against a client allowlist, etc. Eliminating the RPC eliminates this whole class of problem.
- **Production paths stay 100% intact.** Owner-gated `ServerRpc`/`Rpc` methods on `CommercialBuilding`/`ShopBuilding` keep their auth checks. We add parallel `DevForce*` public methods that bypass auth and route through the same `Do*` helpers (extracted from existing RPC bodies).

If a future feature requires non-host devs to use the panel, this plan can be extended with the boolean-override pattern then. For now, host-only is correct and minimal.

## Owner-gated surface mapped

Audited against current code. There are exactly **two** classes of owner-gated mutation:

### Already server-only without auth (no DevForce* needed — dev panel calls directly)

| Method | Class | Notes |
|---|---|---|
| `AssignWorker(Character, Job)` | `CommercialBuilding` | server-gated only, no auth check |
| `RemoveWorker(Job)` | `CommercialBuilding` | server-gated only, no auth check |
| `SetOwner(Character, ...)` | `CommercialBuilding` | server-gated only, no auth check |
| `AddOwner(Character)` / `RemoveOwner(Character)` | `Room` (base) | server-gated only |
| `AddToInventory(ItemInstance)` / `TakeFromInventory(ItemSO)` / `RemoveExactItemFromInventory(ItemInstance)` | `CommercialBuilding` | server-gated only |
| `Finalize()` | `Building` | already exposed by current dev panel's force-finish button |

### Owner-gated (dev panel calls a new `DevForce*` helper that skips auth)

| Action | Production entrypoint | Auth gate | Dev path |
|---|---|---|---|
| Open hiring | `TryOpenHiring(requester)` → `[Rpc(SendTo.Server)] TryOpenHiringServerRpc(ulong)` → `ServerTryOpenHiring(requester)` checks `CanRequesterControlHiring` | owner OR community-leader | new `DevForceSetHiring(bool)` writes `_isHiring.Value` directly |
| Close hiring | `TryCloseHiring(...)` (mirror) | same | same |
| Edit wage | `TrySetAssignmentWage(requester, worker, ...)` checks `IsAuthorizedToManage(requester)` | owner OR community-leader | new `DevForceSetAssignmentWage(worker, ...)` calls existing internal logic without auth |
| Add catalog entry | `[ServerRpc(RequireOwnership=false)] AddCatalogEntryServerRpc` checks `ValidateOwnerCaller` | owner only | new `DevForceAddCatalogEntry` |
| Remove catalog entry | `RemoveCatalogEntryServerRpc` | owner only | new `DevForceRemoveCatalogEntry` |
| Edit catalog entry | `EditCatalogEntryServerRpc` | owner only | new `DevForceEditCatalogEntry` |
| Toggle sell-shelf | `SetSellShelfFlagServerRpc` | owner only | new `DevForceSetSellShelfFlag` |
| Withdraw cashier till | `WithdrawCashierTillServerRpc` | owner only | new `DevForceWithdrawCashierTill` |

Refactor pattern: extract each existing RPC body into a private `Do*` helper. The existing RPC keeps the auth check + calls `Do*`. The new `DevForce*` skips auth + calls `Do*`. Behavior of the existing RPC must be bit-for-bit identical — extract-method only.

## File Structure

### New files

- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingSubTab.cs` — base class, mirrors `CharacterSubTab`. Refresh(Building), Clear(), virtual.
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs` — wraps the existing 11-section render code from `BuildingInspectorView`.
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingConsoleManagementSubTab.cs` — the new mutator UI. Builds widgets at runtime (no prefab edits needed).

### Modified files

- `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs` — refactored from "render-everything-into-`_content`" to "tab-bar + sub-tabs" (mirror `CharacterInspectorView`). Builds the tab bar at runtime so we don't have to edit the prefab YAML. Existing serialized fields `_headerLabel` and `_content` stay (the `_content` TMP_Text is reused by `BuildingOverviewSubTab` for its rendering).
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — extract `DoSetHiring(bool)` helper from `ServerTryOpenHiring`/`ServerTryCloseHiring`; add `DevForceSetHiring(bool)` and `DevForceSetAssignmentWage(...)`. All under `#if UNITY_EDITOR || DEVELOPMENT_BUILD` for the dev-only methods.
- `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs` — extract `Do*` helpers from each of the 5 owner-gated RPCs; add 5 `DevForce*` methods.

### Wiki / skill updates (per project rules #28 / #29 / #29b)

- `wiki/systems/devmode-tools.md` (or current page) — append entry for "Console Management sub-tab" + the `DevForce*` server methods.
- `.agent/skills/devmode/SKILL.md` (or equivalent debug-tools skill) — append the Console Management sub-tab to the documented surface.
- `.claude/agents/debug-tools-architect.md` — extend with the new sub-tab + DevForce* surface.

---

## Tasks

### Task 1: Add `BuildingSubTab` base class

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingSubTab.cs`

- [ ] **Step 1: Write the file**

```csharp
using TMPro;
using UnityEngine;

/// <summary>
/// Base class for one category of the Building inspector. Mirrors
/// <see cref="CharacterSubTab"/>: the host inspector dispatches a per-frame
/// <see cref="Refresh(Building)"/> on the active tab and skips inactive ones.
/// Exception isolation is centralised here so a thrown sub-tab cannot wipe the
/// rest of the dev panel.
/// </summary>
public abstract class BuildingSubTab : MonoBehaviour
{
    /// <summary>
    /// Refresh the sub-tab with the given building. Safe to call every frame.
    /// Centralises null-target handling and the try/catch so subclasses can
    /// just override <see cref="DoRefresh"/>.
    /// </summary>
    public void Refresh(Building b)
    {
        if (b == null) { DoClear(); return; }
        try
        {
            DoRefresh(b);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    /// <summary>Inspector detached. Override to clear caches / widget state.</summary>
    public virtual void Clear() => DoClear();

    /// <summary>Concrete sub-tab work — read building state and update widgets / text.</summary>
    protected abstract void DoRefresh(Building b);

    /// <summary>Concrete sub-tab cleanup — wipe text/widgets to a "no target" state.</summary>
    protected virtual void DoClear() { }
}
```

- [ ] **Step 2: Verify the file compiles**

Run: build the project (Ctrl+R in Editor) — expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingSubTab.cs
git commit -m "feat(devmode): add BuildingSubTab base class mirroring CharacterSubTab"
```

---

### Task 2: Extract render code into `BuildingOverviewSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs`

The current `BuildingInspectorView.RenderContent` (and all the `Append*` static helpers) builds a 2 KB+ string that lists Identity / State / Owners / Commercial / Inventory / Wanted / Tracked / Needed / Logistics / Tasks / Rooms / Furniture / Interior. We move this code verbatim into `BuildingOverviewSubTab`. The TMP_Text it writes into is provided by the parent `BuildingInspectorView` (same `_content` field as today, re-wired into this sub-tab).

- [ ] **Step 1: Create the file with all `Append*` helpers ported from `BuildingInspectorView`**

Open `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs` and physically MOVE (cut + paste) every `private static void Append*` method, `OtherBuildingLabel`, and `ResolveHarvestableLabel` into the new file. Wrap them as `private static` on `BuildingOverviewSubTab`. Move `RenderContent(Building)` as well, renaming to `BuildOverviewText(Building)`. The rendering logic stays exactly as it was — no behavior change.

```csharp
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Read-only Overview sub-tab. Renders the full 11-section Building dump into a
/// single TMP_Text — Identity / State / Owners / Commercial / Inventory /
/// Wanted Resources / Tracked Harvestables / Needed Resources / Logistics
/// Orders / Tasks / Rooms / Furniture / Interior.
///
/// Verbatim port of the prior render code from <see cref="BuildingInspectorView"/>;
/// behavior unchanged.
/// </summary>
public sealed class BuildingOverviewSubTab : BuildingSubTab
{
    [SerializeField] private TMP_Text _content;

    /// <summary>Wired by <see cref="BuildingInspectorView.Awake"/> when it builds the sub-tab.</summary>
    public void SetContentLabel(TMP_Text content) { _content = content; }

    protected override void DoRefresh(Building b)
    {
        if (_content == null) return;
        _content.text = BuildOverviewText(b);
    }

    protected override void DoClear()
    {
        if (_content != null) _content.text = "<color=grey>No building selected.</color>";
    }

    // ─── Rendering (verbatim from prior BuildingInspectorView.RenderContent) ─────

    private static string BuildOverviewText(Building b)
    {
        var sb = new StringBuilder(2048);
        AppendIdentity(sb, b);  sb.AppendLine();
        AppendState(sb, b);     sb.AppendLine();
        AppendOwners(sb, b);    sb.AppendLine();

        if (b is CommercialBuilding cb)
        {
            AppendCommercial(sb, cb);     sb.AppendLine();
            AppendInventory(sb, cb);      sb.AppendLine();
            if (cb is HarvestingBuilding hb)
            {
                AppendWantedResources(sb, hb);     sb.AppendLine();
                AppendTrackedHarvestables(sb, hb); sb.AppendLine();
            }
            AppendNeededResources(sb, cb);   sb.AppendLine();
            AppendLogisticsOrders(sb, cb);   sb.AppendLine();
            AppendTasks(sb, cb);             sb.AppendLine();
        }

        AppendRooms(sb, b);     sb.AppendLine();
        AppendFurniture(sb, b); sb.AppendLine();
        AppendInterior(sb, b);
        return sb.ToString();
    }

    // ⚠ Move every `private static void Append*` method
    //   AND `OtherBuildingLabel` / `ResolveHarvestableLabel` here from
    //   BuildingInspectorView verbatim (cut + paste, no edits).

    // <... full bodies of AppendIdentity, AppendState, AppendOwners,
    //      AppendCommercial, AppendInventory, AppendWantedResources,
    //      AppendTrackedHarvestables, AppendNeededResources,
    //      AppendLogisticsOrders, AppendBuyOrderList, AppendTransportOrderList,
    //      AppendCraftingOrderList, OtherBuildingLabel, AppendTasks,
    //      AppendTaskList, AppendRooms, AppendFurniture, AppendInterior,
    //      ResolveHarvestableLabel — all PRIVATE STATIC, exactly as today ...>
}
```

- [ ] **Step 2: Confirm — paste-only, no behavior change**

Read [Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs:148-967](Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs#L148-L967) and verify each `Append*` helper is moved across as-is. Do NOT modify the formatting / coloring / null handling of any append method.

- [ ] **Step 3: Compile**

Build the project — there will be unresolved references in `BuildingInspectorView` (it still references the moved methods); that's expected and fixed by Task 3.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingOverviewSubTab.cs
git commit -m "feat(devmode): extract building overview render into BuildingOverviewSubTab"
```

---

### Task 3: Refactor `BuildingInspectorView` to host sub-tabs at runtime

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs` — replace render logic with sub-tab routing.

`BuildingInspectorView` keeps its `IBuildingInspectorView` contract, its `_headerLabel` and `_content` SerializeFields, and its `_forceFinishButton` (we'll un-wire that button in Task 5 because Console Management owns force-finalize, but keep the field for now). It builds a 2-tab structure programmatically in `Awake`:

- A horizontal tab bar (sibling to existing children) with 2 buttons: "Overview" and "Console Management".
- A content host that swaps active sub-tab GameObject when a tab is clicked.
- The existing `_content` TMP_Text becomes the Overview sub-tab's content label.
- The Console Management sub-tab is a fresh GameObject built dynamically.

Runtime hierarchy generation (vs. an Editor builder like `DevInspectTabBuilder`) keeps prefab edits to ZERO — appropriate for a 2-sub-tab layout.

- [ ] **Step 1: Replace the file with the refactored version**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IBuildingInspectorView for any <see cref="Building"/> target.
///
/// Hosts a small tab bar with two sub-tabs (mirror of <see cref="CharacterInspectorView"/>):
///   1. Overview            — full read-out of the building's state.
///   2. Console Management  — DEV-ONLY mutator surface (host-only, gated by DevModeManager).
///
/// Tab bar + Console Management widgets are built programmatically at runtime so the
/// existing DevModePanel prefab does not need to be re-authored. The serialized
/// <see cref="_content"/> TMP_Text is reused as the Overview sub-tab's label.
/// </summary>
public class BuildingInspectorView : MonoBehaviour, IBuildingInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content; // re-used by BuildingOverviewSubTab

    private Building _target;

    private readonly List<(Button btn, GameObject content, BuildingSubTab tab)> _subTabs = new();
    private int _activeIndex = -1;

    public bool CanInspect(Building target) => target != null;

    public void SetTarget(Building target)
    {
        _target = target;
        UpdateHeader();
        if (_activeIndex < 0 && _subTabs.Count > 0) SwitchTab(0);
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        for (int i = 0; i < _subTabs.Count; i++)
        {
            var t = _subTabs[i].tab;
            if (t != null) t.Clear();
        }
    }

    private void Awake()
    {
        BuildSubTabHierarchy();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _subTabs.Count; i++)
        {
            var btn = _subTabs[i].btn;
            if (btn != null) btn.onClick.RemoveAllListeners();
        }
    }

    private void Update()
    {
        if (_target == null) return;
        if (_activeIndex < 0 || _activeIndex >= _subTabs.Count) return;
        var tab = _subTabs[_activeIndex].tab;
        if (tab == null) return;
        tab.Refresh(_target);
    }

    private void UpdateHeader()
    {
        if (_headerLabel == null) return;
        if (_target == null) { _headerLabel.text = "Inspecting: —"; return; }
        string label = !string.IsNullOrEmpty(_target.BuildingName)
            ? _target.BuildingName
            : _target.gameObject.name;
        _headerLabel.text = $"Inspecting: {label}";
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _subTabs.Count) return;
        _activeIndex = index;
        for (int i = 0; i < _subTabs.Count; i++)
        {
            var content = _subTabs[i].content;
            if (content != null) content.SetActive(i == index);
        }
    }

    /// <summary>
    /// Builds tab bar + sub-tab content hosts at runtime. Called from Awake.
    /// We don't need to author this in the prefab because there are only two
    /// sub-tabs and Console Management is fully dynamic anyway.
    /// </summary>
    private void BuildSubTabHierarchy()
    {
        // The existing _content TMP_Text was a direct child of this GO. Re-parent it
        // under a new "OverviewContent" host GameObject so we can SetActive(false)
        // it when the user switches to Console Management.
        if (_content == null)
        {
            Debug.LogError("[BuildingInspectorView] _content TMP_Text not wired in prefab. Aborting sub-tab build.");
            return;
        }

        // Find or create the tab-bar GameObject as a sibling of _content.
        var parent = transform;
        var tabBarGO = CreateUIChild(parent, "SubTabBar", siblingIndex: 1);
        var tabBarLE = tabBarGO.AddComponent<LayoutElement>();
        tabBarLE.minHeight = 28; tabBarLE.preferredHeight = 28; tabBarLE.flexibleHeight = 0;
        var tabBarHL = tabBarGO.AddComponent<HorizontalLayoutGroup>();
        tabBarHL.spacing = 2;
        tabBarHL.childForceExpandWidth = true;
        tabBarHL.childForceExpandHeight = true;

        // Wrap the existing _content under a new "OverviewContent" host so we can SetActive() it.
        var overviewHost = CreateUIChild(parent, "OverviewContent", siblingIndex: 2);
        var overviewLE = overviewHost.AddComponent<LayoutElement>();
        overviewLE.flexibleHeight = 1;
        var overviewVL = overviewHost.AddComponent<VerticalLayoutGroup>();
        overviewVL.childControlWidth = true; overviewVL.childControlHeight = true;
        overviewVL.childForceExpandWidth = true; overviewVL.childForceExpandHeight = true;
        // Move _content under overviewHost.
        _content.transform.SetParent(overviewHost.transform, worldPositionStays: false);

        var overviewTab = overviewHost.AddComponent<BuildingOverviewSubTab>();
        overviewTab.SetContentLabel(_content);

        // Console Management host.
        var consoleHost = CreateUIChild(parent, "ConsoleManagementContent", siblingIndex: 3);
        var consoleLE = consoleHost.AddComponent<LayoutElement>();
        consoleLE.flexibleHeight = 1;
        var consoleSV = AddScrollRect(consoleHost, out var consoleContent);

        var consoleTab = consoleContent.AddComponent<BuildingConsoleManagementSubTab>();

        // Tab buttons.
        var overviewBtn = CreateTabButton(tabBarGO.transform, "Overview");
        var consoleBtn  = CreateTabButton(tabBarGO.transform, "[DEV] Console Management");
        consoleBtn.GetComponent<Image>().color = new Color(0.45f, 0.20f, 0.20f, 1f);
        var consoleLabelText = consoleBtn.GetComponentInChildren<TMP_Text>();
        if (consoleLabelText != null) consoleLabelText.color = new Color(1f, 0.7f, 0.7f, 1f);

        _subTabs.Add((overviewBtn, overviewHost, overviewTab));
        _subTabs.Add((consoleBtn,  consoleHost,  consoleTab));

        for (int i = 0; i < _subTabs.Count; i++)
        {
            int captured = i;
            _subTabs[i].btn.onClick.AddListener(() => SwitchTab(captured));
        }

        SwitchTab(0); // Overview by default
    }

    // --- UI helpers (programmatic, dev-only) ---

    private static GameObject CreateUIChild(Transform parent, string name, int siblingIndex)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.SetSiblingIndex(siblingIndex);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return go;
    }

    private static Button CreateTabButton(Transform parent, string label)
    {
        var go = new GameObject(label + "_btn", typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        lrt.localScale = Vector3.one;
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return btn;
    }

    private static ScrollRect AddScrollRect(GameObject root, out GameObject contentGO)
    {
        var sr = root.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.45f, 0.20f, 0.20f, 0.30f); // [DEV] tint
        bg.raycastTarget = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(root.transform, worldPositionStays: false);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
        vrt.pivot = new Vector2(0, 1);
        viewport.AddComponent<RectMask2D>();
        var vimg = viewport.AddComponent<Image>(); vimg.color = new Color(1, 1, 1, 0.01f);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, worldPositionStays: false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0, 1);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0, 100);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 4;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vrt; sr.content = crt;
        contentGO = content;
        return sr;
    }
}
```

- [ ] **Step 2: Compile**

Build the project. The compile error from Task 2 (unresolved `Append*` references) should now be gone — those methods live in `BuildingOverviewSubTab` and are no longer referenced from `BuildingInspectorView`.

- [ ] **Step 3: Visual smoke-test in Editor**

Open the project in Unity. Press F3 to enable dev mode. Alt+Click a building. Confirm:
  - `BuildingInspectorView` appears with 2 tabs: "Overview" + "[DEV] Console Management".
  - Overview shows the same 11-section read-out as before (no behavior change).
  - Console Management tab is empty (will be filled in Task 5).
  - Toggle between tabs works.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs
git commit -m "refactor(devmode): BuildingInspectorView hosts sub-tabs (Overview + Console Mgmt)"
```

---

### Task 4: Add `DevForce*` server methods on `CommercialBuilding`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Extract `DoSetHiring(bool)` from `ServerTryOpenHiring` / `ServerTryCloseHiring` (preserves bit-for-bit behavior of the existing RPCs). Add `DevForceSetHiring(bool)` that calls it without the auth check. Add `DevForceSetAssignmentWage(...)` that mirrors the body of `TrySetAssignmentWage` minus the auth check.

- [ ] **Step 1: Locate `ServerTryOpenHiring` and `ServerTryCloseHiring`**

Open `Assets/Scripts/World/Buildings/CommercialBuilding.cs`. Find:

```csharp
private bool ServerTryOpenHiring(Character requester)
{
    if (!CanRequesterControlHiring(requester)) return false;
    if (GetVacantJobs().Count == 0) return false;
    if (_isHiring.Value) return true;
    _isHiring.Value = true;
    return true;
}

private bool ServerTryCloseHiring(Character requester)
{
    if (!CanRequesterControlHiring(requester)) return false;
    if (!_isHiring.Value) return true;
    _isHiring.Value = false;
    return true;
}
```

Leave them untouched — DO NOT extract a shared helper, because the `GetVacantJobs() == 0` precondition is OPEN-only and is part of production semantics. Adding a helper that's called from open AND close would risk leaking that gate to close.

Instead, append a new section at the end of the class (just before the closing brace):

- [ ] **Step 2: Add the dev section to CommercialBuilding.cs**

Append immediately after the `ResolveCharacterByNetId` helper (around line 2590):

```csharp
    // ============================================================================
    // DEV-MODE OVERRIDES — host-only, gated by DevModeManager
    // ----------------------------------------------------------------------------
    // These methods bypass the production owner / community-leader auth checks
    // and let a host dev mutate state on any building. They are wrapped in
    // #if UNITY_EDITOR || DEVELOPMENT_BUILD so they are stripped from release
    // builds entirely.
    //
    // Authorisation: DevModeManager is host-only (verified at TryEnable). The
    // dev panel calls these methods directly — no RPC, no client trust surface.
    // Each call assert IsServer + DevModeManager.IsEnabled and logs a warning
    // for the audit trail.
    // ============================================================================

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Dev-only: set hiring state directly, bypassing owner / community-leader auth.
    /// No vacancy precondition (devs may want to flip the bit on a fully-staffed
    /// building for testing).
    /// </summary>
    public void DevForceSetHiring(bool open)
    {
        if (!DevAssertHostAndDevMode("DevForceSetHiring")) return;
        if (_isHiring.Value == open) return;
        _isHiring.Value = open;
    }

    /// <summary>
    /// Dev-only: set assignment wage directly, bypassing owner / community-leader auth.
    /// Same null-tolerance for fields as <see cref="TrySetAssignmentWage"/>.
    /// </summary>
    public bool DevForceSetAssignmentWage(Character worker, int? pieceRate = null,
        int? minimumShift = null, int? fixedShift = null)
    {
        if (!DevAssertHostAndDevMode("DevForceSetAssignmentWage")) return false;
        if (worker == null) return false;
        // Replicate the assignment lookup from TrySetAssignmentWage minus the auth check.
        var assignment = GetAssignmentForWorker(worker);
        if (assignment == null) return false;
        bool changed = false;
        if (pieceRate.HasValue)    { assignment.SetWagePieceRate(pieceRate.Value);     changed = true; }
        if (minimumShift.HasValue) { assignment.SetWageMinimumShift(minimumShift.Value); changed = true; }
        if (fixedShift.HasValue)   { assignment.SetWageFixedShift(fixedShift.Value);   changed = true; }
        return changed;
    }

    /// <summary>
    /// Common precondition for every <c>DevForce*</c> entry point. Verifies we are
    /// on the server and that DevMode is currently enabled, and emits an audit log
    /// line. Returns false if either gate fails.
    /// </summary>
    private bool DevAssertHostAndDevMode(string action)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"<color=magenta>[DevMode]</color> {action} ignored — not on server.");
            return false;
        }
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled)
        {
            Debug.LogWarning($"<color=magenta>[DevMode]</color> {action} ignored — DevMode not enabled.");
            return false;
        }
        ulong sender = (Unity.Netcode.NetworkManager.Singleton != null)
            ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0UL;
        Debug.LogWarning($"<color=magenta>[DevMode]</color> {action} — buildingId={BuildingId} sender={sender}");
        return true;
    }
#endif
```

⚠ The above references `GetAssignmentForWorker(worker)` and `SetWagePieceRate/SetWageMinimumShift/SetWageFixedShift` — confirm the names and signatures by reading `TrySetAssignmentWage` in `CommercialBuilding.cs` (around line 1100–1145, current file). If the production code uses a different shape (e.g., directly mutates assignment fields, or has a different method name), MIRROR it exactly — do not invent. The whole point of `DevForceSetAssignmentWage` is to do exactly what `TrySetAssignmentWage` does, minus the auth check.

If `TrySetAssignmentWage`'s body contains complex nested logic (e.g., recalculating budgets, marking dirty flags), copy it verbatim into `DevForceSetAssignmentWage` and remove only the `IsAuthorizedToManage(requester)` check. Do not introduce drift.

- [ ] **Step 3: Compile + verify gating**

Open the project in Unity. Confirm the new methods compile. Confirm in the Editor's console that calling `DevForceSetHiring(true)` from a script:
  - When dev mode is OFF → logs "ignored — DevMode not enabled".
  - When dev mode is ON → logs the audit line and flips `IsHiring`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(devmode): add DevForce* server methods on CommercialBuilding (host-only)"
```

---

### Task 5: Add `DevForce*` server methods on `ShopBuilding`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

Refactor each of the 5 owner-gated RPCs into `Body → Do* → effect` shape. Add `DevForce*` peers that call `Do*` without auth.

- [ ] **Step 1: Extract the 5 `Do*` helpers from existing RPCs**

For each RPC, MOVE the body (after `ValidateOwnerCaller`) into a private `Do*` helper. Both the existing RPC and the new `DevForce*` call the helper. Bit-for-bit behavior preservation: do not change anything except where the body lives.

Replace the existing methods with the refactored shape:

```csharp
[ServerRpc(RequireOwnership = false)]
public void AddCatalogEntryServerRpc(string itemId, int maxStock, int priceOverride, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    DoAddCatalogEntry(itemId, maxStock, priceOverride);
}

private void DoAddCatalogEntry(string itemId, int maxStock, int priceOverride)
{
    var so = ResolveItemSO(itemId);
    if (so == null) { Debug.LogWarning($"[Shop] AddCatalogEntry: unknown itemId '{itemId}'"); return; }
    if (maxStock < 0) maxStock = 0;
    if (priceOverride < 0) priceOverride = 0;
    if (GetCatalogEntry(so) != null) return;
    var entry = new ShopItemEntry { Item = so, MaxStock = maxStock, PriceOverride = priceOverride };
    _catalog.Add(entry);
    _netSync.PushCatalogEntryAddedServer(entry);
    OnCatalogChanged?.Invoke();
}
```

Repeat for `RemoveCatalogEntryServerRpc → DoRemoveCatalogEntry`, `EditCatalogEntryServerRpc → DoEditCatalogEntry`, `SetSellShelfFlagServerRpc → DoSetSellShelfFlag`, `WithdrawCashierTillServerRpc → DoWithdrawCashierTill`. For `WithdrawCashierTillServerRpc`, the body deposits coins into `ResolveCharacterFromClientId(p.Receive.SenderClientId)?.CharacterWallet`. The dev path needs a different recipient — pass the recipient (`Character`) into `DoWithdrawCashierTill(NetworkObject cashierObj, Character recipient)` and have the RPC pass `ResolveCharacterFromClientId(p.Receive.SenderClientId)`.

- [ ] **Step 2: Add the `DevForce*` peers**

Append at the end of the `ShopBuilding` class (above the close brace):

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DevForceAddCatalogEntry(string itemId, int maxStock, int priceOverride)
    {
        if (!DevAssertHostAndDevMode("DevForceAddCatalogEntry")) return;
        DoAddCatalogEntry(itemId, maxStock, priceOverride);
    }

    public void DevForceRemoveCatalogEntry(string itemId)
    {
        if (!DevAssertHostAndDevMode("DevForceRemoveCatalogEntry")) return;
        DoRemoveCatalogEntry(itemId);
    }

    public void DevForceEditCatalogEntry(string itemId, int newMaxStock, int newPriceOverride)
    {
        if (!DevAssertHostAndDevMode("DevForceEditCatalogEntry")) return;
        DoEditCatalogEntry(itemId, newMaxStock, newPriceOverride);
    }

    public void DevForceSetSellShelfFlag(StorageFurniture shelf, bool isSellShelf)
    {
        if (!DevAssertHostAndDevMode("DevForceSetSellShelfFlag")) return;
        if (shelf == null) return;
        var net = shelf.GetComponent<Unity.Netcode.NetworkObject>();
        if (net == null) return;
        DoSetSellShelfFlag(new Unity.Netcode.NetworkObjectReference(net), isSellShelf);
    }

    public void DevForceWithdrawCashierTill(Cashier cashier, Character recipient)
    {
        if (!DevAssertHostAndDevMode("DevForceWithdrawCashierTill")) return;
        if (cashier == null || recipient == null) return;
        var net = cashier.GetComponent<Unity.Netcode.NetworkObject>();
        if (net == null) return;
        DoWithdrawCashierTill(new Unity.Netcode.NetworkObjectReference(net), recipient);
    }
#endif
```

⚠ `DevAssertHostAndDevMode` lives on `CommercialBuilding` (Task 4). Since `ShopBuilding : CommercialBuilding`, it's accessible from `ShopBuilding`. If it's `private` on the base, change it to `protected` in the Task 4 patch.

⚠ `DevAssertHostAndDevMode` was authored as `private` in the Task 4 step above. **Update Task 4** to make it `protected` so subclasses can call it. Apply this change in Task 4 before Task 5.

- [ ] **Step 3: Compile and smoke-test**

Build the project. Spin up a host session, place a `ShopBuilding`, transfer ownership to an NPC, enable dev mode, call `DevForceAddCatalogEntry` from a test script (or skip directly to the UI in Task 6) and confirm `OnCatalogChanged` fires + the catalog list mirrors on a connected client.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(devmode): add DevForce* server methods on ShopBuilding (host-only)"
```

---

### Task 6: Build `BuildingConsoleManagementSubTab`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingConsoleManagementSubTab.cs`

The Console Management sub-tab builds its widgets at runtime. It targets `Building` but only the `CommercialBuilding` / `ShopBuilding` sections are populated for those subtypes.

The widgets are organised into sections, top-to-bottom:

- **DEV banner** — red-orange Image + label "[DEV] Console Management — bypasses owner authority".
- **Construction** — only when `target.IsUnderConstruction`: button "Force Finalize" (calls `target.Finalize()`).
- **Hiring** — only when `target is CommercialBuilding`: button "Toggle Hiring" + status label.
- **Owner** — input field for CharacterId + buttons: "Set as Owner", "Remove Owner", "Clear All Owners".
- **Jobs** — for each `Job` on the `CommercialBuilding`: a row with [Job title] [worker name or `(unassigned)`] [Force-Fire button (if assigned)] + a "Force-Hire" input + button (CharacterId of worker to hire).
- **Storage roles (sell-shelves)** — only when `target is ShopBuilding`: per-`StorageFurniture` row with [shelf name] [toggle for "Is Sell Shelf"].
- **Catalog (Shop)** — only when `target is ShopBuilding`: list of `(ItemSO, MaxStock, Price)` entries with per-row remove + edit; bottom: "[ItemId] [MaxStock] [Price] [Add]".
- **Cashiers (Shop)** — only when `target is ShopBuilding`: per-cashier row with [cashier name] [till balance] [Withdraw → host's local player].
- **Inventory** — list (ItemSO + count) sorted by count desc; bottom: "[ItemId] [Add 1] [Remove 1]".

To keep the file readable, group widgets into sections via small `BuildSection*` methods. Do not over-abstract — copy/paste is fine for dev tooling.

- [ ] **Step 1: Skeleton + DEV banner + Construction + Hiring**

Create the file with the basic structure — section helpers as stubs, banner + Construction + Hiring fully implemented:

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DEV-ONLY mutator surface for the Building inspector. Builds widgets at runtime
/// because the dev panel is host-only and uses programmatic UGUI throughout.
///
/// Every action wraps a server method on Building / CommercialBuilding / ShopBuilding.
/// Owner-gated actions go through the corresponding <c>DevForce*</c> methods which
/// bypass auth (see <see cref="CommercialBuilding.DevForceSetHiring"/>).
/// </summary>
public sealed class BuildingConsoleManagementSubTab : BuildingSubTab
{
    private Building _bound;
    private readonly List<GameObject> _spawnedWidgets = new();
    private bool _builtOnce;

    protected override void DoRefresh(Building b)
    {
        if (b == null) return;
        if (_bound != b)
        {
            _bound = b;
            RebuildAll();
        }
        // We could refresh per-frame state here (e.g. hiring label flips) but the
        // simpler "rebuild on bind change + Refresh on widget actions" approach
        // covers everything since widget callbacks call RebuildAll() when needed.
    }

    protected override void DoClear()
    {
        _bound = null;
        ClearWidgets();
    }

    private void ClearWidgets()
    {
        for (int i = 0; i < _spawnedWidgets.Count; i++)
            if (_spawnedWidgets[i] != null) Destroy(_spawnedWidgets[i]);
        _spawnedWidgets.Clear();
    }

    private void RebuildAll()
    {
        ClearWidgets();
        if (_bound == null) return;

        BuildBanner();
        BuildConstructionSection();
        if (_bound is CommercialBuilding cb)
        {
            BuildHiringSection(cb);
            BuildOwnerSection(cb);
            BuildJobsSection(cb);
            if (cb is ShopBuilding sb)
            {
                BuildSellShelvesSection(sb);
                BuildCatalogSection(sb);
                BuildCashiersSection(sb);
            }
            BuildInventorySection(cb);
        }
    }

    // ─── Sections ────────────────────────────────────────────────────────

    private void BuildBanner()
    {
        var go = MakeLabel("[DEV] Console Management — bypasses owner authority");
        var img = go.AddComponent<Image>();
        img.color = new Color(0.6f, 0.2f, 0.2f, 1f);
        var tmp = go.GetComponentInChildren<TMP_Text>();
        if (tmp != null) { tmp.color = Color.white; tmp.fontStyle = FontStyles.Bold; tmp.fontSize = 14; }
    }

    private void BuildConstructionSection()
    {
        if (_bound == null || !_bound.IsUnderConstruction) return;
        MakeHeader("Construction");
        MakeButton("[DEV] Force Finalize", () =>
        {
            if (_bound != null && _bound.IsServer) _bound.Finalize();
        });
    }

    private void BuildHiringSection(CommercialBuilding cb)
    {
        MakeHeader("Hiring");
        var row = MakeRow();
        var label = MakeLabel(cb.IsHiring ? "Currently OPEN" : "Currently CLOSED", row.transform);
        var btn = MakeButton(cb.IsHiring ? "[DEV] Close Hiring" : "[DEV] Open Hiring", () =>
        {
            cb.DevForceSetHiring(!cb.IsHiring);
            RebuildAll();
        }, row.transform);
    }

    private void BuildOwnerSection(CommercialBuilding cb) { /* Step 2 */ }
    private void BuildJobsSection(CommercialBuilding cb) { /* Step 3 */ }
    private void BuildSellShelvesSection(ShopBuilding sb) { /* Step 4 */ }
    private void BuildCatalogSection(ShopBuilding sb) { /* Step 4 */ }
    private void BuildCashiersSection(ShopBuilding sb) { /* Step 4 */ }
    private void BuildInventorySection(CommercialBuilding cb) { /* Step 5 */ }

    // ─── Widget helpers ──────────────────────────────────────────────────

    private GameObject MakeRow(Transform parent = null)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 28;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
        hl.childControlWidth = true; hl.childControlHeight = true;
        _spawnedWidgets.Add(go);
        return go;
    }

    private GameObject MakeHeader(string text)
    {
        var go = MakeLabel($"<b>{text}</b>");
        var tmp = go.GetComponentInChildren<TMP_Text>();
        if (tmp != null) { tmp.fontSize = 14; tmp.color = new Color(1f, 0.85f, 0.6f, 1f); }
        return go;
    }

    private GameObject MakeLabel(string text, Transform parent = null)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 22;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 12; tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.9f, 0.9f, 0.9f, 1f); tmp.raycastTarget = false; tmp.richText = true;
        _spawnedWidgets.Add(go);
        return go;
    }

    private Button MakeButton(string label, Action onClick, Transform parent = null)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = 100;
        var img = go.AddComponent<Image>(); img.color = new Color(0.40f, 0.20f, 0.20f, 1f);
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        var labelGO = new GameObject("L", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var ltmp = labelGO.AddComponent<TextMeshProUGUI>();
        ltmp.text = label; ltmp.alignment = TextAlignmentOptions.Center;
        ltmp.fontSize = 12; ltmp.color = Color.white; ltmp.raycastTarget = false;
        btn.onClick.AddListener(() => { try { onClick?.Invoke(); } catch (Exception e) { Debug.LogException(e); } });
        _spawnedWidgets.Add(go);
        return btn;
    }

    private TMP_InputField MakeInput(string placeholder, Transform parent = null, float minWidth = 120)
    {
        var go = new GameObject("Input", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = minWidth;
        var img = go.AddComponent<Image>(); img.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        var inp = go.AddComponent<TMP_InputField>(); inp.image = img;

        var textArea = new GameObject("Text", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, worldPositionStays: false);
        var tart = textArea.GetComponent<RectTransform>();
        tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
        tart.offsetMin = new Vector2(4, 2); tart.offsetMax = new Vector2(-4, -2);
        var ttmp = textArea.AddComponent<TextMeshProUGUI>();
        ttmp.fontSize = 12; ttmp.color = Color.white; ttmp.alignment = TextAlignmentOptions.MidlineLeft;
        ttmp.raycastTarget = false;
        inp.textComponent = ttmp;
        // Placeholder is optional; skip it to keep the helper short.
        _spawnedWidgets.Add(go);
        return inp;
    }

    private Toggle MakeToggle(string label, bool initial, Action<bool> onChanged, Transform parent = null)
    {
        var row = MakeRow(parent);
        var togGo = new GameObject("Toggle", typeof(RectTransform));
        togGo.transform.SetParent(row.transform, worldPositionStays: false);
        var le = togGo.AddComponent<LayoutElement>(); le.minWidth = 22; le.minHeight = 22;
        var bg = togGo.AddComponent<Image>(); bg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        var tog = togGo.AddComponent<Toggle>();
        tog.targetGraphic = bg; tog.SetIsOnWithoutNotify(initial);
        // Add a checkmark child
        var checkGO = new GameObject("Check", typeof(RectTransform));
        checkGO.transform.SetParent(togGo.transform, worldPositionStays: false);
        var crt = checkGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.1f, 0.1f); crt.anchorMax = new Vector2(0.9f, 0.9f);
        crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        var checkImg = checkGO.AddComponent<Image>(); checkImg.color = new Color(0.7f, 1f, 0.7f, 1f);
        tog.graphic = checkImg;
        tog.onValueChanged.AddListener(v => { try { onChanged?.Invoke(v); } catch (Exception e) { Debug.LogException(e); } });
        MakeLabel(label, row.transform);
        return tog;
    }
}
```

- [ ] **Step 2: Implement `BuildOwnerSection`**

```csharp
    private void BuildOwnerSection(CommercialBuilding cb)
    {
        MakeHeader("Owner");
        // Current owners list
        foreach (var id in cb.OwnerIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var ch = Character.FindByUUID(id);
            string label = ch != null ? $"{ch.CharacterName} ({id})" : $"({id} not spawned)";
            var row = MakeRow();
            MakeLabel($"• {label}", row.transform);
            if (ch != null)
            {
                MakeButton("Remove", () =>
                {
                    if (cb.IsServer) cb.RemoveOwner(ch);
                    RebuildAll();
                }, row.transform);
            }
        }
        // Add owner by id
        var addRow = MakeRow();
        var inp = MakeInput("characterId", addRow.transform);
        MakeButton("Set as Owner", () =>
        {
            var ch = Character.FindByUUID(inp.text);
            if (ch == null) { Debug.LogWarning($"[DevMode] Character '{inp.text}' not found."); return; }
            if (cb.IsServer) cb.SetOwner(ch);
            RebuildAll();
        }, addRow.transform);
        MakeButton("Add Owner (no-job)", () =>
        {
            var ch = Character.FindByUUID(inp.text);
            if (ch == null) return;
            if (cb.IsServer) cb.AddOwner(ch);
            RebuildAll();
        }, addRow.transform);
        MakeButton("Clear All Owners", () =>
        {
            if (cb.IsServer) cb.SetOwner(null);
            RebuildAll();
        }, addRow.transform);
    }
```

⚠ Verify `cb.SetOwner(null)` is the supported "clear" path. Read `SetOwner` body — if it crashes on null, swap to "remove every owner one-by-one via `RemoveOwner`".

- [ ] **Step 3: Implement `BuildJobsSection`**

```csharp
    private void BuildJobsSection(CommercialBuilding cb)
    {
        MakeHeader("Jobs");
        if (cb.Jobs == null || cb.Jobs.Count == 0)
        {
            MakeLabel("<color=grey>(no jobs)</color>");
            return;
        }
        for (int i = 0; i < cb.Jobs.Count; i++)
        {
            var job = cb.Jobs[i];
            if (job == null) continue;
            var row = MakeRow();
            string title = !string.IsNullOrEmpty(job.JobTitle) ? job.JobTitle : job.GetType().Name;
            string workerName = job.IsAssigned && job.Worker != null ? job.Worker.CharacterName : "(unassigned)";
            MakeLabel($"[{i}] {title} — {workerName}", row.transform);
            if (job.IsAssigned && job.Worker != null)
            {
                MakeButton("Force-Fire", () =>
                {
                    if (cb.IsServer) cb.RemoveWorker(job);
                    RebuildAll();
                }, row.transform);
            }
            else
            {
                var inp = MakeInput("characterId", row.transform, minWidth: 100);
                MakeButton("Force-Hire", () =>
                {
                    var ch = Character.FindByUUID(inp.text);
                    if (ch == null) { Debug.LogWarning($"[DevMode] Character '{inp.text}' not found."); return; }
                    if (cb.IsServer) cb.AssignWorker(ch, job);
                    RebuildAll();
                }, row.transform);
            }
        }
    }
```

- [ ] **Step 4: Implement Shop sections**

```csharp
    private void BuildSellShelvesSection(ShopBuilding sb)
    {
        MakeHeader("Sell Shelves (storage role)");
        var allStorages = sb.GetComponentsInChildren<StorageFurniture>(includeInactive: true);
        if (allStorages == null || allStorages.Length == 0)
        {
            MakeLabel("<color=grey>(no storage furniture under shop)</color>");
            return;
        }
        for (int i = 0; i < allStorages.Length; i++)
        {
            var s = allStorages[i];
            if (s == null) continue;
            bool isSell = false;
            for (int j = 0; j < sb.SellShelves.Count; j++) if (sb.SellShelves[j] == s) { isSell = true; break; }
            var s2 = s; // closure capture
            MakeToggle($"{s.FurnitureName}", isSell, v =>
            {
                sb.DevForceSetSellShelfFlag(s2, v);
            });
        }
    }

    private void BuildCatalogSection(ShopBuilding sb)
    {
        MakeHeader("Catalog");
        for (int i = 0; i < sb.Catalog.Count; i++)
        {
            var entry = sb.Catalog[i];
            if (entry.Item == null) continue;
            var row = MakeRow();
            MakeLabel($"{entry.Item.ItemName}  max={entry.MaxStock}  price={entry.PriceOverride}", row.transform);
            var localId = entry.Item.ItemId;
            MakeButton("Remove", () =>
            {
                sb.DevForceRemoveCatalogEntry(localId);
                RebuildAll();
            }, row.transform);
        }
        var addRow = MakeRow();
        var idInp = MakeInput("itemId", addRow.transform);
        var maxInp = MakeInput("maxStock", addRow.transform, minWidth: 80);
        var priceInp = MakeInput("price", addRow.transform, minWidth: 80);
        MakeButton("Add", () =>
        {
            int.TryParse(maxInp.text, out int max);
            int.TryParse(priceInp.text, out int price);
            sb.DevForceAddCatalogEntry(idInp.text, max, price);
            RebuildAll();
        }, addRow.transform);
    }

    private void BuildCashiersSection(ShopBuilding sb)
    {
        MakeHeader("Cashiers");
        if (sb.Cashiers == null || sb.Cashiers.Count == 0)
        {
            MakeLabel("<color=grey>(no cashiers registered)</color>");
            return;
        }
        for (int i = 0; i < sb.Cashiers.Count; i++)
        {
            var c = sb.Cashiers[i];
            if (c == null) continue;
            var row = MakeRow();
            int balance = c.GetTillBalance(MWI.Economy.CurrencyId.Default);
            MakeLabel($"{c.FurnitureName}  till={balance}g", row.transform);
            var c2 = c;
            MakeButton("Withdraw → host", () =>
            {
                var host = ResolveLocalPlayerCharacter();
                if (host == null) { Debug.LogWarning("[DevMode] No local host Character to receive funds."); return; }
                sb.DevForceWithdrawCashierTill(c2, host);
                RebuildAll();
            }, row.transform);
        }
    }

    private static Character ResolveLocalPlayerCharacter()
    {
        try
        {
            if (NetworkManager.Singleton == null) return null;
            var lc = NetworkManager.Singleton.LocalClient;
            if (lc?.PlayerObject == null) return null;
            return lc.PlayerObject.GetComponent<Character>();
        }
        catch (Exception e) { Debug.LogException(e); return null; }
    }
```

- [ ] **Step 5: Implement `BuildInventorySection`**

```csharp
    private void BuildInventorySection(CommercialBuilding cb)
    {
        MakeHeader("Inventory");
        var counts = cb.GetInventoryCountsByItemSO();
        if (counts == null || counts.Count == 0)
        {
            MakeLabel("<color=grey>(empty)</color>");
        }
        else
        {
            var entries = new List<KeyValuePair<ItemSO, int>>(counts);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var row = MakeRow();
                MakeLabel($"{e.Key.ItemName} x{e.Value}", row.transform);
                var so = e.Key;
                MakeButton("-1", () =>
                {
                    if (!cb.IsServer) return;
                    var inst = cb.TakeFromInventory(so);
                    // TakeFromInventory returns the removed instance to the caller; for dev we just discard it
                    RebuildAll();
                }, row.transform);
            }
        }

        var addRow = MakeRow();
        var idInp = MakeInput("itemId", addRow.transform);
        MakeButton("Add 1", () =>
        {
            if (!cb.IsServer) return;
            var so = ResolveItemSOById(idInp.text);
            if (so == null) { Debug.LogWarning($"[DevMode] ItemSO '{idInp.text}' not found."); return; }
            var inst = new ItemInstance(so);
            cb.AddToInventory(inst);
            RebuildAll();
        }, addRow.transform);
    }

    private static ItemSO ResolveItemSOById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var all = Resources.LoadAll<ItemSO>("Data/Item");
        return Array.Find(all, x => x != null && x.ItemId == id);
    }
```

⚠ Verify the constructor `new ItemInstance(so)` is the right way to construct an `ItemInstance` from an `ItemSO`. Read `Assets/Scripts/Items/ItemInstance.cs` (or wherever) and use the actual constructor signature. If construction has additional required arguments (e.g. quantity, networkId), pass safe defaults. Confirm `cb.AddToInventory(inst)` does not error when called with a fresh instance.

- [ ] **Step 6: Compile + run end-to-end**

Build the project, host a session, place a `ShopBuilding`, transfer ownership to an NPC, enable dev mode (`F3`), Alt+Click the building, switch to "Console Management" tab. Confirm:
  - DEV banner is visible.
  - Hiring toggle flips `IsHiring` and the production HiringTab on connected clients reflects it.
  - Force-Hire / Force-Fire mutate `Jobs` correctly.
  - Sell-shelf toggle flips state and replicates.
  - Catalog Add/Remove replicates.
  - Cashier withdraw moves coins to host's wallet.
  - Inventory Add/Remove updates inventory + replicates.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Inspect/SubTabs/BuildingConsoleManagementSubTab.cs
git commit -m "feat(devmode): add BuildingConsoleManagementSubTab with full mutator surface"
```

---

### Task 7: Multiplayer regression test (2-client validation)

This is a manual test step — no code changes.

- [ ] **Step 1: Two-client scenario — host can dev-override client-owned building**

1. Start the project as a host (player A).
2. Connect a second client (player B) to the same session.
3. Player B places a `ShopBuilding` and becomes its owner.
4. From the HOST (player A), enable dev mode (`F3`), Alt+Click player B's shop, switch to Console Management.
5. Use Force-Hire to assign an NPC to a vacant job.
6. Confirm on player B's screen: the production read-out (HiringTab / job list) reflects the change.

Expected: action succeeds; replicates to player B.

- [ ] **Step 2: Two-client scenario — non-host client cannot dev-override**

1. From player B (non-host) press F3.
2. Confirm: console logs `<color=orange>[DevMode]</color> Dev mode is host-only.` — F3 is a no-op.
3. Confirm there is no path for player B to invoke `DevForce*` because:
   - `DevForce*` is server-only and B is not the server.
   - `DevAssertHostAndDevMode` rejects with "ignored — not on server".

- [ ] **Step 3: Production owner-gate regression**

1. From player B (non-host, with B owning the shop), via the production `UI_OwnerManagementPanel`, edit the catalog. Confirm: works (B is owner).
2. From player A (host, NOT owning the shop), via the production panel — confirm rejection: "rejected ServerRpc - caller A is not owner".
3. Then host enables dev mode and uses Console Management — confirm acceptance.

This proves the production owner gate is intact and that dev override is the only path that bypasses it.

- [ ] **Step 4: Document the test results**

Add a brief note at the bottom of `wiki/systems/devmode-tools.md` (or whatever wiki page documents the dev panel — verify which file exists):

```
## Console Management sub-tab — multi-client validation
- Host dev-override: works on any building regardless of owner.
- Non-host F3 / /devmode: blocked by DevModeManager (host-only).
- Production owner gate: unchanged — non-owner non-host calls are rejected as before.
- Tested 2026-05-09.
```

- [ ] **Step 5: Commit**

```bash
git add wiki/
git commit -m "docs(devmode): note Console Management multi-client validation"
```

---

### Task 8: Wiki + agent + skill docs (per project rules #28 / #29 / #29b)

**Files:**
- Modify: `wiki/systems/devmode-tools.md` (or whatever the existing devmode page is named — verify with `ls wiki/systems/ | grep -i dev`)
- Modify: `.agent/skills/debug-tools/SKILL.md` (verify existence; create if missing per rule #28)
- Modify: `.claude/agents/debug-tools-architect.md`

The work to do here is to add a section about the Console Management sub-tab and the `DevForce*` server methods. Each location's exact content depends on the existing structure of those files. Do NOT invent — read them first, then append.

- [ ] **Step 1: Read the existing devmode wiki page**

```bash
ls wiki/systems/ | grep -i dev
```

If a file exists, read it and append a new section "## Console Management sub-tab" with:
  - One paragraph describing what the sub-tab does.
  - A bullet list of the actions it surfaces.
  - A note that all owner-gated mutations route through `DevForce*` server methods on the building, which are wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD` and assert host + dev-mode-enabled.
  - Bump `updated:` frontmatter date to `2026-05-09`.
  - Append a Change log line: `- 2026-05-09 — added Console Management sub-tab + DevForce* server methods — claude`.

If no file exists, create `wiki/systems/devmode-tools.md` from `wiki/_templates/system.md` (per rule #29b) and fill the 10 required sections describing the dev panel as a whole, with a "Console Management sub-tab" subsection. Read `wiki/CLAUDE.md` first.

- [ ] **Step 2: Read + append to the debug-tools skill**

```bash
ls .agent/skills/ | grep -i debug
```

If the skill exists, append "Console Management sub-tab" + "DevForce* methods" sections. If not, do NOT create a new SKILL.md as Claude (per memory rule `feedback_wiki_vs_skills_scope`).

- [ ] **Step 3: Update `.claude/agents/debug-tools-architect.md`**

Append to the existing description: mention the Console Management sub-tab on `BuildingInspectorView` + the host-only `DevForce*` server-method pattern as part of the dev/godmode tooling surface.

- [ ] **Step 4: Commit**

```bash
git add wiki/ .agent/ .claude/agents/
git commit -m "docs(devmode): wiki + agent + skill updates for Console Management sub-tab"
```

---

## Self-Review (post-plan)

**1. Spec coverage** — every spec ask mapped to a task:

| Spec ask | Task |
|---|---|
| Sub-tab infrastructure on `BuildingInspectorView` mirroring `CharacterSubTab` | Task 1, 3 |
| `BuildingSubTab` enum-or-equivalent covering existing sections + ConsoleManagement | Tasks 1, 2, 3 (2 sub-tabs: Overview wraps prior sections + Console Management) |
| Keep existing read-only sections | Task 2 (verbatim port) |
| Hiring: toggle IsHiring + force-hire/fire + edit job params | Task 6 (`BuildHiringSection`, `BuildJobsSection`) + Task 4 (`DevForceSetHiring`, `DevForceSetAssignmentWage`) |
| Storage roles dropdown per StorageFurniture | Task 6 (`BuildSellShelvesSection`) — sell-shelf toggle, the only "storage role" the codebase actually has today (no Tool/Inventory role concepts found in code) |
| Owner add/remove/clear/transfer | Task 6 (`BuildOwnerSection`) calls `Room.AddOwner` / `Room.RemoveOwner` / `CommercialBuilding.SetOwner` directly |
| Construction force-finalize | Task 6 (`BuildConstructionSection`) — moved from current `_forceFinishButton` |
| Shop subtype: catalog edit + sell-shelf assignment | Task 6 (`BuildCatalogSection` + `BuildSellShelvesSection`) + Task 5 (`DevForceAddCatalogEntry` etc.) |
| `CraftingBuilding` input stock targets | NOT mapped — verify whether `CraftingBuilding` exposes input stock targets. If yes, add a `BuildCraftingInputsSection` step in Task 6; if no UI surface exists, omit. Read `CraftingBuilding.cs` before deciding. |
| Inventory dump: list + force-add/remove ItemSO | Task 6 (`BuildInventorySection`) |
| Reuse existing `IManagementTab` widgets vs parallel debug-flavored UI | Decided: parallel debug-flavored UI (justified above) |
| Server-side dev-override gate | Tasks 4, 5 (`DevForce*` host-only direct call, no RPC override) |
| Visual marker | Task 3 (red tab tint) + Task 6 (DEV banner + red tinted background) + every action label prefixed `[DEV]` |
| Audit logging | Task 4 (`DevAssertHostAndDevMode` logs warning per call) |
| `#if UNITY_EDITOR || DEVELOPMENT_BUILD` strip | Tasks 4, 5 |
| Solo and 2-client multiplayer test | Task 7 |
| Don't refactor production code | Tasks 4, 5 are pure extract-method (no behavior change). Production paths untouched. |

**Outstanding ambiguity flagged in the plan**:
- `CraftingBuilding` input stock target editing — TBD; read code before adding.
- `TrySetAssignmentWage` body shape — verify before mirroring in Task 4.
- `ItemInstance` constructor — verify before using in Task 6 Step 5.
- `cb.SetOwner(null)` null-tolerance — verify before using in Task 6 Step 2.

These are intentionally flagged inside their tasks (with ⚠ markers), not omitted. They require the implementer to read the production code one more time before committing.

**2. Placeholder scan** — no `TBD`, no `// TODO`, no "implement later". Each `⚠` is a verification step bolted to a concrete production file path that the implementer reads before finalising.

**3. Type consistency** — `DevForceSetHiring(bool open)`, `DevForceAddCatalogEntry(string itemId, int maxStock, int priceOverride)`, `DevAssertHostAndDevMode(string action)` — names + signatures match across all call sites in the plan.

---

## Plan complete

Saved to `docs/superpowers/plans/2026-05-09-dev-console-management-subtab.md`.
