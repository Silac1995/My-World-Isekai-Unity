using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Loading
{
    /// <summary>
    /// Generic full-screen loading overlay. Lazy singleton, DontDestroyOnLoad, client-side UI only.
    ///
    /// Pure UI controller — exposes a push API (Show / SetStage / SetDetail / SetCancelHandler /
    /// ShowFailure / Hide) and knows nothing about NGO, save/load, or scene transitions. Producers
    /// (drivers) observe their domain events and push stage updates here. The first ever call to
    /// <see cref="Show"/> instantiates <c>Resources/UI/UI_LoadingOverlay</c> and persists it across
    /// scene loads.
    ///
    /// All animations use <c>Time.unscaledDeltaTime</c> per project rule #26 — UI must remain
    /// responsive when <c>GameSpeedController</c> pauses or warps simulation time.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        private const string ResourcePath = "UI/UI_LoadingOverlay";

        [Header("Wired in prefab")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _stageText;
        [SerializeField] private TextMeshProUGUI _detailText;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private TextMeshProUGUI _cancelButtonLabel;
        [SerializeField] private CanvasGroup _cancelButtonCanvasGroup;

        [Header("Tuning")]
        [Tooltip("Seconds for the progress bar to tween to its target value.")]
        [SerializeField] private float _barTweenDuration = 0.25f;
        [Tooltip("Seconds for the cancel button fade-in once its delay elapses.")]
        [SerializeField] private float _cancelButtonFadeDuration = 0.4f;

        private static LoadingOverlay s_instance;

        private float _targetProgress01;
        private float _displayedProgress01;
        private float _shownAtUnscaledTime;
        private float _cancelButtonDelaySeconds;
        private bool _cancelButtonShown;
        private Action _onCancel;
        private Coroutine _tweenCoroutine;
        private Coroutine _cancelFadeCoroutine;

        public static LoadingOverlay Instance
        {
            get
            {
                if (s_instance != null) return s_instance;

                var prefab = Resources.Load<GameObject>(ResourcePath);
                if (prefab == null)
                {
                    Debug.LogError($"[LoadingOverlay] Prefab not found at Resources/{ResourcePath}.prefab. Cannot show overlay.");
                    return null;
                }

                var go = Instantiate(prefab);
                go.name = "UI_LoadingOverlay (singleton)";
                // DontDestroyOnLoad only works in playmode; calling it from EditMode (e.g. an editor
                // smoke test or asset utility instantiating the singleton) throws. Guarding so the
                // singleton is usable from EditMode without changing runtime behavior in playmode.
                if (Application.isPlaying) DontDestroyOnLoad(go);
                s_instance = go.GetComponent<LoadingOverlay>();
                if (s_instance == null)
                {
                    Debug.LogError($"[LoadingOverlay] Prefab at Resources/{ResourcePath} is missing the LoadingOverlay component on the root.");
                }
                return s_instance;
            }
        }

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        private void Awake()
        {
            // Self-register so direct scene placements also act as the singleton if present.
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            if (_panelRoot != null) _panelRoot.SetActive(false);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(HandleCancelClicked);
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        public void Show(string title)
        {
            EnsurePanelActive();
            if (_titleText != null) _titleText.text = title ?? string.Empty;
            if (_stageText != null) _stageText.text = string.Empty;
            if (_detailText != null) _detailText.text = string.Empty;

            _targetProgress01 = 0f;
            _displayedProgress01 = 0f;
            if (_progressBar != null) _progressBar.value = 0f;

            _shownAtUnscaledTime = UnityEngine.Time.unscaledTime;
            _cancelButtonShown = false;
            _onCancel = null;
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
            if (_cancelButtonLabel != null) _cancelButtonLabel.text = "Cancel";

            CancelTweens();
        }

        public void SetStage(string stageText, float progress01)
        {
            EnsurePanelActive();
            if (_stageText != null) _stageText.text = stageText ?? string.Empty;
            _targetProgress01 = Mathf.Clamp01(progress01);
            StartBarTween();
        }

        public void SetDetail(string detail)
        {
            if (_detailText != null) _detailText.text = detail ?? string.Empty;
        }

        public void SetCancelHandler(Action onCancel, float cancelDelaySeconds = 10f)
        {
            _onCancel = onCancel;
            _cancelButtonDelaySeconds = Mathf.Max(0f, cancelDelaySeconds);
            // Reset shown flag — Update() will re-decide based on elapsed time.
            _cancelButtonShown = false;
            if (_cancelFadeCoroutine != null) { StopCoroutine(_cancelFadeCoroutine); _cancelFadeCoroutine = null; }
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        public void ShowFailure(string reason)
        {
            EnsurePanelActive();
            if (_stageText != null) _stageText.text = string.IsNullOrEmpty(reason) ? "Connection failed." : $"Connection failed: {reason}";
            if (_detailText != null) _detailText.text = string.Empty;
            _targetProgress01 = 1f;
            StartBarTween();

            // Repurpose the cancel button as "Back to main menu" — surface immediately.
            _cancelButtonShown = true;
            if (_cancelButtonLabel != null) _cancelButtonLabel.text = "Back to main menu";
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 1f;
            if (_cancelButton != null) _cancelButton.interactable = true;
        }

        public void Hide()
        {
            CancelTweens();
            if (_panelRoot != null) _panelRoot.SetActive(false);
            _onCancel = null;
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        private void Update()
        {
            if (!IsVisible) return;

            // Cancel-button delay countdown (unscaled — survives GameSpeedController pause).
            if (!_cancelButtonShown && _onCancel != null)
            {
                if (UnityEngine.Time.unscaledTime - _shownAtUnscaledTime >= _cancelButtonDelaySeconds)
                {
                    _cancelButtonShown = true;
                    if (_cancelFadeCoroutine != null) StopCoroutine(_cancelFadeCoroutine);
                    _cancelFadeCoroutine = StartCoroutine(FadeInCancelButton());
                }
            }
        }

        private IEnumerator FadeInCancelButton()
        {
            if (_cancelButton != null) _cancelButton.interactable = true;
            if (_cancelButtonCanvasGroup == null) yield break;

            float t = 0f;
            float start = _cancelButtonCanvasGroup.alpha;
            while (t < _cancelButtonFadeDuration)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                _cancelButtonCanvasGroup.alpha = Mathf.Lerp(start, 1f, Mathf.Clamp01(t / _cancelButtonFadeDuration));
                yield return null;
            }
            _cancelButtonCanvasGroup.alpha = 1f;
        }

        private void StartBarTween()
        {
            if (_tweenCoroutine != null) StopCoroutine(_tweenCoroutine);
            _tweenCoroutine = StartCoroutine(BarTween());
        }

        private IEnumerator BarTween()
        {
            float start = _displayedProgress01;
            float end = _targetProgress01;
            float t = 0f;
            float duration = Mathf.Max(0.01f, _barTweenDuration);
            while (t < duration)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                _displayedProgress01 = Mathf.Lerp(start, end, Mathf.Clamp01(t / duration));
                if (_progressBar != null) _progressBar.value = _displayedProgress01;
                yield return null;
            }
            _displayedProgress01 = end;
            if (_progressBar != null) _progressBar.value = _displayedProgress01;
        }

        private void CancelTweens()
        {
            if (_tweenCoroutine != null) { StopCoroutine(_tweenCoroutine); _tweenCoroutine = null; }
            if (_cancelFadeCoroutine != null) { StopCoroutine(_cancelFadeCoroutine); _cancelFadeCoroutine = null; }
        }

        private void EnsurePanelActive()
        {
            if (_panelRoot != null && !_panelRoot.activeSelf) _panelRoot.SetActive(true);
        }

        private void HandleCancelClicked()
        {
            var cb = _onCancel;
            // Clear so the same click can't double-fire if a coroutine reads it later.
            _onCancel = null;
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(HandleCancelClicked);
            CancelTweens();
        }
    }
}
