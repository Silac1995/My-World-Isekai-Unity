using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Client-only singleton that manages a full-screen fade overlay for map transitions.
/// Attach to a ScreenSpaceOverlay Canvas with the highest sort order.
/// </summary>
public class ScreenFadeManager : MonoBehaviour
{
    public static ScreenFadeManager Instance { get; private set; }

    [SerializeField] private Image _fadeImage;
    [SerializeField] private int _sortOrder = 999;

    private Coroutine _fadeCoroutine;

    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _warningText;
    private int _warningCount;
    private const int MAX_VISIBLE_WARNINGS = 5;

    public bool IsFading => _fadeCoroutine != null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeFadeImage();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void InitializeFadeImage()
    {
        if (_fadeImage != null)
        {
            SetAlpha(0f);
            _fadeImage.raycastTarget = false;
            CreateOverlayTexts();
            return;
        }

        // Self-bootstrap if no image assigned: create Canvas + Image
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _sortOrder;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        GameObject imageGO = new GameObject("FadeImage");
        imageGO.transform.SetParent(transform, false);

        _fadeImage = imageGO.AddComponent<Image>();
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);
        _fadeImage.raycastTarget = false;

        // Stretch to fill entire screen
        RectTransform rt = _fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        CreateOverlayTexts();
    }

    private void CreateOverlayTexts()
    {
        // Status text — centered, white, fontSize 28
        GameObject statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(_fadeImage.transform, false);

        _statusText = statusGO.AddComponent<TextMeshProUGUI>();
        _statusText.fontSize = 28;
        _statusText.color = Color.white;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.raycastTarget = false;

        RectTransform statusRT = _statusText.rectTransform;
        statusRT.anchorMin = new Vector2(0.1f, 0.45f);
        statusRT.anchorMax = new Vector2(0.9f, 0.55f);
        statusRT.offsetMin = Vector2.zero;
        statusRT.offsetMax = Vector2.zero;

        statusGO.SetActive(false);

        // Warning text — below status, orange, fontSize 18
        GameObject warningGO = new GameObject("WarningText");
        warningGO.transform.SetParent(_fadeImage.transform, false);

        _warningText = warningGO.AddComponent<TextMeshProUGUI>();
        _warningText.fontSize = 18;
        _warningText.color = new Color(1f, 0.6f, 0f);
        _warningText.alignment = TextAlignmentOptions.Center;
        _warningText.raycastTarget = false;

        RectTransform warningRT = _warningText.rectTransform;
        warningRT.anchorMin = new Vector2(0.1f, 0.3f);
        warningRT.anchorMax = new Vector2(0.9f, 0.44f);
        warningRT.offsetMin = Vector2.zero;
        warningRT.offsetMax = Vector2.zero;

        warningGO.SetActive(false);
    }

    /// <summary>
    /// Fades screen to black (alpha 0 -> 1).
    /// </summary>
    public void FadeOut(float duration)
    {
        StartFade(0f, 1f, duration);
    }

    /// <summary>
    /// Fades screen from black back to clear (alpha 1 -> 0).
    /// </summary>
    public void FadeIn(float duration)
    {
        StartFade(1f, 0f, duration);
    }

    private void StartFade(float from, float to, float duration)
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        if (_statusText != null) _statusText.gameObject.SetActive(false);
        if (_warningText != null) _warningText.gameObject.SetActive(false);

        _fadeCoroutine = StartCoroutine(FadeRoutine(from, to, duration));
    }

    private System.Collections.IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetAlpha(to);
            _fadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        SetAlpha(from);

        while (elapsed < duration)
        {
            // Use unscaledDeltaTime so fades work regardless of GameSpeedController
            elapsed += UnityEngine.Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetAlpha(Mathf.Lerp(from, to, t));
            yield return null;
        }

        SetAlpha(to);
        _fadeCoroutine = null;
    }

    /// <summary>
    /// Shows a blocking overlay at the given alpha with an optional status message.
    /// Blocks input via raycastTarget. Use UpdateStatus/ShowWarning to update content.
    /// </summary>
    public void ShowOverlay(float alpha, string status = null)
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        SetAlpha(alpha);
        _fadeImage.raycastTarget = true;

        if (_statusText != null)
        {
            _statusText.gameObject.SetActive(true);
            _statusText.text = status ?? "";
        }

        if (_warningText != null)
        {
            _warningText.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Hides the overlay, optionally fading out over the given duration.
    /// </summary>
    public void HideOverlay(float fadeDuration = 0.5f)
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (_statusText != null) _statusText.gameObject.SetActive(false);
        if (_warningText != null) _warningText.gameObject.SetActive(false);

        if (fadeDuration <= 0f)
        {
            SetAlpha(0f);
            _fadeImage.raycastTarget = false;
        }
        else
        {
            _fadeCoroutine = StartCoroutine(HideOverlayRoutine(fadeDuration));
        }
    }

    private System.Collections.IEnumerator HideOverlayRoutine(float duration)
    {
        float startAlpha = _fadeImage.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetAlpha(Mathf.Lerp(startAlpha, 0f, t));
            yield return null;
        }

        SetAlpha(0f);
        _fadeImage.raycastTarget = false;
        _fadeCoroutine = null;
    }

    /// <summary>
    /// Updates the status text shown on the overlay.
    /// </summary>
    public void UpdateStatus(string status)
    {
        if (_statusText != null)
        {
            _statusText.text = status;
        }
    }

    /// <summary>
    /// Appends a warning message to the warning text area.
    /// Only the first MAX_VISIBLE_WARNINGS are shown; after that a "+more" indicator appears.
    /// </summary>
    public void ShowWarning(string warning)
    {
        _warningCount++;

        if (_warningText == null) return;

        if (_warningCount <= MAX_VISIBLE_WARNINGS)
        {
            if (_warningText.text.Length > 0)
                _warningText.text += "\n";
            _warningText.text += $"<color=orange>{warning}</color>";
        }
        else if (_warningCount == MAX_VISIBLE_WARNINGS + 1)
        {
            _warningText.text += "\n<color=orange>+more warnings...</color>";
        }
    }

    /// <summary>
    /// Clears all accumulated warnings.
    /// </summary>
    public void ClearWarnings()
    {
        _warningCount = 0;
        if (_warningText != null)
        {
            _warningText.text = "";
        }
    }

    private void SetAlpha(float alpha)
    {
        if (_fadeImage != null)
        {
            Color c = _fadeImage.color;
            c.a = alpha;
            _fadeImage.color = c;
        }
    }
}
