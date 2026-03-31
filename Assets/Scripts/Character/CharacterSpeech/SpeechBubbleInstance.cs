using UnityEngine;
using TMPro;
using System;
using System.Collections;
using Random = UnityEngine.Random;

/// <summary>
/// Manages a single speech bubble's lifecycle: typing animation, voice playback,
/// entrance/exit animations, expiration timer, and height tracking.
/// Spawned and managed by SpeechBubbleStack.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SpeechBubbleInstance : MonoBehaviour
{
    // ── Serialized Fields ──────────────────────────────────────────────
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private GameObject _separatorLine;
    [Header("Animation")]
    [SerializeField] private float _entranceDuration = 0.3f;
    [SerializeField] private float _entranceSlideDistance = 15f;
    [SerializeField] private float _exitDuration = 0.3f;
    [SerializeField] private float _exitSlideDistance = 10f;

    // ── Events ─────────────────────────────────────────────────────────
    public Action OnExpired;
    public Action OnHeightChanged;
    public Action<bool> OnTypingStateChanged;

    // ── Public Properties ──────────────────────────────────────────────
    public bool IsTyping => _typeRoutine != null;
    public bool IsScripted => _isScripted;

    // ── Private Fields ─────────────────────────────────────────────────
    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Coroutine _typeRoutine;
    private Coroutine _animRoutine;
    private Coroutine _expirationRoutine;
    private Vector3 _targetPosition;
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
        _rectTransform = GetComponent<RectTransform>();
        _targetPosition = transform.localPosition;
        _cachedHeight = _rectTransform.rect.height;
    }

    private void Update()
    {
        if ((transform.localPosition - _targetPosition).sqrMagnitude < 0.001f) return;
        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            _targetPosition,
            8f * Time.unscaledDeltaTime
        );
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

    /// <summary>
    /// Standard bubble setup. Plays entrance, types the message, waits for duration, then dismisses.
    /// </summary>
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

            _cachedHeight = _rectTransform.rect.height;

            if (_animRoutine != null) StopCoroutine(_animRoutine);
            _animRoutine = StartCoroutine(EntranceAnimation(() =>
            {
                if (_typeRoutine != null) StopCoroutine(_typeRoutine);
                _typeRoutine = StartCoroutine(TypeMessage(() =>
                {
                    // Typing complete — start expiration timer
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

    /// <summary>
    /// Scripted bubble setup. Types the message, fires callback when done. No expiration timer.
    /// </summary>
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

            _cachedHeight = _rectTransform.rect.height;

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
    /// Immediately completes the typing animation, showing the full message.
    /// </summary>
    public void CompleteTypingImmediately()
    {
        if (_typeRoutine == null) return;

        StopCoroutine(_typeRoutine);
        _typeRoutine = null;

        if (_textElement != null)
            _textElement.maxVisibleCharacters = _fullMessage.Length;

        OnTypingStateChanged?.Invoke(false);
        CheckHeightChanged();

        // Fire the same completion logic that TypeMessage's onComplete would have triggered
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

    /// <summary>
    /// Restarts the expiration timer from scratch. Called when this bubble is pushed
    /// by a nearby character's speech, so it stays visible during the conversation.
    /// Has no effect on scripted bubbles (they don't auto-expire).
    /// </summary>
    public void ResetExpirationTimer()
    {
        if (_isScripted || _duration <= 0f) return;
        if (_expirationRoutine == null) return; // not yet expiring (still typing) or already dismissed

        StopCoroutine(_expirationRoutine);
        _expirationRoutine = StartCoroutine(ExpirationTimer());
    }

    /// <summary>
    /// Plays exit animation, then destroys this bubble.
    /// </summary>
    public void Dismiss(Action onComplete = null)
    {
        // Stop any running expiration so we don't double-dismiss
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
    /// Returns the current height of this bubble in the parent stack's local space.
    /// The Canvas uses canvas-local units (e.g. 70) but the root's localScale (e.g. 0.03)
    /// converts those to the stack's coordinate system. Multiply canvas height by root scale.
    /// </summary>
    public float GetHeight()
    {
        if (_textElement == null) return 0f;
        // Use the Canvas RectTransform (parent of text element) for the full bubble height
        var canvasRect = _textElement.canvas?.GetComponent<RectTransform>();
        if (canvasRect == null) return 0f;
        // Canvas rect.height is in canvas-local units. Root's localScale.y converts to parent (stack) space.
        return canvasRect.rect.height * transform.localScale.y;
    }

    /// <summary>
    /// Sets the target local position this bubble will lerp toward in Update().
    /// </summary>
    public void SetTargetPosition(Vector3 localPos)
    {
        _targetPosition = localPos;
    }

    /// <summary>
    /// Returns the current target position this bubble is lerping toward.
    /// </summary>
    public Vector3 GetTargetPosition()
    {
        return _targetPosition;
    }

    /// <summary>
    /// Shows or hides the separator line between stacked bubbles.
    /// </summary>
    public void SetSeparatorVisible(bool visible)
    {
        if (_separatorLine != null)
            _separatorLine.SetActive(visible);
    }

    // ── Coroutines ─────────────────────────────────────────────────────

    /// <summary>
    /// Letter-by-letter typing using a time accumulator. Preserved from Speech.cs.
    /// Uses unscaled time so UI is unaffected by GameSpeedController.
    /// </summary>
    private IEnumerator TypeMessage(Action onComplete)
    {
        OnTypingStateChanged?.Invoke(true);

        // Set full text upfront so layout calculates final frame size immediately.
        // Use maxVisibleCharacters to reveal text letter-by-letter.
        _textElement.text = _fullMessage;
        _textElement.maxVisibleCharacters = 0;

        // Force layout rebuild so ContentSizeFitter computes final height now
        _textElement.ForceMeshUpdate();
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
            timeAccumulator += Time.unscaledDeltaTime;

            int lettersToAdd = Mathf.FloorToInt(timeAccumulator / currentSpeed);

            if (lettersToAdd > 0)
            {
                int lettersAdded = 0;
                while (lettersAdded < lettersToAdd && charCount < characters.Length)
                {
                    char letter = characters[charCount];
                    charCount++;
                    lettersAdded++;

                    // Voice playback every 3rd non-space character — same logic as Speech.cs
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

                // Subtract consumed time
                timeAccumulator -= lettersToAdd * currentSpeed;
            }

            yield return null;
        }

        _typeRoutine = null;
        OnTypingStateChanged?.Invoke(false);
        onComplete?.Invoke();
    }

    /// <summary>
    /// Entrance: fade in from alpha 0 and slide up from -15 offset. EaseOut curve.
    /// </summary>
    private IEnumerator EntranceAnimation(Action onComplete)
    {
        _canvasGroup.alpha = 0f;
        Vector3 startPos = transform.localPosition;
        startPos.y -= _entranceSlideDistance;
        transform.localPosition = startPos;

        Vector3 endPos = _targetPosition;
        float elapsed = 0f;

        while (elapsed < _entranceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _entranceDuration);
            // EaseOut: 1 - (1 - t)^2
            float eased = 1f - (1f - t) * (1f - t);

            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            transform.localPosition = Vector3.Lerp(startPos, endPos, eased);

            yield return null;
        }

        _canvasGroup.alpha = 1f;
        transform.localPosition = endPos;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    /// <summary>
    /// Exit: fade out to alpha 0 and slide up +10. EaseIn curve.
    /// </summary>
    private IEnumerator ExitAnimation(Action onComplete)
    {
        Vector3 startPos = transform.localPosition;
        Vector3 endPos = startPos;
        endPos.y += _exitSlideDistance;

        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < _exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _exitDuration);
            // EaseIn: t^2
            float eased = t * t;

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            transform.localPosition = Vector3.Lerp(startPos, endPos, eased);

            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    /// <summary>
    /// Waits for the configured duration, then dismisses and fires the expired callback.
    /// </summary>
    private IEnumerator ExpirationTimer()
    {
        yield return new WaitForSecondsRealtime(_duration);

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
        float currentHeight = _rectTransform.rect.height;
        if (!Mathf.Approximately(currentHeight, _cachedHeight))
        {
            _cachedHeight = currentHeight;
            OnHeightChanged?.Invoke();
        }
    }
}
