using UnityEngine;

/// <summary>
/// Renders a flat rectangular outline around a Building's <c>BuildingZone</c> footprint
/// while the building is in <see cref="MWI.WorldSystem.BuildingState.UnderConstruction"/>.
/// Goes dormant (GameObject deactivated) the rest of the time.
///
/// <para>
/// <b>Why this component exists (modularity):</b> Building.cs is already large.
/// Construction-state visuals are <i>peer-local cosmetics</i> with no networked state,
/// no save/load, no gameplay impact — they don't belong inside the Building facade.
/// This component subscribes to <see cref="Building.OnConstructionStateChanged"/> and
/// owns its own child GameObject + LineRenderer. Building never references it.
/// </para>
///
/// <para>
/// <b>Crash-safety (load-bearing):</b> the LineRenderer's Material is built in code via
/// <c>new Material(Shader.Find(settings.ShaderName))</c> using a <i>built-in shader</i>.
/// Zero <c>.mat</c> assets, zero shaders, zero texture references are serialized to disk.
/// The May 2026 Material::BuildProperties standalone-build crash class is structurally
/// impossible here. See [[material-buildproperties-standalone-crash]].
/// </para>
///
/// <para>
/// <b>Performance:</b> a single 4-vertex LineRenderer with <c>loop = true</c> → one draw
/// call. The runtime Material is a shared static singleton so every active outline batches
/// together. No <c>Update</c>; geometry is written once on creation and never touched again.
/// </para>
///
/// <para>
/// <b>Network behaviour:</b> not a NetworkBehaviour. <c>Building._currentState</c> replicates
/// via NetworkVariable and the event fires on every peer via the existing
/// <c>OnValueChanged</c> chain. Each peer maintains its own LineRenderer locally — no
/// per-peer divergence risk.
/// </para>
/// </summary>
// Run AFTER Building (default order = 0). Building.Start invokes
// EnsureConstructionGhostVisual, which scans every top-level child of the Building for
// Renderers and caches them in _extraOriginalRenderersToToggle. ApplyConstructionVisuals
// then disables every cached renderer while UnderConstruction — and a LineRenderer IS-A
// Renderer, so without this ordering our outline GO is created BEFORE the cache is built,
// gets swept in, and our LineRenderer is disabled exactly when we want it visible.
// Forcing this component's Start to run after Building.Start means our outline GO is
// created AFTER the cache snapshot is taken — invisible to ApplyConstructionVisuals.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class BuildingConstructionOutline : MonoBehaviour
{
    // NOTE: no [RequireComponent(typeof(Building))]. Building.cs is added on prefab
    // *variants* (ShopBuilding, FarmingBuilding, …); the base Building_prefab.prefab
    // has no Building component. This component lives on the base prefab and inherits
    // down to every variant, mirroring the BuildingCurtainSettingsHolder pattern.
    // GetComponent<Building>() in Awake will succeed on every variant at runtime.
    [Tooltip("Shared tuning asset. Authored once and assigned on Building_prefab.prefab so every variant inherits the same look. Leave null to fall back to inline defaults (yellow, 0.15 u thickness).")]
    [SerializeField] private BuildingConstructionOutlineSettings _settings;

    private Building _building;
    private GameObject _outlineGO;
    private LineRenderer _line;
    private bool _subscribed;

    /// <summary>
    /// One Material shared across every active outline in the scene so the engine can
    /// batch them into a single draw call. Built lazily on first use; lifetime tied to
    /// the static field (cleared on domain reload, recreated as needed).
    /// </summary>
    private static Material s_sharedMaterial;

    private void Awake()
    {
        _building = GetComponent<Building>();
        if (_building == null)
        {
            // Component is on the base Building_prefab.prefab (which has no Building)
            // and the prefab is somehow being instantiated standalone (test scenes,
            // raw debug placements). Silently no-op — variants always carry Building.
            return;
        }
    }

    private void OnEnable()
    {
        if (_building == null) return;
        if (_subscribed) return;
        _building.OnConstructionStateChanged += HandleStateChanged;
        _subscribed = true;

        // Best-effort early refresh. For freshly-placed buildings this typically reads
        // the default state (Complete) because Building.OnNetworkSpawn — which writes
        // _currentState — runs AFTER OnEnable. The authoritative refresh happens in
        // Start() below, mirroring Building.Start's own initial-state catch-up.
        Refresh(_building.CurrentState);
    }

    private void Start()
    {
        if (_building == null) return;
        // By Start(), Building.OnNetworkSpawn has run (which assigns _currentState for
        // freshly-placed buildings) and the NetworkVariable's replicated value for
        // late-joining clients has also arrived. NetworkVariable.OnValueChanged does NOT
        // re-fire for the initial spawn-payload value, so without this catch-up Refresh
        // the outline would never appear on the initial UnderConstruction state — only
        // on later transitions. Mirrors Building.Start's own initial-state catch-up via
        // ApplyConstructionVisuals(_currentState.Value) at Building.cs line ~564.
        Refresh(_building.CurrentState);
    }

    private void OnDisable()
    {
        if (_building != null && _subscribed)
        {
            _building.OnConstructionStateChanged -= HandleStateChanged;
            _subscribed = false;
        }
    }

    private void OnDestroy()
    {
        // Rule #16 — defensive unsubscribe in case OnDisable did not run.
        if (_building != null && _subscribed)
        {
            _building.OnConstructionStateChanged -= HandleStateChanged;
            _subscribed = false;
        }
    }

    private void HandleStateChanged(
        MWI.WorldSystem.BuildingState previousValue,
        MWI.WorldSystem.BuildingState newValue)
    {
        Refresh(newValue);
    }

    private void Refresh(MWI.WorldSystem.BuildingState state)
    {
        bool shouldShow = state == MWI.WorldSystem.BuildingState.UnderConstruction;
        if (!shouldShow)
        {
            if (_outlineGO != null) _outlineGO.SetActive(false);
            return;
        }

        if (!EnsureOutlineCreated()) return;
        _outlineGO.SetActive(true);
    }

    /// <summary>
    /// Lazily builds the LineRenderer child GameObject on first activation. Returns
    /// false if construction is impossible (no BoxCollider on BuildingZone, no shader
    /// resolves, etc.) — the component then silently no-ops on subsequent state changes.
    /// </summary>
    private bool EnsureOutlineCreated()
    {
        if (_outlineGO != null) return true;
        if (_building == null) return false;

        var bz = _building.BuildingZone;
        if (bz == null)
        {
            Debug.LogWarning($"[BuildingConstructionOutline] {_building.BuildingName}: BuildingZone is null — outline disabled.", this);
            return false;
        }

        var box = bz as BoxCollider;
        if (box == null)
        {
            Debug.LogWarning($"[BuildingConstructionOutline] {_building.BuildingName}: BuildingZone is not a BoxCollider (got {bz.GetType().Name}) — outline disabled.", this);
            return false;
        }

        // Resolve effective settings — fall back to inline defaults so the component
        // produces a visible outline even when the SO slot is unwired on a prefab.
        float width = _settings != null ? _settings.Width : 0.15f;
        Color color = _settings != null ? _settings.Color : new Color(1f, 0.85f, 0f, 1f);
        float heightOffset = _settings != null ? _settings.HeightOffset : 0.05f;
        int cornerVerts = _settings != null ? _settings.CornerVertices : 0;
        string primaryShader = _settings != null ? _settings.ShaderName : "Sprites/Default";
        string fallbackShader = _settings != null ? _settings.FallbackShaderName : "Unlit/Color";

        var mat = GetSharedMaterial(primaryShader, fallbackShader);
        if (mat == null)
        {
            Debug.LogWarning($"[BuildingConstructionOutline] {_building.BuildingName}: neither shader '{primaryShader}' nor '{fallbackShader}' resolved — outline disabled.", this);
            return false;
        }

        _outlineGO = new GameObject("ConstructionOutline");
        _outlineGO.transform.SetParent(transform, worldPositionStays: false);
        _outlineGO.transform.localPosition = Vector3.zero;
        _outlineGO.transform.localRotation = Quaternion.identity;
        _outlineGO.transform.localScale = Vector3.one;

        _line = _outlineGO.AddComponent<LineRenderer>();
        _line.useWorldSpace = false;
        _line.loop = true;
        _line.startWidth = width;
        _line.endWidth = width;
        _line.startColor = color;
        _line.endColor = color;
        _line.sharedMaterial = mat;
        _line.numCornerVertices = cornerVerts;
        _line.numCapVertices = 0;
        _line.alignment = LineAlignment.View;
        _line.textureMode = LineTextureMode.Stretch;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _line.allowOcclusionWhenDynamic = false;

        // 4 corners of the BoxCollider footprint, in building-root local space.
        //
        // Y math: BoxColliders on Building prefabs are often authored with `center.y` near
        // the visual centroid and `size.y` covering the full vertical extent — meaning the
        // box's *bottom face* (`center.y - size.y/2`) can land BELOW the building's pivot
        // (e.g., Lumberyard's BZ bottom = -0.43 with pivot at 0). Since placed buildings
        // always anchor pivot.y to the NavMesh ground, putting the outline at the box bottom
        // would render it underground. We clamp to max(box-bottom, 0) so the outline never
        // sinks below the building's pivot — and add HeightOffset on top to avoid Z-fight.
        float boxBottomLocalY = box.center.y - (box.size.y * 0.5f);
        float bottomY = Mathf.Max(boxBottomLocalY, 0f) + heightOffset;
        float hx = box.size.x * 0.5f;
        float hz = box.size.z * 0.5f;
        float cx = box.center.x;
        float cz = box.center.z;

        _line.positionCount = 4;
        _line.SetPosition(0, new Vector3(cx - hx, bottomY, cz - hz));
        _line.SetPosition(1, new Vector3(cx + hx, bottomY, cz - hz));
        _line.SetPosition(2, new Vector3(cx + hx, bottomY, cz + hz));
        _line.SetPosition(3, new Vector3(cx - hx, bottomY, cz + hz));
        return true;
    }

    /// <summary>
    /// Returns the shared runtime Material, building it on first call. Tries the primary
    /// built-in shader first, then the fallback. Never serialized — pure runtime construct.
    /// </summary>
    private static Material GetSharedMaterial(string primaryShader, string fallbackShader)
    {
        if (s_sharedMaterial != null) return s_sharedMaterial;

        Shader shader = null;
        if (!string.IsNullOrEmpty(primaryShader)) shader = Shader.Find(primaryShader);
        if (shader == null && !string.IsNullOrEmpty(fallbackShader)) shader = Shader.Find(fallbackShader);
        if (shader == null) return null;

        s_sharedMaterial = new Material(shader)
        {
            name = "BuildingConstructionOutline_SharedMaterial (runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_sharedMaterial;
    }
}
