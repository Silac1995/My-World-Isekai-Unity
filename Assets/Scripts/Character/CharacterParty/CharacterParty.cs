using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.AI;
using MWI.UI.Notifications;
using MWI.WorldSystem;

public class CharacterParty : CharacterSystem, ICharacterSaveData<PartySaveData>
{
    // --- Serialized References ---
    [SerializeField] private SkillSO _leadershipSkill;
    public SkillSO LeadershipSkill => _leadershipSkill;
    [SerializeField] private ToastNotificationChannel _toastChannel;

    // --- Network Variables ---
    private NetworkVariable<FixedString64Bytes> _networkPartyId = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<byte> _networkPartyState = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<byte> _networkFollowMode = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- Runtime State ---
    private PartyData _partyData;

    // --- Gather Zone State ---
    private HashSet<string> _gatheredMemberIds = new();
    private PartyGatherZone _gatherZone;

    // --- Gathering ---
    private string _gatherTargetMapId;
    private Vector3 _gatherTargetPosition;
    private Coroutine _gatherCoroutine;
    private GameObject _gatherZoneGO;

    // --- Public Accessors ---
    public PartyData PartyData => _partyData;
    public bool IsInParty => _partyData != null;
    public bool IsPartyLeader => _partyData != null && _partyData.IsLeader(_character.CharacterId);
    public string NetworkPartyId => _networkPartyId.Value.ToString();
    public PartyState CurrentState => (PartyState)_networkPartyState.Value;
    public PartyFollowMode CurrentFollowMode => (PartyFollowMode)_networkFollowMode.Value;

    // --- Events (fire on both server and client) ---
    public event Action<PartyData> OnJoinedParty;
    public event Action OnLeftParty;
    public event Action<PartyFollowMode> OnFollowModeChanged;
    public event Action<PartyState> OnPartyStateChanged;
    public event Action OnGatheringStarted;
    public event Action OnGatheringComplete;
    public event Action<string> OnMemberKicked;
    public event Action OnPartyRosterChanged;

    // --- Leader event subscriptions ---
    private Character _subscribedLeader;

