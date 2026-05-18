using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// The city's capital building. One per community (enforced by
/// <see cref="Community.IsChartered"/>). Placing an AB charters the community;
/// completing construction grants the founder citizenship.
///
/// Inherits from <see cref="CommercialBuilding"/>. Jobs (JobBuilder, JobHarvester,
/// JobLogisticsManager) are added in Plan 4b — <see cref="InitializeJobs"/> is a
/// deliberate no-op for Plan 4a.
///
/// Network safety (rule #19b): no new replication channels beyond the inherited
/// <c>_ownerIds</c> NetworkList (carries the auto-bound leaders). <see cref="OwnerCommunity"/>
/// is server-only state — Community is not a NetworkBehaviour.
/// </summary>
public class AdministrativeBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Administrative;

    // ── Owner community ───────────────────────────────────────────────────

    /// <summary>
    /// The community this AB charters. Server-only; set by
    /// <see cref="SetOwnerCommunity"/> during building placement (Plan 4a).
    /// Not replicated — clients that need community state pull through MapRegistry.
    /// </summary>
    public Community OwnerCommunity { get; private set; }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Idempotent. Links this AB to <paramref name="community"/>, back-points
    /// <c>community.AdministrativeBuilding = this</c>, and auto-adds every community
    /// leader as an owner of this building (mirrors <see cref="Room.AddOwner(Character)"/>).
    /// Server-only.
    /// </summary>
    public void SetOwnerCommunity(Community community)
    {
        if (!IsServer) return;
        if (OwnerCommunity == community) return;

        OwnerCommunity = community;

        if (community != null)
        {
            community.AdministrativeBuilding = this;
            TryBindLeadersAsOwners();
        }
    }

    /// <summary>
    /// Passes through to <see cref="CommercialBuilding.GetTreasuryBalance(MWI.Economy.CurrencyId)"/>.
    /// Convenience wrapper so callers in the city-founding pipeline don't need to cast.
    /// </summary>
    public int GetTreasuryBalance(MWI.Economy.CurrencyId currency)
        => base.GetTreasuryBalance(currency);

    // ── CommercialBuilding contract ───────────────────────────────────────

    /// <summary>
    /// Staffs the AB with the canonical city-management roster: two
    /// <see cref="JobBuilder"/>s, one <see cref="JobHarvester"/> (which switches into
    /// CityHarvester runtime mode when its workplace is an AB — Plan 4b Task 7), and
    /// one <see cref="JobLogisticsManager"/> (which extends to drive BuildOrder material
    /// sourcing — Plan 4b Task 6).
    /// </summary>
    protected override void InitializeJobs()
    {
        _jobs.Add(new JobBuilder("Builder"));
        _jobs.Add(new JobBuilder("Builder 2"));
        _jobs.Add(new JobHarvester("Harvester"));
        _jobs.Add(new JobLogisticsManager("Logistics Manager"));
    }

    // ── Unfulfillable-material harvest queue ─────────────────────────────

    /// <summary>
    /// Server-only scratch list. When <see cref="JobLogisticsManager.ProcessActiveBuildOrders"/>
    /// requests stock for a missing build material and every supplier tier (B2B shop scan,
    /// crafter producer chain, VirtualResourceSupplier) fails, the requested
    /// <c>(ItemSO, qty)</c> lands here. <see cref="JobHarvester"/>'s CityHarvester branch
    /// then scans for a nearby <see cref="Harvestable"/> yielding the wanted item and
    /// runs the existing harvest→pickup→deposit chain.
    /// </summary>
    private readonly List<UnfulfillableMaterial> _unfulfillableMaterialHarvestQueue = new List<UnfulfillableMaterial>();

    /// <summary>Server-only. Read-only view used by JobHarvester's CityHarvester branch.</summary>
    public IReadOnlyList<UnfulfillableMaterial> GetUnfulfillableHarvestQueue() => _unfulfillableMaterialHarvestQueue;

    /// <summary>
    /// Server-only. Adds (or refreshes) a queued material. Idempotent on
    /// <c>(item)</c> — re-enqueueing the same item with a higher demand overrides the
    /// stored qty; otherwise the existing entry is left alone (its <c>LastEnqueuedDay</c>
    /// is bumped so the entry stays "fresh").
    /// </summary>
    public void EnqueueUnfulfillableMaterial(ItemSO item, int qty)
    {
        if (!IsServer || item == null || qty <= 0) return;
        int day = MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentDay : 0;
        for (int i = 0; i < _unfulfillableMaterialHarvestQueue.Count; i++)
        {
            var entry = _unfulfillableMaterialHarvestQueue[i];
            if (entry.Item == item)
            {
                entry.Qty = Mathf.Max(entry.Qty, qty);
                entry.LastEnqueuedDay = day;
                return;
            }
        }
        _unfulfillableMaterialHarvestQueue.Add(new UnfulfillableMaterial(item, qty, day));
    }

    /// <summary>
    /// Server-only. Decrements a queued material entry after a successful harvest-deposit.
    /// Removes the entry entirely when its qty hits zero. No-op if the item isn't queued.
    /// </summary>
    public void DecrementUnfulfillableMaterial(ItemSO item, int qtyDelivered)
    {
        if (!IsServer || item == null || qtyDelivered <= 0) return;
        for (int i = 0; i < _unfulfillableMaterialHarvestQueue.Count; i++)
        {
            var entry = _unfulfillableMaterialHarvestQueue[i];
            if (entry.Item == item)
            {
                entry.Qty = Mathf.Max(0, entry.Qty - qtyDelivered);
                if (entry.Qty == 0) _unfulfillableMaterialHarvestQueue.RemoveAt(i);
                return;
            }
        }
    }

    // ── Building.OnFinalize override ──────────────────────────────────────

    /// <summary>
    /// Server-only. Fires immediately after construction completes (state flipped to
    /// Complete). Grants the community founder citizenship via
    /// <see cref="CharacterCommunity.SetCitizenship"/>. Idempotent — if OwnerCommunity
    /// is null (placement race or missing wiring), logs a warning and returns.
    /// </summary>
    protected override void OnFinalize()
    {
        if (OwnerCommunity == null)
        {
            Debug.LogWarning($"<color=orange>[AdministrativeBuilding.OnFinalize]</color> " +
                $"'{BuildingName}' completed but OwnerCommunity is null — citizenship not granted. " +
                "Was SetOwnerCommunity called during placement?");
            return;
        }

        Character founder = OwnerCommunity.PrimaryLeader;
        if (founder == null)
        {
            Debug.LogWarning($"<color=orange>[AdministrativeBuilding.OnFinalize]</color> " +
                $"'{BuildingName}': OwnerCommunity '{OwnerCommunity.communityName}' has no primary leader — citizenship not granted.");
            return;
        }

        if (founder.CharacterCommunity == null)
        {
            Debug.LogWarning($"<color=orange>[AdministrativeBuilding.OnFinalize]</color> " +
                $"Founder '{founder.CharacterName}' has no CharacterCommunity component — citizenship not granted.");
            return;
        }

        founder.CharacterCommunity.SetCitizenship(OwnerCommunity);
        Debug.Log($"<color=green>[AdministrativeBuilding]</color> '{BuildingName}' complete — " +
            $"'{founder.CharacterName}' is now a citizen of '{OwnerCommunity.communityName}'.");
    }

    // ── Civic placement (Plan 4c Task 4) ──────────────────────────────────

    /// <summary>
    /// Player UI entry for admin-console-driven civic building placement. Leader picks a
    /// tier-unlocked Civic blueprint, the UI sends prefabId + targetCell here. Server:
    /// 1. Validates requester is a leader of OwnerCommunity.
    /// 2. Validates blueprint is Civic + unlocked at current community tier.
    /// 3. Validates the host map's BuildingGrid.CanPlace at targetCell.
    /// 4. Delegates the actual spawn to <see cref="BuildingPlacementManager.PlaceCivicBuildingForLeader"/>.
    /// 5. Wires the new building into community.ownedBuildings + multi-owner + BuildingGrid.
    /// 6. Auto-creates a <see cref="BuildOrder"/> on this AB's LogisticsManager so
    ///    JobBuilder + JobLogisticsManager pick it up on the next tick (Plan 4b).
    /// 7. Replies via <see cref="PlaceCityBlueprintResultClientRpc"/> for a UI toast.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PlaceCityBlueprintServerRpc(string blueprintPrefabId, Vector2Int targetCell,
                                              ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        var single = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
        };

        if (OwnerCommunity == null)
        {
            PlaceCityBlueprintResultClientRpc(false, "AB has no owner community.", single);
            return;
        }

        Character requester = ResolveCharacterFromClientId(rpcParams.Receive.SenderClientId);
        if (requester == null || !OwnerCommunity.IsLeader(requester))
        {
            PlaceCityBlueprintResultClientRpc(false, "Not a leader of this community.", single);
            return;
        }

        // Resolve blueprint via WorldSettingsData.
        var settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
        var blueprint = settings != null ? settings.GetBuildingBlueprint(blueprintPrefabId) : null;
        if (blueprint == null)
        {
            PlaceCityBlueprintResultClientRpc(false, "Unknown blueprint.", single);
            return;
        }

        if (blueprint.BlueprintCategory != BlueprintCategory.Civic)
        {
            PlaceCityBlueprintResultClientRpc(false, "Personal blueprints must be placed via the normal flow, not the admin console.", single);
            return;
        }

        // Tier-unlock gate.
        var req = MWI.WorldSystem.CommunityTierRegistry.Get(OwnerCommunity.level);
        if (req == null || req.UnlockedBlueprints == null || !req.UnlockedBlueprints.Contains(blueprint))
        {
            PlaceCityBlueprintResultClientRpc(false, $"'{blueprint.BuildingName}' not unlocked at current tier.", single);
            return;
        }

        // Host map + BuildingGrid lookup.
        var hostMap = MapController.GetMapAtPosition(transform.position);
        if (hostMap == null || hostMap.BuildingGrid == null)
        {
            PlaceCityBlueprintResultClientRpc(false, "AB has no host map / grid.", single);
            return;
        }

        if (!hostMap.BuildingGrid.CanPlace(targetCell, blueprint.GridFootprintCells))
        {
            PlaceCityBlueprintResultClientRpc(false, "Cell occupied or out of bounds.", single);
            return;
        }

        Vector3 worldPos = hostMap.BuildingGrid.GetCellCenter(targetCell, transform.position.y);

        // Delegate to the BuildingPlacementManager on the leader's Character.
        var bpm = requester.GetComponentInChildren<BuildingPlacementManager>();
        if (bpm == null)
        {
            PlaceCityBlueprintResultClientRpc(false, "Leader has no BuildingPlacementManager subsystem.", single);
            return;
        }

        Building newBuilding = bpm.PlaceCivicBuildingForLeader(blueprint, requester, worldPos, Quaternion.identity);
        if (newBuilding == null)
        {
            PlaceCityBlueprintResultClientRpc(false, "Placement failed (see Console).", single);
            return;
        }

        // Wire community + multi-owner.
        if (!OwnerCommunity.ownedBuildings.Contains(newBuilding))
            OwnerCommunity.ownedBuildings.Add(newBuilding);
        foreach (var leader in OwnerCommunity.leaders)
        {
            if (leader == null) continue;
            try { newBuilding.AddOwner(leader); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // Register footprint on the grid (host map's BuildingGrid).
        if (newBuilding.NetworkObject != null)
        {
            hostMap.BuildingGrid.Register(newBuilding.NetworkObject.NetworkObjectId, targetCell, blueprint.GridFootprintCells);
        }

        // Auto-create the BuildOrder. JobLogisticsManager.ProcessActiveBuildOrders (Plan 4b
        // Task 6) consumes it on the next tick; JobBuilder picks it up via CurrentBuildOrder.
        if (LogisticsManager != null && newBuilding.IsUnderConstruction)
        {
            int day = MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentDay : 0;
            var order = new BuildOrder(newBuilding, this, requester, day);
            LogisticsManager.AddBuildOrder(order);
        }

        PlaceCityBlueprintResultClientRpc(true, $"{blueprint.BuildingName} placed.", single);
    }

    [ClientRpc]
    private void PlaceCityBlueprintResultClientRpc(bool ok, string message, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"<color={(ok ? "green" : "orange")}>[AdministrativeBuilding]</color> Civic placement: {message}");
    }

    // ── Tier-up (Plan 4c Task 3) ──────────────────────────────────────────

    /// <summary>
    /// Player UI entry for community tier-up. Resolves the requesting Character from the
    /// ServerRpc's ClientId, gates on leader-of-OwnerCommunity, then delegates to
    /// <see cref="Community.TryPromoteLevel"/>. Result is broadcast to the requester only
    /// via <see cref="TierUpResultClientRpc"/> for a UI toast.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestPromoteLevelServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        var single = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
        };

        if (OwnerCommunity == null)
        {
            TierUpResultClientRpc(false, "AB has no owner community.", single);
            return;
        }

        Character requester = ResolveCharacterFromClientId(rpcParams.Receive.SenderClientId);
        if (requester == null)
        {
            TierUpResultClientRpc(false, "Could not resolve requester.", single);
            return;
        }

        if (!OwnerCommunity.IsLeader(requester))
        {
            TierUpResultClientRpc(false, "Not a leader of this community.", single);
            return;
        }

        var (ok, reason) = OwnerCommunity.TryPromoteLevel(this);
        TierUpResultClientRpc(ok, ok ? $"Promoted to {OwnerCommunity.level}!" : reason ?? "Promotion denied.", single);
    }

    [ClientRpc]
    private void TierUpResultClientRpc(bool ok, string message, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"<color={(ok ? "green" : "orange")}>[AdministrativeBuilding]</color> Tier-up result: {message}");
        // UI consumers can subscribe to a static event on PlayerUI if needed. Plan 4c
        // Task 7 wires the UI_CityManagementPanel's TierUpTab to display the toast.
    }

    /// <summary>Server-side resolver: ClientId → Player Character via NGO's connected-client table.</summary>
    private static Character ResolveCharacterFromClientId(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var nc)) return null;
        return nc?.PlayerObject?.GetComponent<Character>();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Adds every current leader of <see cref="OwnerCommunity"/> as an owner of this
    /// building (via <see cref="Room.AddOwner(Character)"/>). Server-only; called from
    /// <see cref="SetOwnerCommunity"/>. Safe to call when the community has no leaders yet.
    /// </summary>
    private void TryBindLeadersAsOwners()
    {
        if (!IsServer || OwnerCommunity == null) return;

        foreach (Character leader in OwnerCommunity.leaders)
        {
            if (leader == null) continue;
            try { AddOwner(leader); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[AdministrativeBuilding.TryBindLeadersAsOwners]</color> " +
                    $"Failed to add leader '{leader.CharacterName}' as owner of '{BuildingName}'.");
            }
        }
    }
}
