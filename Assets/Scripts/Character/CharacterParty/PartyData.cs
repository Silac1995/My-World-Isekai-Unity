using System;
using System.Collections.Generic;

[Serializable]
public class PartyData
{
    public string PartyId;
    public string PartyName;
    public string LeaderId;
    public List<string> MemberIds = new List<string>();
    public PartyFollowMode FollowMode = PartyFollowMode.Strict;

    // Transient — not persisted, resets to Active on load
    [NonSerialized] public PartyState State = PartyState.Active;

    public PartyData(string leaderId, string leaderName, string partyName = null)
    {
        PartyId = Guid.NewGuid().ToString();
        LeaderId = leaderId;
        PartyName = string.IsNullOrEmpty(partyName) ? $"{leaderName}'s Party" : partyName;
        MemberIds.Add(leaderId);
    }

    public bool IsLeader(string characterId) => LeaderId == characterId;
    public bool IsMember(string characterId) => MemberIds.Contains(characterId);
    public bool IsFull(int maxSize) => MemberIds.Count >= maxSize;

    public void AddMember(string characterId)
    {
        if (!MemberIds.Contains(characterId))
            MemberIds.Add(characterId);
    }

    public void RemoveMember(string characterId)
    {
        MemberIds.Remove(characterId);

        if (characterId == LeaderId && MemberIds.Count > 0)
            LeaderId = MemberIds[0];
    }

    public int MemberCount => MemberIds.Count;
}
