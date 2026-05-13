using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Interactables;
using MWI.Farming;
using MWI.Time;

/// <summary>
/// Sibling NetworkBehaviour on a tree prefab. Drives the 3-layer composition:
///
/// <list type="number">
/// <item><b>Trunk</b> — static SpriteRenderer, sprite from the SO, never tinted.</item>
/// <item><b>Foliage</b> — static SpriteRenderer, sprite from the SO, tinted via
///       MaterialPropertyBlock by <see cref="TreeHarvestableSO.FoliageColorOverYear"/>
///       sampled at <see cref="TimeManager.CurrentYearProgress01"/>. Refreshed on
///       <see cref="TimeManager.OnNewDay"/>.</item>
/// <item><b>Fruit</b> — N runtime-spawned SpriteRenderers under <see cref="_fruitContainer"/>,
///       N = <see cref="HarvestableSO.MaxHarvestCount"/>, positioned deterministically
///       inside <see cref="TreeHarvestableSO.FruitSpawnArea"/> via a
///       <see cref="NetworkObject.NetworkObjectId"/>-seeded RNG so every peer sees the
///       same layout. Per-fruit visibility tracks <see cref="HarvestableNetSync.RemainingYield"/>.</item>
/// </list>
///
/// All updates are event-driven — zero per-frame work. MaterialPropertyBlock preserves
/// SRP batching (rule #25). Designed to coexist with the existing growth-stage scale
/// lerp in <see cref="Harvestable.ApplyVisual"/>; the trunk / foliage / fruit container
/// ride along on the root transform's localScale.
/// </summary>
public class HarvestableLayeredVisual : NetworkBehaviour
{
    [Header("Hand-wired children")]
    [SerializeField] private SpriteRenderer _trunkRenderer;
    [SerializeField] private SpriteRenderer _foliageRenderer;
    [Tooltip("Empty Transform child. Runtime-spawned fruit SpriteRenderers are parented here. Local-space FruitSpawnArea on the SO is interpreted in this Transform's frame.")]
    [SerializeField] private Transform _fruitContainer;

    private Harvestable _harvestable;
    private HarvestableNetSync _netSync;
    private TreeHarvestableSO _treeSO;
    private MaterialPropertyBlock _mpb;
    private readonly List<SpriteRenderer> _fruitInstances = new List<SpriteRenderer>();
    private bool _initialised;

    private void Awake()
    {
        _harvestable = GetComponent<Harvestable>();
        _netSync = GetComponent<HarvestableNetSync>();
        _mpb = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        TryInitialise();
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        DestroyFruitInstances();
        _initialised = false;
    }

    private void DestroyFruitInstances()
    {
        for (int i = 0; i < _fruitInstances.Count; i++)
        {
            if (_fruitInstances[i] != null)
                Destroy(_fruitInstances[i].gameObject);
        }
        _fruitInstances.Clear();
    }

    private void TryInitialise()
    {
        if (_initialised) return;
        if (_harvestable == null) return;
        _treeSO = _harvestable.SO as TreeHarvestableSO;
        if (_treeSO == null)
        {
            // Not a tree — disable so we don't carry overhead on rocks / crops / etc.
            enabled = false;
            return;
        }

        AssignStaticSprites();
        SpawnFruits();
        Subscribe();
        RefreshAll();

        _initialised = true;
    }

    private void AssignStaticSprites()
    {
        if (_trunkRenderer != null && _treeSO.TrunkSprite != null)
            _trunkRenderer.sprite = _treeSO.TrunkSprite;

        if (_foliageRenderer != null && _treeSO.FoliageSprite != null)
            _foliageRenderer.sprite = _treeSO.FoliageSprite;

        // Foliage visibility is owned by RefreshFoliageVisibility (maturity-gated).
    }

    private void SpawnFruits()
    {
        if (_fruitContainer == null) return;
        if (_treeSO.FruitSpriteVariants == null || _treeSO.FruitSpriteVariants.Length == 0) return;

        // Spawn count = SO.MaxHarvestCount so late-joiners on a half-harvested tree still
        // create the full slot set; RefreshFruitVisibility then hides already-harvested
        // fruits based on RemainingYield. Cap at byte.MaxValue (NetVar is one byte).
        int count = Mathf.Min(_treeSO.MaxHarvestCount, byte.MaxValue);
        if (count <= 0) return;

        SamplerKit kit = BuildSamplerKit();
        var prevState = Random.state;
        Random.InitState(ComputeFruitSeed());

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Fruit{i}");
            go.transform.SetParent(_fruitContainer, worldPositionStays: false);

            Vector2 localXY = SampleFruitPosition(in kit);

            // Slight per-fruit Z offset so any overlap has a deterministic depth order.
            go.transform.localPosition = new Vector3(localXY.x, localXY.y, -0.001f * (i + 1));
            go.transform.localScale = new Vector3(_treeSO.FruitScale.x, _treeSO.FruitScale.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            int spriteIdx = Random.Range(0, _treeSO.FruitSpriteVariants.Length);
            sr.sprite = _treeSO.FruitSpriteVariants[spriteIdx];
            sr.sortingOrder = 2 + i;

            _fruitInstances.Add(sr);
        }

        Random.state = prevState;
    }

