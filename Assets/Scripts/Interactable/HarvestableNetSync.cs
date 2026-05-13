using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sibling NetworkBehaviour on a runtime-spawned <see cref="Harvestable"/> prefab. Hosts the
/// three NetworkVariables that drive the harvestable's networked visible state on every peer:
///
/// <list type="bullet">
/// <item><see cref="CurrentStage"/> — 0..N. Drives growth visual + maturity gate. For crops,
///       mature when CurrentStage &gt;= CropSO.DaysToMature. Non-staged resources can use 0.</item>
/// <item><see cref="IsDepleted"/> — post-harvest state for refillable nodes (perennial crops,
///       respawning ore veins).</item>
/// <item><see cref="CropIdNet"/> — the <see cref="MWI.Interactables.HarvestableSO"/> Id, so
///       clients can resolve the SO from a registry on join without the server-side `_so`
///       reference being networked. Field name kept (not renamed to ContentIdNet) to preserve
///       existing prefab serialised data through the 2026-04-29 unification.</item>
/// </list>
///
/// Sits next to <see cref="Harvestable"/> because <see cref="InteractableObject"/> is a plain
/// MonoBehaviour and can't host NetVars directly. Server is sole writer. Late-joiners receive
/// the current values automatically via NGO's initial-sync, then OnValueChanged routes to
/// <see cref="Harvestable.OnNetSyncChanged"/>.
///
/// Renamed from <c>MWI.Farming.CropHarvestableNetSync</c> in the 2026-04-29 unification (Phase 3)
/// and moved to the global namespace alongside <see cref="Harvestable"/>. The script GUID is
/// preserved (.meta moved verbatim) so existing CropHarvestable_*.prefab references continue
/// to resolve. Now opt-in (no <c>[RequireComponent]</c>) — wild scenery harvestables that
/// don't need networking simply omit it from their prefab.
/// </summary>
public class HarvestableNetSync : NetworkBehaviour
{
    public NetworkVariable<int> CurrentStage = new NetworkVariable<int>(0);
    public NetworkVariable<bool> IsDepleted = new NetworkVariable<bool>(false);
    public NetworkVariable<FixedString64Bytes> CropIdNet = new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>Server-replicated remaining harvest count for the layered tree visual.
    /// Drives the per-fruit visibility on every peer so harvesting an apple makes the
    /// matching fruit sprite disappear. Capped at 255 — trees with MaxHarvestCount &gt; 255
    /// would clip; revisit if that ever happens (no current designer wants > 255 fruits).</summary>
    public NetworkVariable<byte> RemainingYield = new NetworkVariable<byte>(0);

    /// <summary>Server-replicated seed mixed with <see cref="NetworkObject.NetworkObjectId"/>
    /// to deterministically position runtime-spawned fruits. Re-rolled on every refill
    /// (<see cref="Harvestable.SetReady"/>) so a perennial tree shows a fresh apple layout
    /// each cycle rather than the same arrangement forever. <see cref="HarvestableLayeredVisual"/>
    /// subscribes to <see cref="OnValueChanged"/> and re-positions the existing fruit
    /// instances (no destroy/respawn) when this flips.</summary>
    public NetworkVariable<int> FruitRandomSeed = new NetworkVariable<int>(0);

    private Harvestable _harvestable;

    private void Awake()
    {
        _harvestable = GetComponent<Harvestable>();
    }

    public override void OnNetworkSpawn()
    {
        CurrentStage.OnValueChanged += HandleAnyChange;
        IsDepleted.OnValueChanged += HandleAnyChange;
        CropIdNet.OnValueChanged += HandleCropIdChange;
        RemainingYield.OnValueChanged += HandleAnyChange;
        // FruitRandomSeed is subscribed by HarvestableLayeredVisual directly — keep it off the
        // generic HandleAnyChange bridge so we don't trigger ApplyVisual / sprite refreshes
        // on a refill seed re-roll (the layered visual is the sole consumer).

        if (IsServer) BootstrapScenePlacedCropTree();

        if (_harvestable != null) _harvestable.OnNetSyncChanged();
    }

    /// <summary>
    /// Server-only. Scene-authored crop-tree prefabs (dragged into a scene at edit time) never
    /// go through <see cref="Harvestable.InitializeAtStage"/>, so the NetVars stay at their
    /// declaration defaults — CurrentStage=0, RemainingYield=0, CropIdNet empty. For a CropSO-
    /// driven tree this leaves every peer rendering a stage-0 sapling forever with no fruits.
    /// We detect that case (CropIdNet still empty + SO is a CropSO + not cell-coupled) and
    /// bootstrap the NetVars to a mature, full-yield state so a scene-dragged apple tree just
    /// works. Skipped for cell-coupled harvestables — <see cref="MWI.Farming.FarmGrowthSystem"/>
    /// already wrote the NetVars via InitializeAtStage before this Spawn fired, so CropIdNet
    /// is non-empty and this method is a no-op.
    /// </summary>
    private void BootstrapScenePlacedCropTree()
    {
        if (_harvestable == null) return;
        if (_harvestable.IsCellCoupled) return;
        if (CropIdNet.Value.Length > 0) return;
        if (!(_harvestable.SO is MWI.Farming.CropSO crop)) return;

        // Mirror the SO's authoring into Harvestable's inline serialized cache. The prefab
        // ships with inline override values (_harvestOutputs / _destructionOutputs / tools /
        // _maxHarvestCount) that can drift from the SO over time — runtime paths like
        // Harvest() and CanHarvestWith() read the inline cache, so without this sync a
        // scene-placed crop tree's harvest yields the prefab's stale overrides instead of
        // the SO's current authoring. InitializeAtStage normally handles this for planted
        // crops; the scene-placed path skips InitializeAtStage so we hydrate explicitly.
        _harvestable.HydrateInlineFieldsFromSO();

        CropIdNet.Value = new FixedString64Bytes(crop.Id ?? string.Empty);
        CurrentStage.Value = crop.DaysToMature;
        RemainingYield.Value = (byte)Mathf.Min(byte.MaxValue, crop.MaxHarvestCount);
        // IsDepleted defaults to false — already correct.
    }

    public override void OnNetworkDespawn()
    {
        CurrentStage.OnValueChanged -= HandleAnyChange;
        IsDepleted.OnValueChanged -= HandleAnyChange;
        CropIdNet.OnValueChanged -= HandleCropIdChange;
        RemainingYield.OnValueChanged -= HandleAnyChange;
    }

    private void HandleAnyChange<T>(T _, T __)
    {
        if (_harvestable != null) _harvestable.OnNetSyncChanged();
    }

    private void HandleCropIdChange(FixedString64Bytes _, FixedString64Bytes __)
    {
        if (_harvestable != null) _harvestable.OnCropIdResolved();
    }
}
