using TMPro;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Always-visible minimal HUD widget for the player's currently-focused Quest.
/// Two lines: Title + InstructionLine (with progress appended if Required > 0).
/// Hidden when no active quests. Local-player-only — PlayerUI.Initialize wires it up.
/// </summary>
public class UI_QuestTracker : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _instructionText;
    [SerializeField] private GameObject _root;

    private CharacterQuestLog _log;

    public void Initialize(CharacterQuestLog log)
    {
        if (_log != null)
        {
            _log.OnFocusedChanged -= HandleFocusChanged;
            _log.OnQuestProgressChanged -= HandleProgressChanged;
            _log.OnQuestAdded -= HandleProgressChanged;
        }
        _log = log;
        if (_log != null)
        {
            _log.OnFocusedChanged += HandleFocusChanged;
            _log.OnQuestProgressChanged += HandleProgressChanged;
            // Refresh on new quest in case it gets auto-focused.
            _log.OnQuestAdded += HandleProgressChanged;
        }
        Refresh();
    }

    public void HandleQuestAdded(IQuest quest) => Refresh();
    public void HandleFocusChanged(IQuest quest) => Refresh();

    private void HandleProgressChanged(IQuest quest) => Refresh();

    private void Refresh()
    {
        if (_log == null || _log.FocusedQuest == null)
        {
            if (_root != null) _root.SetActive(false);
            return;
        }
        if (_root != null) _root.SetActive(true);

        var q = _log.FocusedQuest;
        if (_titleText != null) _titleText.text = q.Title;
        if (_instructionText != null)
        {
            string line = q.InstructionLine;
            if (q.Required > 0 && q.Required != int.MaxValue)
                line += $" ({q.TotalProgress} / {q.Required})";
            _instructionText.text = line;
        }
    }

    private void OnDestroy()
    {
        if (_log != null)
        {
            _log.OnFocusedChanged -= HandleFocusChanged;
            _log.OnQuestProgressChanged -= HandleProgressChanged;
            _log.OnQuestAdded -= HandleProgressChanged;
        }
    }
}
