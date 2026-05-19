using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The local player's action progress bar — a world-anchored HUD element that
/// follows the player character on-screen, mirroring the SpeechBubbleInstance /
/// QuestWorldMarkerRenderer pattern (world position → Camera.WorldToScreenPoint
/// → RectTransformUtility.ScreenPointToLocalPointInRectangle → anchoredPosition).
///
/// Lifecycle: PlayerUI.Initialize passes the local Character.transform as the
/// anchor; the bar subscribes to CharacterActions.OnActionStarted / OnActionFinished.
/// While an action is active the gameObject is enabled, Update polls
/// CharacterActions.GetActionProgress() for the fill, and the bar/text
/// RectTransforms lerp toward the projected screen position. When the action
/// ends the gameObject is disabled and the next action snaps cleanly.
/// </summary>
public class UI_Action_ProgressBar : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image _fillImage;
    [SerializeField] private TextMeshProUGUI _actionNameText;
    [Tooltip("Bar RectTransform repositioned each frame to follow the anchor on-screen. Auto-discovered as Canvas/ProgressBar if left empty.")]
    [SerializeField] private RectTransform _progressBarRect;
    [Tooltip("Action-name text RectTransform. Tracks the same anchor with its own offset. Auto-discovered as Canvas/Text_CurrentAction if left empty.")]
    [SerializeField] private RectTransform _textRect;
    [Tooltip("RectTransform of the Canvas this bar lives under — used as the parent rect for the screen→canvas conversion. Auto-discovered as the child Canvas if left empty.")]
    [SerializeField] private RectTransform _canvasRect;

    [Header("World Anchor")]
    [Tooltip("World-space Y offset added to the anchor position, in Unity units. Default 12 sits the bar a small margin above the head of an 11-unit-tall character (rule #32). Tweak live in the Inspector with the slider.")]
    [SerializeField, Range(0f, 30f)] private float _worldHeadOffset = 12f;
    [Tooltip("Canvas-local pixel offset applied on top of the projected screen position, for the bar.")]
    [SerializeField] private Vector2 _barScreenOffsetPx = Vector2.zero;
    [Tooltip("Canvas-local pixel offset for the action-name text, relative to the same projected screen position as the bar.")]
    [SerializeField] private Vector2 _textScreenOffsetPx = new Vector2(0f, 40f);
    [Tooltip("Position lerp speed (uses unscaledDeltaTime so the bar stays smooth even at high GameSpeedController scales / pause). 0 = snap instantly.")]
    [SerializeField] private float _positionLerpSpeed = 14f;

    [Header("UI Scale")]
    [Tooltip("Uniform scale applied to the bar + text rects and to the screen-space offsets. 1 = native (the values shipped in the prefab); 0.5 = half. Use this to shrink/grow the whole HUD element without touching individual fields.")]
    [SerializeField, Range(0.1f, 2f)] private float _uiScale = 1f;

    private CharacterActions _characterActions;
    private Transform _anchor;
    private Camera _camera;
    private Canvas _canvas;

    private void Awake()
    {
        // Defensive auto-wiring: if the scene's prefab instance was authored before
        // these SerializeFields existed, they may serialize as null even though the
        // prefab asset wires them. Discover by canonical hierarchy as a fallback so
        // the bar still works without manual scene re-wiring.
        if (_progressBarRect == null)
        {
            var t = transform.Find("Canvas/ProgressBar");
            if (t != null) _progressBarRect = t as RectTransform;
        }
        if (_textRect == null)
        {
            var t = transform.Find("Canvas/Text_CurrentAction");
            if (t != null) _textRect = t as RectTransform;
        }
        if (_canvasRect == null)
        {
            var t = transform.Find("Canvas");
            if (t != null) _canvasRect = t as RectTransform;
        }
        if (_canvasRect == null && _progressBarRect != null)
            _canvasRect = _progressBarRect.parent as RectTransform;

        if (_canvasRect != null)
            _canvas = _canvasRect.GetComponentInParent<Canvas>();

        if (_progressBarRect == null || _canvasRect == null)
        {
            Debug.LogWarning("<color=orange>[UI_Action_ProgressBar]</color> Could not auto-discover required RectTransforms — bar will not follow the player. Expected prefab hierarchy: Canvas/ProgressBar + Canvas/Text_CurrentAction. Re-import Assets/UI/Player HUD/UI_Action_ProgressBar.prefab and reopen the scene.");
        }
    }

    private bool _anchorsCalibrated;

    /// <summary>
    /// One-shot runtime alignment: the bar/text rects' point-anchors must match the
    /// parent canvas's pivot. ScreenPointToLocalPointInRectangle returns coords in
    /// the canvas's local space (origin at canvas pivot). When the child's anchor
    /// also lives at the canvas pivot, `child.anchoredPosition = lp` puts the child
    /// at the exact screen point — no offset math.
    ///
    /// Deferred to runtime because Unity normalises a ScreenSpaceOverlay canvas's
    /// pivot to (0.5, 0.5) at startup regardless of the authored value, so anchors
    /// pinned in YAML would mismatch. Runs once after first FollowAnchor where
    /// the canvas pivot is stable.
    /// </summary>
    private void CalibrateAnchorsIfNeeded()
    {
        if (_anchorsCalibrated || _canvasRect == null) return;
        Vector2 p = _canvasRect.pivot;
        Vector3 s = Vector3.one * _uiScale;
        if (_progressBarRect != null) { _progressBarRect.anchorMin = p; _progressBarRect.anchorMax = p; _progressBarRect.localScale = s; }
        if (_textRect != null)        { _textRect.anchorMin = p;        _textRect.anchorMax = p;        _textRect.localScale = s;        }
        _anchorsCalibrated = true;
    }

    /// <summary>
    /// Wire the bar to the local player's actions and the world-space transform
    /// it should follow on-screen. Called once by PlayerUI.Initialize per
    /// active character (re-binds cleanly on character swap).
    /// </summary>
    public void InitializeCharacterActions(CharacterActions actions, Transform anchor)
    {
        Unsubscribe();

        _characterActions = actions;
        _anchor = anchor;

        if (_characterActions != null)
        {
            _characterActions.OnActionStarted += HandleActionStarted;
            _characterActions.OnActionFinished += HandleActionEnded;
        }

        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_characterActions == null) return;

        if (_fillImage != null)
            _fillImage.fillAmount = _characterActions.GetActionProgress();

        FollowAnchor(snap: false);
    }

    private void FollowAnchor(bool snap)
    {
        if (_anchor == null || _progressBarRect == null || _canvasRect == null) return;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        CalibrateAnchorsIfNeeded();

        Vector3 worldPos = _anchor.position + Vector3.up * _worldHeadOffset;
        Vector3 sp = _camera.WorldToScreenPoint(worldPos);

        // Anchor is behind the camera — skip the update; the bar stays at its
        // last on-screen position until the camera looks back at the player.
        if (sp.z < 0f) return;

        Camera uiCam = ResolveCanvasCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, sp, uiCam, out Vector2 lp))
            return;

        Vector2 barTarget = lp + _barScreenOffsetPx * _uiScale;
        _progressBarRect.anchoredPosition = (snap || _positionLerpSpeed <= 0f)
            ? barTarget
            : Vector2.Lerp(_progressBarRect.anchoredPosition, barTarget,
                           _positionLerpSpeed * Time.unscaledDeltaTime);

        if (_textRect != null)
        {
            Vector2 textTarget = lp + _textScreenOffsetPx * _uiScale;
            _textRect.anchoredPosition = (snap || _positionLerpSpeed <= 0f)
                ? textTarget
                : Vector2.Lerp(_textRect.anchoredPosition, textTarget,
                               _positionLerpSpeed * Time.unscaledDeltaTime);
        }
    }

    private Camera ResolveCanvasCamera()
    {
        if (_canvas == null) return null;
        return _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
    }

    private void HandleActionStarted(CharacterAction action)
    {
        gameObject.SetActive(true);

        if (_actionNameText != null)
            _actionNameText.text = PrettifyActionName(action.ActionName);

        FollowAnchor(snap: true);
    }

    /// <summary>
    /// Convert raw action class names (e.g. "CharacterMeleeAttackAction") into the
    /// short, human-readable form for the HUD ("Melee Attack"): drops the
    /// "Character" prefix + "Action" suffix and inserts spaces before interior
    /// uppercase letters. Runs once per action start — not in a hot path.
    /// </summary>
    private static string PrettifyActionName(string raw)
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

    private void HandleActionEnded()
    {
        gameObject.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (_characterActions != null)
        {
            _characterActions.OnActionStarted -= HandleActionStarted;
            _characterActions.OnActionFinished -= HandleActionEnded;
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
