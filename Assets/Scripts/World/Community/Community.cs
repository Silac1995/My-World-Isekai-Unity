using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
    public CommunityLevel level;

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

    public void ChangeLevel(CommunityLevel newLevel)
    {
        level = newLevel;
        Debug.Log($"<color=green>[Community]</color> {communityName} has evolved to {level}!");
    }
}
