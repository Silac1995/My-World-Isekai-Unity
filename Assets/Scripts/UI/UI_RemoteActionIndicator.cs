using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-character action indicator — a compact circular badge with a Radial360
/// progress arc + an action-name label above it. Used for BOTH the local
/// player and remote characters (NPC + remote players), instantiated by
/// <see cref="RemoteActionIndicatorLayer"/>. The local player's instance
/// receives <see cref="Bind"/> with <c>isLocalPlayer = true</c>, which
/// disables the distance-fade pass and keeps the indicator at full opacity
/// regardless of the global remote-bars toggle.
///
/// Lifecycle: <see cref="Bind"/> wires a target Character; the indicator
/// subscribes to that Character's CharacterActions OnActionStarted /
/// OnActionFinished, drives the radial fill from GetActionProgress, follows
/// the head world-position via Camera.WorldToScreenPoint each frame, and
/// fades opacity with distance to the local player (speech-bubble pattern,
/// skipped for the local player itself).
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class UI_RemoteActionIndicator : MonoBehaviour
{
    [Header("UI references")]
    [Tooltip("Radial-fill Image (Image.Type=Filled, FillMethod=Radial360, Origin=Top) that visualises the action progress around the badge edge.")]
    [SerializeField] private Image _progressArc;
    [Tooltip("Action-name label rendered above the badge. Leave null to hide the label.")]
    [SerializeField] private TextMeshProUGUI _actionNameText;

    [Header("World anchor")]
    [Tooltip("World-space Y offset added to the target Character's transform.position. 12 ≈ slightly above an 11-unit-tall character's head per rule #32.")]
    [SerializeField, Range(0f, 30f)] private float _worldHeadOffset = 12f;
    [Tooltip("Canvas-local pixel offset for fine-tuning vertical placement.")]
    [SerializeField] private Vector2 _screenOffsetPx = Vector2.zero;
    [Tooltip("Position lerp speed — uses unscaledDeltaTime so the indicator stays smooth at any GameSpeedController scale.")]
    [SerializeField] private float _positionLerpSpeed = 14f;

    [Header("Distance fade (remote only)")]
    [Tooltip("World units between the local player and the target. ≤ FadeStart → full opacity; ≥ FadeEnd → invisible. Skipped when _isLocalPlayer is true.")]
    [SerializeField, Range(0f, 50f)] private float _fadeStartDistance = 12f;
    [SerializeField, Range(1f, 200f)] private float _fadeEndDistance = 30f;

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;
    private Character _target;
    private CharacterActions _targetActions;
    private RectTransform _canvasRect;
    private Camera _camera;
    private Canvas _canvas;
    private Transform _localPlayerAnchor;
    private bool _anchorCalibrated;
    private bool _hasActiveAction;
    private bool _isLocalPlayer;

    public Character Target => _target;
    public bool IsLocalPlayer => _isLocalPlayer;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Wire the indicator to a target Character + parent canvas.
    /// <paramref name="isLocalPlayer"/>=true skips the distance-fade pass —
    /// the local player's own indicator always reads at full opacity.
    /// </summary>
    public void Bind(Character target, RectTransform canvasRect, Transform localPlayerAnchor, bool isLocalPlayer)
    {
        Unbind();

        _target = target;
        _canvasRect = canvasRect;
        _localPlayerAnchor = localPlayerAnchor;
        _isLocalPlayer = isLocalPlayer;
        if (_canvasRect != null) _canvas = _canvasRect.GetComponentInParent<Canvas>();

        if (_target != null)
        {
            _targetActions = _target.CharacterActions;
            if (_targetActions != null)
            {
                _targetActions.OnActionStarted += HandleActionStarted;
                _targetActions.OnActionFinished += HandleActionEnded;
            }

            // If the target is already mid-action when we bind (late-joiner / late-spawn),
            // surface the badge immediately rather than waiting for the next OnActionStarted.
            if (_targetActions != null && _targetActions.CurrentAction != null)
                ActivateFor(_targetActions.CurrentAction);
            else
                Deactivate();
        }
        else
        {
            Deactivate();
        }
    }

    /// <summary>
    /// Re-target the local-player anchor — called by the layer manager when the
    /// local Character changes (portal-gate return, character swap).
    /// </summary>
    public void SetLocalPlayerAnchor(Transform anchor)
    {
        _localPlayerAnchor = anchor;
    }

    public void Unbind()
    {
        if (_targetActions != null)
        {
            _targetActions.OnActionStarted -= HandleActionStarted;
            _targetActions.OnActionFinished -= HandleActionEnded;
        }
        _targetActions = null;
        _target = null;
        Deactivate();
    }

    private void HandleActionStarted(CharacterAction action) => ActivateFor(action);

    private void HandleActionEnded() => Deactivate();

    private void ActivateFor(CharacterAction action)
    {
        if (action == null) { Deactivate(); return; }
        _hasActiveAction = true;
        if (_actionNameText != null) _actionNameText.text = PrettifyActionName(action.ActionName);
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        gameObject.SetActive(true);
        FollowAnchor(snap: true);
    }

    private void Deactivate()
    {
        _hasActiveAction = false;
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        // Don't SetActive(false) — keep Update running so we can re-activate cleanly
        // when the next action starts. Visibility is driven by CanvasGroup.alpha.
    }

    private void Update()
    {
        if (!_hasActiveAction || _target == null || _targetActions == null) return;

        if (_progressArc != null)
            _progressArc.fillAmount = Mathf.Clamp01(_targetActions.GetActionProgress());

        FollowAnchor(snap: false);
        ApplyDistanceFade();
    }

    private void FollowAnchor(bool snap)
    {
        if (_canvasRect == null || _target == null) return;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        CalibrateAnchorIfNeeded();

        Vector3 worldPos = _target.transform.position + Vector3.up * _worldHeadOffset;
        Vector3 sp = _camera.WorldToScreenPoint(worldPos);
        if (sp.z < 0f) return;

        Camera uiCam = (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : _canvas?.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, uiCam, out Vector2 lp))
            return;

        Vector2 target = lp + _screenOffsetPx;
        _rect.anchoredPosition = (snap || _positionLerpSpeed <= 0f)
            ? target
            : Vector2.Lerp(_rect.anchoredPosition, target, _positionLerpSpeed * Time.unscaledDeltaTime);
    }

    private void CalibrateAnchorIfNeeded()
    {
        if (_anchorCalibrated || _canvasRect == null) return;
        Vector2 p = _canvasRect.pivot;
        _rect.anchorMin = p;
        _rect.anchorMax = p;
        _anchorCalibrated = true;
    }

    private void ApplyDistanceFade()
    {
        if (_canvasGroup == null) return;
        if (_isLocalPlayer) { _canvasGroup.alpha = 1f; return; }
        if (_localPlayerAnchor == null || _target == null) { _canvasGroup.alpha = 1f; return; }

        // 2D world distance (X/Z plane) — Y doesn't matter for "is the other character close to me".
        Vector3 a = _localPlayerAnchor.position; a.y = 0f;
        Vector3 b = _target.transform.position; b.y = 0f;
        float d = Vector3.Distance(a, b);

        float alpha;
        if (d <= _fadeStartDistance)      alpha = 1f;
        else if (d >= _fadeEndDistance)   alpha = 0f;
        else                              alpha = 1f - (d - _fadeStartDistance) / Mathf.Max(0.001f, _fadeEndDistance - _fadeStartDistance);

        _canvasGroup.alpha = alpha;
    }

    /// <summary>
    /// Convert raw action class names (e.g. "CharacterMeleeAttackAction") into the
    /// short, human-readable form for the HUD ("Melee Attack"): drops the
    /// "Character" prefix + "Action" suffix and inserts spaces before interior
    /// uppercase letters. Runs once per action start — not in a hot path.
    /// </summary>
    public static string PrettifyActionName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        string s = raw;
        const string prefix = "Character";
        const string suffix = "Action";
        if (s.StartsWith(prefix)) s = s.Substring(prefix.Length);
        if (s.EndsWith(suffix) && s.Length > suffix.Length) s = s.Substring(0, s.Length - suffix.Length);

        if (s.Length <= 1) return s;

        var sb = new StringBuilder(s.Length + 4);
        sb.Append(s[0]);
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void OnDestroy()
    {
        Unbind();
    }
}
