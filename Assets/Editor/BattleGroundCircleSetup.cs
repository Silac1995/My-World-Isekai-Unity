using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using System.IO;
using System.Linq;

/// <summary>
/// One-time setup utility for Battle Ground Circle Indicators.
/// Run from: Tools > Battle Ground Circles > Full Setup
/// After running, delete this script — it's not needed at runtime.
/// </summary>
public static class BattleGroundCircleSetup
{
    private const string SHADER_PATH = "Assets/Shaders/BattleGroundCircle.shader";
    private const string ALLY_MAT_PATH = "Assets/Materials/BattleGroundCircle_Ally_Mat.mat";
    private const string ENEMY_MAT_PATH = "Assets/Materials/BattleGroundCircle_Enemy_Mat.mat";
    private const string PREFAB_PATH = "Assets/Prefabs/BattleGroundCircle.prefab";
    private const string PC_RENDERER_PATH = "Assets/Settings/PC_Renderer.asset";
    private const string MOBILE_RENDERER_PATH = "Assets/Settings/Mobile_Renderer.asset";

    private static readonly string[] CHARACTER_PREFAB_PATHS = new[]
    {
        "Assets/Prefabs/Character/Character_Default.prefab",
        "Assets/Prefabs/Character/Character_Default_Humanoid.prefab",
        "Assets/Prefabs/Character/Character_Default_Quadruped.prefab"
    };

    [MenuItem("Tools/Battle Ground Circles/Full Setup")]
    public static void RunFullSetup()
    {
        Debug.Log("<color=cyan>[BattleCircleSetup]</color> Starting full setup...");

        Step1_EnableDecalFeature();
        Step2_CreateMaterials();
        Step3_CreatePrefab();
        Step4_WireCharacterPrefabs();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=green>[BattleCircleSetup]</color> Full setup complete! Verify in the Inspector.");
    }

    [MenuItem("Tools/Battle Ground Circles/1 - Enable Decal Feature")]
    public static void Step1_EnableDecalFeature()
    {
        string[] rendererPaths = { PC_RENDERER_PATH, MOBILE_RENDERER_PATH };

        foreach (string path in rendererPaths)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                Debug.LogWarning($"[BattleCircleSetup] Renderer not found at {path}");
                continue;
            }

            bool hasDecal = renderer.rendererFeatures.Any(f => f is DecalRendererFeature);

