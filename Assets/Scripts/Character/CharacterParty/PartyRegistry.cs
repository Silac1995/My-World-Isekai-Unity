using System.Collections.Generic;
using UnityEngine;

public static class PartyRegistry
{
    private static readonly Dictionary<string, PartyData> _parties = new();
    private static readonly Dictionary<string, string> _characterToParty = new();

    public static void Register(PartyData party)
    {
        if (party == null || string.IsNullOrEmpty(party.PartyId)) return;

        _parties[party.PartyId] = party;

        foreach (string memberId in party.MemberIds)
            _characterToParty[memberId] = party.PartyId;
    }

    public static void Unregister(string partyId)
    {
        if (!_parties.TryGetValue(partyId, out PartyData party)) return;

        foreach (string memberId in party.MemberIds)
            _characterToParty.Remove(memberId);

        _parties.Remove(partyId);
    }

    public static PartyData GetParty(string partyId)
    {
        if (string.IsNullOrEmpty(partyId)) return null;
        _parties.TryGetValue(partyId, out PartyData party);
        return party;
    }

    public static PartyData GetPartyForCharacter(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        if (!_characterToParty.TryGetValue(characterId, out string partyId)) return null;
        return GetParty(partyId);
    }

    public static IEnumerable<PartyData> GetAllParties() => _parties.Values;

    public static void MapCharacterToParty(string characterId, string partyId)
    {
        _characterToParty[characterId] = partyId;
    }

    public static void UnmapCharacter(string characterId)
    {
        _characterToParty.Remove(characterId);
    }

    public static void Clear()
    {
        _parties.Clear();
        _characterToParty.Clear();
    }
}
