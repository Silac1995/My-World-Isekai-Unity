using UnityEngine;
using UnityEditor;
using TMPro;

/// <summary>
/// One-time setup utility for the Floating Text system.
/// Run from: Tools > Floating Text > Full Setup
/// After running, delete this script — it's not needed at runtime.
/// </summary>
public static class FloatingTextSetup
{
    private const string PREFAB_PATH = "Assets/Prefabs/UI/FloatingText.prefab";

    private static readonly string[] CHARACTER_PREFAB_PATHS = new[]
    {
        "Assets/Prefabs/Character/Character_Default.prefab",
        "Assets/Prefabs/Character/Character_Default_Humanoid.prefab",
        "Assets/Prefabs/Character/Character_Default_Quadruped.prefab"
    };

    [MenuItem("Tools/Floating Text/Full Setup")]
    public static void RunFullSetup()
    {
        Debug.Log("<color=cyan>[FloatingTextSetup]</color> Starting full setup...");

        Step1_CreatePrefab();
        Step2_WireCharacterPrefabs();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=green>[FloatingTextSetup]</color> Full setup complete! Verify in the Inspector.");
    }

    [MenuItem("Tools/Floating Text/1 - Create Prefab")]
    public static void Step1_CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) != null)
        {
            Debug.Log($"[FloatingTextSetup] Prefab already exists at {PREFAB_PATH}");
            return;
        }

        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }

        // Create GameObject
        var go = new GameObject("FloatingText");

        // Add TextMeshPro (world-space, not UI)
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.fontSize = 6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.sortingOrder = 100;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.text = "0";

        // Set RectTransform size for world-space text
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(4f, 2f);

        // Add FloatingTextElement component
        var element = go.AddComponent<FloatingTextElement>();

        // Wire the TMP reference on FloatingTextElement via SerializedObject
        var so = new SerializedObject(element);
        var textProp = so.FindProperty("_text");
        textProp.objectReferenceValue = tmp;
        so.ApplyModifiedProperties();

        // Save as prefab
        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        Object.DestroyImmediate(go);

        Debug.Log($"<color=green>[FloatingTextSetup]</color> Created prefab at {PREFAB_PATH}");
    }

    [MenuItem("Tools/Floating Text/2 - Wire Character Prefabs")]
    public static void Step2_WireCharacterPrefabs()
    {
        var floatingTextPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (floatingTextPrefab == null)
        {
            Debug.LogError("[FloatingTextSetup] FloatingText prefab not found! Run Step 1 first.");
            return;
        }

        foreach (string prefabPath in CHARACTER_PREFAB_PATHS)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[FloatingTextSetup] Character prefab not found: {prefabPath}");
                continue;
            }

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

            // Check if FloatingTextSpawner child already exists
            var existingSpawner = prefabRoot.GetComponentInChildren<FloatingTextSpawner>();
            if (existingSpawner != null)
            {
                Debug.Log($"[FloatingTextSetup] {prefabPath} already has FloatingTextSpawner");
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                continue;
            }

            // Create child GameObject
            var spawnerGO = new GameObject("FloatingTextSpawner");
            spawnerGO.transform.SetParent(prefabRoot.transform);
            spawnerGO.transform.localPosition = Vector3.zero;

            // Add FloatingTextSpawner component
            var spawner = spawnerGO.AddComponent<FloatingTextSpawner>();

            // Wire serialized fields via SerializedObject
            var so = new SerializedObject(spawner);

            var characterProp = so.FindProperty("_character");
            characterProp.objectReferenceValue = prefabRoot.GetComponent<Character>();

            var prefabProp = so.FindProperty("_floatingTextPrefab");
            prefabProp.objectReferenceValue = floatingTextPrefab;

            so.ApplyModifiedProperties();

            // Wire on the root Character component
            var character = prefabRoot.GetComponent<Character>();
            if (character != null)
            {
                var charSO = new SerializedObject(character);
                var fsProp = charSO.FindProperty("_floatingTextSpawner");
                fsProp.objectReferenceValue = spawner;
                charSO.ApplyModifiedProperties();
            }

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            Debug.Log($"<color=green>[FloatingTextSetup]</color> Wired FloatingTextSpawner on {prefabPath}");
        }
    }
}