    /// <summary>
    /// Re-positions the existing fruit GameObjects in place using the current
    /// <see cref="HarvestableNetSync.FruitRandomSeed"/>. Called when a perennial tree refills
    /// so each cycle shows a fresh apple layout instead of repeating the same arrangement
    /// forever. Sprite-variant assignments are NOT changed — the i-th apple keeps its sprite
    /// variant across refills, only the position shifts. We still draw a `Random.Range` per
    /// fruit for the sprite-index slot we ignore, keeping the RNG sequence in lockstep with
    /// <see cref="SpawnFruits"/>'s 3-draws-per-fruit pattern so the same seed always produces
    /// the same positions regardless of which method consumed the stream.
    /// </summary>
    private void RepositionFruits()
    {
        if (_fruitInstances.Count == 0) return;
        if (_treeSO == null || _treeSO.FruitSpriteVariants == null || _treeSO.FruitSpriteVariants.Length == 0) return;

        SamplerKit kit = BuildSamplerKit();
        var prevState = Random.state;
        Random.InitState(ComputeFruitSeed());

        for (int i = 0; i < _fruitInstances.Count; i++)
        {
            Vector2 localXY = SampleFruitPosition(in kit);
            // Burn the sprite-index draw to stay aligned with SpawnFruits' 3-draws-per-fruit
            // sequence — keeps the (seed → positions) mapping stable whether the first call
            // was SpawnFruits or RepositionFruits.
            Random.Range(0, _treeSO.FruitSpriteVariants.Length);

            var inst = _fruitInstances[i];
            if (inst != null)
                inst.transform.localPosition = new Vector3(localXY.x, localXY.y, -0.001f * (i + 1));
        }

        Random.state = prevState;
    }

    /// <summary>Seed = <c>NetworkObjectId ^ FruitRandomSeed</c>. NetworkObjectId is identical
    /// on every peer; FruitRandomSeed is a server-replicated NetVar that the server re-rolls
    /// on each <see cref="Harvestable.SetReady"/> (perennial refill). Combining the two keeps
    /// the layout deterministic across peers AND fresh per refill cycle.</summary>
    private int ComputeFruitSeed()
    {
        int idHash = unchecked((int)NetworkObject.NetworkObjectId);
        int seedNet = _netSync != null ? _netSync.FruitRandomSeed.Value : 0;
        return idHash ^ seedNet;
    }

    /// <summary>Holds the once-per-spawn / once-per-reposition sampler configuration so
    /// <see cref="SpawnFruits"/> and <see cref="RepositionFruits"/> can share the picker
    /// without duplicating the rect-vs-mesh branch + padding-inset logic.</summary>
    private struct SamplerKit
    {
        public bool UseMesh;
        public FoliageMeshSampler MeshSampler;
        public Rect RectArea;
    }

    private SamplerKit BuildSamplerKit()
    {
        var kit = new SamplerKit();
        Rect explicitArea = _treeSO.FruitSpawnArea;
        if (explicitArea == Rect.zero && TryBuildFoliageMeshSampler(out var mesh))
        {
            kit.UseMesh = true;
            kit.MeshSampler = mesh;
            return kit;
        }

        kit.UseMesh = false;
        kit.RectArea = explicitArea != Rect.zero ? explicitArea : ResolveFoliageBoundsAsRect();

        // Apply fruit padding (inset toward center) on the rect path. Mesh sampler applies
        // the same padding internally on its sprite-local coordinates before the offset.
        float p = _treeSO.FruitPadding;
        if (p > 0f)
        {
            float insetW = kit.RectArea.width * p * 0.5f;
            float insetH = kit.RectArea.height * p * 0.5f;
            kit.RectArea = new Rect(
                kit.RectArea.x + insetW,
                kit.RectArea.y + insetH,
                Mathf.Max(0.001f, kit.RectArea.width - insetW * 2f),
                Mathf.Max(0.001f, kit.RectArea.height - insetH * 2f));
        }
        return kit;
    }

    private Vector2 SampleFruitPosition(in SamplerKit kit)
    {
        return kit.UseMesh
            ? kit.MeshSampler.Sample()
            : new Vector2(
                Random.Range(kit.RectArea.xMin, kit.RectArea.xMax),
                Random.Range(kit.RectArea.yMin, kit.RectArea.yMax));
    }

