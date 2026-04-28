using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-world modal that appears when the local player uses a <see cref="BedFurniture"/>.
/// Shows a slider 1–168 with a default of 7 hours (Minecraft-style "until morning")
/// and invokes a caller-provided callback with the chosen hour count on confirm.
///
/// The actual sleep enqueue + EnterSleep + PendingSkipHours wiring is the caller's
/// responsibility (see <see cref="BedFurnitureInteractable"/>). This prompt is
/// pure UI — it does NOT call <c>TimeSkipController.RequestSkip</c> directly any
/// more (that path bypassed EnterSleep, which the auto-trigger watcher needs).
/// Hidden by default.
/// </summary>
public class UI_BedSleepPrompt : MonoBehaviour
{
    [SerializeField] private Slider _hoursSlider;
    [SerializeField] private TMP_Text _hoursLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    [Header("Defaults")]
    [Tooltip("Default value of the hours slider when Show() is called. Minecraft default is 7h.")]
    [SerializeField] private int _defaultHours = 7;

    private Action<int> _onConfirm;

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

    /// <summary>
    /// Open the prompt. <paramref name="onConfirm"/> is invoked with the chosen
    /// hour count if the user clicks Confirm; not invoked on Cancel.
    /// </summary>
    public void Show(Action<int> onConfirm)
    {
        _onConfirm = onConfirm;
        gameObject.SetActive(true);
        if (_hoursSlider != null)
        {
            _hoursSlider.minValue = 1;
            _hoursSlider.maxValue = MWI.Time.TimeSkipController.MaxHours;
            _hoursSlider.wholeNumbers = true;
            _hoursSlider.value = Mathf.Clamp(_defaultHours, 1, MWI.Time.TimeSkipController.MaxHours);
            OnSliderChanged(_hoursSlider.value);
        }
    }

    public void Hide()
    {
        _onConfirm = null;
        gameObject.SetActive(false);
    }

    private void OnSliderChanged(float value)
    {
        if (_hoursLabel != null) _hoursLabel.text = $"Skip {(int)value} h";
    }

    private void OnConfirmClicked()
    {
        int hours = _hoursSlider != null ? (int)_hoursSlider.value : _defaultHours;
        var cb = _onConfirm;
        Hide();
        try { cb?.Invoke(hours); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void OnCancelClicked() => Hide();
}
