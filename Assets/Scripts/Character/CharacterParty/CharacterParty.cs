using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gère un groupe de personnages avec un leader et des membres.
/// </summary>
public class CharacterParty
{
    private string _partyName;
    private Character _leader;
    private List<Character> _members = new List<Character>();

    public string PartyName => _partyName;
    public Character Leader => _leader;
    public IReadOnlyList<Character> Members => _members.AsReadOnly();

    public CharacterParty(string name, Character leader)
    {
        _partyName = name;
        _leader = leader;
        AddMember(leader);
    }

    public void AddMember(Character character)
    {
        if (character == null || _members.Contains(character)) return;

        // Si le personnage était déjà dans un autre groupe, il doit le quitter (logique à gérer dans Character)
        _members.Add(character);
        character.SetParty(this);
        
        Debug.Log($"<color=green>[Party]</color> {character.CharacterName} a rejoint le groupe de {_leader.CharacterName}.");
    }

    public void RemoveMember(Character character)
    {
        if (character == null || !_members.Contains(character)) return;

        _members.Remove(character);
        character.SetParty(null);

        Debug.Log($"<color=green>[Party]</color> {character.CharacterName} a quitté le groupe.");

        if (character == _leader)
        {
            AssignNewLeader();
        }
    }

    private void AssignNewLeader()
    {
        if (_members.Count > 0)
        {
            _leader = _members[0];
            Debug.Log($"<color=green>[Party]</color> Nouveau leader désigné : {_leader.CharacterName}.");
        }
        else
        {
            _leader = null;
            Debug.Log($"<color=green>[Party]</color> Le groupe est maintenant vide et sera dissous.");
        }
    }

    public bool IsLeader(Character character)
    {
        return _leader == character;
    }
}
