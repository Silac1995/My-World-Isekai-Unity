using System.Collections.Generic;
using MWI.WorldSystem;
using UnityEngine;

public class CharacterCommunity : CharacterSystem, ICharacterSaveData<CommunitySaveData>
{
    [Header("Debug")]
    [SerializeField] private string _debugCommunityName = "New Community";

    private Community _currentCommunity;

    /// <summary>
    /// Saved community map ID from deserialization, resolved lazily at runtime.
    /// </summary>
    private string _pendingCommunityMapId;

    public Character Character => _character;
    public Community CurrentCommunity => _currentCommunity;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Checks if this character meets requirements to found a new "Small Group" community.
    /// If the character is already in a community, the new one becomes a sub-community.
    /// Requirements: 'CanCreateCommunity' trait, at least 4 friends, and NOT already LEADING a community.
    /// </summary>
    public void CheckAndCreateCommunity()
    {
        if (_character == null) return;

        // 1. Requirement: Trait
        if (_character.CharacterTraits == null || !_character.CharacterTraits.CanCreateCommunity()) return;

        // 2. Requirement: Not already leading a community (cannot lead two)
        if (_currentCommunity != null && _currentCommunity.leader == _character) return;

        // 3. Requirement: 4 Friends
        int friendCount = _character.CharacterRelation != null ? _character.CharacterRelation.GetFriendCount() : 0;
        if (friendCount < 4) return;

        // Founding
        CreateCommunity();
    }

    /// <summary>
    /// Creates a new community. If currently in one, the new one is a sub-community.
    /// </summary>
    public void CreateCommunity()
    {
        string newCommName = $"{_character.CharacterName}'s Small Group of Friends";
        Community parent = _currentCommunity; // Capture current community to make it a parent
        
        Community newComm = null;
        if (CommunityManager.Instance != null)
        {
            newComm = CommunityManager.Instance.CreateNewCommunity(_character, newCommName);
        }
        else
        {
            newComm = new Community(newCommName, _character);
        }

        if (newComm != null)
        {
            // Link hierarchy if founder was in a community
            if (parent != null)
            {
                parent.AddSubCommunity(newComm);
            }

            SetCurrentCommunity(newComm);
            newComm.ChangeLevel(CommunityLevel.SmallGroup);
            
            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded a new {(parent != null ? "sub-" : "independent ")}Community '{newCommName}'.");
        }
    }

    /// <summary>
    /// Declares independence from the parent community if this character is the leader.
    /// </summary>
    public void BreakFreeFromParent()
    {
        if (_currentCommunity != null && _currentCommunity.leader == _character)
        {
            _currentCommunity.DeclareIndependence();
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
    // --- ICharacterSaveData<CommunitySaveData> IMPLEMENTATION ---

    public string SaveKey => "CharacterCommunity";
    public int LoadPriority => 60;

    public CommunitySaveData Serialize()
    {
        var data = new CommunitySaveData();

        if (_currentCommunity != null)
        {
            // Try to find the community's map ID via CommunityTracker
            string mapId = "";
            if (MWI.WorldSystem.CommunityTracker.Instance != null)
            {
                foreach (var commData in MWI.WorldSystem.CommunityTracker.Instance.GetAllCommunities())
                {
                    // Match by leader — communities are uniquely led
                    if (_currentCommunity.leader != null && commData.IsLeader(_currentCommunity.leader.CharacterId))
                    {
                        mapId = commData.MapId;
                        break;
                    }
                }
            }

            data.communityMapId = mapId;
        }
        else if (!string.IsNullOrEmpty(_pendingCommunityMapId))
        {
            // Preserve unresolved pending data
            data.communityMapId = _pendingCommunityMapId;
        }

        return data;
    }

    public void Deserialize(CommunitySaveData data)
    {
        if (data == null) return;

        _pendingCommunityMapId = data.communityMapId;

        // Community references are resolved at runtime when the map loads
        // and CommunityTracker becomes available.
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    // --- DEBUG ---

    [ContextMenu("Debug Create Community")]
    public void DebugCreateCommunity()
    {
        string originalName = _character.CharacterName;
        // Temporarily override name or just use the debug string
        string customName = string.IsNullOrEmpty(_debugCommunityName) ? $"{originalName}'s Band" : _debugCommunityName;
        
        CreateCommunity(customName);
    }

    private void CreateCommunity(string name)
    {
        Community newComm = null;
        if (CommunityManager.Instance != null)
        {
            newComm = CommunityManager.Instance.CreateNewCommunity(_character, name);
        }
        else
        {
            newComm = new Community(name, _character);
        }

        if (newComm != null)
        {
            SetCurrentCommunity(newComm);
            newComm.ChangeLevel(CommunityLevel.SmallGroup);
            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded '{name}'.");
        }
    }
}
