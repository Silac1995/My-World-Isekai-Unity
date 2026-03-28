using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single member entry in the party window member list.
/// Instantiated per party member from a prefab.
/// </summary>
public class UI_PartyMemberSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _roleText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _hpText;
    [SerializeField] private Slider _hpBar;
    [SerializeField] private Button _kickButton;
    [SerializeField] private Button _promoteButton;

    private string _characterId;
    private Character _character;
    private Action<string> _onKick;
    private Action<string> _onPromote;

    public void Setup(string characterId, string characterName, bool isLeader, string status,
        bool showLeaderControls, Action<string> onKick, Action<string> onPromote)
    {
        _characterId = characterId;
        _character = Character.FindByUUID(characterId);
        _onKick = onKick;
        _onPromote = onPromote;

        if (_nameText != null)
            _nameText.text = characterName;

        if (_roleText != null)
            _roleText.text = isLeader ? "Leader" : "Member";

        if (_statusText != null)
            _statusText.text = status;

        UpdateHP();

        // Leader controls: only show on non-leader members, only if the viewer is the leader
        bool showButtons = showLeaderControls && !isLeader;
        if (_kickButton != null)
        {
            _kickButton.gameObject.SetActive(showButtons);
            _kickButton.onClick.RemoveAllListeners();
            if (showButtons) _kickButton.onClick.AddListener(OnKickClicked);
        }
        if (_promoteButton != null)
        {
            _promoteButton.gameObject.SetActive(showButtons);
            _promoteButton.onClick.RemoveAllListeners();
            if (showButtons) _promoteButton.onClick.AddListener(OnPromoteClicked);
        }

        // Subscribe to HP changes for live updates
        if (_character != null && _character.Stats != null && _character.Stats.Health != null)
        {
            _character.Stats.Health.OnAmountChanged += OnHPChanged;
        }
    }

    private void UpdateHP()
    {
        if (_character == null || _character.Stats == null || _character.Stats.Health == null)
        {
            if (_hpText != null) _hpText.text = "?";
            if (_hpBar != null) _hpBar.gameObject.SetActive(false);
            return;
        }

        float current = _character.Stats.Health.CurrentAmount;
        float max = _character.Stats.Health.MaxValue;

        if (_hpText != null)
            _hpText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

        if (_hpBar != null)
        {
            _hpBar.gameObject.SetActive(true);
            _hpBar.maxValue = max;
            _hpBar.value = current;
        }
    }

    private void OnHPChanged(float oldAmount, float newAmount)
    {
        UpdateHP();
    }

    private void OnKickClicked() => _onKick?.Invoke(_characterId);
    private void OnPromoteClicked() => _onPromote?.Invoke(_characterId);

    private void OnDestroy()
    {
        if (_character != null && _character.Stats != null && _character.Stats.Health != null)
        {
            _character.Stats.Health.OnAmountChanged -= OnHPChanged;
        }
    }
}
