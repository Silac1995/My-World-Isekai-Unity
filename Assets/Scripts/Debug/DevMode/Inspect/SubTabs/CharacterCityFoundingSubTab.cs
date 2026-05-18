using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using MWI.Ambition;
using MWI.WorldSystem;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// DEV-ONLY city-founding mutator surface for the Character inspector. Lets the host
/// drive the Plan 4 city-founding loop (create community → ambition → AB placement →
/// treasury credit → tier-up → drifter migration → join requests) without authoring
/// content or waiting for the natural in-game cadence.
///
/// Lives as a sibling to the 10 stock <see cref="CharacterSubTab"/>s. Inherits the
/// base class but overrides <see cref="Refresh(Character)"/> directly to render
/// programmatic UGUI widgets instead of the text-only <c>RenderContent</c> path
/// (the base's _content TMP_Text is intentionally left unwired on this prefab —
/// the override never touches it).
///
/// Authority model: every mutator either calls a public POCO method on
/// <see cref="CharacterCommunity"/> / <see cref="Community"/> directly (safe because
/// dev mode is host-only, so we are always on the server), or routes through a
/// <c>DevForce*</c> wrapper on <see cref="CommercialBuilding"/> / <see cref="AdministrativeBuilding"/> /
/// <see cref="MWI.Time.TimeManager"/> that asserts host + DevMode and emits an audit log.
///
/// Sections (top to bottom):
///   1. DEV banner
///   2. Citizenship + community status header (read-only)
///   3. Create Community         — visible when CurrentCommunity == null
///   4. Assign Ambition_FoundACity
///   5. Community read-only panel — visible when CurrentCommunity != null
///   6. Force-Promote Community  — visible when CurrentCommunity != null
///   7. Grant Treasury 1000      — visible when CurrentCommunity != null && AB != null
///   8. Submit Join Request      — visible when target has no community + a chartered AB on map
///   9. Time control (Force NewDay) — universal
/// </summary>
public sealed class CharacterCityFoundingSubTab : CharacterSubTab
{
    /// <summary>
    /// Where widgets get parented. Wired in the prefab to the inner
    /// <c>Viewport/Content</c> RectTransform (which carries the VerticalLayoutGroup +
    /// ContentSizeFitter inherited from the duplicated sub-tab base). Falls back to
    /// the script's own transform when null so a designer can also use this sub-tab
    /// on a flat (non-scrolling) panel.
    /// </summary>
    [SerializeField] private RectTransform _widgetRoot;

    private Character _bound;
    private readonly List<GameObject> _spawnedWidgets = new();

    private Transform WidgetParent => _widgetRoot != null ? (Transform)_widgetRoot : transform;

    /// <summary>Required by the abstract base — this sub-tab renders widgets, not text.</summary>
    protected override string RenderContent(Character c) => string.Empty;

    public override void Refresh(Character c)
    {
        if (c == null) { Clear(); return; }
        if (_bound != c)
        {
            _bound = c;
            RebuildAll();
        }
        // Live state (treasury / member count) refreshes only on rebuild — the user
        // clicks a button to trigger a fresh read. A "Refresh" button at the top of
        // RebuildAll gives an explicit refresh path without per-frame allocation.
    }

    public override void Clear()
    {
        _bound = null;
        ClearWidgets();
        base.Clear();
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
        BuildRefreshButton();
        BuildStatusHeader(_bound);
        // Feature sections land here in subsequent commits.
    }

    // ─── Sections ────────────────────────────────────────────────────────

