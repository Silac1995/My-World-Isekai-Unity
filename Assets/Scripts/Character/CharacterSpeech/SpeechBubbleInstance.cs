using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using Random = UnityEngine.Random;

/// <summary>
/// A single speech bubble instance — typing, voice, entrance/exit animation,
/// expiration timer, height tracking. Spawned and owned by SpeechBubbleStack.
///
/// HUD-space rewrite: bubbles now live as RectTransform children of
/// HUDSpeechBubbleLayer.Local.ContentRoot (inside a per-stack CanvasGroup
/// wrapper). Each frame the bubble projects its speaker anchor's world
/// position to screen coordinates and lerps anchoredPosition.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public class SpeechBubbleInstance : MonoBehaviour
{
    // ── Serialized Fields ──────────────────────────────────────────────
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private Image _nameStripBackground;
    [SerializeField] private GameObject _tailRoot;

    [Header("Animation (reference-resolution pixels)")]
    [SerializeField] private float _entranceDuration = 0.3f;
    [SerializeField] private float _entranceSlideDistance = 40f;
    [SerializeField] private float _exitDuration = 0.3f;
    [SerializeField] private float _exitSlideDistance = 25f;
    [SerializeField] private float _positionLerpSpeed = 8f;

    [Header("Adaptive size (HUD pixels — bubble grows with text, wraps at max)")]
    [Tooltip("Minimum bubble width. Short messages stretch to at least this width so the name strip remains readable.")]
    [SerializeField] private float _minBubbleWidth = 180f;
    [Tooltip("Maximum bubble width before the body text wraps to a new line.")]
    [SerializeField] private float _maxBubbleWidth = 320f;
    [Tooltip("Total horizontal padding inside the BodyPanel (sum of left + right inner margins). Subtracted from bubble width to get available text width.")]
    [SerializeField] private float _bodyHorizontalPadding = 16f;
    [Tooltip("Height of the NameStrip at the top of the bubble. Used to inset BodyPanel and to add to the final bubble height.")]
    [SerializeField] private float _nameStripHeight = 22f;
    [Tooltip("Total vertical padding around the body text (sum of top + bottom inner margins).")]
    [SerializeField] private float _bodyVerticalPadding = 12f;

    // ── Events ─────────────────────────────────────────────────────────
    public Action OnExpired;
    public Action OnHeightChanged;
    public Action<bool> OnTypingStateChanged;

    // ── Public Properties ──────────────────────────────────────────────
    public bool IsTyping => _typeRoutine != null;
    public bool IsScripted => _isScripted;
    public bool IsOffScreen => _isOffScreen;

    // ── Private Fields ─────────────────────────────────────────────────
    private CanvasGroup _canvasGroup;
    private RectTransform _rect;
    private Coroutine _typeRoutine;
    private Coroutine _animRoutine;
    private Coroutine _expirationRoutine;

    private Transform _speakerAnchor;
    private Camera _camera;
    private Vector2 _stackOffsetPx;
    private Vector2 _animationBiasPx;
    private bool _isOffScreen;
    private float _cachedHeight;
    private bool _isScripted;

    // Stored params for typing
    private string _fullMessage;
    private AudioSource _audioSource;
    private VoiceSO _voiceSO;
    private float _pitch;
    private float _typingSpeed;
    private float _duration;
    private Action _onExpiredCallback;
    private Action _onTypingFinishedCallback;

    // ── Unity Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rect = GetComponent<RectTransform>();
        _cachedHeight = _rect.rect.height;
    }

    private void Update()
    {
        if (_speakerAnchor == null) return;

        // Lazy camera resolution: NPC bubbles can be created before the local player HUD
        // is ready on a freshly-joined client. Re-resolve on every frame until we have one.
        if (_camera == null)
        {
            _camera = HUDSpeechBubbleLayer.Local?.Camera;
            if (_camera == null) return;
        }

        Vector3 sp = _camera.WorldToScreenPoint(_speakerAnchor.position);
        _isOffScreen = sp.z < 0f
                    || sp.x < 0f || sp.x > Screen.width
                    || sp.y < 0f || sp.y > Screen.height;

        Vector2 target = (Vector2)sp + _stackOffsetPx + _animationBiasPx;
        _rect.anchoredPosition = Vector2.Lerp(
            _rect.anchoredPosition,
            target,
            _positionLerpSpeed * Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = null;

        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = null;

        if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
        _expirationRoutine = null;
    }

    private void OnDestroy()
    {
        OnExpired = null;
        OnHeightChanged = null;
        OnTypingStateChanged = null;
        _onExpiredCallback = null;
        _onTypingFinishedCallback = null;
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void SetSpeakerAnchor(Transform anchor) => _speakerAnchor = anchor;
    public void SetCamera(Camera camera) => _camera = camera;
    public void SetStackOffsetPx(Vector2 offsetPx) => _stackOffsetPx = offsetPx;
    public Vector2 GetStackOffsetPx() => _stackOffsetPx;

    public void Setup(string message, AudioSource audioSource, VoiceSO voiceSO,
        float pitch, float typingSpeed, float duration, Action onExpired)
    {
        try
        {
            _fullMessage = message;
            _audioSource = audioSource;
            _voiceSO = voiceSO;
            _pitch = pitch;
            _typingSpeed = typingSpeed;
            _duration = duration;
            _onExpiredCallback = onExpired;
            _isScripted = false;

            // Lock the final bubble size BEFORE the entrance animation runs so the
            // bubble fades in at its real size — not the prefab's authored default.
            // Without this the user sees a brief flash of the big default rect during
            // the 0.3s entrance fade-in before TypeMessage resizes it down.
            PrepareTextAndApplySize();
            _cachedHeight = _rect.rect.height;

            if (_animRoutine != null) StopCoroutine(_animRoutine);
            _animRoutine = StartCoroutine(EntranceAnimation(() =>
            {
                if (_typeRoutine != null) StopCoroutine(_typeRoutine);
                _typeRoutine = StartCoroutine(TypeMessage(() =>
                {
                    if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
                    _expirationRoutine = StartCoroutine(ExpirationTimer());
                }));
            }));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleInstance] Exception in Setup: {e.Message}\n{e.StackTrace}");
        }
    }

    public void SetupScripted(string message, AudioSource audioSource, VoiceSO voiceSO,
        float pitch, float typingSpeed, Action onTypingFinished)
    {
        try
        {
            _fullMessage = message;
            _audioSource = audioSource;
            _voiceSO = voiceSO;
            _pitch = pitch;
            _typingSpeed = typingSpeed;
            _onTypingFinishedCallback = onTypingFinished;
            _isScripted = true;

            PrepareTextAndApplySize();
            _cachedHeight = _rect.rect.height;

            if (_animRoutine != null) StopCoroutine(_animRoutine);
            _animRoutine = StartCoroutine(EntranceAnimation(() =>
            {
                if (_typeRoutine != null) StopCoroutine(_typeRoutine);
                _typeRoutine = StartCoroutine(TypeMessage(() =>
                {
                    _onTypingFinishedCallback?.Invoke();
                }));
            }));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleInstance] Exception in SetupScripted: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Assigns the full message to the TMP, hides it via maxVisibleCharacters = 0,
    /// forces a mesh update so GetPreferredValues has a current layout to measure,
    /// then resizes the bubble. Runs synchronously inside Setup / SetupScripted so
    /// the Habbo push height (via GetHeightPx) is accurate from frame 0 and the
    /// entrance animation fades in at the final size.
    /// </summary>
    private void PrepareTextAndApplySize()
    {
        if (_textElement == null) return;
        _textElement.text = _fullMessage ?? string.Empty;
        _textElement.maxVisibleCharacters = 0;
        _textElement.ForceMeshUpdate();
        ApplyAdaptiveSize();
    }

    public void CompleteTypingImmediately()
    {
        if (_typeRoutine == null) return;

        StopCoroutine(_typeRoutine);
        _typeRoutine = null;

        if (_textElement != null)
            _textElement.maxVisibleCharacters = _fullMessage.Length;

        OnTypingStateChanged?.Invoke(false);
        CheckHeightChanged();

        if (_isScripted)
        {
            _onTypingFinishedCallback?.Invoke();
            _onTypingFinishedCallback = null;
        }
        else
        {
            if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
            _expirationRoutine = StartCoroutine(ExpirationTimer());
        }
    }

    public void ResetExpirationTimer()
    {
        if (_isScripted || _duration <= 0f) return;
        if (_expirationRoutine == null) return;

        StopCoroutine(_expirationRoutine);
        _expirationRoutine = StartCoroutine(ExpirationTimer());
    }

    public void Dismiss(Action onComplete = null)
    {
        if (_expirationRoutine != null)
        {
            StopCoroutine(_expirationRoutine);
            _expirationRoutine = null;
        }

        if (_typeRoutine != null)
        {
            StopCoroutine(_typeRoutine);
            _typeRoutine = null;
        }

        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(ExitAnimation(() =>
        {
            onComplete?.Invoke();
            Destroy(gameObject);
        }));
    }

    /// <summary>
    /// Returns the bubble's current rendered height in reference-resolution HUD pixels.
    /// Called right after Setup() to compute the push height for the Habbo stack.
    /// </summary>
    public float GetHeightPx()
    {
        if (_rect == null) return 0f;
        return _rect.rect.height;
    }

    /// <summary>
    /// Sets the speaker-specific visuals: accent colour on the name strip + display name.
    /// Called by SpeechBubbleStack right after Setup/SetupScripted.
    /// </summary>
    public void SetSpeakerDisplay(Color accent, string displayName)
    {
        if (_nameStripBackground != null) _nameStripBackground.color = accent;
        if (_nameText != null) _nameText.text = displayName;
    }

    /// <summary>
    /// Toggles the tail visibility. Only the newest bubble in a stack should have its tail visible.
    /// Called by SpeechBubbleStack on push (new bubble = true, previously-newest = false) and on remove.
    /// </summary>
    public void SetIsNewest(bool isNewest)
    {
        if (_tailRoot != null) _tailRoot.SetActive(isNewest);
    }

    /// <summary>
    /// Resizes the bubble's RectTransform to fit the current text:
    /// - Width = max(_minBubbleWidth, naturalTextWidth + _bodyHorizontalPadding), clamped to _maxBubbleWidth.
    /// - When the text's natural single-line width exceeds the available area, the bubble pegs to
    ///   _maxBubbleWidth and the text wraps; the bubble grows taller to fit the wrapped height.
    /// - Height = _nameStripHeight + wrapped text height + _bodyVerticalPadding.
    /// The full message is laid out via <c>TMP_Text.GetPreferredValues</c> so the size is stable
    /// throughout the typewriter reveal — the bubble doesn't grow while characters appear.
    /// </summary>
    private void ApplyAdaptiveSize()
    {
        if (_textElement == null || _rect == null) return;
        if (string.IsNullOrEmpty(_fullMessage)) return;

        // Measure the full text's natural (unwrapped) preferred size — uses TMP's font / size / settings.
        Vector2 natural = _textElement.GetPreferredValues(_fullMessage);
        float availableWidth = Mathf.Max(1f, _maxBubbleWidth - _bodyHorizontalPadding);

        float bubbleWidth;
        float textHeight;
        if (natural.x <= availableWidth)
        {
            // Fits on one line — bubble width follows the text, clamped to the minimum.
            bubbleWidth = Mathf.Clamp(natural.x + _bodyHorizontalPadding, _minBubbleWidth, _maxBubbleWidth);
            textHeight = natural.y;
        }
        else
        {
            // Text would overflow the max width — peg to max and wrap. Re-measure at the wrapped width.
            bubbleWidth = _maxBubbleWidth;
            Vector2 wrapped = _textElement.GetPreferredValues(_fullMessage, availableWidth, 0f);
            textHeight = wrapped.y;
        }

        float bubbleHeight = _nameStripHeight + textHeight + _bodyVerticalPadding;
        _rect.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);
    }

    // ── Coroutines ─────────────────────────────────────────────────────

    private IEnumerator TypeMessage(Action onComplete)
    {
        OnTypingStateChanged?.Invoke(true);

        _textElement.text = _fullMessage;
        _textElement.maxVisibleCharacters = 0;

        _textElement.ForceMeshUpdate();
        ApplyAdaptiveSize();
        CheckHeightChanged();

        float currentSpeed = _typingSpeed > 0f ? _typingSpeed : 0.04f;

        if (currentSpeed <= 0f)
        {
            _textElement.maxVisibleCharacters = _fullMessage.Length;
            _typeRoutine = null;
            OnTypingStateChanged?.Invoke(false);
            onComplete?.Invoke();
            yield break;
        }

        int charCount = 0;
        float timeAccumulator = 0f;
        char[] characters = _fullMessage.ToCharArray();

        while (charCount < characters.Length)
        {
            // Scaled time: typing is a simulation event (NPC actively speaking),
            // so it speeds up at high GameSpeedController scales and freezes on pause.
            // The bubble's entrance/exit fade and HUD position lerp stay unscaled
            // so they remain smooth and never freeze mid-transition.
            timeAccumulator += Time.deltaTime;

            int lettersToAdd = Mathf.FloorToInt(timeAccumulator / currentSpeed);

            if (lettersToAdd > 0)
            {
                int lettersAdded = 0;
                while (lettersAdded < lettersToAdd && charCount < characters.Length)
                {
                    char letter = characters[charCount];
                    charCount++;
                    lettersAdded++;

                    if (letter != ' ' && charCount % 3 == 0 && _voiceSO != null && _audioSource != null)
                    {
                        AudioClip clipToPlay = _voiceSO.GetRandomClip();
                        if (clipToPlay != null)
                        {
                            _audioSource.pitch = _pitch + Random.Range(-0.05f, 0.05f);
                            _audioSource.PlayOneShot(clipToPlay);
                        }
                    }
                }

                _textElement.maxVisibleCharacters = charCount;
                timeAccumulator -= lettersToAdd * currentSpeed;
            }

            yield return null;
        }

        _typeRoutine = null;
        OnTypingStateChanged?.Invoke(false);
        onComplete?.Invoke();
    }

    private IEnumerator EntranceAnimation(Action onComplete)
    {
        _canvasGroup.alpha = 0f;
        _animationBiasPx = new Vector2(0f, -_entranceSlideDistance);

        float elapsed = 0f;

        while (elapsed < _entranceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _entranceDuration);
            float eased = 1f - (1f - t) * (1f - t);

            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            _animationBiasPx = new Vector2(0f, Mathf.Lerp(-_entranceSlideDistance, 0f, eased));

            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _animationBiasPx = Vector2.zero;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator ExitAnimation(Action onComplete)
    {
        float startBiasY = _animationBiasPx.y;
        float endBiasY = startBiasY + _exitSlideDistance;

        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < _exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _exitDuration);
            float eased = t * t;

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            _animationBiasPx = new Vector2(0f, Mathf.Lerp(startBiasY, endBiasY, eased));

            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator ExpirationTimer()
    {
        // Scaled time: bubble lifetime is part of the speech simulation event.
        // At 5x speed bubbles disappear 5x sooner; on pause they persist indefinitely.
        yield return new WaitForSeconds(_duration);

        _expirationRoutine = null;

        Dismiss(() =>
        {
            OnExpired?.Invoke();
            _onExpiredCallback?.Invoke();
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void CheckHeightChanged()
    {
        float currentHeight = _rect.rect.height;
        if (!Mathf.Approximately(currentHeight, _cachedHeight))
        {
            _cachedHeight = currentHeight;
            OnHeightChanged?.Invoke();
        }
    }
}