            if (!hasDecal)
            {
                var decalFeature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                decalFeature.name = "Decal";
                AssetDatabase.AddObjectToAsset(decalFeature, renderer);
                // Use SerializedObject to add to the rendererFeatures list properly
                var so = new SerializedObject(renderer);
                var features = so.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = decalFeature;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(decalFeature);
                EditorUtility.SetDirty(renderer);
                Debug.Log($"<color=green>[BattleCircleSetup]</color> Decal Feature ADDED to {path}");
            }
            else
            {
                Debug.Log($"[BattleCircleSetup] {path} already has Decal Feature");
            }
        }
    }

    [MenuItem("Tools/Battle Ground Circles/2 - Create Materials")]
    public static void Step2_CreateMaterials()
    {
        // Find the shader
        Shader shader = Shader.Find("Custom/BattleGroundCircle");
        if (shader == null)
        {
            // Try Shader Graph decal as fallback
            shader = Shader.Find("Shader Graphs/BattleGroundCircle");
        }
        if (shader == null)
        {
            Debug.LogError("[BattleCircleSetup] Shader 'Custom/BattleGroundCircle' not found! Make sure the shader file exists and compiles.");
            return;
        }

        // Create Ally material (Blue)
        if (!File.Exists(ALLY_MAT_PATH))
        {
            var allyMat = new Material(shader);
            allyMat.SetColor("_Color", new Color(0.2f, 0.5f, 1.0f, 0.7f));
            allyMat.SetFloat("_InnerRadius", 0.3f);
            allyMat.SetFloat("_OuterRadius", 0.5f);
            allyMat.SetFloat("_Softness", 0.05f);
            allyMat.SetFloat("_PulseSpeed", 2.0f);
            allyMat.SetFloat("_PulseIntensity", 0.15f);
            AssetDatabase.CreateAsset(allyMat, ALLY_MAT_PATH);
            Debug.Log($"<color=green>[BattleCircleSetup]</color> Created ally material at {ALLY_MAT_PATH}");
        }
        else
        {
            Debug.Log($"[BattleCircleSetup] Ally material already exists at {ALLY_MAT_PATH}");
        }

        // Create Enemy material (Red)
        if (!File.Exists(ENEMY_MAT_PATH))
        {
            var enemyMat = new Material(shader);
            enemyMat.SetColor("_Color", new Color(1.0f, 0.2f, 0.2f, 0.7f));
            enemyMat.SetFloat("_InnerRadius", 0.3f);
            enemyMat.SetFloat("_OuterRadius", 0.5f);
            enemyMat.SetFloat("_Softness", 0.05f);
            enemyMat.SetFloat("_PulseSpeed", 2.0f);
            enemyMat.SetFloat("_PulseIntensity", 0.15f);
            AssetDatabase.CreateAsset(enemyMat, ENEMY_MAT_PATH);
            Debug.Log($"<color=green>[BattleCircleSetup]</color> Created enemy material at {ENEMY_MAT_PATH}");
        }
        else
        {
            Debug.Log($"[BattleCircleSetup] Enemy material already exists at {ENEMY_MAT_PATH}");
        }
    }

    [MenuItem("Tools/Battle Ground Circles/3 - Create Prefab")]
    public static void Step3_CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) != null)
        {
            Debug.Log($"[BattleCircleSetup] Prefab already exists at {PREFAB_PATH}");
            return;
        }

        // Grab Unity's built-in Quad mesh from a temporary primitive
        var tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var quadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tempQuad);

        // Create root GameObject — no baked rotation needed; BattleCircleManager sets world rotation at spawn
        var go = new GameObject("BattleGroundCircle");
        go.transform.localScale = new Vector3(2f, 2f, 1f);

        var meshFilter   = go.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = quadMesh;

        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows    = false;

        // Add BattleGroundCircle script and wire MeshRenderer via SerializedObject
        var circleScript = go.AddComponent<BattleGroundCircle>();
        var so = new SerializedObject(circleScript);
        so.FindProperty("_meshRenderer").objectReferenceValue = meshRenderer;
        so.ApplyModifiedProperties();

        // Save as prefab
        PrefabUtility.SaveAsPrefabAsset(go, PREFAB_PATH);
        Object.DestroyImmediate(go);

        Debug.Log($"<color=green>[BattleCircleSetup]</color> Created prefab at {PREFAB_PATH}");
    }

    [MenuItem("Tools/Battle Ground Circles/4 - Wire Character Prefabs")]
    public static void Step4_WireCharacterPrefabs()
    {
        // Load the battle circle prefab and materials
        var circlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        var allyMat = AssetDatabase.LoadAssetAtPath<Material>(ALLY_MAT_PATH);
        var enemyMat = AssetDatabase.LoadAssetAtPath<Material>(ENEMY_MAT_PATH);

        if (circlePrefab == null)
        {
            Debug.LogError("[BattleCircleSetup] BattleGroundCircle prefab not found! Run Step 3 first.");
            return;
        }

        foreach (string prefabPath in CHARACTER_PREFAB_PATHS)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[BattleCircleSetup] Character prefab not found: {prefabPath}");
                continue;
            }

            // Open prefab for editing
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

            // Check if BattleCircleManager child already exists
            var existingManager = prefabRoot.GetComponentInChildren<BattleCircleManager>();
            if (existingManager != null)
            {
                Debug.Log($"[BattleCircleSetup] {prefabPath} already has BattleCircleManager");
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                continue;
            }

            // Create child GameObject
            var managerGO = new GameObject("BattleCircleManager");
            managerGO.transform.SetParent(prefabRoot.transform);
            managerGO.transform.localPosition = Vector3.zero;

            // Add BattleCircleManager component
            var manager = managerGO.AddComponent<BattleCircleManager>();

            // Wire serialized fields via SerializedObject
            var so = new SerializedObject(manager);

            var characterProp = so.FindProperty("_character");
            characterProp.objectReferenceValue = prefabRoot.GetComponent<Character>();

            var prefabProp = so.FindProperty("_battleCirclePrefab");
            prefabProp.objectReferenceValue = circlePrefab;

            var allyProp = so.FindProperty("_allyMaterial");
            allyProp.objectReferenceValue = allyMat;

            var enemyProp = so.FindProperty("_enemyMaterial");
            enemyProp.objectReferenceValue = enemyMat;

            so.ApplyModifiedProperties();

            // Wire on the root Character component
            var character = prefabRoot.GetComponent<Character>();
            if (character != null)
            {
                var charSO = new SerializedObject(character);
                var bcmProp = charSO.FindProperty("_battleCircleManager");
                bcmProp.objectReferenceValue = manager;
                charSO.ApplyModifiedProperties();
            }

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            Debug.Log($"<color=green>[BattleCircleSetup]</color> Wired BattleCircleManager on {prefabPath}");
        }
    }
}
