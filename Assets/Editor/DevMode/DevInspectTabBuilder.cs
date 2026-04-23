#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot Editor tool that programmatically builds the Inspect tab inside the
/// DevModePanel prefab. Creates all required GameObjects (InspectContent, Placeholder,
/// Views/CharacterInspectorView and its 10 sub-tabs) and wires every serialized
/// reference (DevModePanel._tabs, CharacterInspectorView._subTabs, etc.) via
/// SerializedObject so private fields are writable.
///
/// Menu:
///   Tools/DevMode/Build Inspect Tab                    — additive build, aborts if InspectContent exists.
///   Tools/DevMode/Rebuild Inspect Tab (Destructive)    — deletes existing InspectContent then rebuilds.
/// </summary>
public static class DevInspectTabBuilder
{
    private const string PrefabPath = "Assets/Resources/UI/DevModePanel.prefab";
    private const string InspectContentName = "InspectContent";
    private const string InspectTabButtonName = "InspectTabButton";

    // Ordered list of the 10 sub-tabs. Name used for the GameObject, the tab button label,
    // and selects which CharacterSubTab subclass is attached.
    private static readonly (string displayName, string componentTypeName)[] SubTabSpec = new (string, string)[]
    {
        ("Identity",     "IdentitySubTab"),
        ("Stats",        "StatsSubTab"),
        ("SkillsTraits", "SkillsTraitsSubTab"),
        ("Needs",        "NeedsSubTab"),
        ("AI",           "AISubTab"),
        ("Combat",       "CombatSubTab"),
        ("Social",       "SocialSubTab"),
        ("Economy",      "EconomySubTab"),
        ("Knowledge",    "KnowledgeSubTab"),
        ("Inventory",    "InventorySubTab"),
    };

    [MenuItem("Tools/DevMode/Build Inspect Tab")]
    public static void BuildInspectTab()
    {
        RunBuild(destructive: false);
    }

