using System.Collections.Generic;
using UnityEngine;

public class CharacterCommunity : MonoBehaviour
{
    [SerializeField] private Character _character;

    private Community _currentCommunity;

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
        {
            // Debug.Log($"[Community Debug] {_character?.CharacterName} failed CanCreateCommunity trait check.");
            return;
        }

        // To prevent community spam (200+ communities bug),
        // ONLY characters who are NOT in a community yet can found one.
        if (_currentCommunity != null)
        {
            Debug.Log($"[Community Debug] {_character.CharacterName} failed check because they already belong to a community: {_currentCommunity.communityName}");
            return;
        }

        // Count close friends
        int closeFriendsCount = 0;
        List<Character> potentialMembers = new List<Character>();

        if (_character.CharacterRelation != null)
        {
            foreach (var rel in _character.CharacterRelation.Relationships)
            {
                if (rel.RelatedCharacter != null && rel.RelatedCharacter.IsAlive() &&
                    _character.CharacterRelation.IsFriend(rel.RelatedCharacter))
                {
                    closeFriendsCount++;
                    potentialMembers.Add(rel.RelatedCharacter);
                }
            }
        }

        if (closeFriendsCount >= 4)
        {
            CreateCommunity(potentialMembers);
        }
        else
        {
            Debug.Log($"[Community Debug] {_character.CharacterName} failed check because they only have {closeFriendsCount} close friends.");
        }
    }

    public void CreateCommunity(List<Character> potentialMembers)
    {
        string newCommName = $"{_character.CharacterName}'s Band";
        Community newComm = new Community(newCommName, _character);
        
        // We NO LONGER add initialMembers directly. 
        // The leader creates the community for themselves first,
        // and will invite friends later through Interactions.

        // Set the founder's own community reference (this adds them as a member in the Community constructor/logic)
        SetCurrentCommunity(newComm);

        // Root community registration
        if (CommunityManager.Instance != null)
        {
            CommunityManager.Instance.RegisterCommunity(newComm);
        }
        
        Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded a new independent Community '{newCommName}' and is looking for {potentialMembers.Count} members to invite!");
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
    [ContextMenu("Create Community")] public void CreateCommunity() => CreateCommunity(new List<Character>());
}
