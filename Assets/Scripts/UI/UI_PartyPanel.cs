using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Passive HUD panel that displays party info and member list.
/// No buttons, no actions — just a read-only display.
/// Visible only when the player is in a party.
/// </summary>
public class UI_PartyPanel : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject _panelRoot;

    [Header("Party Info")]
    [SerializeField] private TMP_Text _partyNameText;
    [SerializeField] private TMP_Text _memberCountText;

    [Header("Member List")]
    [SerializeField] private Transform _memberListContainer;
    [SerializeField] private GameObject _memberEntryPrefab;

    private Character _localCharacter;
    private CharacterParty _localParty;
    private List<GameObject> _spawnedEntries = new List<GameObject>();

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
            Hide();
            return;
        }

        _localParty.OnJoinedParty += HandlePartyChanged;
        _localParty.OnLeftParty += HandlePartyLeft;
        _localParty.OnPartyStateChanged += HandleStateChanged;
        _localParty.OnMemberKicked += HandleMemberKicked;
        _localParty.OnPartyRosterChanged += HandleRosterChanged;

        RefreshUI();
    }

    public void Unbind()
    {
        if (_localParty != null)
        {
            _localParty.OnJoinedParty -= HandlePartyChanged;
            _localParty.OnLeftParty -= HandlePartyLeft;
            _localParty.OnPartyStateChanged -= HandleStateChanged;
            _localParty.OnMemberKicked -= HandleMemberKicked;
            _localParty.OnPartyRosterChanged -= HandleRosterChanged;
        }

        _localCharacter = null;
        _localParty = null;
        Hide();
    }

    // =============================================
    //  DISPLAY
    // =============================================

    private void RefreshUI()
    {
        if (_localParty == null || !_localParty.IsInParty)
        {
            Hide();
            return;
        }

        Show();
    }

    private void Show()
    {
        if (_panelRoot != null) _panelRoot.SetActive(true);

        PartyData data = _localParty?.PartyData;
        if (data == null) return;

        if (_partyNameText != null)
            _partyNameText.text = data.PartyName;

        if (_memberCountText != null)
            _memberCountText.text = data.MemberCount.ToString();

        RebuildMemberList(data);
    }

    private void Hide()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
        ClearEntries();
    }

    private void RebuildMemberList(PartyData data)
    {
        ClearEntries();

        if (_memberEntryPrefab == null || _memberListContainer == null) return;

        foreach (string memberId in data.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            string memberName = member != null ? member.CharacterName : "...";
            bool isLeader = data.IsLeader(memberId);

            GameObject entry = Instantiate(_memberEntryPrefab, _memberListContainer);
            TMP_Text nameText = entry.GetComponentInChildren<TMP_Text>();
            if (nameText != null)
            {
                string prefix = isLeader ? "[L] " : "";
                nameText.text = $"{prefix}{memberName}";
            }

            _spawnedEntries.Add(entry);
        }
    }

    private void ClearEntries()
    {
        foreach (var entry in _spawnedEntries)
        {
            if (entry != null) Destroy(entry);
        }
        _spawnedEntries.Clear();
    }

    // =============================================
    //  EVENT HANDLERS
    // =============================================

    private void HandlePartyChanged(PartyData data) => RefreshUI();
    private void HandlePartyLeft() => RefreshUI();
    private void HandleStateChanged(PartyState state) => RefreshUI();
    private void HandleMemberKicked(string characterId) => RefreshUI();
    private void HandleRosterChanged() => RefreshUI();

    private void OnDestroy()
    {
        Unbind();
    }
}