    /// <summary>
    /// Builds an area-weighted triangle sampler over the foliage sprite's tight mesh, so
    /// fruit positions land *inside* the leaf silhouette rather than uniformly across a
    /// bounding rectangle. Triangles are weighted by their area so the result is a uniform
    /// distribution over the visible foliage. Output positions are in the FruitContainer's
    /// local space (sibling-to-sibling translation + foliage local-scale applied).
    ///
    /// Returns false if the sprite has no usable mesh (e.g. a runtime-generated atlas frame
    /// with empty vertex/triangle arrays); caller falls back to rect sampling.
    /// </summary>
    private bool TryBuildFoliageMeshSampler(out FoliageMeshSampler sampler)
    {
        sampler = default;
        if (_foliageRenderer == null || _foliageRenderer.sprite == null) return false;
        var sprite = _foliageRenderer.sprite;
        var verts = sprite.vertices;
        var tris = sprite.triangles;
        if (verts == null || verts.Length < 3 || tris == null || tris.Length < 3) return false;

        int triCount = tris.Length / 3;
        var cum = new float[triCount];
        float total = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector2 a = verts[tris[i * 3]];
            Vector2 b = verts[tris[i * 3 + 1]];
            Vector2 c = verts[tris[i * 3 + 2]];
            float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            total += area;
            cum[i] = total;
        }
        if (total <= 0f) return false;

        Vector3 offset = Vector3.zero;
        Vector3 scale = Vector3.one;
        if (_fruitContainer != null && _fruitContainer != _foliageRenderer.transform)
        {
            offset = _foliageRenderer.transform.localPosition - _fruitContainer.localPosition;
            scale = _foliageRenderer.transform.localScale;
        }

