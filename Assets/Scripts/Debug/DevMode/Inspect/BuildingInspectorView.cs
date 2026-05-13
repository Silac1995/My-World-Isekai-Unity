using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IBuildingInspectorView for any <see cref="Building"/> target. Hosts a small tab
/// bar with two sub-tabs (mirror of <see cref="CharacterInspectorView"/> /
/// <see cref="CharacterSubTab"/>):
///   1. Overview            — full read-out of the building's state (Identity /
///                            State / Owners / Commercial / Inventory / Wanted /
///                            Tracked / Needed / Logistics / Tasks / Rooms /
///                            Furniture / Interior). Verbatim port of the prior
///                            view's render code into <see cref="BuildingOverviewSubTab"/>.
///   2. Console Management  — DEV-ONLY mutator surface (host-only, gated by
///                            <see cref="DevModeManager"/>). Built dynamically by
///                            <see cref="BuildingConsoleManagementSubTab"/>.
///
/// The tab bar + sub-tab content hosts are built programmatically at runtime so
/// the existing DevModePanel prefab does not need to be re-authored. The
/// serialized <see cref="_content"/> TMP_Text is reused as the Overview sub-tab's
/// label — re-parented under a runtime "OverviewContent" host on Awake.
/// </summary>
public class BuildingInspectorView : MonoBehaviour, IBuildingInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

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
        try
        {
            BuildSubTabHierarchy();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
        }
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
        if (_target == null)
        {
            _headerLabel.text = "Inspecting: —";
            return;
        }
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
    /// We don't author this in the prefab because there are only two sub-tabs
    /// and the Console Management sub-tab is fully dynamic anyway.
    ///
    /// Layout:
    ///   this (BuildingInspectorView GO, VerticalLayoutGroup from prefab)
    ///   ├─ HeaderLabel (existing prefab child)
    ///   ├─ SubTabBar (runtime — sibling 1)
    ///   ├─ OverviewContent (runtime — sibling 2)
    ///   │   └─ _content (re-parented existing prefab TMP_Text)
    ///   └─ ConsoleManagementContent (runtime — sibling 3)
    ///       └─ ScrollRect/Viewport/Content (BuildingConsoleManagementSubTab)
    /// </summary>
    private void BuildSubTabHierarchy()
    {
        if (_content == null)
        {
            Debug.LogError("[BuildingInspectorView] _content TMP_Text not wired in prefab. Aborting sub-tab build.");
            return;
        }

        var parent = transform;

        // Hide every legacy prefab child that isn't the header label. The prefab
        // originally hosted the read-out inside a wrapper (e.g. "Slots" — a
        // ScrollRect with min=200 flex=10) that would otherwise hog the parent VLG's
        // vertical space and squeeze our content hosts down to ~30px. We pull
        // _content (the serialized TMP_Text) up to the root first so it survives.
        if (_content.transform.parent != parent)
        {
            _content.transform.SetParent(parent, worldPositionStays: false);
        }
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;
            if (_headerLabel != null && child == _headerLabel.transform) continue;
            if (_content != null && child == _content.transform) continue;
            child.gameObject.SetActive(false);
        }

        // Layout strategy: bypass the prefab's outer VerticalLayoutGroup with
        // ignoreLayout + manual stretch anchors. The outer VLG cannot reliably size
        // our sub-tab hosts (flex distribution doesn't kick in cleanly when sibling
        // legacy children carry their own preferred heights), so we manually anchor
        // SubTabBar at the top, then stretch-anchor the two content hosts to fill
        // everything below it.
        const float HeaderHeight = 36f;
        const float TabBarHeight = 28f;
        const float TopInset = HeaderHeight + TabBarHeight + 4f; // 4px gap

        // SubTabBar — anchored top-stretch, just below the header.
        var tabBarGO = CreateUIChild(parent, "SubTabBar", siblingIndex: 1);
        var tabBarRT = tabBarGO.GetComponent<RectTransform>();
        tabBarRT.anchorMin = new Vector2(0, 1); tabBarRT.anchorMax = new Vector2(1, 1);
        tabBarRT.pivot = new Vector2(0.5f, 1f);
        tabBarRT.anchoredPosition = new Vector2(0, -HeaderHeight);
        tabBarRT.sizeDelta = new Vector2(0, TabBarHeight);
        var tabBarLE = tabBarGO.AddComponent<LayoutElement>();
        tabBarLE.ignoreLayout = true;
        var tabBarHL = tabBarGO.AddComponent<HorizontalLayoutGroup>();
        tabBarHL.spacing = 2;
        tabBarHL.childForceExpandWidth = true;
        tabBarHL.childForceExpandHeight = true;
        tabBarHL.childControlWidth = true;
        tabBarHL.childControlHeight = true;

        // OverviewContent — full-stretch, top inset = Header + TabBar + spacing.
        // ScrollRect inside (mirrors ConsoleManagementContent) so growing
        // building read-outs scroll instead of overflowing the panel.
        var overviewHost = CreateUIChild(parent, "OverviewContent", siblingIndex: 2);
        ConfigureContentHostStretch(overviewHost, topInset: TopInset);
        AddScrollRect(overviewHost, out var overviewContent);
        // Suppress AddScrollRect's [DEV] red background tint on this non-dev tab.
        var overviewBg = overviewHost.GetComponent<Image>();
        if (overviewBg != null) overviewBg.color = new Color(0f, 0f, 0f, 0f);
        // Move _content under the scrollable Content (so it scrolls with overflow).
        _content.transform.SetParent(overviewContent.transform, worldPositionStays: false);

        var overviewTab = overviewHost.AddComponent<BuildingOverviewSubTab>();
        overviewTab.SetContentLabel(_content);

        // Tab buttons.
        var overviewBtn = CreateTabButton(tabBarGO.transform, "Overview");
        _subTabs.Add((overviewBtn, overviewHost, overviewTab));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Console Management host — full-stretch, same top inset. ScrollRect inside.
        // Dev-only: BuildingConsoleManagementSubTab is gated to editor/development builds
        // because every widget calls a DevForce* method that is itself dev-only-gated.
        var consoleHost = CreateUIChild(parent, "ConsoleManagementContent", siblingIndex: 3);
        ConfigureContentHostStretch(consoleHost, topInset: TopInset);
        AddScrollRect(consoleHost, out var consoleContent);

        // The console tab MonoBehaviour lives on the scrollable content (so it can
        // attach widgets as direct children that follow the VerticalLayoutGroup).
        consoleContent.AddComponent<BuildingConsoleManagementSubTab>();
        var consoleTab = consoleContent.GetComponent<BuildingConsoleManagementSubTab>();

        var consoleBtn = CreateTabButton(tabBarGO.transform, "[DEV] Console Management");
        // Tint the dev tab button red so it's impossible to confuse with a production surface.
        var consoleImg = consoleBtn.GetComponent<Image>();
        if (consoleImg != null) consoleImg.color = new Color(0.45f, 0.20f, 0.20f, 1f);
        var consoleLabelText = consoleBtn.GetComponentInChildren<TMP_Text>();
        if (consoleLabelText != null) consoleLabelText.color = new Color(1f, 0.7f, 0.7f, 1f);

        _subTabs.Add((consoleBtn, consoleHost, consoleTab));
