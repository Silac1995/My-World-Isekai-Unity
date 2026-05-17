using System.Collections.Generic;
using MWI.WorldSystem;
using UnityEngine;

public class CharacterCommunity : CharacterSystem, ICharacterSaveData<CommunitySaveData>
{
    [Header("Debug")]
    [SerializeField] private string _debugCommunityName = "New Community";

    private Community _currentCommunity;
    private Community _citizenship;

    /// <summary>
    /// Saved community map ID from deserialization, resolved lazily at runtime.
    /// </summary>
    private string _pendingCommunityMapId;

    /// <summary>
    /// Saved citizenship map ID from deserialization, resolved lazily at runtime.
    /// Mirrors the <c>_pendingCommunityMapId</c> pattern — the live Community
    /// reference is rebound when MapRegistry has surfaced the matching CommunityData.
    /// </summary>
    private string _pendingCitizenshipMapId;

    public Character Character => _character;
    public Community CurrentCommunity => _currentCommunity;

    /// <summary>
    /// The community of which this character is a *citizen* (sticky — granted by
    /// completing an <c>AdministrativeBuilding</c> in Plan 4). Distinct from
    /// <see cref="CurrentCommunity"/> (which is the community the character is
    /// currently a *member* of, transient).
    /// </summary>
    public Community Citizenship => _citizenship;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Founds a new community led by this character. The only gate is "not already
    /// leading a community" — the trait + 4-friends prerequisites were lifted as part
    /// of the city-founding redesign (Plan 1 of 5). Any character with the
    /// Ambition_FoundACity active (Plan 3) or invoking the dev "Create Community"
    /// button (out of scope here) reaches this method.
    /// </summary>
    public void CheckAndCreateCommunity()
    {
        if (_character == null) return;

        // Sole guard: cannot lead two communities at once.
        if (_currentCommunity != null && _currentCommunity.IsLeader(_character)) return;

        CreateCommunity();
    }

    /// <summary>
    /// Creates a new community. If currently in one, the new one is a sub-community.
    /// </summary>
    public void CreateCommunity()
    {
        string newCommName = $"{_character.CharacterName}'s Settlement";
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
        if (_currentCommunity != null && _currentCommunity.IsLeader(_character))
        {
            _currentCommunity.DeclareIndependence();
        }
    }

    public void SetCurrentCommunity(Community newCommunity)
    {
        _currentCommunity = newCommunity;
    }

    /// <summary>
    /// Server-only. Grants citizenship to <paramref name="c"/>. If the character was
    /// already a citizen of a different community, that previous citizenship is
    /// implicitly renounced (no double-citizenship in v1).
    /// Called by <c>AdministrativeBuilding.OnFinalize</c> on the founder, and by
    /// <c>JoinRequestDesk</c> when a join request is accepted (both ship in Plan 4).
    /// </summary>
    public void SetCitizenship(Community c)
    {
        if (_citizenship == c) return;
        _citizenship = c;
    }

    /// <summary>
    /// Server-only. Clears citizenship. Used when a character formally leaves a city
    /// (UI exit gesture in Plan 5) or when a community dissolves.
    /// </summary>
    public void RenounceCitizenship()
    {
        _citizenship = null;
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
        if (target == null || _currentCommunity == null || !_currentCommunity.IsLeader(_character)) return;

        // In a real scenario, this might trigger an Interaction "Offer to join".
        // For now, immediately force join.
        if (target.CharacterCommunity != null)
        {
            target.CharacterCommunity.JoinCommunity(_currentCommunity);
        }
    }

    public void RemoveFromCommunity(Character target)
    {
        if (target == null || _currentCommunity == null || !_currentCommunity.IsLeader(_character)) return;

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

        // --- communityMapId (existing) ---
        if (_currentCommunity != null)
        {
            string mapId = "";
            if (MWI.WorldSystem.MapRegistry.Instance != null)
            {
                foreach (var commData in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
                {
                    // Match by primary leader — every chartered community has a unique primary.
                    if (_currentCommunity.PrimaryLeader != null && commData.IsLeader(_currentCommunity.PrimaryLeader.CharacterId))
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
            data.communityMapId = _pendingCommunityMapId;
        }

        // --- citizenshipMapId (new) ---
        if (_citizenship != null)
        {
            string mapId = "";
            if (MWI.WorldSystem.MapRegistry.Instance != null)
            {
                foreach (var commData in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
                {
                    if (_citizenship.PrimaryLeader != null && commData.IsLeader(_citizenship.PrimaryLeader.CharacterId))
                    {
                        mapId = commData.MapId;
                        break;
                    }
                }
            }
            data.citizenshipMapId = mapId;
        }
        else if (!string.IsNullOrEmpty(_pendingCitizenshipMapId))
        {
            data.citizenshipMapId = _pendingCitizenshipMapId;
        }

        return data;
    }

    public void Deserialize(CommunitySaveData data)
    {
        if (data == null) return;

        _pendingCommunityMapId = data.communityMapId;
        _pendingCitizenshipMapId = data.citizenshipMapId;

        // Community + Citizenship references are resolved at runtime when the map
        // loads and MapRegistry becomes available. Defensive try/catch lives in
        // whichever subsystem performs the late-rebind (rule #31).
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