        float padding = _treeSO != null ? _treeSO.FruitPadding : 0f;
        sampler = new FoliageMeshSampler(verts, tris, cum, total, offset, scale, padding);
        return true;
    }

    /// <summary>Lightweight value type holding the precomputed cumulative-area table and the
    /// foliage-to-fruit-container affine offset. <see cref="Sample"/> performs one binary
    /// search + one barycentric mix per call. Allocation-free per sample.</summary>
    private readonly struct FoliageMeshSampler
    {
        private readonly Vector2[] _verts;
        private readonly ushort[] _tris;
        private readonly float[] _cum;
        private readonly float _total;
        private readonly Vector3 _offset;
        private readonly Vector3 _scale;
        private readonly float _padding;

        public FoliageMeshSampler(Vector2[] verts, ushort[] tris, float[] cum, float total,
                                  Vector3 offset, Vector3 scale, float padding)
        {
            _verts = verts;
            _tris = tris;
            _cum = cum;
            _total = total;
            _offset = offset;
            _scale = scale;
            _padding = padding;
        }

        public Vector2 Sample()
        {
            // Area-weighted triangle pick.
            float r = Random.Range(0f, _total);
            int idx = System.Array.BinarySearch(_cum, r);
            if (idx < 0) idx = ~idx;
            if (idx >= _cum.Length) idx = _cum.Length - 1;

            Vector2 va = _verts[_tris[idx * 3]];
            Vector2 vb = _verts[_tris[idx * 3 + 1]];
            Vector2 vc = _verts[_tris[idx * 3 + 2]];

            // Uniform barycentric inside the triangle: reflect (u, v) across (0.5, 0.5) when
            // u + v > 1 so the resulting (u, v, w) point is in the triangle, not the
            // mirrored quad half.
            float u = Random.value;
            float v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            float w = 1f - u - v;
            Vector2 inSprite = va * w + vb * u + vc * v;

            // Inset toward the foliage sprite's pivot (its center for a 0.5,0.5-pivot sprite)
            // by _padding fraction. Keeps fruit sprite extents from visually overhanging the
            // leaf silhouette when a sample lands right at the mesh boundary.
            inSprite *= (1f - _padding);

            return new Vector2(
                inSprite.x * _scale.x + _offset.x,
                inSprite.y * _scale.y + _offset.y);
        }
    }

    /// <summary>
    /// Foliage sprite bounds expressed in <see cref="_fruitContainer"/>'s local space, so
    /// fruits land on top of the visible leaves regardless of how the prefab parents the
    /// Foliage vs FruitContainer children. Handles two prefab shapes uniformly:
    /// <list type="bullet">
    /// <item>FruitContainer is a child of Foliage — offset is zero, bounds map 1:1.</item>
    /// <item>FruitContainer is a sibling of Foliage (current Tree Default layout) — the
    ///       position delta between the two transforms is added to the bounds so the rect
    ///       still covers the foliage.</item>
    /// </list>
    /// Also multiplies by Foliage's localScale relative to FruitContainer's so the area
    /// scales with a re-sized foliage. Used as the fallback when
    /// <see cref="TreeHarvestableSO.FruitSpawnArea"/> is <see cref="Rect.zero"/>.
    /// </summary>
    private Rect ResolveFoliageBoundsAsRect()
    {
        if (_foliageRenderer == null || _foliageRenderer.sprite == null)
            return new Rect(-0.5f, -0.5f, 1f, 1f);

        var b = _foliageRenderer.sprite.bounds;
        Vector2 min = new Vector2(b.min.x, b.min.y);
        Vector2 size = new Vector2(b.size.x, b.size.y);

        var foliageTf = _foliageRenderer.transform;
        if (_fruitContainer != null && foliageTf != null && _fruitContainer != foliageTf)
        {
            // Compose sibling-to-sibling: scale by foliage local scale, then add the local-
            // position delta from FruitContainer to Foliage. Works as long as both share the
            // same parent (the typical prefab layout); under different parents the math would
            // need to lift through the common ancestor — out of scope for v1.
            Vector3 fScale = foliageTf.localScale;
            min.x *= fScale.x;
            min.y *= fScale.y;
            size.x *= fScale.x;
            size.y *= fScale.y;

            Vector3 delta = foliageTf.localPosition - _fruitContainer.localPosition;
            min.x += delta.x;
            min.y += delta.y;
        }

        return new Rect(min.x, min.y, size.x, size.y);
    }

    private void Subscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay += RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged += HandleStateChanged;
        if (_netSync != null)
        {
            _netSync.RemainingYield.OnValueChanged += HandleYieldChanged;
            _netSync.CurrentStage.OnValueChanged += HandleStageChanged;
            _netSync.FruitRandomSeed.OnValueChanged += HandleFruitSeedChanged;
        }
    }

    private void Unsubscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay -= RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged -= HandleStateChanged;
        if (_netSync != null)
        {
            _netSync.RemainingYield.OnValueChanged -= HandleYieldChanged;
            _netSync.CurrentStage.OnValueChanged -= HandleStageChanged;
            _netSync.FruitRandomSeed.OnValueChanged -= HandleFruitSeedChanged;
        }
    }

    private void HandleFruitSeedChanged(int _, int __) => RepositionFruits();

    private void HandleStateChanged(Harvestable _)
    {
        RefreshFoliageVisibility();
        RefreshFruitVisibility();
    }

    private void HandleYieldChanged(byte _, byte __) => RefreshFruitVisibility();

    private void HandleStageChanged(int _, int __)
    {
        RefreshFoliageVisibility();
        RefreshFruitVisibility();
    }

    private void RefreshAll()
    {
        RefreshFoliageColor();
        RefreshFoliageVisibility();
        RefreshFruitVisibility();
    }

    private void RefreshFoliageColor()
    {
        if (_foliageRenderer == null || _treeSO == null || _treeSO.FoliageColorOverYear == null) return;
        if (TimeManager.Instance == null) return;

        Color c = _treeSO.FoliageColorOverYear.Evaluate(TimeManager.Instance.CurrentYearProgress01);
        _foliageRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_Color", c);
        _foliageRenderer.SetPropertyBlock(_mpb);
    }

    /// <summary>True when the tree has reached <see cref="MWI.Farming.CropSO.DaysToMature"/>.
    /// Non-CropSO trees (rare — e.g. a wild tree that opts into the layered visual without
    /// going through the farming pipeline) are always considered mature.</summary>
    private bool IsMature()
    {
        if (_netSync == null) return true;
        if (_treeSO is MWI.Farming.CropSO crop)
            return _netSync.CurrentStage.Value >= crop.DaysToMature;
        return true;
    }

    /// <summary>Foliage renderer is enabled only once the tree is mature, so saplings show
    /// trunk only. Re-evaluated whenever <see cref="HarvestableNetSync.CurrentStage"/> changes
    /// or the harvestable broadcasts a state change (refill, etc.).</summary>
    private void RefreshFoliageVisibility()
    {
        if (_foliageRenderer == null || _treeSO == null) return;
        bool hasSprite = _treeSO.FoliageSprite != null;
        _foliageRenderer.enabled = hasSprite && IsMature();
    }

    private void RefreshFruitVisibility()
    {
        if (_fruitInstances.Count == 0) return;

        int visible = ResolveVisibleFruitCount();
        for (int i = 0; i < _fruitInstances.Count; i++)
        {
            if (_fruitInstances[i] != null)
                _fruitInstances[i].enabled = i < visible;
        }
    }

    private int ResolveVisibleFruitCount()
    {
        if (_harvestable == null) return 0;
        if (_harvestable.IsDepleted) return 0;

        // Crop-aware: hide all fruit until mature.
        if (_harvestable.SO is MWI.Farming.CropSO crop && _netSync != null)
        {
            if (_netSync.CurrentStage.Value < crop.DaysToMature) return 0;
        }

        if (_netSync != null) return _netSync.RemainingYield.Value;
        return _harvestable.RemainingYield;
    }
}
