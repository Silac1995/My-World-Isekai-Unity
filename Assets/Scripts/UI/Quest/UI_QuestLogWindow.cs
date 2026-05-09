using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.Quests;

/// <summary>
/// Full-panel quest log. Two columns:
/// - Left: list of active quests (one button per quest).
/// - Right: details panel (title, description, contributors, Set Focused + Abandon buttons).
///
/// Bound to L key by PlayerUI. Opens/closes via UI_WindowBase.OpenWindow/CloseWindow.
/// Local-player-only — PlayerUI.Initialize wires it up to the local Character's CharacterQuestLog.
/// </summary>
public class UI_QuestLogWindow : UI_WindowBase
{
    [SerializeField] private RectTransform _listParent;
    [Tooltip("Prefab for one list entry. Must have a Button + a TextMeshProUGUI child.")]
    [SerializeField] private GameObject _listEntryPrefab;
    [SerializeField] private TextMeshProUGUI _detailsTitle;
    [SerializeField] private TextMeshProUGUI _detailsBody;
    [SerializeField] private TextMeshProUGUI _detailsContributors;
    [SerializeField] private Button _setFocusedButton;
    [SerializeField] private Button _abandonButton;

    private CharacterQuestLog _log;
    private IQuest _selected;

    public void Initialize(CharacterQuestLog log)
    {
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleAnyChange;
            _log.OnQuestRemoved -= HandleAnyChange;
            _log.OnQuestProgressChanged -= HandleAnyChange;
        }
        _log = log;
        if (_log != null)
        {
            _log.OnQuestAdded += HandleAnyChange;
            _log.OnQuestRemoved += HandleAnyChange;
            _log.OnQuestProgressChanged += HandleAnyChange;
        }
        RefreshList();
    }

    private void HandleAnyChange(IQuest _)
    {
        RefreshList();
        RefreshDetails();
    }

    public void RefreshList()
    {
        if (_listParent == null) return;
        // Clear existing children
        for (int i = _listParent.childCount - 1; i >= 0; i--)
            Destroy(_listParent.GetChild(i).gameObject);

        if (_log == null) return;
        foreach (var q in _log.ActiveQuests)
        {
            if (_listEntryPrefab == null) continue;
            var entry = Instantiate(_listEntryPrefab, _listParent);
            var label = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = q.Title;
            var btn = entry.GetComponent<Button>();
            if (btn != null)
            {
                var captured = q;
                btn.onClick.AddListener(() => SelectQuest(captured));
            }
        }
    }

    private void SelectQuest(IQuest quest)
    {
        _selected = quest;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_selected == null) { ClearDetails(); return; }

        if (_detailsTitle != null) _detailsTitle.text = _selected.Title;
        if (_detailsBody != null) _detailsBody.text = $"{_selected.InstructionLine}\n\n{_selected.Description}";
        if (_detailsContributors != null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Contributors ({_selected.Contributors.Count}):");
            foreach (var c in _selected.Contributors)
            {
                int contrib = _selected.Contribution.TryGetValue(c.CharacterId, out var v) ? v : 0;
                sb.AppendLine($"  • {c.CharacterName} ({contrib})");
            }
            _detailsContributors.text = sb.ToString();
        }

        if (_setFocusedButton != null)
        {
            _setFocusedButton.onClick.RemoveAllListeners();
            _setFocusedButton.onClick.AddListener(() => _log?.SetFocused(_selected));
        }

        if (_abandonButton != null)
        {
            _abandonButton.onClick.RemoveAllListeners();
            _abandonButton.onClick.AddListener(() =>
            {
                if (_selected != null && _log != null) _log.TryAbandon(_selected);
                _selected = null;
                ClearDetails();
            });
        }
    }

    private void ClearDetails()
    {
        if (_detailsTitle != null) _detailsTitle.text = "";
        if (_detailsBody != null) _detailsBody.text = "";
        if (_detailsContributors != null) _detailsContributors.text = "";
    }

    private void OnDestroy()
    {
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleAnyChange;
            _log.OnQuestRemoved -= HandleAnyChange;
            _log.OnQuestProgressChanged -= HandleAnyChange;
        }
    }
}
