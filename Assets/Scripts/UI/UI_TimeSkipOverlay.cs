using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fade-to-black overlay shown during a time-skip. Subscribes to
/// <see cref="MWI.Time.TimeSkipController"/> events; shows progress and a Cancel button.
/// One instance lives in the persistent UI canvas.
/// </summary>
public class UI_TimeSkipOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _hourLabel;
    [SerializeField] private Button _cancelButton;

    private void Awake()
    {
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnDestroy()
    {
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
        UnsubscribeFromController();
    }

    private void OnEnable() => SubscribeToController();
    private void OnDisable() => UnsubscribeFromController();

    private void SubscribeToController()
    {
        if (MWI.Time.TimeSkipController.Instance == null) return;
        MWI.Time.TimeSkipController.Instance.OnSkipStarted += HandleStarted;
        MWI.Time.TimeSkipController.Instance.OnSkipHourTick += HandleHourTick;
        MWI.Time.TimeSkipController.Instance.OnSkipEnded += HandleEnded;
    }

    private void UnsubscribeFromController()
    {
        if (MWI.Time.TimeSkipController.Instance == null) return;
        MWI.Time.TimeSkipController.Instance.OnSkipStarted -= HandleStarted;
        MWI.Time.TimeSkipController.Instance.OnSkipHourTick -= HandleHourTick;
        MWI.Time.TimeSkipController.Instance.OnSkipEnded -= HandleEnded;
    }

    private void HandleStarted(int totalHours)
    {
        gameObject.SetActive(true);
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        if (_hourLabel != null) _hourLabel.text = $"Skipping… 0 / {totalHours} h";
    }

    private void HandleHourTick(int elapsed, int total)
    {
        if (_hourLabel != null) _hourLabel.text = $"Skipping… {elapsed} / {total} h";
    }

    private void HandleEnded()
    {
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void OnCancelClicked()
    {
        if (MWI.Time.TimeSkipController.Instance != null)
            MWI.Time.TimeSkipController.Instance.RequestAbort();
    }
}
