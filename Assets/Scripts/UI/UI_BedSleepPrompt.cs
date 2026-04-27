using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-world modal that appears when the local player uses a <see cref="BedFurniture"/>.
/// Shows a slider 1–168 with a default of "until 06:00 next day" and routes the
/// confirmation through <see cref="MWI.Time.TimeSkipController"/> — same entry point
/// as the chat command and dev panel.
/// Hidden by default. Shown by external triggers (a future BedFurnitureInteractable
/// will call <see cref="Show"/> when the local player uses a bed).
/// </summary>
public class UI_BedSleepPrompt : MonoBehaviour
{
    [SerializeField] private Slider _hoursSlider;
    [SerializeField] private TMP_Text _hoursLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    private void Awake()
    {
        gameObject.SetActive(false);
        if (_hoursSlider != null) _hoursSlider.onValueChanged.AddListener(OnSliderChanged);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnDestroy()
    {
        if (_hoursSlider != null) _hoursSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (_hoursSlider != null)
        {
            _hoursSlider.minValue = 1;
            _hoursSlider.maxValue = MWI.Time.TimeSkipController.MaxHours;
            _hoursSlider.wholeNumbers = true;
            _hoursSlider.value = ComputeDefaultUntilSixAm();
            OnSliderChanged(_hoursSlider.value);
        }
    }

    public void Hide() => gameObject.SetActive(false);

    private float ComputeDefaultUntilSixAm()
    {
        if (MWI.Time.TimeManager.Instance == null) return 8f;
        int currentHour = MWI.Time.TimeManager.Instance.CurrentHour;
        int target = 6;  // 06:00 next morning
        int delta = (target - currentHour + 24) % 24;
        if (delta == 0) delta = 24;
        return delta;
    }

    private void OnSliderChanged(float value)
    {
        if (_hoursLabel != null) _hoursLabel.text = $"Skip {(int)value} h";
    }

    private void OnConfirmClicked()
    {
        int hours = _hoursSlider != null ? (int)_hoursSlider.value : 0;
        if (MWI.Time.TimeSkipController.Instance == null) { Hide(); return; }
        bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours);
        if (ok) Hide();
        else Debug.LogWarning($"<color=orange>[UI_BedSleepPrompt]</color> RequestSkip({hours}) rejected — see Console.");
    }

    private void OnCancelClicked() => Hide();
}
