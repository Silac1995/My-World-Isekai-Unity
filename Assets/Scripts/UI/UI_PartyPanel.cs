using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Party HUD panel. Shows party members, leader controls, and gathering status.
/// Bound to a Character via PlayerUI.Initialize().
/// </summary>
public class UI_PartyPanel : MonoBehaviour
{
    [Header("Sections")]
    [SerializeField] private GameObject _createPartySection;
    [SerializeField] private GameObject _partyViewSection;

    [Header("Create Party")]
    [SerializeField] private TMP_InputField _partyNameInput;
    [SerializeField] private Button _createPartyButton;

    [Header("Party View")]
    [SerializeField] private TMP_Text _partyNameText;
    [SerializeField] private TMP_Text _followModeText;
    [SerializeField] private Transform _memberListContainer;
    [SerializeField] private Button _disbandButton;
    [SerializeField] private Button _leaveButton;

    private Character _localCharacter;
    private CharacterParty _localParty;

    private void Start()
    {
        if (_createPartyButton != null)
            _createPartyButton.onClick.AddListener(OnCreatePartyClicked);
        if (_disbandButton != null)
            _disbandButton.onClick.AddListener(OnDisbandClicked);
        if (_leaveButton != null)
            _leaveButton.onClick.AddListener(OnLeaveClicked);
    }

    // =============================================
    //  BIND / UNBIND (called by PlayerUI)
    // =============================================

    public void Bind(Character localCharacter)
    {
        Unbind();

        _localCharacter = localCharacter;
        _localParty = localCharacter?.CharacterParty;

        if (_localParty == null)
        {
            HideAll();
            return;
        }

        _localParty.OnJoinedParty += HandleJoinedParty;
        _localParty.OnLeftParty += HandleLeftParty;
        _localParty.OnPartyStateChanged += HandleStateChanged;
        _localParty.OnFollowModeChanged += HandleFollowModeChanged;
        _localParty.OnMemberKicked += HandleMemberKicked;

        RefreshUI();
    }

    public void Unbind()
    {
        if (_localParty != null)
        {
            _localParty.OnJoinedParty -= HandleJoinedParty;
            _localParty.OnLeftParty -= HandleLeftParty;
            _localParty.OnPartyStateChanged -= HandleStateChanged;
            _localParty.OnFollowModeChanged -= HandleFollowModeChanged;
            _localParty.OnMemberKicked -= HandleMemberKicked;
        }

        _localCharacter = null;
        _localParty = null;
        HideAll();
    }

    // =============================================
    //  UI STATE
    // =============================================

    private void RefreshUI()
    {
        if (_localParty == null)
        {
            HideAll();
            return;
        }

        if (_localParty.IsInParty)
        {
            ShowPartyView();
        }
        else
        {
            ShowCreateSection();
        }
    }

    private void ShowCreateSection()
    {
        if (_createPartySection != null) _createPartySection.SetActive(true);
        if (_partyViewSection != null) _partyViewSection.SetActive(false);

        // Disable create button if player doesn't have Leadership skill
        if (_createPartyButton != null)
        {
            bool canCreate = _localCharacter != null
                && _localCharacter.CharacterSkills != null
                && _localParty != null
                && !_localParty.IsInParty;
            _createPartyButton.interactable = canCreate;
        }
    }

    private void ShowPartyView()
    {
        if (_createPartySection != null) _createPartySection.SetActive(false);
        if (_partyViewSection != null) _partyViewSection.SetActive(true);

        PartyData data = _localParty?.PartyData;
        if (data == null) return;

        if (_partyNameText != null)
            _partyNameText.text = data.PartyName;

        if (_followModeText != null)
            _followModeText.text = _localParty.CurrentFollowMode.ToString();

        bool isLeader = _localParty.IsPartyLeader;
        if (_disbandButton != null) _disbandButton.gameObject.SetActive(isLeader);
        if (_leaveButton != null) _leaveButton.gameObject.SetActive(!isLeader);
    }

    private void HideAll()
    {
        if (_createPartySection != null) _createPartySection.SetActive(false);
        if (_partyViewSection != null) _partyViewSection.SetActive(false);
    }

    // =============================================
    //  BUTTON HANDLERS
    // =============================================

    private void OnCreatePartyClicked()
    {
        if (_localParty == null) return;
        string name = _partyNameInput != null ? _partyNameInput.text : null;
        _localParty.CreateParty(string.IsNullOrWhiteSpace(name) ? null : name);
    }

    private void OnDisbandClicked()
    {
        _localParty?.DisbandParty();
    }

    private void OnLeaveClicked()
    {
        _localParty?.LeaveParty();
    }

    // =============================================
    //  EVENT HANDLERS
    // =============================================

    private void HandleJoinedParty(PartyData data) => RefreshUI();
    private void HandleLeftParty() => RefreshUI();
    private void HandleStateChanged(PartyState state) => RefreshUI();
    private void HandleFollowModeChanged(PartyFollowMode mode) => RefreshUI();
    private void HandleMemberKicked(string characterId) => RefreshUI();

    private void OnDestroy()
    {
        Unbind();
    }
}
