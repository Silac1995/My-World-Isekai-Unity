using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// DEV-ONLY relations mutator surface for the Character inspector. Lets the host
/// flip <c>HasMet</c> + <c>KnowsName</c> per relationship and nudge
/// <c>RelationValue</c> up/down without going through the social interaction
/// pipeline. Designed to unblock the speech-bubble rework's Task 14 late-joiner
/// repro (need to set <c>KnowsName=true</c> on the host so a joining client sees
/// the real name; need to flip it false to see <c>???</c>).
///
/// Lives as a sibling to the 10 stock <see cref="CharacterSubTab"/>s + the
/// city-founding sub-tab. Inherits the base class but overrides
/// <see cref="Refresh(Character)"/> directly to render programmatic UGUI widgets
/// instead of the text-only <c>RenderContent</c> path (the base's _content
/// TMP_Text is intentionally left unwired on this prefab — the override never
/// touches it). Mirrors <c>CharacterCityFoundingSubTab</c>'s widget pattern.
///
/// Authority model: every mutator routes through a <c>DevForce*</c> method on
/// <see cref="CharacterRelation"/> that asserts host + DevMode and emits an
/// audit log. Each DevForce method writes through the same
/// <c>RelationSyncData → NetworkList</c> path production social code uses, so
/// the visible state stays consistent across peers (and propagates to
/// late-joiners via the standard NetworkList replay).
///
/// Sections (top to bottom):
///   1. DEV banner
///   2. Refresh button
///   3. Per-relationship row: target name + RelationType, value with ±1/±10 buttons,
///      HasMet toggle, KnowsName toggle.
/// </summary>
public sealed class CharacterRelationsDevSubTab : CharacterSubTab
{
    /// <summary>
    /// Where widgets get parented. Wired in the prefab to the inner
    /// <c>Viewport/Content</c> RectTransform (which carries the
    /// VerticalLayoutGroup + ContentSizeFitter inherited from the duplicated
    /// sub-tab base). Falls back to the script's own transform when null.
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
        // Live mutators trigger an explicit RebuildAll on click — no per-frame
        // refresh path needed.
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
        BuildRelationshipList(_bound);
    }

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
        tmp.text = "[DEV] Relations — host-only, bypasses compatibility filter";
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

    private void BuildRelationshipList(Character c)
    {
        MakeHeader($"Relationships ({c.CharacterName ?? "—"})");

        var relSys = c.CharacterRelation;
        if (relSys == null)
        {
            MakeLabel("<color=#FF6464>CharacterRelation component missing.</color>");
            return;
        }

        var list = relSys.Relationships;
        if (list == null || list.Count == 0)
        {
            MakeLabel("<color=grey>None — this character has no relationships yet. Trigger an interaction or use the Social system to seed one.</color>");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var rel = list[i];
            if (rel == null || rel.RelatedCharacter == null) continue;
            BuildRelationRow(relSys, rel);
        }
    }

    /// <summary>
    /// Renders three rows per relationship: a header (name + RelationType), a
    /// value row with ±1 / ±10 buttons, and a toggles row with HasMet +
    /// KnowsName flippers. Each mutator routes through CharacterRelation's
    /// DevForce* path so the NetworkList sync stays consistent for clients +
    /// late-joiners.
    /// </summary>
    private void BuildRelationRow(CharacterRelation relSys, Relationship rel)
    {
        var headerRow = MakeRow();
        string targetName = rel.RelatedCharacter.CharacterName ?? "<unknown>";
        MakeLabel($"<b>{targetName}</b> — <color=#FFD27A>{rel.RelationType}</color>", headerRow.transform);

        var valueRow = MakeRow();
        var capturedRel = rel;
        MakeButton("−10", () => { relSys.DevForceAdjustRelationValue(capturedRel, -10); RebuildAll(); }, valueRow.transform, minWidth: 48);
        MakeButton("−1",  () => { relSys.DevForceAdjustRelationValue(capturedRel, -1);  RebuildAll(); }, valueRow.transform, minWidth: 48);
        string valueColor = rel.RelationValue > 0 ? "#7FFF7F" : (rel.RelationValue < 0 ? "#FF7F7F" : "#AAAAAA");
        MakeLabel($"  Value: <color={valueColor}>{rel.RelationValue:+#;-#;0}</color>  ", valueRow.transform);
        MakeButton("+1",  () => { relSys.DevForceAdjustRelationValue(capturedRel, +1);  RebuildAll(); }, valueRow.transform, minWidth: 48);
        MakeButton("+10", () => { relSys.DevForceAdjustRelationValue(capturedRel, +10); RebuildAll(); }, valueRow.transform, minWidth: 48);

        var toggleRow = MakeRow();
        string hasMetColor = rel.HasMet ? "#7FFF7F" : "#FF7F7F";
        MakeLabel($"HasMet: <color={hasMetColor}>{rel.HasMet}</color>  ", toggleRow.transform);
        MakeButton("Toggle HasMet", () => { relSys.DevForceSetHasMet(capturedRel, !capturedRel.HasMet); RebuildAll(); }, toggleRow.transform, minWidth: 130);

        string knowsNameColor = rel.KnowsName ? "#7FFF7F" : "#FF7F7F";
        MakeLabel($"KnowsName: <color={knowsNameColor}>{rel.KnowsName}</color>  ", toggleRow.transform);
        MakeButton("Toggle KnowsName", () => { relSys.DevForceSetKnowsName(capturedRel, !capturedRel.KnowsName); RebuildAll(); }, toggleRow.transform, minWidth: 160);
    }

    // ─── Widget helpers (mirror CharacterCityFoundingSubTab) ────────────

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
}
#endif
