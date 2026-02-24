using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Community
{
    public string communityName;
    public CommunityLevel level;
    
    [Header("Leadership")]
    public Character leader;
    
    [Header("Members")]
    public List<Character> members = new List<Character>();
    
    [Header("Hierarchy")]
    public Community parentCommunity;
    public List<Community> subCommunities = new List<Community>();

    [Header("Territory")]
    public List<Zone> communityZones = new List<Zone>();

    public Community(string name, Character founder)
    {
        communityName = name;
        leader = founder;
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
        if (members.Contains(member))
        {
            members.Remove(member);
            if (member != null && member.CharacterCommunity != null) 
            {
                // Unset only if it currently points to this community to avoid bugs when swapping
                if (member.CharacterCommunity.CurrentCommunity == this)
                {
                    member.CharacterCommunity.SetCurrentCommunity(null);
                }
            }

            // Handle case where leader leaves
            if (leader == member)
            {
                leader = (members.Count > 0) ? members[0] : null;
            }
        }
    }

    public void AddSubCommunity(Community subComm)
    {
        if (!subCommunities.Contains(subComm))
        {
            subCommunities.Add(subComm);
            subComm.parentCommunity = this;
        }
    }

    public void SetLeader(Character newLeader)
    {
        if (members.Contains(newLeader))
        {
            leader = newLeader;
        }
        else
        {
            Debug.LogWarning($"[Community] Cannot set {newLeader?.name} as leader because they are not a member of {communityName}.");
        }
    }

    public void ChangeLevel(CommunityLevel newLevel)
    {
        level = newLevel;
        Debug.Log($"<color=green>[Community]</color> {communityName} has evolved to {level}!");
    }
}
