using UnityEngine;
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
    [SerializeField] private GameObject _separatorLine;

    [Header("Animation (reference-resolution pixels)")]
    [SerializeField] private float _entranceDuration = 0.3f;
    [SerializeField] private float _entranceSlideDistance = 40f;
    [SerializeField] private float _exitDuration = 0.3f;
    [SerializeField] private float _exitSlideDistance = 25f;
    [SerializeField] private float _positionLerpSpeed = 8f;

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

        Vector2 target = (Vector2)sp + _stackOffsetPx;
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

    public void SetSeparatorVisible(bool visible)
    {
        if (_separatorLine != null)
            _separatorLine.SetActive(visible);
    }

    // ── Coroutines ─────────────────────────────────────────────────────

    private IEnumerator TypeMessage(Action onComplete)
    {
        OnTypingStateChanged?.Invoke(true);

        _textElement.text = _fullMessage;
        _textElement.maxVisibleCharacters = 0;

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

        // Start position: _stackOffsetPx shifted DOWN by the slide distance.
        // The Update() lerp will aim at _stackOffsetPx, so we temporarily bias the
        // offset downward, fade in, then restore.
        Vector2 targetOffset = _stackOffsetPx;
        _stackOffsetPx = new Vector2(targetOffset.x, targetOffset.y - _entranceSlideDistance);

        float elapsed = 0f;

        while (elapsed < _entranceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _entranceDuration);
            float eased = 1f - (1f - t) * (1f - t);

            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            _stackOffsetPx = new Vector2(
                targetOffset.x,
                Mathf.Lerp(targetOffset.y - _entranceSlideDistance, targetOffset.y, eased));

            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _stackOffsetPx = targetOffset;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator ExitAnimation(Action onComplete)
    {
        Vector2 startOffset = _stackOffsetPx;
        Vector2 endOffset = new Vector2(startOffset.x, startOffset.y + _exitSlideDistance);

        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < _exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _exitDuration);
            float eased = t * t;

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            _stackOffsetPx = Vector2.Lerp(startOffset, endOffset, eased);

            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _animRoutine = null;

        onComplete?.Invoke();
    }

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
        float currentHeight = _rect.rect.height;
        if (!Mathf.Approximately(currentHeight, _cachedHeight))
        {
            _cachedHeight = currentHeight;
            OnHeightChanged?.Invoke();
        }
    }
}
