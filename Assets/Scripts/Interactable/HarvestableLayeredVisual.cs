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

        if (_foliageRenderer != null)
        {
            if (_treeSO.FoliageSprite != null)
            {
                _foliageRenderer.sprite = _treeSO.FoliageSprite;
                _foliageRenderer.enabled = true;
            }
            else
            {
                _foliageRenderer.enabled = false;
            }
        }
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

        Rect area = _treeSO.FruitSpawnArea;
        if (area == Rect.zero)
            area = ResolveFoliageBoundsAsRect();

        // Deterministic seed: NetworkObjectId is identical on every peer.
        var prevState = Random.state;
        Random.InitState((int)NetworkObject.NetworkObjectId);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Fruit{i}");
            go.transform.SetParent(_fruitContainer, worldPositionStays: false);

            float x = Random.Range(area.xMin, area.xMax);
            float y = Random.Range(area.yMin, area.yMax);
            // Slight per-fruit Z offset so any overlap has a deterministic depth order.
            go.transform.localPosition = new Vector3(x, y, -0.001f * (i + 1));
            go.transform.localScale = new Vector3(_treeSO.FruitScale.x, _treeSO.FruitScale.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            int spriteIdx = Random.Range(0, _treeSO.FruitSpriteVariants.Length);
            sr.sprite = _treeSO.FruitSpriteVariants[spriteIdx];
            sr.sortingOrder = 2 + i;

            _fruitInstances.Add(sr);
        }

        Random.state = prevState;
    }

    private Rect ResolveFoliageBoundsAsRect()
    {
        if (_foliageRenderer == null || _foliageRenderer.sprite == null)
            return new Rect(-0.5f, -0.5f, 1f, 1f);

        var b = _foliageRenderer.sprite.bounds;
        return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
    }

    private void Subscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay += RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged += HandleStateChanged;
        if (_netSync != null)
            _netSync.RemainingYield.OnValueChanged += HandleYieldChanged;
    }

    private void Unsubscribe()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnNewDay -= RefreshFoliageColor;
        if (_harvestable != null)
            _harvestable.OnStateChanged -= HandleStateChanged;
        if (_netSync != null)
            _netSync.RemainingYield.OnValueChanged -= HandleYieldChanged;
    }

    private void HandleStateChanged(Harvestable _) => RefreshFruitVisibility();
    private void HandleYieldChanged(byte _, byte __) => RefreshFruitVisibility();

    private void RefreshAll()
    {
        RefreshFoliageColor();
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
