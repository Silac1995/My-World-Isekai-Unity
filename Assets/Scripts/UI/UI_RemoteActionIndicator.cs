using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Per-remote-character action indicator — a compact circular badge with a
/// radial progress arc that floats above an NPC / remote player's head.
/// Local-player parity for the labeled action bar; this one is intentionally
/// minimal (no text) for ambient awareness without competing with the
/// player's own bar.
///
/// Lifecycle: instantiated by <see cref="RemoteActionIndicatorLayer"/> for each
/// non-local Character. <see cref="Bind"/> wires the target Character; the
/// indicator subscribes to that Character's <c>CharacterActions</c>
/// OnActionStarted / OnActionFinished, drives the radial fill from
/// GetActionProgress, follows the head world-position via
/// Camera.WorldToScreenPoint each frame, and fades opacity with distance to
/// the local player (speech-bubble convention).
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class UI_RemoteActionIndicator : MonoBehaviour
{
    [Header("UI references")]
    [Tooltip("Radial-fill Image (Image.Type=Filled, FillMethod=Radial360, Origin=Top) that visualises the action progress around the badge edge.")]
    [SerializeField] private Image _progressArc;
    [Tooltip("Optional inner icon Image — left null for the V1 generic-glyph treatment.")]
    [SerializeField] private Image _iconImage;

    [Header("World anchor")]
    [Tooltip("World-space Y offset added to the target Character's transform.position. 12 ≈ slightly above an 11-unit-tall character's head per rule #32.")]
    [SerializeField, Range(0f, 30f)] private float _worldHeadOffset = 12f;
    [Tooltip("Canvas-local pixel offset for fine-tuning vertical placement.")]
    [SerializeField] private Vector2 _screenOffsetPx = Vector2.zero;
    [Tooltip("Position lerp speed — uses unscaledDeltaTime so the indicator stays smooth at any GameSpeedController scale.")]
    [SerializeField] private float _positionLerpSpeed = 14f;

    [Header("Distance fade")]
    [Tooltip("World units between the local player and the target. ≤ FadeStart → full opacity; ≥ FadeEnd → invisible.")]
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

    public Character Target => _target;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// Wire the indicator to a target Character + parent canvas. Resolves the
    /// local-player anchor lazily because the local player can spawn after
    /// this indicator does on a freshly-joined client.
    /// </summary>
    public void Bind(Character target, RectTransform canvasRect, Transform localPlayerAnchor)
    {
        Unbind();

        _target = target;
        _canvasRect = canvasRect;
        _localPlayerAnchor = localPlayerAnchor;
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

    private void OnDestroy()
    {
        Unbind();
    }
}