    private void BuildBanner()
    {
        var go = new GameObject("DEV_Banner", typeof(RectTransform));
        go.transform.SetParent(WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 28; le.preferredHeight = 28;
        var img = go.AddComponent<Image>(); img.color = new Color(0.6f, 0.2f, 0.2f, 1f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = new Vector2(-8, 0);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "[DEV] City Founding — host-only, bypasses production gates";
        tmp.fontSize = 13; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white; tmp.raycastTarget = false;

        _spawnedWidgets.Add(go);
    }

    private void BuildRefreshButton()
    {
        var row = MakeRow();
        MakeButton("⟳ Refresh", RebuildAll, row.transform);
        MakeLabel("<color=grey>(rebuild widgets from live state)</color>", row.transform);
    }

    private void BuildStatusHeader(Character c)
    {
        if (c == null) return;
        MakeHeader("Status");

        var cc = c.CharacterCommunity;
        if (cc == null)
        {
            MakeLabel("<color=#FF6464>CharacterCommunity component missing.</color>");
            return;
        }

        var current = cc.CurrentCommunity;
        var citizenship = cc.Citizenship;

        var sb = new StringBuilder(160);
        sb.Append("<color=#FFD27A>Character:</color> ").Append(c.CharacterName ?? "—");
        sb.Append("    <color=#FFD27A>CurrentCommunity:</color> ");
        sb.Append(current != null
            ? $"<color=#64FF64>{current.communityName}</color> ({current.level})"
            : "<color=grey>none</color>");
        sb.Append("    <color=#FFD27A>Citizenship:</color> ");
        sb.Append(citizenship != null
            ? $"<color=#64FF64>{citizenship.communityName}</color>"
            : "<color=grey>none</color>");
        MakeLabel(sb.ToString());
    }

    // ─── Widget helpers (programmatic UGUI, dev-only) ────────────────────

    private GameObject MakeRow(Transform parent = null)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 26;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childAlignment = TextAnchor.MiddleLeft;
        _spawnedWidgets.Add(go);
        return go;
    }

    private GameObject MakeHeader(string text)
    {
        var go = MakeLabel($"<b>{text}</b>");
        var tmp = go.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            tmp.fontSize = 14;
            tmp.color = new Color(1f, 0.85f, 0.6f, 1f);
        }
        return go;
    }

    private GameObject MakeLabel(string text, Transform parent = null)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 22; le.flexibleWidth = 1;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        tmp.raycastTarget = false; tmp.richText = true;
        _spawnedWidgets.Add(go);
        return go;
    }

    private Button MakeButton(string label, Action onClick, Transform parent = null, float minWidth = 100)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = minWidth;
        var img = go.AddComponent<Image>(); img.color = new Color(0.40f, 0.20f, 0.20f, 1f);
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var ltmp = labelGO.AddComponent<TextMeshProUGUI>();
        ltmp.text = label; ltmp.alignment = TextAlignmentOptions.Center;
        ltmp.fontSize = 12; ltmp.color = Color.white; ltmp.raycastTarget = false;

        btn.onClick.AddListener(() =>
        {
            try { onClick?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        });
        _spawnedWidgets.Add(go);
        return btn;
    }

    private TMP_InputField MakeInput(string placeholder, string initialText = "", Transform parent = null, float minWidth = 100)
    {
        var go = new GameObject("Input", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = minWidth;
        var img = go.AddComponent<Image>(); img.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        var inp = go.AddComponent<TMP_InputField>(); inp.image = img;

        var textArea = new GameObject("Text", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, worldPositionStays: false);
        var tart = textArea.GetComponent<RectTransform>();
        tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
        tart.offsetMin = new Vector2(4, 2); tart.offsetMax = new Vector2(-4, -2);
        var ttmp = textArea.AddComponent<TextMeshProUGUI>();
        ttmp.fontSize = 12; ttmp.color = Color.white;
        ttmp.alignment = TextAlignmentOptions.MidlineLeft;
        ttmp.raycastTarget = false;
        inp.textComponent = ttmp;

        var phGO = new GameObject("Placeholder", typeof(RectTransform));
        phGO.transform.SetParent(textArea.transform, worldPositionStays: false);
        var phrt = phGO.GetComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
        phrt.offsetMin = Vector2.zero; phrt.offsetMax = Vector2.zero;
        var phtmp = phGO.AddComponent<TextMeshProUGUI>();
        phtmp.text = placeholder; phtmp.fontSize = 12;
        phtmp.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        phtmp.alignment = TextAlignmentOptions.MidlineLeft;
        phtmp.raycastTarget = false;
        inp.placeholder = phtmp;

        if (!string.IsNullOrEmpty(initialText)) inp.SetTextWithoutNotify(initialText);

        _spawnedWidgets.Add(go);
        return inp;
    }
}
#endif
