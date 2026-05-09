#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor utility that adds a <see cref="HarvestableInspectorView"/> GameObject
/// to the DevModePanel prefab as a sibling of the existing inspector views. The view is
/// auto-discovered at runtime by <see cref="DevInspectModule"/> via
/// <c>GetComponentsInChildren&lt;IInspectorView&gt;(true)</c>, so no further wiring is required
/// once this builder runs.
///
/// Menu:
///   Tools/DevMode/Build Harvestable Inspector                 — additive, aborts if it already exists.
///   Tools/DevMode/Rebuild Harvestable Inspector (Destructive) — deletes existing then rebuilds.
/// </summary>
public static class DevHarvestableInspectorBuilder
{
    private const string PrefabPath = "Assets/Resources/UI/DevModePanel.prefab";
    private const string ViewName = "HarvestableInspectorView";

    [MenuItem("Tools/DevMode/Build Harvestable Inspector")]
    public static void BuildHarvestableInspector()
    {
        RunBuild(destructive: false);
    }

    [MenuItem("Tools/DevMode/Rebuild Harvestable Inspector (Destructive)")]
    public static void RebuildHarvestableInspector()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Rebuild Harvestable Inspector",
            "This will DELETE any existing 'HarvestableInspectorView' under DevModePanel/ContentRoot/InspectContent/Views, then rebuild it from scratch.\n\nAre you sure?",
            "Yes, rebuild",
            "Cancel");
        if (!confirmed) return;
        RunBuild(destructive: true);
    }

    private static void RunBuild(bool destructive)
    {
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (contents == null)
        {
            Debug.LogError($"[DevHarvestableInspectorBuilder] Could not load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var contentRootT = contents.transform.Find("ContentRoot");
            if (contentRootT == null)
            {
                Debug.LogError("[DevHarvestableInspectorBuilder] ContentRoot not found. Run 'Tools/DevMode/Build Inspect Tab' first.");
                return;
            }
            var inspectContentT = contentRootT.Find("InspectContent");
            if (inspectContentT == null)
            {
                Debug.LogError("[DevHarvestableInspectorBuilder] InspectContent not found under ContentRoot. Run 'Tools/DevMode/Build Inspect Tab' first.");
                return;
            }
            var viewsT = inspectContentT.Find("Views");
            if (viewsT == null)
            {
                Debug.LogError("[DevHarvestableInspectorBuilder] Views not found under InspectContent. Run 'Tools/DevMode/Build Inspect Tab' first.");
                return;
            }

            var existing = viewsT.Find(ViewName);
            if (destructive && existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }
            if (existing != null)
            {
                Debug.LogWarning($"[DevHarvestableInspectorBuilder] '{ViewName}' already exists. Use the destructive variant to overwrite.");
                return;
            }

            var referenceFont = FindFirstFontAsset(contents);
            if (referenceFont == null)
            {
                referenceFont = TMP_Settings.defaultFontAsset;
                Debug.LogWarning("[DevHarvestableInspectorBuilder] No TMP font found in prefab; falling back to TMP_Settings.defaultFontAsset.");
            }

            // --- Build the view root -----------------------------------------------------
            GameObject viewRoot = CreateUIGameObject(ViewName, viewsT);
            var viewLayout = viewRoot.AddComponent<VerticalLayoutGroup>();
            viewLayout.padding = new RectOffset(0, 0, 0, 0);
            viewLayout.spacing = 4;
            viewLayout.childControlWidth = true;
            viewLayout.childControlHeight = true;
            viewLayout.childForceExpandWidth = true;
            // feh=false so Header keeps its preferred 28 px and Body's flexH=10 absorbs all leftover space
            // — ensures the ScrollRect viewport gets a tight bound and content actually overflows + scrolls.
            viewLayout.childForceExpandHeight = false;

            var viewComponent = viewRoot.AddComponent<HarvestableInspectorView>();

            GameObject headerGO = CreateTMPLabel(viewRoot.transform, "Header",
                "Inspecting: —", referenceFont, fontSize: 18, alignment: TextAlignmentOptions.Left);
            var headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.minHeight = 28;
            headerLE.preferredHeight = 28;
            headerLE.flexibleHeight = 0;
            TMP_Text headerText = headerGO.GetComponent<TMP_Text>();

            GameObject scrollGO = CreateScrollRect(viewRoot.transform, "Body", referenceFont, out TMP_Text contentTMP);
            var scrollLE = scrollGO.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 10;
            scrollLE.minHeight = 200;

            // --- Wire serialized fields via SerializedObject ----------------------------
            var so = new SerializedObject(viewComponent);
            var headerProp = so.FindProperty("_headerLabel");
            var contentProp = so.FindProperty("_content");
            if (headerProp == null || contentProp == null)
            {
                Debug.LogError("[DevHarvestableInspectorBuilder] HarvestableInspectorView is missing expected serialized fields (_headerLabel / _content).");
            }
            else
            {
                headerProp.objectReferenceValue = headerText;
                contentProp.objectReferenceValue = contentTMP;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- Save -------------------------------------------------------------------
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool success);
            if (!success)
            {
                Debug.LogError("[DevHarvestableInspectorBuilder] PrefabUtility.SaveAsPrefabAsset returned success=false.");
            }
            else
            {
                Debug.Log($"[DevHarvestableInspectorBuilder] HarvestableInspectorView added under Views. Saved: {PrefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ============================================================================
    // Helpers (mirror DevStorageFurnitureInspectorBuilder so the new view matches the rest of the tab)
    // ============================================================================

    private static GameObject CreateUIGameObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        StretchToParent(go);
        return go;
    }

    private static void StretchToParent(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static GameObject CreateTMPLabel(Transform parent, string name, string text,
        TMP_FontAsset font, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = true;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return go;
    }

    private static GameObject CreateScrollRect(Transform parent, string name, TMP_FontAsset font, out TMP_Text contentText)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, worldPositionStays: false);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0, 0);
        rootRT.anchorMax = new Vector2(1, 1);
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;
        rootRT.localScale = Vector3.one;

        var rootBg = root.AddComponent<Image>();
        rootBg.color = new Color(0.1f, 0.1f, 0.1f, 0.6f);
        rootBg.raycastTarget = true;

        var scrollRect = root.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(root.transform, worldPositionStays: false);
        var viewportRT = viewport.GetComponent<RectTransform>();
        viewportRT.anchorMin = new Vector2(0, 0);
        viewportRT.anchorMax = new Vector2(1, 1);
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportRT.pivot = new Vector2(0, 1);
        viewportRT.localScale = Vector3.one;
        viewport.AddComponent<RectMask2D>();
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(1, 1, 1, 0.01f);
        viewportImg.raycastTarget = true;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, worldPositionStays: false);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0, 1);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 100);
        contentRT.localScale = Vector3.one;

        var contentVL = content.AddComponent<VerticalLayoutGroup>();
        contentVL.padding = new RectOffset(6, 6, 6, 6);
        contentVL.spacing = 2;
        contentVL.childControlWidth = true;
        contentVL.childControlHeight = false;
        contentVL.childForceExpandWidth = true;
        contentVL.childForceExpandHeight = false;
        contentVL.childAlignment = TextAnchor.UpperLeft;

        var contentCSF = content.AddComponent<ContentSizeFitter>();
        contentCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGO = new GameObject("Content_TMP", typeof(RectTransform));
        textGO.transform.SetParent(content.transform, worldPositionStays: false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 1);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.pivot = new Vector2(0, 1);
        textRT.localScale = Vector3.one;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        if (font != null) tmp.font = font;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        tmp.richText = true;
        tmp.raycastTarget = false;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        contentText = tmp;
        return root;
    }

    private static TMP_FontAsset FindFirstFontAsset(GameObject root)
    {
        var texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].font != null) return texts[i].font;
        }
        return null;
    }
}
#endif
