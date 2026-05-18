using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Server-side group container. Holds members, leaders (List — primary at index 0,
/// secondaries 1..n), a hierarchy of sub-communities, and territory references.
/// Not a NetworkBehaviour — clients that need community state pull through
/// MapRegistry / CommunityData. See [[wiki/gotchas/singular-leader-vs-multi-leader-isleader]].
/// </summary>
[System.Serializable]
public class Community
{
    public string communityName;

    /// <summary>
    /// Legacy display field. Kept for save-data back-compat + any caller that still
    /// reads the enum. Authoritative tier identity lives on
    /// <see cref="CurrentTier"/> / <see cref="currentTierId"/>; this enum value
    /// shadow-tracks <see cref="CurrentTierRef"/>.<see cref="CommunityTierRequirementsSO.Level"/>
    /// so older consumers keep working. Designers adding off-enum tiers (e.g. a
    /// "Province" tier between Town and City) can leave their SO's Level at SmallGroup —
    /// gameplay paths use Tier / Order, not this field.
    /// </summary>
    public CommunityLevel level;

    /// <summary>
    /// Authoritative tier identity, persisted as the SO's <see cref="CommunityTierRequirementsSO.TierId"/>
    /// string so save data is stable across CommunityLevel enum churn and supports
    /// off-enum designer-authored tiers. Resolved to <see cref="CurrentTier"/> lazily.
    /// </summary>
    public string currentTierId = string.Empty;

    [System.NonSerialized] private CommunityTierRequirementsSO _currentTier;

    /// <summary>
    /// The community's current tier as a <see cref="CommunityTierRequirementsSO"/>
    /// reference. Resolved from <see cref="currentTierId"/> via
    /// <see cref="CommunityTierRegistry.GetById"/>; falls back to the legacy
    /// <see cref="level"/> enum mapping when the id is empty (covers loads from
    /// pre-migration save data).
    /// </summary>
    public CommunityTierRequirementsSO CurrentTier
    {
        get
        {
            // Lazy populate from the persisted id, then from the legacy enum.
            if (_currentTier == null)
            {
                if (!string.IsNullOrEmpty(currentTierId))
                    _currentTier = CommunityTierRegistry.GetById(currentTierId);
                if (_currentTier == null)
                    _currentTier = CommunityTierRegistry.Get(level);
            }

            // Desync sync: if a caller assigned to the legacy `level` field directly
            // (tests + any pre-migration code paths), prefer that — honours the
            // long-standing contract that writing `c.level = X` shifts the community
            // tier. New code should call ChangeTier(SO) for off-enum tiers.
            if (_currentTier != null && _currentTier.Level != level)
            {
                var byLevel = CommunityTierRegistry.Get(level);
                if (byLevel != null)
                {
                    _currentTier = byLevel;
                    currentTierId = byLevel.TierId;
                }
            }
            return _currentTier;
        }
    }

    [Header("Leadership")]
    /// <summary>
    /// All leaders of the community. <c>leaders[0]</c> is the primary (decision-of-last-resort);
    /// <c>leaders[1..]</c> are secondaries (can co-administer via the admin console — Plan 5).
    /// Multi-leader is a *capability* of every community; communities founded by a single founder
    /// begin with one entry and stay single-leader until a primary uses the admin console to promote.
    /// </summary>
    public List<Character> leaders = new List<Character>();

    [Header("Members")]
    public List<Character> members = new List<Character>();

    [Header("Hierarchy")]
    [NonSerialized] public Community parentCommunity;
    [NonSerialized] public List<Community> subCommunities = new List<Community>();

    [Header("Territory & Assets")]
    public List<Zone> communityZones = new List<Zone>();
    public List<Building> ownedBuildings = new List<Building>();

    [Header("City Charter")]
    /// <summary>
    /// The <see cref="AdministrativeBuilding"/> chartering this community.
    /// Server-only state; set when an AB's <see cref="AdministrativeBuilding.SetOwnerCommunity"/>
    /// runs during placement (Plan 4a). NonSerialized — Communities are plain C# objects,
    /// the AB ref doesn't survive JSON serialization. On wake-up the ref is rebuilt
    /// by scanning <see cref="BuildingManager"/> for AB instances whose owner community
    /// matches (handled by Plan 4c's lifecycle hooks; Plan 4a leaves the rebuild gap
    /// documented as a known limitation — a save/load round-trip in Plan 4a-only state
    /// loses the AB ref until Plan 4c ships).
    /// </summary>
    [System.NonSerialized] public AdministrativeBuilding AdministrativeBuilding;

    /// <summary>True iff this community has a chartered AdministrativeBuilding (placed,
    /// not necessarily complete). Plan 4c's drifter migration gates on
    /// <c>AdministrativeBuilding != null &amp;&amp; AdministrativeBuilding.IsUnderConstruction == false</c>;
    /// Plan 4a's placement gate uses this for the 1-per-community check.</summary>
    public bool IsChartered => AdministrativeBuilding != null;

    // ── Convenience accessors ─────────────────────────────────────────────
    /// <summary>The primary leader (index 0), or null if the community is currently leaderless.</summary>
    public Character PrimaryLeader => leaders.Count > 0 ? leaders[0] : null;
    /// <summary>All leaders except the primary, in roster order.</summary>
    public IEnumerable<Character> SecondaryLeaders => leaders.Skip(1);
    /// <summary>
    /// Canonical "is this character a recognised leader?" predicate. Mirrors
    /// <c>Room.IsOwner(Character)</c> from the building hierarchy and is the
    /// authority-gate you should use for every leader-only feature.
    /// See [[wiki/gotchas/singular-leader-vs-multi-leader-isleader]] for the
    /// rationale — never compare against <c>PrimaryLeader</c> or <c>leaders[0]</c>
    /// directly for an auth check.
    /// </summary>
    public bool IsLeader(Character c) => c != null && leaders.Contains(c);

    /// <summary>
    /// Members whose <see cref="CharacterCommunity.Citizenship"/> is this community.
    /// Citizenship is granted by completing an AdministrativeBuilding (Plan 4) and
    /// by JoinRequestDesk-accept (Plan 4). In Plan 1 this returns an empty enumerable
    /// until a writer ships, but the filter is wired correctly.
    /// </summary>
    public IEnumerable<Character> Citizens => members.Where(m =>
        m != null
        && m.CharacterCommunity != null
        && m.CharacterCommunity.Citizenship == this);

    public Community(string name, Character founder)
    {
        communityName = name;
        leaders.Add(founder);   // founder = primary leader (index 0)
        level = CommunityLevel.SmallGroup;
        // Bind the canonical first tier SO if available. CurrentTier getter falls back
        // to the legacy level enum mapping when this stays empty, so this is best-effort.
        var firstTier = CommunityTierRegistry.GetById("TierRequirements_SmallGroup")
                      ?? CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
        if (firstTier != null) currentTierId = firstTier.TierId;
        members.Add(founder);
    }

    public void AddMember(Character newMember)
    {
        if (!members.Contains(newMember))
        {
            members.Add(newMember);
            if (newMember != null && newMember.CharacterCommunity != null)
            {
                newMember.CharacterCommunity.SetCurrentCommunity(this);
            }
        }
    }

    public void RemoveMember(Character member)
    {
        if (!members.Contains(member)) return;

        members.Remove(member);
        if (member != null && member.CharacterCommunity != null)
        {
            // Unset only if it currently points to this community to avoid bugs when swapping
            if (member.CharacterCommunity.CurrentCommunity == this)
            {
                member.CharacterCommunity.SetCurrentCommunity(null);
            }
        }

        // Multi-leader-aware: removing a leader from the roster shifts list indices, which
        // is the same as "auto-promote next secondary to primary" (the next leader is now
        // at index 0). No-op if the community becomes leaderless — it stays so until a new
        // leader is appointed or it dissolves.
        if (leaders.Contains(member))
        {
            leaders.Remove(member);
        }
    }

    /// <summary>
    /// Adds a sub-community. Note that a parent only tracks its DIRECT children.
    /// </summary>
    public void AddSubCommunity(Community subComm)
    {
        if (subComm == null || subComm == this) return;

        if (!subCommunities.Contains(subComm))
        {
            // If it already has a parent, leave it first
            subComm.DeclareIndependence();

            subCommunities.Add(subComm);
            subComm.parentCommunity = this;
        }
    }

    /// <summary>
    /// Breaks the link with the parent community, making this community independent.
    /// </summary>
    public void DeclareIndependence()
    {
        if (parentCommunity != null)
        {
            Debug.Log($"<color=orange>[Community]</color> {communityName} has declared independence from {parentCommunity.communityName}!");
            parentCommunity.subCommunities.Remove(this);
            parentCommunity = null;
        }
    }

    // ── Server-only leadership mutators ───────────────────────────────────
    /// <summary>
    /// Server-only. Adds <paramref name="c"/> to the leader roster as a secondary.
    /// No-op if <paramref name="c"/> is null, not a member, or already a leader.
    /// Authority gate (primary-only): the caller is expected to check
    /// <c>IsLeader(callingCharacter) &amp;&amp; callingCharacter == PrimaryLeader</c>.
    /// </summary>
    public bool PromoteToSecondaryLeader(Character c)
    {
        if (c == null || !members.Contains(c) || leaders.Contains(c)) return false;
        leaders.Add(c);
        return true;
    }

    /// <summary>
    /// Server-only. Removes <paramref name="c"/> from the leader roster.
    /// No-op if <paramref name="c"/> is the primary (use <see cref="TransferPrimaryLeadership"/>
    /// to step down) or is not a leader.
    /// </summary>
    public bool DemoteFromLeadership(Character c)
    {
        if (c == null || c == PrimaryLeader || !leaders.Contains(c)) return false;
        leaders.Remove(c);
        return true;
    }

    /// <summary>
    /// Server-only. Moves <paramref name="newPrimary"/> to index 0 (primary slot).
    /// Requires <paramref name="newPrimary"/> to already be a leader. Old primary
    /// drops into the secondary slot they were displaced from.
    /// </summary>
    public bool TransferPrimaryLeadership(Character newPrimary)
    {
        if (newPrimary == null || !leaders.Contains(newPrimary)) return false;
        if (newPrimary == PrimaryLeader) return false;
        leaders.Remove(newPrimary);
        leaders.Insert(0, newPrimary);
        return true;
    }

    /// <summary>
    /// Legacy enum-based tier mutator. Resolves <paramref name="newLevel"/> to a tier SO
    /// via <see cref="CommunityTierRegistry.Get(CommunityLevel)"/> and routes through
    /// <see cref="ChangeTier"/>. Kept so older callers keep compiling — new code should
    /// pass the <see cref="CommunityTierRequirementsSO"/> directly.
    /// </summary>
    public void ChangeLevel(CommunityLevel newLevel)
    {
        var tier = CommunityTierRegistry.Get(newLevel);
        if (tier != null)
        {
            ChangeTier(tier);
            return;
        }
        // No SO authored for that enum value — keep the enum-only fallback so old tests
        // that never touch the SO registry don't crash.
        level = newLevel;
        Debug.Log($"<color=green>[Community]</color> {communityName} has evolved to {level} (no SO).");
    }

    /// <summary>
    /// Authoritative tier mutator. Sets <see cref="CurrentTier"/> + the persistent
    /// <see cref="currentTierId"/>, and shadow-writes the legacy <see cref="level"/>
    /// enum for back-compat. Idempotent on the same tier.
    /// </summary>
    public void ChangeTier(CommunityTierRequirementsSO newTier)
    {
        if (newTier == null) return;
        if (_currentTier == newTier && currentTierId == newTier.TierId) return;
        _currentTier = newTier;
        currentTierId = newTier.TierId;
        level = newTier.Level;
        Debug.Log($"<color=green>[Community]</color> {communityName} has evolved to {newTier.DisplayName}!");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier-up (Plan 4c Task 3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Validates the next tier's requirements
    /// (<see cref="MWI.WorldSystem.CommunityTierRequirementsSO"/>) against the AB's
    /// accumulators (population, treasury, completed buildings). On success calls
    /// <see cref="ChangeLevel"/> and returns <c>(true, null)</c>. On failure returns
    /// <c>(false, reason)</c> for UI display.
    ///
    /// Pure POCO method — caller (<see cref="AdministrativeBuilding.RequestPromoteLevelServerRpc"/>)
    /// is responsible for the <c>IsServer</c> + leader-authority gate.
    /// </summary>
    public (bool ok, string reason) TryPromoteLevel(AdministrativeBuilding ab)
    {
        // SO-ladder lookup. Reads from CommunityTierRegistry.GetNext(CurrentTier) so the
        // ladder is fully authored in Resources/Data/CommunityTiers/ — designers add a new
        // tier between existing ones by dropping a new SO with an Order between two
        // existing values, no enum edit needed.
        var current = CurrentTier;
        var req = CommunityTierRegistry.GetNext(current);
        if (req == null) return (false, "Already at max tier.");

        // 1. Population gate.
        int memberCount = members != null ? members.Count : 0;
        if (memberCount < req.MinPopulation)
            return (false, $"Need {req.MinPopulation - memberCount} more citizen(s).");

        // 2. Happy-population-fraction gate. v1 stub: no NeedHappiness / CharacterMood
        // system exists, so every citizen is treated as "happy" — the gate passes when
        // MinHappyPopulationFraction <= 1.0 (the SO's [0,1] Range attribute enforces
        // this). When the mood system ships, swap the citizens loop for a real read.
        if (req.MinHappyPopulationFraction > 0f)
        {
            int happy = memberCount; // v1 stub — assume everyone is happy.
            float fraction = memberCount > 0 ? (float)happy / memberCount : 0f;
            if (fraction < req.MinHappyPopulationFraction)
                return (false, $"Citizens not happy enough ({fraction:P0} vs {req.MinHappyPopulationFraction:P0} required).");
        }

        // 3. Treasury gate. v1 uses the default currency (typically the map's
        // NativeCurrency). Plan 4c follow-up may surface per-currency tier requirements.
        if (req.MinTreasury > 0)
        {
            int treasury = ab != null ? ab.GetTreasuryBalance(MWI.Economy.CurrencyId.Default) : 0;
            if (treasury < req.MinTreasury)
                return (false, $"Treasury needs {req.MinTreasury - treasury} more gold.");
        }

        // 4. Required-building gate (duplicate-aware).
        var requiredList = req.RequiredBuildings;
        if (requiredList != null && requiredList.Count > 0)
        {
            // Walk unique blueprint SOs in the requirement list, count duplicates in the
            // requirement and compare with the community's completed-owned count.
            var counted = new HashSet<BuildingSO>();
            for (int i = 0; i < requiredList.Count; i++)
            {
                var so = requiredList[i];
                if (so == null) continue;
                if (!counted.Add(so)) continue;
                int needed = CountInList(requiredList, so);
                int have   = CountOwnedCompletedBuildingsOfSO(so);
                if (have < needed)
                {
                    string buildingName = string.IsNullOrEmpty(so.BuildingName) ? so.name : so.BuildingName;
                    return (false, $"Need {needed - have} more {buildingName}.");
                }
            }
        }

        ChangeTier(req);
        return (true, null);
    }

    private int CountOwnedCompletedBuildingsOfSO(BuildingSO so)
    {
        if (ownedBuildings == null || so == null) return 0;
        int n = 0;
        for (int i = 0; i < ownedBuildings.Count; i++)
        {
            var b = ownedBuildings[i];
            if (b == null) continue;
            if (b.IsUnderConstruction) continue;
            if (b.Blueprint == so) n++;
        }
        return n;
    }

    private static int CountInList(System.Collections.Generic.IReadOnlyList<BuildingSO> list, BuildingSO so)
    {
        if (list == null || so == null) return 0;
        int n = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == so) n++;
        return n;
    }
}
