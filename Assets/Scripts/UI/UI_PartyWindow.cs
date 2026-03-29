using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full party management window. Toggled open/close like Stats or Relations.
/// Allows creating a party, viewing members, and managing the group.
/// </summary>
public class UI_PartyWindow : UI_WindowBase
{
    [Header("Sections")]
    [SerializeField] private GameObject _noPartySection;
    [SerializeField] private GameObject _partySection;

    [Header("Create Party")]
    [SerializeField] private TMP_InputField _partyNameInput;
    [SerializeField] private Button _createPartyButton;

    [Header("Party Info")]
    [SerializeField] private TextMeshProUGUI _partyNameText;
    [SerializeField] private TextMeshProUGUI _followModeText;
    [SerializeField] private TextMeshProUGUI _partyStateText;
    [SerializeField] private TextMeshProUGUI _memberCountText;

    [Header("Member List")]
    [SerializeField] private Transform _memberListContainer;
    [SerializeField] private GameObject _memberSlotPrefab;

    [Header("Leader Controls")]
    [SerializeField] private GameObject _leaderControlsGroup;
    [SerializeField] private Button _followModeToggleButton;
    [SerializeField] private TextMeshProUGUI _followModeToggleText;
    [SerializeField] private Button _disbandButton;

    [Header("Member Controls")]
    [SerializeField] private Button _leaveButton;

    private Character _character;
    private CharacterParty _party;
    private List<UI_PartyMemberSlot> _spawnedSlots = new List<UI_PartyMemberSlot>();

    protected override void Awake()
    {
        base.Awake();

        if (_createPartyButton != null)
            _createPartyButton.onClick.AddListener(OnCreatePartyClicked);
        if (_disbandButton != null)
            _disbandButton.onClick.AddListener(OnDisbandClicked);
        if (_leaveButton != null)
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        if (_followModeToggleButton != null)
            _followModeToggleButton.onClick.AddListener(OnToggleFollowModeClicked);
    }

    // =============================================
    //  INITIALIZE (called by PlayerUI)
    // =============================================

    public void Initialize(Character character)
    {
        // Unsubscribe from old
        if (_party != null)
        {
            _party.OnJoinedParty -= HandlePartyChanged;
            _party.OnLeftParty -= HandlePartyChanged;
            _party.OnPartyStateChanged -= HandleStateChanged;
            _party.OnFollowModeChanged -= HandleFollowModeChanged;
            _party.OnMemberKicked -= HandleMemberKicked;
            _party.OnPartyRosterChanged -= HandleRosterChanged;
        }

        _character = character;
        _party = character?.CharacterParty;

        // Subscribe to new
        if (_party != null)
        {
            _party.OnJoinedParty += HandlePartyChanged;
            _party.OnLeftParty += HandlePartyChanged;
            _party.OnPartyStateChanged += HandleStateChanged;
            _party.OnFollowModeChanged += HandleFollowModeChanged;
            _party.OnMemberKicked += HandleMemberKicked;
            _party.OnPartyRosterChanged += HandleRosterChanged;
            RefreshDisplay();
        }
        else
        {
            ClearSlots();
        }
    }

    private void OnEnable()
    {
        if (_character != null)
        {
            RefreshDisplay();
        }
    }

    // =============================================
    //  DISPLAY
    // =============================================

    public void RefreshDisplay()
    {
        if (_character == null || _party == null)
        {
            ShowNoPartySection();
            return;
        }

        if (_party.IsInParty)
        {
            ShowPartySection();
        }
        else
        {
            ShowNoPartySection();
        }
    }

    private void ShowNoPartySection()
    {
        if (_noPartySection != null) _noPartySection.SetActive(true);
        if (_partySection != null) _partySection.SetActive(false);

        if (_createPartyButton != null)
        {
            _createPartyButton.interactable = _character != null && _party != null;
        }

        ClearSlots();
    }

    private void ShowPartySection()
    {
        if (_noPartySection != null) _noPartySection.SetActive(false);
        if (_partySection != null) _partySection.SetActive(true);

        PartyData data = _party.PartyData;
        if (data == null) return;

        bool isLeader = _party.IsPartyLeader;

        // Party info
        if (_partyNameText != null)
            _partyNameText.text = data.PartyName;

        if (_memberCountText != null)
            _memberCountText.text = $"{data.MemberCount} / {GetMaxPartySize()}";

        if (_followModeText != null)
            _followModeText.text = _party.CurrentFollowMode.ToString();

        if (_partyStateText != null)
            _partyStateText.text = _party.CurrentState.ToString();

        // Follow mode toggle
        if (_followModeToggleText != null)
        {
            _followModeToggleText.text = _party.CurrentFollowMode == PartyFollowMode.Strict
                ? "Switch to Loose"
                : "Switch to Strict";
        }

        // Leader controls vs member controls
        if (_leaderControlsGroup != null) _leaderControlsGroup.SetActive(isLeader);
        if (_leaveButton != null) _leaveButton.gameObject.SetActive(!isLeader);

        // Rebuild member list
        RebuildMemberList(data, isLeader);
    }

