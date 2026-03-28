using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class CharacterParty : CharacterSystem
{
    // --- Serialized References ---
    [SerializeField] private SkillSO _leadershipSkill;
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
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName);
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
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName);
        NotifyPartyMemberJoinedClientRpc(_character.CharacterName);
        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} joined party '{_partyData.PartyName}'");
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
            GrantLeadershipSkillIfNeeded(_partyData.LeaderId);
            NotifyLeaderChangedClientRpc(_partyData.LeaderId);
        }
        if (_partyData.MemberCount == 0) PartyRegistry.Unregister(partyId);
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
        _partyData.LeaderId = characterId;
        GrantLeadershipSkillIfNeeded(characterId);
        NotifyLeaderChangedClientRpc(characterId);
        UnsubscribeFromLeader();
        SubscribeToLeader(Character.FindByUUID(characterId));
        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} promoted {characterId} to party leader");
    }

    public void SetFollowMode(PartyFollowMode mode)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        _partyData.FollowMode = mode;
        _networkFollowMode.Value = (byte)mode;
        OnFollowModeChanged?.Invoke(mode);
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

    private void OnLeaderDied(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        if (!_partyData.IsMember(leader.CharacterId)) return; // Guard against duplicate processing
        UnsubscribeFromLeader();
        if (_partyData.MemberCount <= 1) { HandleDisbanded(); return; }
        _partyData.RemoveMember(leader.CharacterId);
        PartyRegistry.UnmapCharacter(leader.CharacterId);
        GrantLeadershipSkillIfNeeded(_partyData.LeaderId);
        NotifyLeaderChangedClientRpc(_partyData.LeaderId);
        if (!_partyData.IsLeader(_character.CharacterId))
        {
            Character newLeader = Character.FindByUUID(_partyData.LeaderId);
            SubscribeToLeader(newLeader);
        }
        else Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} became party leader after leader death");
    }

    private void OnLeaderIncapacitated(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        SetPartyState(PartyState.LeaderlessHold);
    }

    private void OnLeaderWokeUp(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        if (_partyData.State == PartyState.LeaderlessHold) SetPartyState(PartyState.Active);
    }

    private void SubscribeToLeader(Character leader)
    {
        if (leader == null || leader == _character) return;
        UnsubscribeFromLeader();
        _subscribedLeader = leader;
        _subscribedLeader.OnDeath += OnLeaderDied;
        _subscribedLeader.OnIncapacitated += OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp += OnLeaderWokeUp;
    }

    private void UnsubscribeFromLeader()
    {
        if (_subscribedLeader == null) return;
        _subscribedLeader.OnDeath -= OnLeaderDied;
        _subscribedLeader.OnIncapacitated -= OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp -= OnLeaderWokeUp;
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
        _partyData.State = state;
        _networkPartyState.Value = (byte)state;
        OnPartyStateChanged?.Invoke(state);
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
    private void NotifyJoinedPartyClientRpc(FixedString64Bytes partyId, FixedString64Bytes partyName)
    {
        string id = partyId.ToString();
        if (_partyData == null) _partyData = PartyRegistry.GetParty(id);
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
        Character newLeader = Character.FindByUUID(id);
        SubscribeToLeader(newLeader);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyKickedToastClientRpc(FixedString64Bytes partyName) { }

    // NETWORK VARIABLE CHANGE CALLBACKS
    private void OnNetworkPartyIdChanged(FixedString64Bytes prev, FixedString64Bytes next) { }
    private void OnNetworkPartyStateChanged(byte prev, byte next) { OnPartyStateChanged?.Invoke((PartyState)next); }
    private void OnNetworkFollowModeChanged(byte prev, byte next) { OnFollowModeChanged?.Invoke((PartyFollowMode)next); }

    // CLEANUP
    protected override void OnDisable()
    {
        UnsubscribeFromLeader();
        base.OnDisable();
    }
}
