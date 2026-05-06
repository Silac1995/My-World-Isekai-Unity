using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only sub-component on a Building. While the building is UnderConstruction,
/// ticks at 2 Hz: scans physical WorldItems inside Building.BuildingZone, buckets by
/// ItemSO, updates Building.ConstructionProgress + Building.DeliveredMaterials.
///
/// Purely observational — does not consume items. Item consumption happens inside
/// CharacterAction_FinishConstruction.OnTick (per-tick) and via Building.EvictLeftoversToPerimeter
/// on completion.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
[RequireComponent(typeof(Building))]
public class ConstructionSiteScanner : MonoBehaviour
{
    [Tooltip("Server tick cadence in seconds. Default 0.5s (2 Hz).")]
    [SerializeField] private float _tickIntervalSeconds = 0.5f;

    private Building _building;
    private float _tickTimer;

    // Reused per Rule #34 — zero per-tick allocation.
    private readonly List<WorldItem> _scratchItems = new List<WorldItem>(64);
    private readonly Dictionary<ItemSO, int> _bucketCache = new Dictionary<ItemSO, int>(8);

    private void Awake() => _building = GetComponent<Building>();

    private void Update()
    {
        if (_building == null) return;
        if (!_building.IsServer) return;
        if (!_building.IsUnderConstruction) return;

        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickIntervalSeconds) return;
        _tickTimer = 0f;

        try { Tick(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    private void Tick()
    {
        var zone = _building.BuildingZone;
        if (zone == null) return;

        // Scan physical items in the footprint.
        _building.GetPhysicalItemsInCollider(zone, _scratchItems);

        // Bucket by ItemSO. WorldItems are non-stacking — each instance counts as 1 unit.
        _bucketCache.Clear();
        foreach (var item in _scratchItems)
        {
            if (item == null || item.IsBeingCarried) continue;
            var instance = item.ItemInstance;
            if (instance == null) continue;
            var so = instance.ItemSO;
            if (so == null) continue;
            if (_bucketCache.TryGetValue(so, out int existing)) _bucketCache[so] = existing + 1;
            else _bucketCache[so] = 1;
        }

        // Compare against requirements; update DeliveredMaterials NetworkList by index.
        var reqs = _building.ConstructionRequirements;
        if (reqs == null) return;

        var list = _building.DeliveredMaterials;
        for (int i = 0; i < reqs.Count; i++)
        {
            var req = reqs[i];
            // CraftingIngredient is a struct — only the Item field can be null.
            if (req.Item == null) continue;

            int delivered = _bucketCache.TryGetValue(req.Item, out int b) ? Mathf.Min(b, req.Amount) : 0;
            UpsertDeliveredEntry(list, i, delivered);
        }

        // Recompute progress & write only on meaningful change.
        // For pre-action display (no consumption yet), summing the NetworkList against
        // required amounts is the right shape — _contributedMaterials is empty until
        // CharacterAction_FinishConstruction runs.
        float progress = ComputeProgressFromList(reqs, list);
        if (Mathf.Abs(progress - _building.ConstructionProgress.Value) > 0.001f)
        {
            _building.ConstructionProgress.Value = Mathf.Clamp01(progress);
        }
    }

    private static void UpsertDeliveredEntry(NetworkList<DeliveredMaterialEntry> list, int reqIndex, int delivered)
    {
        for (int j = 0; j < list.Count; j++)
        {
            var entry = list[j];
            if (entry.RequirementIndex == reqIndex)
            {
                if (entry.Delivered != delivered)
                {
                    list[j] = new DeliveredMaterialEntry { RequirementIndex = reqIndex, Delivered = delivered };
                }
                return;
            }
        }
        list.Add(new DeliveredMaterialEntry { RequirementIndex = reqIndex, Delivered = delivered });
    }

    private static float ComputeProgressFromList(IReadOnlyList<CraftingIngredient> reqs, NetworkList<DeliveredMaterialEntry> list)
    {
        int totalRequired = 0;
        int totalSatisfied = 0;
        for (int i = 0; i < reqs.Count; i++)
        {
            var r = reqs[i];
            if (r.Item == null) continue;
            totalRequired += r.Amount;

            int delivered = 0;
            for (int j = 0; j < list.Count; j++)
            {
                var e = list[j];
                if (e.RequirementIndex == i) { delivered = e.Delivered; break; }
            }
            totalSatisfied += Mathf.Min(delivered, r.Amount);
        }
        if (totalRequired <= 0) return 1f;
        return Mathf.Clamp01((float)totalSatisfied / totalRequired);
    }
}
