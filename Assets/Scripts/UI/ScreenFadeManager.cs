using UnityEngine;
using UnityEngine.UI;

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
