using UnityEngine;
using UnityEditor;

/// <summary>
/// One-time setup: configures the BattleManager_Prefab with a translucent border material
/// on its LineRenderer and a child ParticleSystem for ambient edge particles.
/// Run from: Tools > Battle Zone Visuals > Setup Prefab
/// </summary>
public static class BattleZoneVisualSetup
{
    private const string PREFAB_PATH = "Assets/Prefabs/BattleManager_Prefab.prefab";
    private const string BORDER_MAT_PATH = "Assets/Materials/M_BattleZoneBorder.mat";
    private const string PARTICLE_MAT_PATH = "Assets/Materials/M_BattleZoneParticle.mat";

    [MenuItem("Tools/Battle Zone Visuals/Setup Prefab")]
    public static void SetupPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"[BattleZoneVisualSetup] Prefab not found at {PREFAB_PATH}");
            return;
        }

        var borderMat = AssetDatabase.LoadAssetAtPath<Material>(BORDER_MAT_PATH);
        var particleMat = AssetDatabase.LoadAssetAtPath<Material>(PARTICLE_MAT_PATH);

        if (borderMat == null || particleMat == null)
        {
            Debug.LogError("[BattleZoneVisualSetup] Materials not found. Ensure M_BattleZoneBorder.mat and M_BattleZoneParticle.mat exist.");
            return;
        }

        // Open prefab for editing
        string assetPath = AssetDatabase.GetAssetPath(prefab);
        var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

        // --- Configure LineRenderer ---
        var line = prefabRoot.GetComponent<LineRenderer>();
        if (line != null)
        {
            line.material = borderMat;
            line.widthMultiplier = 0.15f;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // Smooth alpha gradient: full at center, fading at edges
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            line.colorGradient = gradient;

            Debug.Log("[BattleZoneVisualSetup] LineRenderer configured with border material.");
        }

        // --- Create or find child ParticleSystem ---
        Transform existingChild = prefabRoot.transform.Find("BattleZoneParticles");
        GameObject particleGO;

        if (existingChild != null)
        {
            particleGO = existingChild.gameObject;
            Debug.Log("[BattleZoneVisualSetup] Existing BattleZoneParticles child found — reconfiguring.");
        }
        else
        {
            particleGO = new GameObject("BattleZoneParticles");
            particleGO.transform.SetParent(prefabRoot.transform, false);
            particleGO.transform.localPosition = Vector3.zero;
            particleGO.AddComponent<ParticleSystem>();
            Debug.Log("[BattleZoneVisualSetup] Created BattleZoneParticles child.");
        }

        var ps = particleGO.GetComponent<ParticleSystem>();
        ConfigureParticleSystem(ps, particleMat);

        // --- Wire BattleManager._battleZoneParticles ---
        var battleManager = prefabRoot.GetComponent<BattleManager>();
        if (battleManager != null)
        {
            var so = new SerializedObject(battleManager);
            var prop = so.FindProperty("_battleZoneParticles");
            if (prop != null)
            {
                prop.objectReferenceValue = ps;
                so.ApplyModifiedProperties();
                Debug.Log("[BattleZoneVisualSetup] Wired _battleZoneParticles on BattleManager.");
            }
        }

        // Save prefab
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        Debug.Log("<color=green>[BattleZoneVisualSetup]</color> Prefab setup complete.");
    }

    private static void ConfigureParticleSystem(ParticleSystem ps, Material material)
    {
        // Main module
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor = new Color(1.2f, 0.9f, 0.4f, 0.5f);
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true;
        main.gravityModifier = 0f;

        // Emission
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 25f;

        // Shape — box edge emission
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.boxThickness = new Vector3(1, 1, 1); // edge-only emission
        shape.scale = new Vector3(25f, 0.1f, 10f); // default base zone size
        shape.position = Vector3.zero;

        // Color over Lifetime — fade in, hold, fade out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var alphaGradient = new Gradient();
        alphaGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(alphaGradient);

        // Size over Lifetime — gentle shrink
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));

        // Velocity over Lifetime — slight upward drift
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f);
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;

        // Renderer
        var renderer = particleGO_GetRenderer(ps);
        if (renderer != null)
        {
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static ParticleSystemRenderer particleGO_GetRenderer(ParticleSystem ps)
    {
        return ps.GetComponent<ParticleSystemRenderer>();
    }
}