    private void RebuildMemberList(PartyData data, bool isLeader)
    {
        ClearSlots();

        if (_memberSlotPrefab == null || _memberListContainer == null) return;

        foreach (string memberId in data.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            string memberName = member != null ? member.CharacterName : memberId;
            bool isMemberLeader = data.IsLeader(memberId);
            string status = GetMemberStatus(member);

            GameObject slotGO = Instantiate(_memberSlotPrefab, _memberListContainer);
            UI_PartyMemberSlot slot = slotGO.GetComponent<UI_PartyMemberSlot>();

            if (slot != null)
            {
                slot.Setup(memberId, memberName, isMemberLeader, status,
                    isLeader, OnKickMember, OnPromoteMember);
                _spawnedSlots.Add(slot);
            }
        }
    }

    private string GetMemberStatus(Character member)
    {
        if (member == null) return "Offline";
        if (!member.IsAlive()) return "Dead";
        if (!member.IsFree(out CharacterBusyReason reason))
        {
            return reason switch
            {
                CharacterBusyReason.InCombat => "In Combat",
                CharacterBusyReason.Interacting => "Interacting",
                CharacterBusyReason.DoingAction => "Busy",
                CharacterBusyReason.Unconscious => "Unconscious",
                _ => "Busy"
            };
        }
        return "Active";
    }

    private int GetMaxPartySize()
    {
        if (_character == null || _character.CharacterSkills == null) return 2;
        PartyData data = _party?.PartyData;
        if (data == null) return 2;

        Character leader = Character.FindByUUID(data.LeaderId);
        if (leader == null || leader.CharacterSkills == null) return 2;

        // Match the formula from CharacterParty: Mathf.Min(2 + level, 8)
        // We read the leader's skill level but we don't have the SkillSO ref here,
        // so we approximate from CharacterParty's accessor
        return 8; // UI shows max cap; exact number requires SkillSO ref
    }

    private void ClearSlots()
    {
        foreach (var slot in _spawnedSlots)
        {
            if (slot != null && slot.gameObject != null)
                Destroy(slot.gameObject);
        }
        _spawnedSlots.Clear();
    }

    // =============================================
    //  BUTTON HANDLERS
    // =============================================

    private void OnCreatePartyClicked()
    {
        if (_party == null) return;
        string name = _partyNameInput != null ? _partyNameInput.text : null;
        string cleanName = string.IsNullOrWhiteSpace(name) ? null : name;

        if (_party.IsServer)
            _party.CreateParty(cleanName);
        else
            _party.RequestCreatePartyServerRpc(cleanName ?? "");
    }

    private void OnDisbandClicked()
    {
        if (_party == null) return;
        if (_party.IsServer)
            _party.DisbandParty();
        else
            _party.RequestDisbandPartyServerRpc();
    }

    private void OnLeaveClicked()
    {
        if (_party == null) return;
        if (_party.IsServer)
            _party.LeaveParty();
        else
            _party.RequestLeavePartyServerRpc();
    }

    private void OnToggleFollowModeClicked()
    {
        if (_party == null || !_party.IsPartyLeader) return;

        PartyFollowMode newMode = _party.CurrentFollowMode == PartyFollowMode.Strict
            ? PartyFollowMode.Loose
            : PartyFollowMode.Strict;

        if (_party.IsServer)
            _party.SetFollowMode(newMode);
        else
            _party.RequestSetFollowModeServerRpc((byte)newMode);
    }

    private void OnKickMember(string characterId)
    {
        if (_party == null) return;
        if (_party.IsServer)
            _party.KickMember(characterId);
        else
            _party.RequestKickMemberServerRpc(characterId);
    }

    private void OnPromoteMember(string characterId)
    {
        if (_party == null) return;
        if (_party.IsServer)
            _party.PromoteLeader(characterId);
        else
            _party.RequestPromoteLeaderServerRpc(characterId);
    }

    // =============================================
    //  EVENT HANDLERS
    // =============================================

    private void HandlePartyChanged(PartyData data) => RefreshDisplay();
    private void HandlePartyChanged() => RefreshDisplay();
    private void HandleStateChanged(PartyState state) => RefreshDisplay();
    private void HandleFollowModeChanged(PartyFollowMode mode) => RefreshDisplay();
    private void HandleMemberKicked(string characterId) => RefreshDisplay();
    private void HandleRosterChanged() => RefreshDisplay();

    // =============================================
    //  CLEANUP
    // =============================================

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (_party != null)
        {
            _party.OnJoinedParty -= HandlePartyChanged;
            _party.OnLeftParty -= HandlePartyChanged;
            _party.OnPartyStateChanged -= HandleStateChanged;
            _party.OnFollowModeChanged -= HandleFollowModeChanged;
            _party.OnMemberKicked -= HandleMemberKicked;
            _party.OnPartyRosterChanged -= HandleRosterChanged;
        }
    }
}