    [MenuItem("Tools/DevMode/Rebuild Inspect Tab (Destructive)")]
    public static void RebuildInspectTab()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Rebuild Inspect Tab",
            "This will DELETE any existing 'InspectContent' under DevModePanel/ContentRoot " +
            "AND any existing 'InspectTabButton' under TabBar, then rebuild them from scratch.\n\n" +
            "Are you sure?",
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
            Debug.LogError($"[DevInspectTabBuilder] Could not load prefab at {PrefabPath}");
            return;
        }

        try
        {
            // --- Locate existing structural references --------------------------------
            var devModePanel = contents.GetComponent<DevModePanel>();
            if (devModePanel == null)
            {
                Debug.LogError("[DevInspectTabBuilder] Prefab root has no DevModePanel component. Aborting.");
                return;
            }

            // ContentRoot is the private field on DevModePanel; also appears as a child GO.
            var contentRootField = typeof(DevModePanel).GetField("_contentRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            var contentRoot = contentRootField != null ? contentRootField.GetValue(devModePanel) as GameObject : null;
            if (contentRoot == null)
            {
                // Fallback: search by name
                var found = contents.transform.Find("ContentRoot");
                if (found != null) contentRoot = found.gameObject;
            }
            if (contentRoot == null)
            {
                Debug.LogError("[DevInspectTabBuilder] ContentRoot GameObject not found. Aborting.");
                return;
            }

            // TabBar is a child of ContentRoot named "TabBar"
            var tabBarT = contentRoot.transform.Find("TabBar");
            if (tabBarT == null)
            {
                Debug.LogError("[DevInspectTabBuilder] TabBar GameObject not found under ContentRoot. Aborting.");
                return;
            }
            var tabBar = tabBarT.gameObject;

            // DevSelectionModule lives somewhere in the hierarchy (on SelectTab)
            var selectionModule = contents.GetComponentInChildren<DevSelectionModule>(true);
            if (selectionModule == null)
            {
                Debug.LogError("[DevInspectTabBuilder] DevSelectionModule not found in prefab. Aborting.");
                return;
            }

            // Grab any existing TMP font asset to reuse
            var referenceFont = FindFirstFontAsset(contents);
            if (referenceFont == null)
            {
                referenceFont = TMP_Settings.defaultFontAsset;
                Debug.LogWarning("[DevInspectTabBuilder] No TMP_Text with font found in prefab, falling back to TMP_Settings.defaultFontAsset.");
            }

            // --- Idempotence / destructive path ---------------------------------------
            var existingInspect = contentRoot.transform.Find(InspectContentName);
            var existingInspectBtn = tabBar.transform.Find(InspectTabButtonName);

            if (destructive)
            {
                if (existingInspect != null) UnityEngine.Object.DestroyImmediate(existingInspect.gameObject);
                if (existingInspectBtn != null) UnityEngine.Object.DestroyImmediate(existingInspectBtn.gameObject);
            }
            else
            {
                if (existingInspect != null || existingInspectBtn != null)
                {
                    Debug.LogWarning($"[DevInspectTabBuilder] InspectContent or InspectTabButton already exists in the prefab. Aborting build (use 'Rebuild Inspect Tab (Destructive)' to overwrite).");
                    return;
                }
            }

            // --- Build the outer Inspect tab button ----------------------------------
            GameObject inspectTabButtonGO = CreateTabButton(tabBar.transform, InspectTabButtonName, "Inspect", referenceFont);
            Button inspectTabButton = inspectTabButtonGO.GetComponent<Button>();

            // --- Build InspectContent --------------------------------------------------
            GameObject inspectContent = CreateUIGameObject(InspectContentName, contentRoot.transform);
            StretchToParent(inspectContent);

            // Add a vertical layout group so Placeholder / Views stack
            var inspectLayout = inspectContent.AddComponent<VerticalLayoutGroup>();
            inspectLayout.padding = new RectOffset(4, 4, 4, 4);
            inspectLayout.spacing = 4;
            inspectLayout.childControlWidth = true;
            inspectLayout.childControlHeight = true;
            inspectLayout.childForceExpandWidth = true;
            inspectLayout.childForceExpandHeight = true;

            // Placeholder ---------------------------------------------------------------
            GameObject placeholder = CreateTMPLabel(inspectContent.transform, "Placeholder",
                "Select an InteractableObject to inspect it.", referenceFont, fontSize: 18,
                alignment: TextAlignmentOptions.Center);
            // Make placeholder take at least a full row height
            var phLE = placeholder.AddComponent<LayoutElement>();
            phLE.minHeight = 40;
            phLE.flexibleHeight = 1;

            // Views parent --------------------------------------------------------------
            GameObject views = CreateUIGameObject("Views", inspectContent.transform);
            var viewsLayout = views.AddComponent<VerticalLayoutGroup>();
            viewsLayout.padding = new RectOffset(0, 0, 0, 0);
            viewsLayout.spacing = 0;
            viewsLayout.childControlWidth = true;
            viewsLayout.childControlHeight = true;
            viewsLayout.childForceExpandWidth = true;
            viewsLayout.childForceExpandHeight = true;
            var viewsLE = views.AddComponent<LayoutElement>();
            viewsLE.flexibleHeight = 10;

            // CharacterInspectorView ---------------------------------------------------
            GameObject inspectorViewGO = CreateUIGameObject("CharacterInspectorView", views.transform);
            var inspectorViewLayout = inspectorViewGO.AddComponent<VerticalLayoutGroup>();
            inspectorViewLayout.padding = new RectOffset(0, 0, 0, 0);
            inspectorViewLayout.spacing = 4;
            inspectorViewLayout.childControlWidth = true;
            inspectorViewLayout.childControlHeight = true;
            inspectorViewLayout.childForceExpandWidth = true;
            inspectorViewLayout.childForceExpandHeight = true;

            var characterInspectorView = inspectorViewGO.AddComponent<CharacterInspectorView>();

            // Header label
            GameObject headerGO = CreateTMPLabel(inspectorViewGO.transform, "Header",
                "Inspecting: —", referenceFont, fontSize: 18, alignment: TextAlignmentOptions.Left);
            var headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.minHeight = 28;
            headerLE.preferredHeight = 28;
            headerLE.flexibleHeight = 0;
            TMP_Text headerText = headerGO.GetComponent<TMP_Text>();

            // Sub-tab button bar (Grid 5x2)
            GameObject subTabBarGO = CreateUIGameObject("TabBar", inspectorViewGO.transform);
            var grid = subTabBarGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(110, 28);
            grid.spacing = new Vector2(2, 2);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            var subTabBarLE = subTabBarGO.AddComponent<LayoutElement>();
            subTabBarLE.minHeight = 58;
            subTabBarLE.preferredHeight = 58;
            subTabBarLE.flexibleHeight = 0;

            // SubTabContents (parent of the 10 ScrollRects)
            GameObject subTabContentsGO = CreateUIGameObject("SubTabContents", inspectorViewGO.transform);
            var subTabContentsLayout = subTabContentsGO.AddComponent<VerticalLayoutGroup>();
            subTabContentsLayout.padding = new RectOffset(0, 0, 0, 0);
            subTabContentsLayout.spacing = 0;
            subTabContentsLayout.childControlWidth = true;
            subTabContentsLayout.childControlHeight = true;
            subTabContentsLayout.childForceExpandWidth = true;
            subTabContentsLayout.childForceExpandHeight = true;
            var subTabContentsLE = subTabContentsGO.AddComponent<LayoutElement>();
            subTabContentsLE.flexibleHeight = 10;
            subTabContentsLE.minHeight = 200;

            // Build 10 (tabButton, content, subTabComponent) triples ---------------------
            var subTabEntries = new List<(Button btn, GameObject content, CharacterSubTab tab)>(10);
            for (int i = 0; i < SubTabSpec.Length; i++)
            {
                var (displayName, componentTypeName) = SubTabSpec[i];

                // 1. Tab button
                GameObject btnGO = CreateTabButton(subTabBarGO.transform, $"Btn_{displayName}", displayName, referenceFont);
                var btn = btnGO.GetComponent<Button>();

                // 2. ScrollRect content
                GameObject contentGO = CreateScrollRect(subTabContentsGO.transform, displayName, referenceFont, out TMP_Text contentTMP);

                // 3. Sub-tab component (IdentitySubTab, StatsSubTab, ...)
                Type subTabType = FindTypeByName(componentTypeName);
                if (subTabType == null)
                {
                    Debug.LogError($"[DevInspectTabBuilder] Could not resolve type '{componentTypeName}' for sub-tab '{displayName}'. Skipping component add.");
                    continue;
                }
                var subTabComponent = (CharacterSubTab)contentGO.AddComponent(subTabType);

                // 4. Wire CharacterSubTab._content via SerializedObject (private field)
                var so = new SerializedObject(subTabComponent);
                var contentProp = so.FindProperty("_content");
                if (contentProp == null)
                {
                    Debug.LogError($"[DevInspectTabBuilder] CharacterSubTab._content serialized property not found on {componentTypeName}. Wiring skipped.");
                }
                else
                {
                    contentProp.objectReferenceValue = contentTMP;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                subTabEntries.Add((btn, contentGO, subTabComponent));
            }

            // --- Add DevInspectModule component on InspectContent ---------------------
            var devInspectModule = inspectContent.AddComponent<DevInspectModule>();

            // Wire DevInspectModule._selectionModule + _placeholder
            var diSO = new SerializedObject(devInspectModule);
            var selModProp = diSO.FindProperty("_selectionModule");
            var placeholderProp = diSO.FindProperty("_placeholder");
            if (selModProp != null) selModProp.objectReferenceValue = selectionModule;
            if (placeholderProp != null) placeholderProp.objectReferenceValue = placeholder;
            diSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Wire CharacterInspectorView._subTabs + _headerLabel ------------------
            var civSO = new SerializedObject(characterInspectorView);
            var subTabsProp = civSO.FindProperty("_subTabs");
            subTabsProp.arraySize = subTabEntries.Count;
            for (int i = 0; i < subTabEntries.Count; i++)
            {
                var element = subTabsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("TabButton").objectReferenceValue = subTabEntries[i].btn;
                element.FindPropertyRelative("Content").objectReferenceValue = subTabEntries[i].content;
                element.FindPropertyRelative("Tab").objectReferenceValue = subTabEntries[i].tab;
            }
            var headerLabelProp = civSO.FindProperty("_headerLabel");
            if (headerLabelProp != null) headerLabelProp.objectReferenceValue = headerText;
            civSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Append entry to DevModePanel._tabs -----------------------------------
            var dmpSO = new SerializedObject(devModePanel);
            var tabsProp = dmpSO.FindProperty("_tabs");
            int newIndex = tabsProp.arraySize;
            tabsProp.arraySize = newIndex + 1;
            var newTab = tabsProp.GetArrayElementAtIndex(newIndex);
            newTab.FindPropertyRelative("TabButton").objectReferenceValue = inspectTabButton;
            newTab.FindPropertyRelative("Content").objectReferenceValue = inspectContent;
            dmpSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Save ------------------------------------------------------------------
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool success);
            if (!success)
            {
                Debug.LogError("[DevInspectTabBuilder] PrefabUtility.SaveAsPrefabAsset returned success=false.");
            }
            else
            {
                Debug.Log($"[DevInspectTabBuilder] Inspect tab built successfully. 10 sub-tabs wired. Saved: {PrefabPath}");
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
    // Helpers
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

    /// <summary>
    /// Creates a Button GameObject (Image background + child Label TMP) with the given name and label.
    /// </summary>
    private static GameObject CreateTabButton(Transform parent, string name, string label, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // LayoutElement so it fits the HorizontalLayoutGroup / GridLayoutGroup parent
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 28;
        le.preferredHeight = 28;

        // Label child
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(1, 1);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        labelRT.localScale = Vector3.one;

        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        if (font != null) labelTMP.font = font;
        labelTMP.fontSize = 14;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = Color.white;
        labelTMP.enableWordWrapping = false;
        labelTMP.raycastTarget = false;

        return go;
    }

    /// <summary>
    /// Creates a ScrollRect hierarchy: rootGO with ScrollRect + Viewport (RectMask2D + Image) +
    /// Content (VerticalLayoutGroup + ContentSizeFitter) + TMP_Text. Returns the root GO and the inner TMP_Text.
    /// </summary>
    private static GameObject CreateScrollRect(Transform parent, string name, TMP_FontAsset font, out TMP_Text contentText)
    {
        // Root: ScrollRect + Image (bg)
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

        // Viewport
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
        viewportImg.color = new Color(1, 1, 1, 0.01f); // nearly invisible but needed for masking on some setups
        viewportImg.raycastTarget = true;

        // Content
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
        contentVL.childControlHeight = false; // let text size itself
        contentVL.childForceExpandWidth = true;
        contentVL.childForceExpandHeight = false;
        contentVL.childAlignment = TextAnchor.UpperLeft;

        var contentCSF = content.AddComponent<ContentSizeFitter>();
        contentCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Inner TMP text (the _content field of CharacterSubTab)
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

        // Wire ScrollRect refs
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

    private static Type FindTypeByName(string name)
    {
        // Scan AppDomain — sub-tab classes live in Assembly-CSharp in the global namespace.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = asm.GetType(name, throwOnError: false); }
            catch { /* ignore */ }
            if (t != null) return t;

            // Some classes may be in sub-namespace; fallback scan.
            try
            {
                foreach (var candidate in asm.GetTypes())
                {
                    if (candidate.Name == name) return candidate;
                }
            }
            catch { /* ReflectionTypeLoadException — ignore */ }
        }
        return null;
    }
}
#endif
