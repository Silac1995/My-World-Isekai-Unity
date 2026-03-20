using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class SetupStatsUI
{
    public static void Run()
    {
        string baseWindowPath = "Assets/Resources/UI/Player HUD/UI_WindowBase.prefab";
        string playerHudPath = "Assets/Resources/UI/Player HUD/UI_PlayerHUD.prefab";
        
        string statSlotPath = "Assets/Resources/UI/Player HUD/UI_StatSlot.prefab";
        string statsWindowPath = "Assets/Resources/UI/Player HUD/UI_CharacterStats.prefab";

        // 1. CREATE STAT SLOT PREFAB
        GameObject statSlotGo = new GameObject("UI_StatSlot", typeof(RectTransform));
        var rectList = statSlotGo.GetComponent<RectTransform>();
        rectList.sizeDelta = new Vector2(300, 40);

        HorizontalLayoutGroup hlg = statSlotGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.spacing = 10;
        hlg.padding = new RectOffset(10, 10, 5, 5);

        // Name text
        GameObject nameGo = new GameObject("Text_StatName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(statSlotGo.transform, false);
        TextMeshProUGUI tmpName = nameGo.GetComponent<TextMeshProUGUI>();
        tmpName.text = "Strength";
        tmpName.fontSize = 18;
        tmpName.color = Color.white;
        tmpName.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject valGo = new GameObject("Text_StatValue", typeof(RectTransform), typeof(TextMeshProUGUI));
        valGo.transform.SetParent(statSlotGo.transform, false);
        TextMeshProUGUI tmpVal = valGo.GetComponent<TextMeshProUGUI>();
        tmpVal.text = "10.0";
        tmpVal.fontSize = 18;
        tmpVal.color = Color.yellow;
        tmpVal.alignment = TextAlignmentOptions.MidlineRight;

        UI_StatSlot slotScript = statSlotGo.AddComponent<UI_StatSlot>();

        SerializedObject soSlot = new SerializedObject(slotScript);
        soSlot.FindProperty("_statNameText").objectReferenceValue = tmpName;
        soSlot.FindProperty("_statValueText").objectReferenceValue = tmpVal;
        soSlot.ApplyModifiedProperties();

        GameObject savedSlotPrefab = PrefabUtility.SaveAsPrefabAsset(statSlotGo, statSlotPath);
        Object.DestroyImmediate(statSlotGo);

        // 2. CREATE STATS WINDOW VARIANT
        GameObject baseWindow = AssetDatabase.LoadAssetAtPath<GameObject>(baseWindowPath);
        GameObject statsInstance = (GameObject)PrefabUtility.InstantiatePrefab(baseWindow);
        statsInstance.name = "UI_CharacterStats";

        Transform panelMainBackground = statsInstance.transform.Find("Canvas/Panel_Main_Background");
        
        GameObject statsPanel = new GameObject("Panel_Stats", typeof(RectTransform));
        statsPanel.transform.SetParent(panelMainBackground, false);
        RectTransform statsRect = statsPanel.GetComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(0.5f, 0.5f);
        statsRect.anchorMax = new Vector2(0.5f, 0.5f);
        statsRect.sizeDelta = new Vector2(400, 500);
        statsRect.anchoredPosition = Vector2.zero;

        // Title
        GameObject titleObj = new GameObject("Text_Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleObj.transform.SetParent(statsPanel.transform, false);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1);
        titleRect.anchorMax = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(300, 50);
        titleRect.anchoredPosition = new Vector2(0, -30);
        TextMeshProUGUI tmpTitle = titleObj.GetComponent<TextMeshProUGUI>();
        tmpTitle.text = "CHARACTER STATS";
        tmpTitle.fontSize = 24;
        tmpTitle.alignment = TextAlignmentOptions.Center;

        // Container
        GameObject containerObj = new GameObject("Grid_Stats", typeof(RectTransform));
        containerObj.transform.SetParent(statsPanel.transform, false);
        RectTransform containerRect = containerObj.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0);
        containerRect.anchorMax = new Vector2(1, 1);
        containerRect.offsetMin = new Vector2(20, 20);
        containerRect.offsetMax = new Vector2(-20, -70); // leave room for title

        GridLayoutGroup grid = containerObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(170, 35);
        grid.spacing = new Vector2(10, 5);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2; // two columns of stats!

        UI_CharacterStats statsUI = statsInstance.AddComponent<UI_CharacterStats>();
        SerializedObject soStats = new SerializedObject(statsUI);
        soStats.FindProperty("_slotContainer").objectReferenceValue = containerObj.transform;
        soStats.FindProperty("_statSlotPrefab").objectReferenceValue = savedSlotPrefab;
        soStats.ApplyModifiedProperties();

        GameObject savedStatsPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(statsInstance, statsWindowPath, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(statsInstance);

        // 3. INTEGRATE INTO PLAYER HUD
        GameObject hudGo = PrefabUtility.LoadPrefabContents(playerHudPath);
        
        Transform buttonsContainer = hudGo.transform.Find("Canvas/Panel_RightSide/Panel_Buttons");
        if (buttonsContainer == null)
        {
            buttonsContainer = hudGo.transform.Find("Canvas").GetComponentInChildren<HorizontalLayoutGroup>()?.transform;
        }

        Transform relationBtnTrans = hudGo.transform.Find("Canvas/Panel_RightSide/Panel_Buttons/Button_Relations");
        if (relationBtnTrans == null && buttonsContainer != null) 
        {
            // fallback
            foreach(Transform t in buttonsContainer) {
                if (t.name.Contains("Relations")) relationBtnTrans = t;
            }
        }

        Button statsBtn = null;
        if (relationBtnTrans != null)
        {
            GameObject newBtnGo = Object.Instantiate(relationBtnTrans.gameObject, relationBtnTrans.parent);
            newBtnGo.name = "Button_Stats";
            TextMeshProUGUI btnText = newBtnGo.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "Stats";
            statsBtn = newBtnGo.GetComponent<Button>();
        }

        // Add Window to HUD Canvas
        Transform hudCanvas = hudGo.transform.Find("Canvas");
        GameObject statsWindowInst = (GameObject)PrefabUtility.InstantiatePrefab(savedStatsPrefab, hudCanvas);
        statsWindowInst.name = "UI_CharacterStats";
        statsWindowInst.SetActive(false); // Default hidden

        PlayerUI playerUI = hudGo.GetComponent<PlayerUI>();
        if (playerUI != null)
        {
            SerializedObject soHud = new SerializedObject(playerUI);
            if (statsBtn != null) soHud.FindProperty("_buttonStatsUI").objectReferenceValue = statsBtn;
            soHud.FindProperty("_statsUI").objectReferenceValue = statsWindowInst.GetComponent<UI_CharacterStats>();
            soHud.ApplyModifiedProperties();
        }

        PrefabUtility.SaveAsPrefabAsset(hudGo, playerHudPath);
        PrefabUtility.UnloadPrefabContents(hudGo);

        Debug.Log("<color=cyan>SetupStatsUI automation complete!</color>");
    }
}
