using System.Collections.Generic;
using UnityEngine;

public class CharacterCommunity : MonoBehaviour
{
    [SerializeField] private Character _character;

    [SerializeField] private Community _currentCommunity;

    public Character Character => _character;
    public Community CurrentCommunity => _currentCommunity;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Checks if this character has the required traits and close friends to found a new community.
    /// Requirements: 'CanCreateCommunity' trait and at least 4 close relationships (Friend+).
    /// </summary>
    public void CheckAndCreateCommunity()
    {
        if (_character == null || _character.CharacterTraits == null || !_character.CharacterTraits.CanCreateCommunity()) 
            return;

        // Prevent forming multiple communities by the same leader
        if (_currentCommunity != null && _currentCommunity.leader == _character)
            return;

        // Count close friends
        int closeFriendsCount = 0;
        List<Character> potentialMembers = new List<Character>();

        if (_character.CharacterRelation != null)
        {
            foreach (var rel in _character.CharacterRelation.Relationships)
            {
                if (rel.RelationType == RelationshipType.Friend || 
                    rel.RelationType == RelationshipType.Lover || 
                    rel.RelationType == RelationshipType.Soulmate)
                {
                    if (rel.RelatedCharacter != null && rel.RelatedCharacter.IsAlive())
                    {
                        closeFriendsCount++;
                        potentialMembers.Add(rel.RelatedCharacter);
                    }
                }
            }
        }

        if (closeFriendsCount >= 4)
        {
            CreateCommunity(potentialMembers);
        }
    }

    private void CreateCommunity(List<Character> initialMembers)
    {
        string newCommName = $"{_character.CharacterName}'s Band";
        Community newComm = new Community(newCommName, _character);
        
        // Add the friends to the new community
        foreach(var friend in initialMembers)
        {
            newComm.AddMember(friend);
            // Link the friend's local component to the new community
            if (friend.CharacterCommunity != null)
            {
                friend.CharacterCommunity.SetCurrentCommunity(newComm);
            }
        }

        // Set the founder's own community reference
        SetCurrentCommunity(newComm);

        // Handle hierarchy
        if (_currentCommunity != null && _currentCommunity != newComm) // If they were already in another community
        {
            // Become a sub-community
            _currentCommunity.AddSubCommunity(newComm);
            _currentCommunity.RemoveMember(_character); 
            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} formed a Sub-Community '{newCommName}' under {_currentCommunity.communityName} with {initialMembers.Count} followers!");
        }
        else
        {
            // Root community
            if (CommunityManager.Instance != null)
            {
                CommunityManager.Instance.RegisterCommunity(newComm);
            }
            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded a new independent Community '{newCommName}' with {initialMembers.Count} followers!");
        }
    }

    public void SetCurrentCommunity(Community newCommunity)
    {
        _currentCommunity = newCommunity;
    }

    public void JoinCommunity(Community communityToJoin)
    {
        if (communityToJoin == null || _currentCommunity == communityToJoin) return;
        
        LeaveCurrentCommunity();
        communityToJoin.AddMember(_character);
        SetCurrentCommunity(communityToJoin);
    }

    public void LeaveCurrentCommunity()
    {
        if (_currentCommunity != null)
        {
            _currentCommunity.RemoveMember(_character);
            SetCurrentCommunity(null);
        }
    }

    public void InviteToCommunity(Character target)
    {
        if (target == null || _currentCommunity == null || _currentCommunity.leader != _character) return;

        // In a real scenario, this might trigger an Interaction "Offer to join".
        // For now, immediately force join.
        if (target.CharacterCommunity != null)
        {
            target.CharacterCommunity.JoinCommunity(_currentCommunity);
        }
    }

    public void RemoveFromCommunity(Character target)
    {
        if (target == null || _currentCommunity == null || _currentCommunity.leader != _character) return;

        if (target.CharacterCommunity != null)
        {
            target.CharacterCommunity.LeaveCurrentCommunity();
        }
    }
}
