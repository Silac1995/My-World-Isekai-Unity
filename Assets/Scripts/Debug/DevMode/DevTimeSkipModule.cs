using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dev-mode panel module that exposes a single "Skip N hours" control. Lives on
/// a tab content GameObject inside <see cref="DevModePanel"/>; the tab entry is
/// added to the DevModePanel prefab via Inspector. Clicking the button delegates
/// to <see cref="MWI.Time.TimeSkipController"/> — same entry point as the
/// /timeskip chat command.
/// </summary>
public class DevTimeSkipModule : MonoBehaviour
{
    [SerializeField] private TMP_InputField _hoursInput;
    [SerializeField] private Button _skipButton;
    [SerializeField] private TMP_Text _statusLabel;

    private void Awake()
    {
        if (_skipButton != null) _skipButton.onClick.AddListener(OnSkipClicked);
    }

    private void OnDestroy()
    {
        if (_skipButton != null) _skipButton.onClick.RemoveListener(OnSkipClicked);
    }

    private void OnSkipClicked()
    {
        if (_hoursInput == null || string.IsNullOrWhiteSpace(_hoursInput.text))
        {
            SetStatus("Enter a number of hours (1–168).");
            return;
        }
        if (!int.TryParse(_hoursInput.text, out int hours))
        {
            SetStatus($"'{_hoursInput.text}' is not a number.");
            return;
        }
        if (MWI.Time.TimeSkipController.Instance == null)
        {
            SetStatus("TimeSkipController not in scene.");
            return;
        }
        bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours);
        SetStatus(ok ? $"Skipping {hours}h…" : "Skip rejected — see Console for reason.");
    }

    private void SetStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.text = msg;
        Debug.Log($"<color=magenta>[DevTimeSkip]</color> {msg}");
    }
}