#endif

        for (int i = 0; i < _subTabs.Count; i++)
        {
            int captured = i;
            _subTabs[i].btn.onClick.AddListener(() => SwitchTab(captured));
        }

        SwitchTab(0); // Overview by default
    }

    // ─── UI helpers (programmatic, dev-only) ────────────────────────────

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

    /// <summary>
    /// Configures a runtime-built sub-tab host GameObject to bypass the prefab's
    /// outer VLG (which can't reliably size flex children when sibling legacy
    /// elements have their own preferred heights). Sets <see cref="LayoutElement.ignoreLayout"/>
    /// + full-stretch anchors with the given top inset (to clear Header + SubTabBar).
    /// </summary>
    private static void ConfigureContentHostStretch(GameObject host, float topInset)
    {
        var rt = host.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(0, 0);
        rt.offsetMax = new Vector2(0, -topInset);

        var le = host.GetComponent<LayoutElement>();
        if (le == null) le = host.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
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
        bg.color = new Color(0.45f, 0.20f, 0.20f, 0.30f); // [DEV] tint background
        bg.raycastTarget = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(root.transform, worldPositionStays: false);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        // Right inset = scrollbar width so handle doesn't overlap content.
        vrt.offsetMin = Vector2.zero; vrt.offsetMax = new Vector2(-16, 0);
        vrt.pivot = new Vector2(0, 1);
        viewport.AddComponent<RectMask2D>();
        var vimg = viewport.AddComponent<Image>(); vimg.color = new Color(1, 1, 1, 0.01f);

        // Vertical Scrollbar — right edge, 16 px wide. Visibility honors
        // sr.verticalScrollbarVisibility = AutoHide set above (hidden when
        // content fits, shown when it overflows).
        var scrollbarGO = new GameObject("Scrollbar Vertical", typeof(RectTransform));
        scrollbarGO.transform.SetParent(root.transform, worldPositionStays: false);
        var sbRT = scrollbarGO.GetComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0); sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 0.5f);
        sbRT.sizeDelta = new Vector2(16, 0);
        sbRT.anchoredPosition = Vector2.zero;
        var sbBg = scrollbarGO.AddComponent<Image>();
        sbBg.color = new Color(0.15f, 0.15f, 0.15f, 0.6f);
        var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.transform.SetParent(scrollbarGO.transform, worldPositionStays: false);
        var saRT = slidingArea.GetComponent<RectTransform>();
        saRT.anchorMin = Vector2.zero; saRT.anchorMax = Vector2.one;
        saRT.offsetMin = new Vector2(2, 2); saRT.offsetMax = new Vector2(-2, -2);

        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(slidingArea.transform, worldPositionStays: false);
        var hRT = handle.GetComponent<RectTransform>();
        hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one;
        hRT.offsetMin = Vector2.zero; hRT.offsetMax = Vector2.zero;
        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(0.55f, 0.55f, 0.55f, 0.9f);

        scrollbar.targetGraphic = hImg;
        scrollbar.handleRect = hRT;

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

        sr.viewport = vrt; sr.content = crt; sr.verticalScrollbar = scrollbar;
        contentGO = content;
        return sr;
    }
}
