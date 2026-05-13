using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.Time;

/// <summary>
/// Game-speed tab of the dev-mode panel. Mirrors the preset surface of
/// <see cref="MWI.UI.UI_GameSpeedController"/> (0× / 1× / 2× / 4× / 8×) and adds
/// an arbitrary-value field for dev stress-testing (slow-mo, 16× fast-forward,
/// etc.). All routed through <see cref="GameSpeedController.RequestSpeedChange"/>
/// — server-authoritative, fully replicated. Falls back to writing
/// <c>Time.timeScale</c> directly when no networked controller exists
/// (solo scene playback or before the network spawns it).
///
/// Self-registering — no edits to <c>DevModeManager</c> or <c>DevModePanel</c>
/// required. Lives as a child GameObject under the panel's content root and is
/// activated by <c>DevModePanel.SwitchTab</c> like every other module.
/// </summary>
public class DevGameSpeedModule : MonoBehaviour
{
    [Header("Preset buttons")]
    [SerializeField, Tooltip("Sets speed to 0× (pause).")] private Button _pauseButton;
    [SerializeField, Tooltip("Sets speed to 1× (normal).")] private Button _normalButton;
    [SerializeField, Tooltip("Sets speed to 2× (fast).")] private Button _fastButton;
    [SerializeField, Tooltip("Sets speed to 4× (super fast).")] private Button _superFastButton;
    [SerializeField, Tooltip("Sets speed to 8× (giga).")] private Button _gigaButton;

    [Header("Custom value")]
    [SerializeField, Tooltip("Optional free-form speed input. Parses as float (InvariantCulture).")] private TMP_InputField _customField;
    [SerializeField] private Button _applyCustomButton;

    [Header("Status")]
    [SerializeField, Tooltip("Read-only label showing the current Time.timeScale.")] private TMP_Text _currentSpeedLabel;

    [Header("Visuals")]
    [SerializeField] private Color _activeColor = new Color(0.4f, 1f, 0.4f, 1f);
    [SerializeField] private Color _inactiveColor = Color.white;

    private void Start()
    {
        WireButton(_pauseButton,     0f);
        WireButton(_normalButton,    1f);
        WireButton(_fastButton,      2f);
        WireButton(_superFastButton, 4f);
        WireButton(_gigaButton,      8f);

        if (_applyCustomButton != null)
        {
            _applyCustomButton.onClick.AddListener(OnApplyCustom);
        }

        UpdateVisuals(UnityEngine.Time.timeScale);
    }

    private void OnEnable()
    {
        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.OnSpeedChanged += UpdateVisuals;
        }
        UpdateVisuals(UnityEngine.Time.timeScale);
    }

    private void OnDisable()
    {
        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.OnSpeedChanged -= UpdateVisuals;
        }
    }

    private void OnDestroy()
    {
        UnwireButton(_pauseButton);
        UnwireButton(_normalButton);
        UnwireButton(_fastButton);
        UnwireButton(_superFastButton);
        UnwireButton(_gigaButton);
        if (_applyCustomButton != null)
        {
            _applyCustomButton.onClick.RemoveAllListeners();
        }
    }

    private void WireButton(Button btn, float speed)
    {
        if (btn == null) return;
        btn.onClick.AddListener(() => RequestSpeed(speed));
    }

    private void UnwireButton(Button btn)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
    }

    private void OnApplyCustom()
    {
        if (_customField == null) return;
        string raw = _customField.text;
        if (float.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float value))
        {
            RequestSpeed(value);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[DevGameSpeed]</color> Could not parse '{raw}' as a float — speed unchanged.");
        }
    }

    private void RequestSpeed(float target)
    {
        target = Mathf.Max(0f, target);

        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.RequestSpeedChange(target);
        }
        else
        {
            // Fallback for offline play / pre-spawn windows. RequestSpeedChange already
            // handles the !IsSpawned case the same way; this branch covers the case where
            // the controller hasn't been instantiated at all (e.g. running a scene without
            // the networked DayNightCycle prefab in it).
            UnityEngine.Time.timeScale = target;
            UpdateVisuals(target);
            Debug.Log($"<color=magenta>[DevGameSpeed]</color> GameSpeedController missing — applied {target:F2}× to Time.timeScale directly.");
        }
    }

    private void UpdateVisuals(float speed)
    {
        UpdateButton(_pauseButton,     Mathf.Approximately(speed, 0f));
        UpdateButton(_normalButton,    Mathf.Approximately(speed, 1f));
        UpdateButton(_fastButton,      Mathf.Approximately(speed, 2f));
        UpdateButton(_superFastButton, Mathf.Approximately(speed, 4f));
        UpdateButton(_gigaButton,      Mathf.Approximately(speed, 8f));

        if (_currentSpeedLabel != null)
        {
            _currentSpeedLabel.text = $"Current: {speed:F2}×";
        }
    }

    private void UpdateButton(Button btn, bool isActive)
    {
        if (btn == null || btn.targetGraphic == null) return;
        btn.targetGraphic.color = isActive ? _activeColor : _inactiveColor;
    }
}
