using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// Handles the visual representation and lifecycle of a single Toast Notification.
    /// Returns itself to the manager's object pool instead of destroying after its duration.
    /// </summary>
    public class UI_ToastElement : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Animation Settings")]
        [SerializeField] private float _fadeDuration = 0.3f;

        private Action<UI_ToastElement> _returnToPoolAction;
        private Coroutine _activeRoutine;

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void Initialize(ToastNotificationPayload payload, Action<UI_ToastElement> returnToPoolAction)
        {
            _returnToPoolAction = returnToPoolAction;
            
            // Set Text
            if (_titleText != null)
            {
                _titleText.text = payload.Title;
                _titleText.gameObject.SetActive(!string.IsNullOrEmpty(payload.Title));
            }

            if (_messageText != null)
            {
                _messageText.text = payload.Message;
            }

            // Set Icon
            if (_iconImage != null)
            {
                _iconImage.sprite = payload.Icon;
                _iconImage.gameObject.SetActive(payload.Icon != null);
            }

            // Optional: You could adjust the _backgroundImage.color based on payload.Type here

            // Start Animation
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
            }
            
            _activeRoutine = StartCoroutine(ToastLifecycleRoutine(payload.Duration));
        }

        private IEnumerator ToastLifecycleRoutine(float holdDuration)
        {
            // Reset state
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            // Fade In
            float timer = 0f;
            while (timer < _fadeDuration)
            {
                timer += UnityEngine.Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(0f, 1f, timer / _fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;

            // Hold
            yield return new WaitForSecondsRealtime(holdDuration);

            // Fade Out
            timer = 0f;
            while (timer < _fadeDuration)
            {
                timer += UnityEngine.Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / _fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = 0f;

            // Return to pool
            _activeRoutine = null;
            _returnToPoolAction?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }
            _returnToPoolAction = null;
        }
    }
}