    // NETWORK LIFECYCLE
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) TryReconnectToParty();
        _networkPartyId.OnValueChanged += OnNetworkPartyIdChanged;
        _networkPartyState.OnValueChanged += OnNetworkPartyStateChanged;
        _networkFollowMode.OnValueChanged += OnNetworkFollowModeChanged;
    }

    public override void OnNetworkDespawn()
    {
        // If a player party leader is despawning, flag NPC members as abandoned
        if (IsServer && _character.IsPlayer() && IsInParty && IsPartyLeader)
        {
            HandleLeaderDisconnected();
        }

        _networkPartyId.OnValueChanged -= OnNetworkPartyIdChanged;
        _networkPartyState.OnValueChanged -= OnNetworkPartyStateChanged;
        _networkFollowMode.OnValueChanged -= OnNetworkFollowModeChanged;
        UnsubscribeFromLeader();
        base.OnNetworkDespawn();
    }

    // PARTY LIFECYCLE (Server-Only)
    public bool CreateParty(string partyName = null)
    {
        if (!IsServer) return false;
        if (IsInParty) return false;
        if (_leadershipSkill != null && !_character.CharacterSkills.HasSkill(_leadershipSkill)) return false;
        _partyData = new PartyData(_character.CharacterId, _character.CharacterName, partyName);
        PartyRegistry.Register(_partyData);
        SyncNetworkVariables();
        SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));
        OnJoinedParty?.Invoke(_partyData);
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName,
            _partyData.LeaderId, string.Join(",", _partyData.MemberIds));
        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} created party '{_partyData.PartyName}'");
        return true;
    }

    public bool JoinParty(string partyId)
    {
        if (!IsServer) return false;
        PartyData party = PartyRegistry.GetParty(partyId);
        if (party == null) return false;
        if (IsInParty) LeaveParty();
        int maxSize = GetMaxPartySize(party.LeaderId);
        if (party.IsFull(maxSize)) return false;
        party.AddMember(_character.CharacterId);
        PartyRegistry.MapCharacterToParty(_character.CharacterId, partyId);
        _partyData = party;
        SyncNetworkVariables();
        SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));
        OnJoinedParty?.Invoke(_partyData);
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName,
            _partyData.LeaderId, string.Join(",", _partyData.MemberIds));
        NotifyPartyMemberJoinedClientRpc(_character.CharacterName);
        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} joined party '{_partyData.PartyName}'");
        UpdateFollowState();
        BroadcastRosterChanged();
        return true;
    }

    public bool JoinCharacterParty(Character leader)
    {
        if (leader == null) return false;
        CharacterParty leaderParty = leader.CharacterParty;
        if (leaderParty == null || !leaderParty.IsInParty) return false;
        return JoinParty(leaderParty.PartyData.PartyId);
    }

    public void LeaveParty()
    {
        if (!IsServer || !IsInParty) return;
        string partyId = _partyData.PartyId;
        string charId = _character.CharacterId;
        bool wasLeader = _partyData.IsLeader(charId);
        _partyData.RemoveMember(charId);
        PartyRegistry.UnmapCharacter(charId);
        UnsubscribeFromLeader();
        if (wasLeader && _partyData.MemberCount > 0)
        {
            string newLeaderId = _partyData.LeaderId;
            GrantLeadershipSkillIfNeeded(newLeaderId);

            // Notify all remaining members about new leader
            foreach (string memberId in _partyData.MemberIds)
            {
                Character member = Character.FindByUUID(memberId);
                if (member != null && member.CharacterParty != null)
                {
                    member.CharacterParty.UnsubscribeFromLeader();
                    if (memberId != newLeaderId)
                    {
                        member.CharacterParty.SubscribeToLeader(Character.FindByUUID(newLeaderId));
                    }
                    member.CharacterParty.NotifyLeaderChangedClientRpc(newLeaderId);
                }
            }
            UpdateAllMembersFollowState();
        }
        if (_partyData.MemberCount > 0) BroadcastRosterChanged();
        if (_partyData.MemberCount == 0) PartyRegistry.Unregister(partyId);
        ClearFollowState();
        _partyData = null;
        SyncNetworkVariables();
        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();
        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} left party");
    }

    public void KickMember(string characterId)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        if (characterId == _character.CharacterId) return;
        _partyData.RemoveMember(characterId);
        PartyRegistry.UnmapCharacter(characterId);
        Character kicked = Character.FindByUUID(characterId);
        if (kicked != null && kicked.CharacterParty != null) kicked.CharacterParty.HandleKicked();
        OnMemberKicked?.Invoke(characterId);
        NotifyMemberKickedClientRpc(characterId);
        if (_partyData.MemberCount > 0) BroadcastRosterChanged();
        if (_partyData.MemberCount == 0)
        {
            PartyRegistry.Unregister(_partyData.PartyId);
            _partyData = null;
            SyncNetworkVariables();
            OnLeftParty?.Invoke();
            NotifyLeftPartyClientRpc();
        }
    }

    public void PromoteLeader(string characterId)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        if (!_partyData.IsMember(characterId)) return;
        if (characterId == _character.CharacterId) return; // Already leader

        _partyData.LeaderId = characterId;
        GrantLeadershipSkillIfNeeded(characterId);

        // Notify ALL members about the leader change (not just self)
        foreach (string memberId in _partyData.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null)
            {
                // Server-side: update subscriptions
                member.CharacterParty.UnsubscribeFromLeader();
                if (memberId != characterId)
                {
                    Character newLeader = Character.FindByUUID(characterId);
                    member.CharacterParty.SubscribeToLeader(newLeader);
                }

                // Client-side: sync via RPC
                member.CharacterParty.NotifyLeaderChangedClientRpc(characterId);
            }
        }

        // Update follow states — old leader should follow, new leader should stop
        UpdateAllMembersFollowState();
        BroadcastRosterChanged();

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} promoted {characterId} to party leader");
    }

    public void SetFollowMode(PartyFollowMode mode)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        _partyData.FollowMode = mode;
        _networkFollowMode.Value = (byte)mode;
        OnFollowModeChanged?.Invoke(mode);
        UpdateAllMembersFollowState();
    }

    /// <summary>
    /// Called when the party leader's Character despawns (player disconnect).
    /// Marks all NPC members as abandoned before disbanding the party,
    /// so they can be reclaimed later by the same player.
    /// </summary>
    public void HandleLeaderDisconnected()
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;

        foreach (string memberId in new List<string>(_partyData.MemberIds))
        {
            if (memberId == _character.CharacterId) continue;
            Character member = Character.FindByUUID(memberId);
            if (member == null || member.IsPlayer()) continue;

            member.IsAbandoned = true;
            member.FormerPartyLeaderId = _character.CharacterId;
            member.FormerPartyLeaderWorldGuid = _character.OriginWorldGuid;
        }

        DisbandParty();
    }

    public void DisbandParty()
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        string partyId = _partyData.PartyId;
        List<string> memberIds = new List<string>(_partyData.MemberIds);
        foreach (string memberId in memberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null) member.CharacterParty.HandleDisbanded();
            PartyRegistry.UnmapCharacter(memberId);
        }
        PartyRegistry.Unregister(partyId);
    }

    // INTERNAL HANDLERS
    private void HandleKicked()
    {
        if (!IsServer) return;
        UnsubscribeFromLeader();
        string partyName = _partyData?.PartyName ?? "the party";
        ClearFollowState();
        _partyData = null;
        SyncNetworkVariables();
        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();
        NotifyKickedToastClientRpc(partyName);
    }

    private void HandleDisbanded()
    {
        if (!IsServer) return;
        UnsubscribeFromLeader();
        ClearFollowState();
        _partyData = null;
        SyncNetworkVariables();
        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();
    }

    // LEADER EVENT SUBSCRIPTIONS
    // These are already handled by CharacterSystem base class for _character's own events.
    // We override them here to be no-ops since we manage leader subscriptions manually.
    protected override void HandleDeath(Character character) { }
    protected override void HandleIncapacitated(Character character) { }
    protected override void HandleWakeUp(Character character) { }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (!IsServer || !IsInParty) return;

        // Combat ended — resume following
        if (!inCombat)
        {
            UpdateFollowState();
            return;
        }

        // Combat started — clear follow key so NPC stops pathfinding to leader
        ClearFollowState();

        // This character just entered combat. Send a CombatAssistInvitation
        // to all player party members within awareness range, through the
        // standard CharacterInvitation pipeline (UI prompt, accept/refuse).
        var invitation = new CombatAssistInvitation(_character);

        foreach (string memberId in _partyData.MemberIds)
        {
            if (memberId == _character.CharacterId) continue;

            Character member = Character.FindByUUID(memberId);
            if (member == null || !member.IsAlive()) continue;
            if (!member.IsPlayer()) continue; // NPCs auto-assist via BTCond_FriendInDanger
            if (member.CharacterCombat != null && member.CharacterCombat.IsInBattle) continue;
            if (member.CharacterInvitation != null && member.CharacterInvitation.HasPendingInvitation) continue;

            // Check awareness range
            if (member.CharacterAwareness != null)
            {
                float dist = UnityEngine.Vector3.Distance(member.transform.position, _character.transform.position);
                if (dist <= member.CharacterAwareness.AwarenessRadius)
                {
                    // Send through the invitation pipeline — shows the UI prompt
                    invitation.Execute(_character, member);
                }
            }
        }
    }

    private void OnLeaderDied(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        if (!_partyData.IsMember(leader.CharacterId)) return; // Guard against duplicate processing

        // Remove dead leader from party
        _partyData.RemoveMember(leader.CharacterId);
        PartyRegistry.UnmapCharacter(leader.CharacterId);

        if (_partyData.MemberCount == 0) { HandleDisbanded(); return; }

        // Auto-promote (RemoveMember already set LeaderId to MemberIds[0])
        string newLeaderId = _partyData.LeaderId;
        GrantLeadershipSkillIfNeeded(newLeaderId);

        // Notify ALL remaining members about the new leader
        foreach (string memberId in _partyData.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null)
            {
                member.CharacterParty.UnsubscribeFromLeader();
                if (memberId != newLeaderId)
                {
                    Character newLeader = Character.FindByUUID(newLeaderId);
                    member.CharacterParty.SubscribeToLeader(newLeader);
                }
                member.CharacterParty.NotifyLeaderChangedClientRpc(newLeaderId);
            }
        }

        UpdateAllMembersFollowState();
        BroadcastRosterChanged();

        Debug.Log($"<color=cyan>[CharacterParty]</color> Leader died. {newLeaderId} is the new leader.");
    }

    private void OnLeaderIncapacitated(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        SetPartyState(PartyState.LeaderlessHold);
    }

    private void OnLeaderMapChanged(FixedString128Bytes prevMapId, FixedString128Bytes newMapId)
    {
        if (!IsServer || !IsInParty) return;
        if (IsPartyLeader) return; // Leader doesn't follow themselves
        if (_character.IsPlayer()) return; // Players handle doors themselves

        string leaderNewMap = newMapId.ToString();
        string myMap = "";
        var myTracker = _character.GetComponent<CharacterMapTracker>();
        if (myTracker != null) myMap = myTracker.CurrentMapID.Value.ToString();

        // Leader changed to a different map — find the door and go through it.
        // BuildingInteriorDoor → queue CharacterEnterBuildingAction (rule #22 player↔NPC parity).
        // Other portals/gates → small inlined PortalFollowRoutine.
        if (!string.IsNullOrEmpty(leaderNewMap) && leaderNewMap != myMap)
        {
            MapTransitionDoor door = FindDoorToMap(_character, leaderNewMap);
            if (door != null)
            {
                ClearFollowState();
                StopPortalFollow();

                if (door is BuildingInteriorDoor bd)
                {
                    Building building = bd.GetComponentInParent<Building>();
                    if (building != null)
                    {
                        _character.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(_character, building));
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[CharacterParty] {_character.CharacterName}: BuildingInteriorDoor '{bd.name}' has no Building parent — skipping follow.</color>");
                    }
                }
                else
                {
                    StartPortalFollow(door);
                }
            }
        }

        // Leader came back to our map — resume normal follow
        if (!string.IsNullOrEmpty(leaderNewMap) && leaderNewMap == myMap)
        {
            StopPortalFollow();
            UpdateFollowState();
        }
    }

    private void OnLeaderWokeUp(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        if (_partyData.State == PartyState.LeaderlessHold) SetPartyState(PartyState.Active);
        UpdateAllMembersFollowState();
    }

    private CharacterMapTracker _subscribedLeaderTracker;

    private void SubscribeToLeader(Character leader)
    {
        if (leader == null || leader == _character) return;
        UnsubscribeFromLeader();
        _subscribedLeader = leader;
        _subscribedLeader.OnDeath += OnLeaderDied;
        _subscribedLeader.OnIncapacitated += OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp += OnLeaderWokeUp;

        // Subscribe to leader's map changes (server-only) to detect interior transitions
        if (IsServer)
        {
            _subscribedLeaderTracker = leader.GetComponent<CharacterMapTracker>();
            if (_subscribedLeaderTracker != null)
                _subscribedLeaderTracker.CurrentMapID.OnValueChanged += OnLeaderMapChanged;
        }
    }

    private void UnsubscribeFromLeader()
    {
        if (_subscribedLeader == null) return;
        _subscribedLeader.OnDeath -= OnLeaderDied;
        _subscribedLeader.OnIncapacitated -= OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp -= OnLeaderWokeUp;

        if (_subscribedLeaderTracker != null)
        {
            _subscribedLeaderTracker.CurrentMapID.OnValueChanged -= OnLeaderMapChanged;
            _subscribedLeaderTracker = null;
        }
        _subscribedLeader = null;
    }

    // RECONNECT
    private void TryReconnectToParty()
    {
        PartyData existing = PartyRegistry.GetPartyForCharacter(_character.CharacterId);
        if (existing != null)
        {
            _partyData = existing;
            SyncNetworkVariables();
            SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));
            OnJoinedParty?.Invoke(_partyData);
            Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} reconnected to party '{_partyData.PartyName}'");
        }
    }

    // HELPERS
    public void SetPartyState(PartyState state)
    {
        if (!IsServer || _partyData == null) return;
        if (_partyData.State == state) return; // no-op if already in this state
        _partyData.State = state;
        _networkPartyState.Value = (byte)state;
        OnPartyStateChanged?.Invoke(state);
        UpdateAllMembersFollowState();
    }

    private void SyncNetworkVariables()
    {
        if (!IsServer) return;
        string partyId = _partyData?.PartyId ?? "";
        _networkPartyId.Value = new FixedString64Bytes(partyId);
        _networkPartyState.Value = (byte)(_partyData?.State ?? PartyState.Active);
        _networkFollowMode.Value = (byte)(_partyData?.FollowMode ?? PartyFollowMode.Strict);
    }

    private int GetMaxPartySize(string leaderId)
    {
        Character leader = Character.FindByUUID(leaderId);
        if (leader == null || _leadershipSkill == null) return 2;
        int level = leader.CharacterSkills.GetSkillLevel(_leadershipSkill);
        return Mathf.Min(2 + level, 8);
    }

    private void GrantLeadershipSkillIfNeeded(string characterId)
    {
        if (_leadershipSkill == null) return;
        Character c = Character.FindByUUID(characterId);
        if (c != null && !c.CharacterSkills.HasSkill(_leadershipSkill))
        {
            c.CharacterSkills.AddSkill(_leadershipSkill, 1);
            Debug.Log($"<color=cyan>[CharacterParty]</color> {c.CharacterName} gained Leadership skill through succession");
        }
    }

    // CLIENT RPCs
    [Rpc(SendTo.NotServer)]
    private void NotifyJoinedPartyClientRpc(FixedString64Bytes partyId, FixedString64Bytes partyName,
        FixedString64Bytes leaderId, string memberIdsCsv)
    {
        string id = partyId.ToString();
        string name = partyName.ToString();
        string leader = leaderId.ToString();

        // Client-side: create or update local PartyData with correct leader and members
        if (_partyData == null)
        {
            _partyData = new PartyData(leader, "", name);
            _partyData.PartyId = id;
        }
        else
        {
            _partyData.PartyName = name;
            _partyData.LeaderId = leader;
        }

        // Sync member list
        if (!string.IsNullOrEmpty(memberIdsCsv))
        {
            _partyData.MemberIds.Clear();
            _partyData.MemberIds.AddRange(memberIdsCsv.Split(','));
        }

        Debug.Log($"<color=cyan>[CharacterParty Client]</color> {_character.CharacterName} joined party '{name}' (leader={leader})");
        OnJoinedParty?.Invoke(_partyData);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyLeftPartyClientRpc()
    {
        _partyData = null;
        OnLeftParty?.Invoke();
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyPartyMemberJoinedClientRpc(FixedString64Bytes memberName) { }

    [Rpc(SendTo.NotServer)]
    private void NotifyMemberKickedClientRpc(FixedString64Bytes characterId) { OnMemberKicked?.Invoke(characterId.ToString()); }

    [Rpc(SendTo.NotServer)]
    private void NotifyLeaderChangedClientRpc(FixedString64Bytes newLeaderId)
    {
        string id = newLeaderId.ToString();
        if (_partyData != null) _partyData.LeaderId = id;
        UnsubscribeFromLeader();
        if (_character.CharacterId != id)
        {
            Character newLeader = Character.FindByUUID(id);
            SubscribeToLeader(newLeader);
        }
        // Fire roster changed so UI refreshes (leader controls visibility, etc.)
        OnPartyRosterChanged?.Invoke();
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyKickedToastClientRpc(FixedString64Bytes partyName) { }

    [Rpc(SendTo.NotServer)]
    private void NotifyRosterChangedClientRpc(FixedString64Bytes leaderId, string memberIdsCsv)
    {
        if (_partyData != null)
        {
            _partyData.LeaderId = leaderId.ToString();
            _partyData.MemberIds.Clear();
            if (!string.IsNullOrEmpty(memberIdsCsv))
            {
                _partyData.MemberIds.AddRange(memberIdsCsv.Split(','));
            }
        }
        OnPartyRosterChanged?.Invoke();
    }

    // SERVER RPCs (Client requests party operations)
    [Rpc(SendTo.Server)]
    public void RequestCreatePartyServerRpc(string partyName)
    {
        CreateParty(string.IsNullOrWhiteSpace(partyName) ? null : partyName);
    }

    [Rpc(SendTo.Server)]
    public void RequestLeavePartyServerRpc()
    {
        LeaveParty();
    }

    [Rpc(SendTo.Server)]
    public void RequestKickMemberServerRpc(FixedString64Bytes characterId)
    {
        KickMember(characterId.ToString());
    }

    [Rpc(SendTo.Server)]
    public void RequestPromoteLeaderServerRpc(FixedString64Bytes characterId)
    {
        PromoteLeader(characterId.ToString());
    }

    [Rpc(SendTo.Server)]
    public void RequestDisbandPartyServerRpc()
    {
        DisbandParty();
    }

    [Rpc(SendTo.Server)]
    public void RequestSetFollowModeServerRpc(byte mode)
    {
        SetFollowMode((PartyFollowMode)mode);
    }

    /// <summary>
    /// Client requests to invite a target character to their party.
    /// Runs the full invitation flow on the server where it has authority.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestInviteToPartyServerRpc(ulong targetNetworkObjectId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetObj))
            return;

        Character target = targetObj.GetComponent<Character>();
        if (target == null) return;

        // Auto-create party if not in one
        if (!IsInParty)
        {
            if (!CreateParty()) return;
        }

        if (!IsPartyLeader) return;

        var invitation = new PartyInvitation(_leadershipSkill);
        if (invitation.CanExecute(_character, target))
        {
            invitation.Execute(_character, target);
        }
    }

    // NETWORK VARIABLE CHANGE CALLBACKS
    private void OnNetworkPartyIdChanged(FixedString64Bytes prev, FixedString64Bytes next)
    {
        string id = next.ToString();
        if (string.IsNullOrEmpty(id))
        {
            _partyData = null;
            OnLeftParty?.Invoke();
        }
        else if (_partyData == null || _partyData.PartyId != id)
        {
            // Late-joiner fallback — create a minimal shell.
            // The correct leader and member list will arrive via
            // NotifyJoinedPartyClientRpc or NotifyRosterChangedClientRpc.
            _partyData = PartyRegistry.GetParty(id);
            if (_partyData == null)
            {
                // Don't assume self is leader — leave LeaderId empty,
                // it will be set by the next RPC.
                _partyData = new PartyData("", _character.CharacterName);
                _partyData.PartyId = id;
            }
            OnJoinedParty?.Invoke(_partyData);
        }
    }
    private void OnNetworkPartyStateChanged(byte prev, byte next) { OnPartyStateChanged?.Invoke((PartyState)next); }
    private void OnNetworkFollowModeChanged(byte prev, byte next) { OnFollowModeChanged?.Invoke((PartyFollowMode)next); }

    // GATHER ZONE CALLBACKS (Server-Only)
    public void OnGatherZoneEnter(Character character)
    {
        if (!IsServer || _partyData == null) return;
        if (_partyData.State != PartyState.Gathering) return;
        if (!_partyData.IsMember(character.CharacterId)) return;
        _gatheredMemberIds.Add(character.CharacterId);
        Debug.Log($"<color=cyan>[CharacterParty]</color> {character.CharacterName} entered gather zone ({_gatheredMemberIds.Count}/{_partyData.MemberCount})");
    }

    public void OnGatherZoneExit(Character character)
    {
        if (!IsServer || _partyData == null) return;
        _gatheredMemberIds.Remove(character.CharacterId);
    }

    // =============================================
    //  GATHERING LOGIC (Server-Only)
    // =============================================

    /// <summary>
    /// Called by MapTransitionDoor or MapTransitionZone when the leader tries to transition
    /// to a Region or Dungeon map.
    /// </summary>
    public void StartGathering(string targetMapId, Vector3 targetPosition)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        if (_partyData.State == PartyState.Gathering) return;

        _gatherTargetMapId = targetMapId;
        _gatherTargetPosition = targetPosition;
        _gatheredMemberIds.Clear();
        _gatheredMemberIds.Add(_character.CharacterId); // Leader is always gathered

        _character.CharacterMovement.Stop();

        EnableGatherZone();
        SetPartyState(PartyState.Gathering);
        OnGatheringStarted?.Invoke();
        NotifyGatheringStartedClientRpc();

        UpdateAllMembersFollowState();

        if (!_character.IsPlayer())
        {
            _gatherCoroutine = StartCoroutine(NPCGatherTimeoutRoutine(30f));
        }
        else
        {
            _gatherCoroutine = StartCoroutine(PlayerGatherCheckRoutine());
        }

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} started gathering for transition to {targetMapId}");
    }

    public void ProceedTransition()
    {
        if (!IsServer || _partyData.State != PartyState.Gathering) return;

        if (_gatherCoroutine != null)
        {
            StopCoroutine(_gatherCoroutine);
            _gatherCoroutine = null;
        }

        foreach (string memberId in _gatheredMemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member == null) continue;

            var transitionAction = new CharacterMapTransitionAction(
                member, null, _gatherTargetMapId, _gatherTargetPosition, 0.5f);
            member.CharacterActions.ExecuteAction(transitionAction);
        }

        DisableGatherZone();
        _gatheredMemberIds.Clear();
        SetPartyState(PartyState.Active);

        if (_partyData.FollowMode == PartyFollowMode.Loose)
        {
            SetFollowMode(PartyFollowMode.Strict);
        }

        OnGatheringComplete?.Invoke();
        Debug.Log($"<color=cyan>[CharacterParty]</color> Gathering complete, transitioning party to {_gatherTargetMapId}");
    }

    public void CancelGathering()
    {
        if (!IsServer || _partyData.State != PartyState.Gathering) return;

        if (_gatherCoroutine != null)
        {
            StopCoroutine(_gatherCoroutine);
            _gatherCoroutine = null;
        }

        DisableGatherZone();
        _gatheredMemberIds.Clear();
        SetPartyState(PartyState.Active);
        UpdateAllMembersFollowState();
    }

    private System.Collections.IEnumerator NPCGatherTimeoutRoutine(float timeout)
    {
        yield return new WaitForSecondsRealtime(timeout);
        ProceedTransition();
    }

    private System.Collections.IEnumerator PlayerGatherCheckRoutine()
    {
        while (_partyData != null && _partyData.State == PartyState.Gathering)
        {
            yield return new WaitForSecondsRealtime(0.5f);

            int totalMembers = _partyData.MemberCount;
            int gathered = _gatheredMemberIds.Count;
            int busy = 0;

            foreach (string memberId in _partyData.MemberIds)
            {
                Character member = Character.FindByUUID(memberId);
                if (member != null && !member.IsFree() && !_gatheredMemberIds.Contains(memberId))
                    busy++;
            }

            int freeUngathered = totalMembers - gathered - busy;

            if (freeUngathered <= 0 && busy == 0)
            {
                ProceedTransition();
                yield break;
            }
        }
    }

    // GATHER ZONE MANAGEMENT
    private void EnableGatherZone()
    {
        if (_gatherZoneGO == null)
        {
            _gatherZoneGO = new GameObject("GatherZone");
            _gatherZoneGO.transform.SetParent(transform);
            _gatherZoneGO.transform.localPosition = Vector3.zero;

            int partyGatherLayer = LayerMask.NameToLayer("PartyGather");
            if (partyGatherLayer >= 0)
                _gatherZoneGO.layer = partyGatherLayer;

            BoxCollider col = _gatherZoneGO.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(6f, 4f, 6f);

            _gatherZone = _gatherZoneGO.AddComponent<PartyGatherZone>();
            _gatherZone.Initialize(this);
        }

        _gatherZoneGO.SetActive(true);
    }

    private void DisableGatherZone()
    {
        if (_gatherZoneGO != null)
            _gatherZoneGO.SetActive(false);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyGatheringStartedClientRpc()
    {
        OnGatheringStarted?.Invoke();
    }

    // =============================================
    //  FOLLOW LOGIC (Server-Only)
    // =============================================

    /// <summary>
    /// Updates the blackboard follow flag for this NPC member.
    /// Server-only. Only affects NPCs.
    /// </summary>
    public void UpdateFollowState()
    {
        if (!IsServer || !IsInParty) return;
        if (_character.IsPlayer()) return;
        if (IsPartyLeader)
        {
            // Leader doesn't follow anyone — clear any leftover follow key
            ClearFollowState();
            return;
        }

        Character leader = Character.FindByUUID(_partyData.LeaderId);
        bool shouldFollow = _partyData.State == PartyState.Active
                         && _partyData.FollowMode == PartyFollowMode.Strict
                         && leader != null
                         && leader.IsAlive()
                         && IsOnSameMap(leader)
                         && !_character.CharacterCombat.IsInBattle;

        // Access the NPC's blackboard through the controller -> behaviour tree
        NPCController controller = _character.Controller as NPCController;
        if (controller == null || controller.BehaviourTree == null) return;

        Blackboard bb = controller.BehaviourTree.Blackboard;
        if (bb == null) return;

        if (shouldFollow)
        {
            bb.Set(Blackboard.KEY_PARTY_FOLLOW, leader);
        }
        else
        {
            bb.Remove(Blackboard.KEY_PARTY_FOLLOW);
        }
    }

    public void ClearFollowState()
    {
        if (_character.IsPlayer()) return;

        NPCController controller = _character.Controller as NPCController;
        if (controller == null) return;

        controller.BehaviourTree?.Blackboard?.Remove(Blackboard.KEY_PARTY_FOLLOW);
    }

    private bool IsOnSameMap(Character other)
    {
        if (other == null) return false;
        var myTracker = _character.GetComponent<CharacterMapTracker>();
        var otherTracker = other.GetComponent<CharacterMapTracker>();
        if (myTracker == null || otherTracker == null) return true; // No tracker = assume same map
        string myMap = myTracker.CurrentMapID.Value.ToString();
        string otherMap = otherTracker.CurrentMapID.Value.ToString();
        if (string.IsNullOrEmpty(myMap) || string.IsNullOrEmpty(otherMap)) return true;
        return myMap == otherMap;
    }

    /// <summary>
    /// Broadcasts UpdateFollowState() to all NPC members in the party.
    /// </summary>
    public void UpdateAllMembersFollowState()
    {
        if (_partyData == null) return;
        foreach (string memberId in _partyData.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null)
                member.CharacterParty.UpdateFollowState();
        }
    }

    /// <summary>
    /// Notifies all online party members that the roster changed.
    /// Fires OnPartyRosterChanged on each member (server-side + ClientRpc).
    /// </summary>
    private void BroadcastRosterChanged()
    {
        if (_partyData == null) return;

        string memberIdsCsv = string.Join(",", _partyData.MemberIds);
        FixedString64Bytes leaderId = new FixedString64Bytes(_partyData.LeaderId);

        foreach (string memberId in _partyData.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null)
            {
                member.CharacterParty.OnPartyRosterChanged?.Invoke();
                member.CharacterParty.NotifyRosterChangedClientRpc(leaderId, memberIdsCsv);
            }
        }
    }

    // =============================================
    //  INTERIOR FOLLOW — NPCs follow leader through doors
    // =============================================

    private Coroutine _portalFollowCoroutine;

    /// <summary>
    /// Called when the party leader transitions to a different map.
    /// For each NPC follower:
    ///   - If the connecting door is a BuildingInteriorDoor → queue CharacterEnterBuildingAction.
    ///   - Otherwise (portal / gate / outdoor↔outdoor) → run a small portal-follow coroutine.
    /// </summary>
    public void OrderFollowersThroughDoor(string leaderTargetMapId)
    {
        if (!IsServer || !IsInParty) return;

        foreach (string memberId in _partyData.MemberIds)
        {
            if (memberId == _partyData.LeaderId) continue;

            Character member = Character.FindByUUID(memberId);
            if (member == null || !member.IsAlive()) continue;
            if (member.IsPlayer()) continue;
            if (member.CharacterCombat != null && member.CharacterCombat.IsInBattle) continue;

            MapTransitionDoor door = FindDoorToMap(member, leaderTargetMapId);
            if (door == null) continue;

            if (member.CharacterParty == null) continue;
            member.CharacterParty.ClearFollowState();
            member.CharacterParty.StopPortalFollow();

            if (door is BuildingInteriorDoor bd)
            {
                Building building = bd.GetComponentInParent<Building>();
                if (building != null)
                {
                    member.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(member, building));
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[CharacterParty] {member.CharacterName}: BuildingInteriorDoor '{bd.name}' has no Building parent — skipping follow.</color>");
                }
            }
            else
            {
                // Portal door (outdoor↔outdoor / gate / non-building map transition).
                member.CharacterParty.StartPortalFollow(door);
            }
        }
    }

    /// <summary>
    /// Searches for a MapTransitionDoor or BuildingInteriorDoor near the follower
    /// that leads to the target map ID.
    /// </summary>
    private MapTransitionDoor FindDoorToMap(Character follower, string targetMapId)
    {
        if (string.IsNullOrEmpty(targetMapId)) return null;

        var allDoors = UnityEngine.Object.FindObjectsByType<MapTransitionDoor>(FindObjectsSortMode.None);
        MapTransitionDoor bestDoor = null;
        float bestDist = float.MaxValue;

        foreach (var door in allDoors)
        {
            string doorTargetMapId = null;

            if (door is BuildingInteriorDoor buildingDoor)
            {
                string interiorId = buildingDoor.GetInteriorMapId();
                if (interiorId == targetMapId)
                {
                    doorTargetMapId = targetMapId;
                }
                else if (buildingDoor.ExteriorMapId == targetMapId)
                {
                    // Leader went to the exterior — but this is an entry door, not an exit. Skip.
                    continue;
                }
            }
            else
            {
                if (door.TargetMapId == targetMapId)
                {
                    doorTargetMapId = targetMapId;
                }
            }

            if (doorTargetMapId == null) continue;

            float dist = Vector3.Distance(follower.transform.position, door.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDoor = door;
            }
        }

        return bestDoor;
    }

    internal void StartPortalFollow(MapTransitionDoor door)
    {
        StopPortalFollow();
        _portalFollowCoroutine = StartCoroutine(PortalFollowRoutine(door));
    }

    internal void StopPortalFollow()
    {
        if (_portalFollowCoroutine != null)
        {
            StopCoroutine(_portalFollowCoroutine);
            _portalFollowCoroutine = null;
        }
    }

    /// <summary>
    /// Walks the follower to a non-building portal door and triggers it.
    /// Mirrors <see cref="CharacterDoorTraversalAction"/>'s walk-loop, but inlined here
    /// because Enter/Leave actions don't semantically cover outdoor↔outdoor portals.
    /// IMPORTANT: every exit path must Unfreeze the controller — same discipline as
    /// the action base class.
    /// </summary>
    private System.Collections.IEnumerator PortalFollowRoutine(MapTransitionDoor door)
    {
        if (door == null || _character == null) yield break;

        if (_character.Controller != null && !_character.IsPlayer())
            _character.Controller.Freeze();
        _character.CharacterMovement.Resume();
        _character.CharacterMovement.SetDestination(door.transform.position);

        const float Timeout = 15f;
        const float Repath = 2f;
        float elapsed = 0f;
        float sinceRepath = 0f;

        while (elapsed < Timeout)
        {
            if (_character == null || !_character.IsAlive() || door == null) break;

            if (door.IsCharacterInInteractionZone(_character))
            {
                _character.CharacterMovement.Stop();
                if (_character.Controller != null) _character.Controller.Unfreeze();
                door.Interact(_character);
                _portalFollowCoroutine = null;
                yield break;
            }

            sinceRepath += UnityEngine.Time.deltaTime;
            if (sinceRepath >= Repath)
            {
                _character.CharacterMovement.SetDestination(door.transform.position);
                sinceRepath = 0f;
            }

            elapsed += UnityEngine.Time.deltaTime;
            yield return null;
        }

        // Timeout / error path — same Unfreeze discipline.
        _character.CharacterMovement.Stop();
        if (_character.Controller != null) _character.Controller.Unfreeze();
        UpdateFollowState();
        _portalFollowCoroutine = null;
    }

    private bool IsOnSameMapAs(Character a, Character b)
    {
        if (a == null || b == null) return false;
        var trackerA = a.GetComponent<CharacterMapTracker>();
        var trackerB = b.GetComponent<CharacterMapTracker>();
        if (trackerA == null || trackerB == null) return true;
        string mapA = trackerA.CurrentMapID.Value.ToString();
        string mapB = trackerB.CurrentMapID.Value.ToString();
        if (string.IsNullOrEmpty(mapA) || string.IsNullOrEmpty(mapB)) return true;
        return mapA == mapB;
    }

    // CLEANUP
    protected override void OnDisable()
    {
        StopPortalFollow();
        if (_gatherCoroutine != null)
        {
            StopCoroutine(_gatherCoroutine);
            _gatherCoroutine = null;
        }
        DisableGatherZone();
        UnsubscribeFromLeader();
        base.OnDisable();
    }

    // === ICharacterSaveData IMPLEMENTATION ===
    public string SaveKey => "CharacterParty";
    public int LoadPriority => 60;

    public PartySaveData Serialize()
    {
        if (_partyData == null)
            return new PartySaveData();

        return new PartySaveData
        {
            partyId = _partyData.PartyId,
            isLeader = _partyData.IsLeader(_character.CharacterId),
            followMode = (int)_partyData.FollowMode
        };
    }

    public void Deserialize(PartySaveData data)
    {
        // Store the data for later party reformation by CharacterDataCoordinator.
        // Actual party creation/joining happens after all profiles are imported.
        if (data == null || string.IsNullOrEmpty(data.partyId)) return;

        _deserializedPartySaveData = data;
    }

    /// <summary>
    /// Cached deserialized party data for deferred party reformation.
    /// CharacterDataCoordinator reads this after all profiles are loaded.
    /// </summary>
    private PartySaveData _deserializedPartySaveData;
    public PartySaveData DeserializedPartySaveData => _deserializedPartySaveData;

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
