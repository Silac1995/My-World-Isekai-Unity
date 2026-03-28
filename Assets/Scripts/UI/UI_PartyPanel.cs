using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Party HUD panel. Shows party members, leader controls, and gathering status.
/// </summary>
public class UI_PartyPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private GameObject _createPartySection;
    [SerializeField] private GameObject _partyViewSection;
    [SerializeField] private TMP_InputField _partyNameInput;
    [SerializeField] private Button _createPartyButton;
    [SerializeField] private Button _disbandButton;
    [SerializeField] private Button _leaveButton;
    [SerializeField] private Transform _memberListContainer;
    [SerializeField] private TMP_Text _partyNameText;
    [SerializeField] private TMP_Text _followModeText;

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

        HideAll();
    }

    public void Bind(Character localCharacter)
    {
        Unbind();

        _localCharacter = localCharacter;
        _localParty = localCharacter?.CharacterParty;

        if (_localParty == null) return;

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
    }

    private void RefreshUI()
    {
        if (_localParty == null || !_localParty.IsInParty)
        {
            ShowCreateSection();
            return;
        }

        ShowPartyView();
    }

    private void ShowCreateSection()
    {
        if (_createPartySection != null) _createPartySection.SetActive(true);
        if (_partyViewSection != null) _partyViewSection.SetActive(false);
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
        if (_disbandButton != null)
            _disbandButton.gameObject.SetActive(isLeader);
        if (_leaveButton != null)
            _leaveButton.gameObject.SetActive(!isLeader);
    }

    private void HideAll()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
    }

    // --- Button Handlers ---

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

    // --- Event Handlers ---

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
