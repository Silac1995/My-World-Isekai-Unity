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
    [Tooltip("Bar RectTransform repositioned each frame to follow the anchor on-screen.")]
    [SerializeField] private RectTransform _progressBarRect;
    [Tooltip("Optional action-name text RectTransform. Tracks the same anchor with its own offset. Leave null to keep the text static.")]
    [SerializeField] private RectTransform _textRect;
    [Tooltip("RectTransform of the Canvas this bar lives under — used as the parent rect for the screen→canvas conversion. Auto-resolved from _progressBarRect.parent if left null.")]
    [SerializeField] private RectTransform _canvasRect;

    [Header("World Anchor")]
    [Tooltip("World-space Y offset added to the anchor position, in Unity units. Defaults to 12 (~1.82m — slightly above an 11-unit-tall character's head per rule #32).")]
    [SerializeField] private float _worldHeadOffset = 12f;
    [Tooltip("Canvas-local pixel offset applied on top of the projected screen position, for the bar.")]
    [SerializeField] private Vector2 _barScreenOffsetPx = Vector2.zero;
    [Tooltip("Canvas-local pixel offset for the action-name text, relative to the same projected screen position as the bar.")]
    [SerializeField] private Vector2 _textScreenOffsetPx = new Vector2(0f, 40f);
    [Tooltip("Position lerp speed (uses unscaledDeltaTime so the bar stays smooth even at high GameSpeedController scales / pause). 0 = snap instantly.")]
    [SerializeField] private float _positionLerpSpeed = 14f;

    private CharacterActions _characterActions;
    private Transform _anchor;
    private Camera _camera;
    private Canvas _canvas;

    private void Awake()
    {
        // Fallback wiring so the script still works if _canvasRect is left empty.
        if (_canvasRect == null && _progressBarRect != null)
            _canvasRect = _progressBarRect.parent as RectTransform;

        if (_canvasRect != null)
            _canvas = _canvasRect.GetComponentInParent<Canvas>();
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

        // Hidden by default at startup; HandleActionStarted re-enables.
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

        Vector3 worldPos = _anchor.position + Vector3.up * _worldHeadOffset;
        Vector3 sp = _camera.WorldToScreenPoint(worldPos);

        // Anchor is behind the camera — skip the update; the bar stays at its
        // last on-screen position until the camera looks back at the player.
        if (sp.z < 0f) return;

        Camera uiCam = ResolveCanvasCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, sp, uiCam, out Vector2 lp))
            return;

        Vector2 barTarget = lp + _barScreenOffsetPx;
        _progressBarRect.anchoredPosition = (snap || _positionLerpSpeed <= 0f)
            ? barTarget
            : Vector2.Lerp(_progressBarRect.anchoredPosition, barTarget,
                           _positionLerpSpeed * Time.unscaledDeltaTime);

        if (_textRect != null)
        {
            Vector2 textTarget = lp + _textScreenOffsetPx;
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
            _actionNameText.text = action.ActionName.Replace("Character", "");

        // Snap to the player's current screen position so the bar doesn't lerp
        // in from its last-known location after being disabled.
        FollowAnchor(snap: true);
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
