using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
    /// Plan 4b adds JobBuilder, JobHarvester, and JobLogisticsManager here.
    /// Deliberate no-op for Plan 4a — the skeleton ships without staffed roles.
    /// </summary>
    protected override void InitializeJobs() { }

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
