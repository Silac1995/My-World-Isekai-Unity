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

        if (_harvestable != null) _harvestable.OnNetSyncChanged();
    }

    public override void OnNetworkDespawn()
    {
        CurrentStage.OnValueChanged -= HandleAnyChange;
        IsDepleted.OnValueChanged -= HandleAnyChange;
        CropIdNet.OnValueChanged -= HandleCropIdChange;
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
